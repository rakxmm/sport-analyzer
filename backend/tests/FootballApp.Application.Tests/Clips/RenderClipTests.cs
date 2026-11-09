using FootballApp.Application.Clips;
using FootballApp.Application.Clips.Commands.FailClip;
using FootballApp.Application.Clips.Commands.RenderClip;
using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Models;
using FootballApp.Application.Tests.Fakes;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using Microsoft.Extensions.Options;

namespace FootballApp.Application.Tests.Clips;

public class RenderClipTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly TimeSpan ShareLifetime = TimeSpan.FromDays(7);

    private readonly TestDbContext _db = TestDbContext.Create();
    private readonly FakeVideoStorage _storage = new();
    private readonly FakeVideoProcessor _processor = new();
    private readonly Match _match = TestData.ReadyMatch(durationSeconds: 100);

    public RenderClipTests()
    {
        _db.Matches.Add(_match);
        _db.SaveChanges();
    }

    private Clip AddClip(ClipType type, params Tag[] orderedTags)
    {
        var clip = TestData.Clip(_match, type, orderedTags);
        _db.Tags.AddRange(orderedTags);
        _db.Clips.Add(clip);
        _db.SaveChanges();
        return clip;
    }

    private Task Render(Guid clipId) =>
        new RenderClipCommandHandler(_db, _storage, _processor, Options.Create(new ClipOptions { ShareLinkLifetime = ShareLifetime }))
            .Handle(new RenderClipCommand(clipId), Ct);

    [Fact]
    public async Task Renders_the_clip_stores_it_and_opens_the_share_link()
    {
        var clip = AddClip(ClipType.SingleClip, TestData.Tag(_match, 50));
        var before = DateTimeOffset.UtcNow;

        await Render(clip.Id);

        var expectedKey = ClipRules.BuildStorageKey(_match.Id, clip.Id);
        Assert.Equal(ClipStatus.Done, clip.Status);
        Assert.Equal(expectedKey, clip.StorageKey);
        Assert.Null(clip.FailureReason);
        Assert.InRange(clip.ShareExpiresAt!.Value, before + ShareLifetime, DateTimeOffset.UtcNow + ShareLifetime);
        Assert.Equal([expectedKey], _storage.Objects.Keys);
        Assert.Equal([new ClipSegment(45, 53)], Assert.Single(_processor.Renders));
    }

    [Fact]
    public async Task Reel_is_cut_in_the_order_the_tags_were_selected()
    {
        var clip = AddClip(ClipType.HighlightReel, TestData.Tag(_match, 80), TestData.Tag(_match, 20), TestData.Tag(_match, 99));

        await Render(clip.Id);

        Assert.Equal(
            [new ClipSegment(75, 83), new ClipSegment(15, 23), new ClipSegment(94, 100)],
            Assert.Single(_processor.Renders));
    }

    [Fact]
    public async Task The_local_file_is_removed_after_the_upload()
    {
        var clip = AddClip(ClipType.SingleClip, TestData.Tag(_match, 50));

        await Render(clip.Id);

        Assert.False(File.Exists(Assert.Single(_processor.DeletedFiles)));
    }

    [Fact]
    public async Task Running_the_job_again_for_a_finished_clip_changes_nothing()
    {
        var clip = AddClip(ClipType.SingleClip, TestData.Tag(_match, 50));
        await Render(clip.Id);
        var expiresAt = clip.ShareExpiresAt;

        await Render(clip.Id);

        Assert.Equal(ClipStatus.Done, clip.Status);
        Assert.Equal(expiresAt, clip.ShareExpiresAt);
        Assert.Single(_processor.Renders);
        Assert.Equal(1, _storage.UploadCount);
        Assert.Single(_storage.Objects);
    }

    [Fact]
    public async Task A_retry_after_a_failed_upload_ends_with_exactly_one_object()
    {
        var clip = AddClip(ClipType.SingleClip, TestData.Tag(_match, 50));
        _storage.FailNextUpload = true;

        await Assert.ThrowsAsync<IOException>(() => Render(clip.Id));

        // Left in Processing for the queue to retry, with nothing stored and no scratch file left behind.
        Assert.Equal(ClipStatus.Processing, clip.Status);
        Assert.Null(clip.StorageKey);
        Assert.Null(clip.ShareExpiresAt);
        Assert.Empty(_storage.Objects);
        Assert.Single(_processor.DeletedFiles);

        await Render(clip.Id);

        Assert.Equal(ClipStatus.Done, clip.Status);
        Assert.Equal([ClipRules.BuildStorageKey(_match.Id, clip.Id)], _storage.Objects.Keys);
    }

    [Fact]
    public async Task A_temporary_ffmpeg_failure_is_left_for_the_queue_to_retry()
    {
        var clip = AddClip(ClipType.SingleClip, TestData.Tag(_match, 50));
        _processor.RenderError = new VideoProcessingException("connection reset");

        await Assert.ThrowsAsync<VideoProcessingException>(() => Render(clip.Id));
        Assert.Equal(ClipStatus.Processing, clip.Status);

        _processor.RenderError = null;
        await Render(clip.Id);

        Assert.Equal(ClipStatus.Done, clip.Status);
        Assert.Single(_storage.Objects);
    }

    [Fact]
    public async Task An_unreadable_source_fails_the_clip_straight_away()
    {
        var clip = AddClip(ClipType.SingleClip, TestData.Tag(_match, 50));
        _processor.RenderError = new InvalidVideoException("The match video could not be read.");

        await Render(clip.Id);

        Assert.Equal(ClipStatus.Failed, clip.Status);
        Assert.Equal("The match video could not be read.", clip.FailureReason);
        Assert.Null(clip.ShareExpiresAt);
        Assert.Empty(_storage.Objects);
    }

    [Fact]
    public async Task A_failed_clip_is_not_rendered_again()
    {
        var clip = AddClip(ClipType.SingleClip, TestData.Tag(_match, 50));
        _processor.RenderError = new InvalidVideoException("broken");
        await Render(clip.Id);
        _processor.RenderError = null;

        await Render(clip.Id);

        Assert.Equal(ClipStatus.Failed, clip.Status);
        Assert.Single(_processor.Renders);
    }

    [Fact]
    public async Task A_clip_that_is_not_saved_yet_is_retried_later()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Render(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_clip_fails_when_the_match_video_is_not_ready()
    {
        var clip = AddClip(ClipType.SingleClip, TestData.Tag(_match, 50));
        _match.VideoStatus = VideoStatus.Failed;
        await _db.SaveChangesAsync(Ct);

        await Render(clip.Id);

        Assert.Equal(ClipStatus.Failed, clip.Status);
        Assert.NotNull(clip.FailureReason);
        Assert.Empty(_processor.Renders);
    }

    [Fact]
    public async Task A_tag_outside_the_video_fails_the_clip_without_rendering()
    {
        var clip = AddClip(ClipType.SingleClip, TestData.Tag(_match, 500));

        await Render(clip.Id);

        Assert.Equal(ClipStatus.Failed, clip.Status);
        Assert.Contains("outside the video", clip.FailureReason);
        Assert.Empty(_processor.Renders);
        Assert.Empty(_storage.Objects);
    }

    [Theory]
    [InlineData(ClipStatus.Queued)]
    [InlineData(ClipStatus.Processing)]
    public async Task Giving_up_marks_an_unfinished_clip_failed(ClipStatus status)
    {
        var clip = AddClip(ClipType.SingleClip, TestData.Tag(_match, 50));
        clip.Status = status;
        await _db.SaveChangesAsync(Ct);

        await new FailClipCommandHandler(_db).Handle(new FailClipCommand(clip.Id), Ct);

        Assert.Equal(ClipStatus.Failed, clip.Status);
        Assert.Equal(FailClipCommandHandler.Reason, clip.FailureReason);
    }

    [Fact]
    public async Task Giving_up_leaves_a_finished_clip_alone()
    {
        var clip = AddClip(ClipType.SingleClip, TestData.Tag(_match, 50));
        await Render(clip.Id);

        await new FailClipCommandHandler(_db).Handle(new FailClipCommand(clip.Id), Ct);
        await new FailClipCommandHandler(_db).Handle(new FailClipCommand(Guid.NewGuid()), Ct);

        Assert.Equal(ClipStatus.Done, clip.Status);
        Assert.Null(clip.FailureReason);
    }
}
