using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Models;
using FootballApp.Infrastructure.Processing;
using Microsoft.Extensions.Options;

namespace FootballApp.Infrastructure.Tests.Processing;

/// <summary>Integration tests: these run the real ffmpeg and ffprobe on short generated videos.</summary>
public class FfmpegVideoProcessorTests(SampleVideos samples) : IClassFixture<SampleVideos>
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly FfmpegVideoProcessor _processor = new(Options.Create(new ProcessingOptions()));

    private static void AssertRed((byte Red, byte Green, byte Blue) c) => Assert.True(c.Red > 150 && c.Green < 90 && c.Blue < 90, $"expected red, got {c}");
    private static void AssertGreen((byte Red, byte Green, byte Blue) c) => Assert.True(c.Green > 90 && c.Red < 60 && c.Blue < 60, $"expected green, got {c}");
    private static void AssertBlue((byte Red, byte Green, byte Blue) c) => Assert.True(c.Blue > 150 && c.Red < 90 && c.Green < 90, $"expected blue, got {c}");

    [Fact]
    public async Task Probe_reads_the_duration_and_the_tracks()
    {
        samples.SkipIfFfmpegIsMissing();

        var probe = await _processor.ProbeAsync(samples.Colors, Ct);

        Assert.InRange(probe.DurationSeconds, 17.9, 18.2);
        Assert.True(probe.HasVideo);
        Assert.True(probe.HasAudio);
    }

    [Fact]
    public async Task Probe_notices_a_video_without_sound()
    {
        samples.SkipIfFfmpegIsMissing();

        var probe = await _processor.ProbeAsync(samples.Silent, Ct);

        Assert.InRange(probe.DurationSeconds, 5.9, 6.1);
        Assert.True(probe.HasVideo);
        Assert.False(probe.HasAudio);
    }

    [Fact]
    public async Task Probe_rejects_a_corrupt_video_file()
    {
        samples.SkipIfFfmpegIsMissing();

        await Assert.ThrowsAsync<InvalidVideoException>(() => _processor.ProbeAsync(samples.Corrupt, Ct));
    }

    [Fact]
    public async Task Probe_finds_no_video_in_a_text_file()
    {
        samples.SkipIfFfmpegIsMissing();

        // ffprobe may guess some audio format for arbitrary bytes, so "no video track" is as valid an
        // answer as an outright rejection — either way the match ends up Failed.
        bool hasVideo;
        try
        {
            hasVideo = (await _processor.ProbeAsync(samples.TextFile, Ct)).HasVideo;
        }
        catch (InvalidVideoException)
        {
            hasVideo = false;
        }

        Assert.False(hasVideo);
    }

    [Fact]
    public async Task Probe_reports_a_missing_source_as_a_retryable_failure()
    {
        samples.SkipIfFfmpegIsMissing();

        var error = await Assert.ThrowsAsync<VideoProcessingException>(() =>
            _processor.ProbeAsync(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.mp4"), Ct));

        Assert.Contains("ffprobe", error.Message);
    }

    [Fact]
    public async Task Single_segment_becomes_a_streamable_h264_mp4_cut_at_the_exact_second()
    {
        samples.SkipIfFfmpegIsMissing();

        // 5–7 s straddles the red→green change at 6 s, which is not on a keyframe boundary of the cut.
        using var clip = await _processor.RenderAsync(samples.Colors, [new ClipSegment(5, 7)], Ct);

        var info = Ffmpeg.Inspect(clip.FilePath);
        Assert.InRange(info.DurationSeconds, 1.9, 2.15);
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal("aac", info.AudioCodec);
        Assert.Equal("yuv420p", info.PixelFormat);
        Assert.Equal((640, 360), (info.Width, info.Height));
        Assert.True(Ffmpeg.IsFastStart(clip.FilePath), "moov must come before mdat");

        AssertRed(Ffmpeg.ColorAt(clip.FilePath, 0.5));
        AssertGreen(Ffmpeg.ColorAt(clip.FilePath, 1.5));
    }

    [Fact]
    public async Task Reel_joins_the_segments_in_the_given_order()
    {
        samples.SkipIfFfmpegIsMissing();

        // blue (3 s), then red (2 s), then green (3 s) — deliberately not in chronological order.
        ClipSegment[] segments = [new(13, 16), new(1, 3), new(7, 10)];

        using var reel = await _processor.RenderAsync(samples.Colors, segments, Ct);

        var info = Ffmpeg.Inspect(reel.FilePath);
        Assert.InRange(info.DurationSeconds, 7.9, 8.3);
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal("aac", info.AudioCodec);
        Assert.True(Ffmpeg.IsFastStart(reel.FilePath), "moov must come before mdat");

        AssertBlue(Ffmpeg.ColorAt(reel.FilePath, 1.5));
        AssertRed(Ffmpeg.ColorAt(reel.FilePath, 4.0));
        AssertGreen(Ffmpeg.ColorAt(reel.FilePath, 6.5));
    }

    [Fact]
    public async Task Source_is_read_over_http_with_range_requests()
    {
        samples.SkipIfFfmpegIsMissing();

        ClipSegment[] segments = [new(14, 16), new(2, 4)];

        using var reel = await _processor.RenderAsync(samples.ColorsUrl, segments, Ct);

        var info = Ffmpeg.Inspect(reel.FilePath);
        Assert.InRange(info.DurationSeconds, 3.9, 4.3);
        AssertBlue(Ffmpeg.ColorAt(reel.FilePath, 1.0));
        AssertRed(Ffmpeg.ColorAt(reel.FilePath, 3.0));

        lock (samples.RangeRequests)
        {
            Assert.Contains(samples.RangeRequests, range => range.StartsWith("bytes=", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Video_without_sound_renders_without_an_audio_track()
    {
        samples.SkipIfFfmpegIsMissing();

        using var single = await _processor.RenderAsync(samples.Silent, [new ClipSegment(1, 3)], Ct);
        using var reel = await _processor.RenderAsync(samples.Silent, [new ClipSegment(4, 6), new ClipSegment(0, 2)], Ct);

        var singleInfo = Ffmpeg.Inspect(single.FilePath);
        Assert.Equal("h264", singleInfo.VideoCodec);
        Assert.Null(singleInfo.AudioCodec);
        Assert.InRange(singleInfo.DurationSeconds, 1.9, 2.1);

        var reelInfo = Ffmpeg.Inspect(reel.FilePath);
        Assert.Null(reelInfo.AudioCodec);
        Assert.InRange(reelInfo.DurationSeconds, 3.9, 4.1);
    }

    [Fact]
    public async Task Video_larger_than_full_hd_is_scaled_down()
    {
        samples.SkipIfFfmpegIsMissing();

        using var clip = await _processor.RenderAsync(samples.Large, [new ClipSegment(0, 1)], Ct);

        var info = Ffmpeg.Inspect(clip.FilePath);
        Assert.Equal((1920, 1080), (info.Width, info.Height));
    }

    [Fact]
    public async Task Portrait_video_keeps_its_shape_and_is_not_enlarged()
    {
        samples.SkipIfFfmpegIsMissing();

        using var clip = await _processor.RenderAsync(samples.Portrait, [new ClipSegment(0, 1)], Ct);

        var info = Ffmpeg.Inspect(clip.FilePath);
        Assert.Equal((720, 1280), (info.Width, info.Height));
    }

    [Fact]
    public async Task Location_and_other_source_metadata_are_not_copied_into_the_clip()
    {
        samples.SkipIfFfmpegIsMissing();
        Assert.Contains("location", Ffmpeg.Inspect(samples.Colors).Tags);

        using var single = await _processor.RenderAsync(samples.Colors, [new ClipSegment(1, 2)], Ct);
        using var reel = await _processor.RenderAsync(samples.Colors, [new ClipSegment(1, 2), new ClipSegment(8, 9)], Ct);

        foreach (var tags in new[] { Ffmpeg.Inspect(single.FilePath).Tags, Ffmpeg.Inspect(reel.FilePath).Tags })
        {
            Assert.DoesNotContain("location", tags);
            Assert.DoesNotContain("title", tags);
        }
    }

    [Fact]
    public async Task Scratch_files_are_removed_when_the_clip_is_disposed()
    {
        samples.SkipIfFfmpegIsMissing();

        var reel = await _processor.RenderAsync(samples.Colors, [new ClipSegment(1, 2), new ClipSegment(8, 9)], Ct);
        var directory = Path.GetDirectoryName(reel.FilePath)!;
        Assert.True(File.Exists(reel.FilePath));

        reel.Dispose();

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Scratch_files_of_a_killed_render_are_removed_at_startup()
    {
        var leftover = Directory.CreateTempSubdirectory("football-clip-").FullName;
        File.WriteAllBytes(Path.Combine(leftover, "segment-000.ts"), new byte[1024]);
        var unrelated = Directory.CreateTempSubdirectory("something-else-").FullName;

        try
        {
            FfmpegVideoProcessor.DeleteLeftoverScratchDirectories();

            Assert.False(Directory.Exists(leftover));
            Assert.True(Directory.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(unrelated, recursive: true);
        }
    }

    [Fact]
    public async Task Render_refuses_sources_without_a_picture()
    {
        samples.SkipIfFfmpegIsMissing();

        await Assert.ThrowsAsync<InvalidVideoException>(() =>
            _processor.RenderAsync(samples.Corrupt, [new ClipSegment(0, 1)], Ct));
        await Assert.ThrowsAsync<InvalidVideoException>(() =>
            _processor.RenderAsync(samples.TextFile, [new ClipSegment(0, 1)], Ct));
    }

    [Fact]
    public async Task A_segment_past_the_end_of_the_recording_fails_instead_of_producing_an_empty_clip()
    {
        samples.SkipIfFfmpegIsMissing();
        var scratchBefore = Directory.GetDirectories(Path.GetTempPath(), "football-clip-*");

        // The sample is 6 s long. ffmpeg itself exits cleanly here, with nothing encoded.
        await Assert.ThrowsAsync<InvalidVideoException>(() =>
            _processor.RenderAsync(samples.Silent, [new ClipSegment(100, 102)], Ct));
        await Assert.ThrowsAsync<InvalidVideoException>(() =>
            _processor.RenderAsync(samples.Silent, [new ClipSegment(1, 2), new ClipSegment(100, 102)], Ct));

        Assert.Equal(scratchBefore, Directory.GetDirectories(Path.GetTempPath(), "football-clip-*"));
    }
}
