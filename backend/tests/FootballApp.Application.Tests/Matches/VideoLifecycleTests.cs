using FootballApp.Application.Matches.VideoProcessing;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Tests.Matches;

public class VideoLifecycleTests
{
    private static Match MatchIn(VideoStatus status) => new() { VideoStatus = status };

    [Theory]
    [InlineData(VideoStatus.PendingUpload, false)]
    [InlineData(VideoStatus.Uploaded, true)]
    [InlineData(VideoStatus.Processing, true)]
    [InlineData(VideoStatus.Ready, false)]
    [InlineData(VideoStatus.Failed, false)]
    public void Only_uploaded_and_processing_videos_await_processing(VideoStatus status, bool expected)
    {
        Assert.Equal(expected, VideoLifecycle.IsAwaitingProcessing(MatchIn(status)));
    }

    [Theory]
    [InlineData(VideoStatus.Uploaded)]
    [InlineData(VideoStatus.Processing)]
    public void Processing_starts_from_uploaded_or_resumes_from_processing(VideoStatus from)
    {
        var match = MatchIn(from);

        VideoLifecycle.MarkProcessing(match);

        Assert.Equal(VideoStatus.Processing, match.VideoStatus);
    }

    [Theory]
    [InlineData(VideoStatus.PendingUpload)]
    [InlineData(VideoStatus.Ready)]
    [InlineData(VideoStatus.Failed)]
    public void Processing_cannot_start_from_any_other_status(VideoStatus from)
    {
        var match = MatchIn(from);

        Assert.Throws<InvalidOperationException>(() => VideoLifecycle.MarkProcessing(match));
        Assert.Equal(from, match.VideoStatus);
    }

    [Fact]
    public void Ready_stores_the_duration()
    {
        var match = MatchIn(VideoStatus.Processing);

        VideoLifecycle.MarkReady(match, 5400);

        Assert.Equal(VideoStatus.Ready, match.VideoStatus);
        Assert.Equal(5400, match.DurationSeconds);
        Assert.Null(match.VideoFailureReason);
    }

    [Theory]
    [InlineData(VideoStatus.PendingUpload)]
    [InlineData(VideoStatus.Uploaded)]
    [InlineData(VideoStatus.Ready)]
    [InlineData(VideoStatus.Failed)]
    public void Only_a_processing_video_can_become_ready(VideoStatus from)
    {
        var match = MatchIn(from);

        Assert.Throws<InvalidOperationException>(() => VideoLifecycle.MarkReady(match, 5400));
        Assert.Equal(from, match.VideoStatus);
        Assert.Null(match.DurationSeconds);
    }

    [Fact]
    public void A_ready_video_needs_a_real_duration()
    {
        var match = MatchIn(VideoStatus.Processing);

        Assert.Throws<ArgumentOutOfRangeException>(() => VideoLifecycle.MarkReady(match, 0));
        Assert.Equal(VideoStatus.Processing, match.VideoStatus);
    }

    [Theory]
    [InlineData(VideoStatus.Uploaded)]
    [InlineData(VideoStatus.Processing)]
    public void A_video_awaiting_processing_can_fail_with_a_reason(VideoStatus from)
    {
        var match = MatchIn(from);

        VideoLifecycle.MarkFailed(match, "Not a video.");

        Assert.Equal(VideoStatus.Failed, match.VideoStatus);
        Assert.Equal("Not a video.", match.VideoFailureReason);
    }

    [Theory]
    [InlineData(VideoStatus.PendingUpload)]
    [InlineData(VideoStatus.Ready)]
    [InlineData(VideoStatus.Failed)]
    public void Other_videos_cannot_fail(VideoStatus from)
    {
        var match = MatchIn(from);

        Assert.Throws<InvalidOperationException>(() => VideoLifecycle.MarkFailed(match, "late"));
        Assert.Equal(from, match.VideoStatus);
        Assert.Null(match.VideoFailureReason);
    }

    [Fact]
    public void Very_long_failure_reasons_fit_the_column()
    {
        var match = MatchIn(VideoStatus.Processing);

        VideoLifecycle.MarkFailed(match, new string('x', 5000));

        Assert.Equal(VideoLifecycle.MaxFailureReasonLength, match.VideoFailureReason!.Length);
    }
}
