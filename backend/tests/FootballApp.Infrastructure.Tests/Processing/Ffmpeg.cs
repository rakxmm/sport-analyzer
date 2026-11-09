using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace FootballApp.Infrastructure.Tests.Processing;

/// <summary>Runs the real ffmpeg/ffprobe for the tests' own needs: making sample videos and inspecting results.</summary>
internal static class Ffmpeg
{
    public static bool IsInstalled()
    {
        try
        {
            return Run("ffmpeg", "-version").ExitCode == 0 && Run("ffprobe", "-version").ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public static void Generate(params string[] arguments)
    {
        var result = Run("ffmpeg", ["-hide_banner", "-loglevel", "error", "-y", .. arguments]);
        Assert.True(result.ExitCode == 0, $"ffmpeg could not create a sample video: {result.Error}");
    }

    public static MediaInfo Inspect(string path)
    {
        var result = Run(
            "ffprobe", "-v", "error", "-print_format", "json",
            "-show_entries", "format=duration:format_tags:stream=codec_type,codec_name,width,height,pix_fmt",
            path);
        Assert.True(result.ExitCode == 0, $"ffprobe could not read {path}: {result.Error}");

        using var json = JsonDocument.Parse(result.Output);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var format = json.RootElement.GetProperty("format");

        JsonElement? Stream(string type) => streams
            .Where(s => s.GetProperty("codec_type").GetString() == type)
            .Select(s => (JsonElement?)s)
            .FirstOrDefault();

        var video = Stream("video");
        var audio = Stream("audio");

        return new MediaInfo(
            double.Parse(format.GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            video?.GetProperty("codec_name").GetString(),
            audio?.GetProperty("codec_name").GetString(),
            video?.GetProperty("width").GetInt32() ?? 0,
            video?.GetProperty("height").GetInt32() ?? 0,
            video?.GetProperty("pix_fmt").GetString(),
            format.TryGetProperty("tags", out var tags)
                ? tags.EnumerateObject().Select(t => t.Name.ToLowerInvariant()).ToList()
                : []);
    }

    /// <summary>The average colour of the frame shown at the given time.</summary>
    public static (byte Red, byte Green, byte Blue) ColorAt(string path, double seconds)
    {
        var pixelPath = Path.Combine(Path.GetTempPath(), $"pixel-{Guid.NewGuid():N}.rgb");
        try
        {
            Generate(
                "-ss", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-i", path,
                "-frames:v", "1",
                "-vf", "scale=1:1",
                "-f", "rawvideo", "-pix_fmt", "rgb24",
                pixelPath);

            var pixel = File.ReadAllBytes(pixelPath);
            return (pixel[0], pixel[1], pixel[2]);
        }
        finally
        {
            File.Delete(pixelPath);
        }
    }

    /// <summary>True if the index (moov) comes before the media data (mdat), so playback can start while downloading.</summary>
    public static bool IsFastStart(string path)
    {
        using var file = File.OpenRead(path);
        var header = new byte[8];

        while (file.Read(header, 0, 8) == 8)
        {
            long size = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
            var type = System.Text.Encoding.ASCII.GetString(header, 4, 4);

            if (type == "moov") return true;
            if (type == "mdat") return false;

            if (size == 1)
            {
                var extended = new byte[8];
                file.ReadExactly(extended);
                size = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(extended) - 8;
            }

            if (size < 8) return false;
            file.Seek(size - 8, SeekOrigin.Current);
        }

        return false;
    }

    private static (int ExitCode, string Output, string Error) Run(string tool, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = tool,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error.Result);
    }
}

internal record MediaInfo(
    double DurationSeconds,
    string? VideoCodec,
    string? AudioCodec,
    int Width,
    int Height,
    string? PixelFormat,
    IReadOnlyList<string> Tags);
