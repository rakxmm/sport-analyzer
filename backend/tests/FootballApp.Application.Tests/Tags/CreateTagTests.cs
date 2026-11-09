using FootballApp.Application.Clips;
using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Tags;
using FootballApp.Application.Tags.Commands.CreateTag;
using FootballApp.Application.Tests.Fakes;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Tests.Tags;

public class CreateTagTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly TestDbContext _db = TestDbContext.Create();
    private readonly Match _match = TestData.ReadyMatch(durationSeconds: 5400);

    public CreateTagTests()
    {
        _db.Matches.Add(_match);
        _db.SaveChanges();
    }

    private Task<TagDto> Create(int timestamp, int leadIn = 5, int leadOut = 3, string userId = TestData.OwnerId) =>
        new CreateTagCommandHandler(_db, new FakeCurrentUser(userId))
            .Handle(new CreateTagCommand(_match.Id, TagType.Goal, timestamp, leadIn, leadOut, null), Ct);

    [Theory]
    [InlineData(0)]
    [InlineData(5400)]
    public async Task Tags_within_the_video_are_saved(int timestamp)
    {
        var tag = await Create(timestamp);

        Assert.Equal(timestamp, tag.TimestampSeconds);
        Assert.Single(_db.Tags);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5401)]
    public async Task Tags_outside_the_video_are_rejected(int timestamp)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(timestamp));

        Assert.Empty(_db.Tags);
    }

    [Fact]
    public async Task Any_time_is_accepted_while_the_video_length_is_still_unknown()
    {
        _match.VideoStatus = VideoStatus.Uploaded;
        _match.DurationSeconds = null;
        await _db.SaveChangesAsync(Ct);

        var tag = await Create(99_999);

        Assert.Equal(99_999, tag.TimestampSeconds);
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(5, -1)]
    [InlineData(ClipRules.MaxLeadSeconds + 1, 3)]
    [InlineData(5, ClipRules.MaxLeadSeconds + 1)]
    public async Task Lead_in_and_lead_out_must_be_sensible(int leadIn, int leadOut)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(100, leadIn, leadOut));
    }

    [Fact]
    public async Task Other_users_cannot_tag_the_match()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Create(100, userId: TestData.IntruderId));
    }
}
