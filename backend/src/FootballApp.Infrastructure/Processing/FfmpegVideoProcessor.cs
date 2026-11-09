using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Application.Common.Models;
using Microsoft.Extensions.Options;

namespace FootballApp.Infrastructure.Processing;

/// <summary>
/// IVideoProcessor on top of the ffmpeg and ffprobe command-line tools. The source is normally a signed
/// R2 URL: ffmpeg seeks in it with HTTP range requests, so cutting a few seconds out of a 15 GB match
/// only downloads those few seconds.
/// </summary>
public partial class FfmpegVideoProcessor(IOptions<ProcessingOptions> options) : IVideoProcessor
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan EncodeTimeout = TimeSpan.FromMinutes(30);
    private const string ScratchPrefix = "football-clip-";

    // Never upscales; caps landscape video at 1920x1080 and portrait at 1080x1920 so a 4K phone recording
    // doesn't take minutes per clip on a small container. Even dimensions are required by yuv420p.
    private const string VideoFilter =
        "scale=w='if(gte(iw,ih),min(1920,iw),min(1080,iw))':h='if(gte(iw,ih),min(1080,ih),min(1920,ih))'"
        + ":force_original_aspect_ratio=decrease:force_divisible_by=2,setsar=1,format=yuv420p";

    public async Task<VideoProbeResult> ProbeAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        List<string> arguments =
        [
            "-v", "error",
            "-print_format", "json",
            "-show_entries", "format=duration:stream=codec_type",
            .. NetworkInputOptions(sourceUrl),
            "-i", sourceUrl
        ];

        var result = await ProcessRunner.RunAsync(options.Value.FfprobePath, arguments, ProbeTimeout, cancellationToken);
        if (result.ExitCode != 0)
        {
            if (IsUnreadableMedia(result.StandardError))
            {
                throw new InvalidVideoException("The uploaded file is not a playable video.");
            }

            throw new VideoProcessingException($"ffprobe exited with code {result.ExitCode}: {Summarize(result.StandardError)}");
        }

        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;

        var streamTypes = root.TryGetProperty("streams", out var streams)
            ? streams.EnumerateArray()
                .Select(s => s.TryGetProperty("codec_type", out var type) ? type.GetString() : null)
                .ToList()
            : [];

        if (!root.TryGetProperty("format", out var format)
            || !format.TryGetProperty("duration", out var durationText)
            || !double.TryParse(durationText.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
            || !double.IsFinite(duration)
            || duration < 0)
        {
            throw new InvalidVideoException("The uploaded file is not a playable video.");
        }

        return new VideoProbeResult(duration, streamTypes.Contains("video"), streamTypes.Contains("audio"));
    }

    public async Task<RenderedVideo> RenderAsync(string sourceUrl, IReadOnlyList<ClipSegment> segments, CancellationToken cancellationToken)
    {
        if (segments.Count == 0)
        {
            throw new ArgumentException("At least one segment is required.", nameof(segments));
        }

        // Every segment comes from the same source, so one probe decides the audio mapping for all of them.
        var source = await ProbeAsync(sourceUrl, cancellationToken);
        if (!source.HasVideo)
        {
            throw new InvalidVideoException("The match video does not contain any video.");
        }

        var workDirectory = Directory.CreateTempSubdirectory(ScratchPrefix).FullName;
        try
        {
            var outputPath = Path.Combine(workDirectory, "clip.mp4");

            if (segments.Count == 1)
            {
                await EncodeSegmentAsync(sourceUrl, segments[0], source.HasAudio, outputPath, asMp4: true, cancellationToken);
            }
            else
            {
                // Segments are encoded one at a time (one decoder and one connection to R2 at any moment,
                // however long the reel) into MPEG-TS, which can be joined without re-encoding because
                // every segment was produced with identical settings.
                var segmentFiles = new List<string>();
                for (var i = 0; i < segments.Count; i++)
                {
                    var fileName = $"segment-{i:D3}.ts";
                    await EncodeSegmentAsync(sourceUrl, segments[i], source.HasAudio, Path.Combine(workDirectory, fileName), asMp4: false, cancellationToken);
                    segmentFiles.Add(fileName);
                }

                var listPath = Path.Combine(workDirectory, "segments.txt");
                await File.WriteAllLinesAsync(
                    listPath,
                    ["ffconcat version 1.0", .. segmentFiles.Select(name => $"file '{name}'")],
                    cancellationToken);

                await RunFfmpegAsync(
                    ["-f", "concat", "-safe", "0", "-i", listPath, "-c", "copy", "-movflags", "+faststart", outputPath],
                    cancellationToken);
            }

            return new RenderedVideo(outputPath, () => DeleteDirectory(workDirectory));
        }
        catch
        {
            DeleteDirectory(workDirectory);
            throw;
        }
    }

    /// <summary>
    /// Removes scratch directories left behind by a render that was killed outright (out of memory,
    /// container restart) and so never got to clean up. Call once when the worker starts, before any
    /// job runs — it assumes this process is the only one rendering into this temp directory.
    /// </summary>
    public static void DeleteLeftoverScratchDirectories()
    {
        foreach (var directory in Directory.EnumerateDirectories(Path.GetTempPath(), ScratchPrefix + "*"))
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>
    /// Re-encodes one segment to H.264/AAC. Stream copy would be faster, but it can only cut on keyframes
    /// (clips would start seconds early or with a frozen picture) and would pass through codecs such as
    /// HEVC that many browsers cannot play from a share link.
    /// </summary>
    private async Task EncodeSegmentAsync(
        string sourceUrl,
        ClipSegment segment,
        bool hasAudio,
        string outputPath,
        bool asMp4,
        CancellationToken cancellationToken)
    {
        string[] audioMapping = hasAudio ? ["-map", "0:a:0"] : ["-an"];
        string[] audioCodec = hasAudio ? ["-c:a", "aac", "-b:a", "128k", "-ar", "48000", "-ac", "2"] : [];
        string[] container = asMp4 ? ["-movflags", "+faststart"] : ["-f", "mpegts"];

        List<string> arguments =
        [
            .. NetworkInputOptions(sourceUrl),
            // Before -i these seek in the input, so only the segment itself is read and decoded.
            "-ss", segment.StartSeconds.ToString(CultureInfo.InvariantCulture),
            "-t", segment.DurationSeconds.ToString(CultureInfo.InvariantCulture),
            "-i", sourceUrl,
            "-map", "0:v:0",
            .. audioMapping,
            "-sn", "-dn",
            // Phone recordings carry GPS position and device details; a public share link must not.
            "-map_metadata", "-1",
            "-map_chapters", "-1",
            "-vf", VideoFilter,
            // No B-frames keeps timestamps monotonic where segments are joined.
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-bf", "0",
            .. audioCodec,
            .. container,
            outputPath
        ];

        await RunFfmpegAsync(arguments, cancellationToken);

        // Seeking past the last frame is not an error for ffmpeg: it exits cleanly with an empty file.
        // That happens when the picture ends before the container's stated duration (e.g. the sound runs
        // longer), and must not be published as a finished clip.
        if (!await HasPictureAsync(outputPath, cancellationToken))
        {
            throw new InvalidVideoException("The clip came out empty — the tag is past the end of the recorded picture.");
        }
    }

    private async Task<bool> HasPictureAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var probe = await ProbeAsync(path, cancellationToken);
            return probe.HasVideo && probe.DurationSeconds > 0;
        }
        catch (InvalidVideoException)
        {
            return false;
        }
    }

    private async Task RunFfmpegAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        string[] allArguments = ["-hide_banner", "-nostdin", "-loglevel", "error", "-y", .. arguments];

        var result = await ProcessRunner.RunAsync(options.Value.FfmpegPath, allArguments, EncodeTimeout, cancellationToken);
        if (result.ExitCode != 0)
        {
            if (IsUnreadableMedia(result.StandardError))
            {
                throw new InvalidVideoException("The match video could not be read.");
            }

            throw new VideoProcessingException($"ffmpeg exited with code {result.ExitCode}: {Summarize(result.StandardError)}");
        }
    }

    /// <summary>Lets ffmpeg ride out a dropped connection instead of hanging or failing the whole job.</summary>
    private static string[] NetworkInputOptions(string sourceUrl)
    {
        var isHttp = sourceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || sourceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        return isHttp
            ? ["-rw_timeout", "30000000", "-reconnect", "1", "-reconnect_on_network_error", "1", "-reconnect_delay_max", "10"]
            : [];
    }

    private static bool IsUnreadableMedia(string standardError) =>
        standardError.Contains("Invalid data found when processing input", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("moov atom not found", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tail of the error output, with the query string of any signed URL removed so it never reaches the logs.</summary>
    private static string Summarize(string standardError)
    {
        var text = SignedUrlQuery().Replace(standardError.Trim(), "$1?<redacted>");
        return text.Length > 1500 ? text[^1500..] : text;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Scratch space in the container's temp directory; leaving it behind is not worth failing a finished job.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex(@"(https?://[^\s?'""]+)\?[^\s'""]*")]
    private static partial Regex SignedUrlQuery();
}
