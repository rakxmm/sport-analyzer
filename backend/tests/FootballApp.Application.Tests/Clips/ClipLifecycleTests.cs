using FootballApp.Application.Clips;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Tests.Clips;

public class ClipLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private static Clip ClipIn(ClipStatus status) => new() { Status = status };

    [Theory]
    [InlineData(ClipStatus.Queued, true)]
    [InlineData(ClipStatus.Processing, true)]
    [InlineData(ClipStatus.Done, false)]
    [InlineData(ClipStatus.Failed, false)]
    public void Only_queued_and_processing_clips_are_unfinished(ClipStatus status, bool expected)
    {
        Assert.Equal(expected, ClipLifecycle.IsUnfinished(ClipIn(status)));
    }

    [Theory]
    [InlineData(ClipStatus.Queued)]
    [InlineData(ClipStatus.Processing)]
    public void Processing_starts_from_queued_or_resumes_from_processing(ClipStatus from)
    {
        var clip = ClipIn(from);

        ClipLifecycle.MarkProcessing(clip);

        Assert.Equal(ClipStatus.Processing, clip.Status);
    }

    [Theory]
    [InlineData(ClipStatus.Done)]
    [InlineData(ClipStatus.Failed)]
    public void Finished_clips_cannot_be_processed_again(ClipStatus from)
    {
        var clip = ClipIn(from);

        Assert.Throws<InvalidOperationException>(() => ClipLifecycle.MarkProcessing(clip));
        Assert.Equal(from, clip.Status);
    }

    [Fact]
    public void Done_stores_the_file_and_starts_the_share_link_countdown()
    {
        var clip = ClipIn(ClipStatus.Processing);

        ClipLifecycle.MarkDone(clip, "matches/a/clips/b.mp4", Now, Lifetime);

        Assert.Equal(ClipStatus.Done, clip.Status);
        Assert.Equal("matches/a/clips/b.mp4", clip.StorageKey);
        Assert.Equal(Now.AddDays(30), clip.ShareExpiresAt);
        Assert.Null(clip.FailureReason);
    }

    [Theory]
    [InlineData(ClipStatus.Queued)]
    [InlineData(ClipStatus.Done)]
    [InlineData(ClipStatus.Failed)]
    public void Only_a_processing_clip_can_become_done(ClipStatus from)
    {
        var clip = ClipIn(from);

        Assert.Throws<InvalidOperationException>(() => ClipLifecycle.MarkDone(clip, "key", Now, Lifetime));
        Assert.Equal(from, clip.Status);
        Assert.Null(clip.StorageKey);
        Assert.Null(clip.ShareExpiresAt);
    }

    [Theory]
    [InlineData(ClipStatus.Queued)]
    [InlineData(ClipStatus.Processing)]
    public void Unfinished_clips_can_fail_with_a_reason(ClipStatus from)
    {
        var clip = ClipIn(from);

        ClipLifecycle.MarkFailed(clip, "Something broke.");

        Assert.Equal(ClipStatus.Failed, clip.Status);
        Assert.Equal("Something broke.", clip.FailureReason);
        Assert.Null(clip.ShareExpiresAt);
    }

    [Theory]
    [InlineData(ClipStatus.Done)]
    [InlineData(ClipStatus.Failed)]
    public void Finished_clips_cannot_fail_afterwards(ClipStatus from)
    {
        var clip = ClipIn(from);

        Assert.Throws<InvalidOperationException>(() => ClipLifecycle.MarkFailed(clip, "late"));
        Assert.Equal(from, clip.Status);
        Assert.Null(clip.FailureReason);
    }

    [Fact]
    public void Very_long_failure_reasons_are_cut_short()
    {
        var clip = ClipIn(ClipStatus.Processing);

        ClipLifecycle.MarkFailed(clip, new string('x', 5000));

        Assert.Equal(500, clip.FailureReason!.Length);
    }
}
