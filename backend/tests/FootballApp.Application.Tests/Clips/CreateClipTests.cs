using FootballApp.Application.Clips;
using FootballApp.Application.Clips.Commands.CreateClip;
using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Tests.Fakes;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Tests.Clips;

public class CreateClipTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly TestDbContext _db = TestDbContext.Create();
    private readonly FakeProcessingQueue _queue = new();
    private readonly Match _match = TestData.ReadyMatch(durationSeconds: 5400);
    private readonly Tag _goal;
    private readonly Tag _save;

    public CreateClipTests()
    {
        _goal = TestData.Tag(_match, 1200);
        _save = TestData.Tag(_match, 300);
        _db.Matches.Add(_match);
        _db.Tags.AddRange(_goal, _save);
        _db.SaveChanges();
    }

    private Task<ClipDto> Create(ClipType type, IReadOnlyList<Guid> tagIds, string userId = TestData.OwnerId) =>
        new CreateClipCommandHandler(_db, new FakeCurrentUser(userId), _queue)
            .Handle(new CreateClipCommand(_match.Id, type, tagIds), Ct);

    [Fact]
    public async Task Single_clip_is_saved_and_queued()
    {
        var result = await Create(ClipType.SingleClip, [_goal.Id]);

        Assert.Equal(ClipStatus.Queued, result.Status);
        Assert.Null(result.ShareExpiresAt);
        Assert.Equal([result.Id], _queue.ClipIds);
        Assert.Single(_db.Clips);
    }

    [Fact]
    public async Task Reel_remembers_the_order_the_tags_were_selected_in()
    {
        var result = await Create(ClipType.HighlightReel, [_goal.Id, _save.Id]);

        var order = await _db.ClipTags
            .Where(ct => ct.ClipId == result.Id)
            .OrderBy(ct => ct.Order)
            .Select(ct => ct.TagId)
            .ToListAsync(Ct);
        Assert.Equal([_goal.Id, _save.Id], order);
    }

    [Fact]
    public async Task Other_users_cannot_create_clips_for_the_match()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Create(ClipType.SingleClip, [_goal.Id], TestData.IntruderId));

        Assert.Empty(_queue.ClipIds);
        Assert.Empty(_db.Clips);
    }

    [Theory]
    [InlineData(VideoStatus.PendingUpload)]
    [InlineData(VideoStatus.Uploaded)]
    [InlineData(VideoStatus.Processing)]
    [InlineData(VideoStatus.Failed)]
    public async Task Clips_need_a_ready_video(VideoStatus status)
    {
        _match.VideoStatus = status;
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(ClipType.SingleClip, [_goal.Id]));

        Assert.Empty(_queue.ClipIds);
        Assert.Empty(_db.Clips);
    }

    [Fact]
    public async Task Tags_beyond_the_end_of_the_video_are_rejected()
    {
        var late = TestData.Tag(_match, 5401);
        _db.Tags.Add(late);
        await _db.SaveChangesAsync(Ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(ClipType.HighlightReel, [_goal.Id, late.Id]));

        Assert.Contains("outside the video", error.Message);
        Assert.Empty(_queue.ClipIds);
        Assert.Empty(_db.Clips);
    }

    [Fact]
    public async Task Tags_of_another_match_are_rejected()
    {
        var otherMatch = TestData.ReadyMatch();
        var foreignTag = TestData.Tag(otherMatch, 10);
        _db.Matches.Add(otherMatch);
        _db.Tags.Add(foreignTag);
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(ClipType.HighlightReel, [_goal.Id, foreignTag.Id]));
    }

    [Fact]
    public async Task The_same_tag_twice_is_rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(ClipType.HighlightReel, [_goal.Id, _goal.Id]));
    }

    [Fact]
    public async Task No_tags_are_rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(ClipType.HighlightReel, []));
    }

    [Fact]
    public async Task A_single_clip_from_two_tags_is_rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(ClipType.SingleClip, [_goal.Id, _save.Id]));
    }

    [Fact]
    public async Task Nothing_is_saved_when_the_clip_cannot_be_queued()
    {
        _queue.Fail = true;

        await Assert.ThrowsAsync<IOException>(() => Create(ClipType.SingleClip, [_goal.Id]));

        Assert.Empty(_db.Clips);
    }
}
