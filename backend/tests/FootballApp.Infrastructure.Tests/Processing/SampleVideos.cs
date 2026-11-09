using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FootballApp.Infrastructure.Tests.Processing;

/// <summary>
/// Short videos generated with ffmpeg once per test run, plus a local HTTP server that serves them with
/// range support the way R2 does. If ffmpeg is not installed the tests are skipped — unless
/// FFMPEG_TESTS_REQUIRED=true, which CI should set so a missing ffmpeg cannot go unnoticed.
/// </summary>
public sealed class SampleVideos : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("football-tests-").FullName;
    private WebApplication? _server;

    public bool FfmpegInstalled { get; private set; }

    /// <summary>18 s, 640x360, with sound: red for 0–6 s, green for 6–12 s, blue for 12–18 s. Carries a GPS tag.</summary>
    public string Colors => Path.Combine(_directory, "colors.mp4");

    /// <summary>6 s, 320x240, no audio track.</summary>
    public string Silent => Path.Combine(_directory, "silent.mp4");

    /// <summary>2 s, 2560x1440.</summary>
    public string Large => Path.Combine(_directory, "large.mp4");

    /// <summary>2 s, 720x1280.</summary>
    public string Portrait => Path.Combine(_directory, "portrait.mp4");

    /// <summary>Starts like an MP4 but has no index — what a recording cut off halfway looks like.</summary>
    public string Corrupt => Path.Combine(_directory, "corrupt.mp4");

    /// <summary>Plain text with a video file name. ffprobe does not reject it outright; it just finds no picture in it.</summary>
    public string TextFile => Path.Combine(_directory, "text.mp4");

    /// <summary>URL of <see cref="Colors"/> on the local HTTP server.</summary>
    public string ColorsUrl { get; private set; } = string.Empty;

    public List<string> RangeRequests { get; } = new();

    public void SkipIfFfmpegIsMissing()
    {
        var required = string.Equals(Environment.GetEnvironmentVariable("FFMPEG_TESTS_REQUIRED"), "true", StringComparison.OrdinalIgnoreCase);
        Assert.True(FfmpegInstalled || !required, "FFMPEG_TESTS_REQUIRED is set, but ffmpeg/ffprobe are not on PATH.");
        Assert.SkipUnless(FfmpegInstalled, "ffmpeg/ffprobe are not on PATH.");
    }

    public async ValueTask InitializeAsync()
    {
        FfmpegInstalled = Ffmpeg.IsInstalled();
        if (!FfmpegInstalled)
        {
            return;
        }

        Ffmpeg.Generate(
            "-f", "lavfi", "-i", "color=c=red:s=640x360:r=25:d=6",
            "-f", "lavfi", "-i", "color=c=green:s=640x360:r=25:d=6",
            "-f", "lavfi", "-i", "color=c=blue:s=640x360:r=25:d=6",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=18",
            "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0[v]",
            "-map", "[v]", "-map", "3:a",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac",
            "-metadata", "location=+48.1486+017.1077/", "-metadata", "title=Secret",
            Colors);

        Ffmpeg.Generate(
            "-f", "lavfi", "-i", "testsrc=duration=6:size=320x240:rate=25",
            "-c:v", "libx264", "-pix_fmt", "yuv420p",
            Silent);

        Ffmpeg.Generate(
            "-f", "lavfi", "-i", "testsrc=duration=2:size=2560x1440:rate=25",
            "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
            Large);

        Ffmpeg.Generate(
            "-f", "lavfi", "-i", "testsrc=duration=2:size=720x1280:rate=25",
            "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
            Portrait);

        byte[] mp4Header = [0, 0, 0, 24, .. "ftypmp42"u8, 0, 0, 0, 0, .. "mp42isom"u8];
        await File.WriteAllBytesAsync(Corrupt, [.. mp4Header, .. new byte[100_000]]);

        await File.WriteAllTextAsync(TextFile, string.Concat(Enumerable.Repeat("This is not a video. ", 500)));

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _server = builder.Build();
        _server.MapGet("/colors.mp4", (HttpRequest request) =>
        {
            lock (RangeRequests)
            {
                RangeRequests.Add(request.Headers.Range.ToString());
            }

            return Results.File(Colors, "video/mp4", enableRangeProcessing: true);
        });
        await _server.StartAsync();

        var address = _server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        // A query string like a signed URL has, to make sure ffmpeg receives it intact.
        ColorsUrl = $"{address}/colors.mp4?X-Amz-Signature=abc123&X-Amz-Expires=3600";
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        Directory.Delete(_directory, recursive: true);
    }
}
