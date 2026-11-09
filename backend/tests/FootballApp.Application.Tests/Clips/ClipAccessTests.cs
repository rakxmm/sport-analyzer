using FootballApp.Application.Clips.Queries.GetClipDownloadUrl;
using FootballApp.Application.Clips.Queries.GetClipsForMatch;
using FootballApp.Application.Clips.Queries.GetSharedClip;
using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Tests.Fakes;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Tests.Clips;

/// <summary>Who may see, download and open a clip (FR-5, FR-15, FR-16).</summary>
public class ClipAccessTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly TestDbContext _db = TestDbContext.Create();
    private readonly FakeVideoStorage _storage = new();
    private readonly Match _match = TestData.ReadyMatch();
    private readonly Clip _clip;

    public ClipAccessTests()
    {
        var tag = TestData.Tag(_match, 100);
        _clip = TestData.Clip(_match, ClipType.SingleClip, tag);
        _clip.Status = ClipStatus.Done;
        _clip.StorageKey = $"matches/{_match.Id:N}/clips/{_clip.Id:N}.mp4";
        _clip.ShareExpiresAt = DateTimeOffset.UtcNow.AddDays(30);

        _db.Matches.Add(_match);
        _db.Tags.Add(tag);
        _db.Clips.Add(_clip);
        _db.SaveChanges();
    }

    private Task<ClipDownloadUrlDto> Download(string userId) =>
        new GetClipDownloadUrlQueryHandler(_db, new FakeCurrentUser(userId), _storage)
            .Handle(new GetClipDownloadUrlQuery(_clip.Id), Ct);

    private Task<SharedClipDto> OpenShareLink(string? token = null) =>
        new GetSharedClipQueryHandler(_db, _storage).Handle(new GetSharedClipQuery(token ?? _clip.ShareToken), Ct);

    [Fact]
    public async Task Owner_sees_the_clips_of_the_match()
    {
        var clips = await new GetClipsForMatchQueryHandler(_db, new FakeCurrentUser(TestData.OwnerId))
            .Handle(new GetClipsForMatchQuery(_match.Id), Ct);

        var clip = Assert.Single(clips);
        Assert.Equal(_clip.Id, clip.Id);
        Assert.Equal(_clip.ShareExpiresAt, clip.ShareExpiresAt);
    }

    [Fact]
    public async Task Other_users_cannot_list_the_clips()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            new GetClipsForMatchQueryHandler(_db, new FakeCurrentUser(TestData.IntruderId))
                .Handle(new GetClipsForMatchQuery(_match.Id), Ct));
    }

    [Fact]
    public async Task Owner_gets_a_download_link_with_a_file_name()
    {
        var result = await Download(TestData.OwnerId);

        Assert.Equal("derby-clip.mp4", result.FileName);
        Assert.Contains(_clip.StorageKey!, result.Url);
        Assert.Contains("download=derby-clip.mp4", result.Url);
        Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Other_users_cannot_download_the_clip()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Download(TestData.IntruderId));
    }

    [Fact]
    public async Task Owner_can_still_download_after_the_share_link_expired()
    {
        _clip.ShareExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync(Ct);

        var result = await Download(TestData.OwnerId);

        Assert.NotEmpty(result.Url);
    }

    [Theory]
    [InlineData(ClipStatus.Queued)]
    [InlineData(ClipStatus.Processing)]
    [InlineData(ClipStatus.Failed)]
    public async Task Unfinished_clips_cannot_be_downloaded(ClipStatus status)
    {
        _clip.Status = status;
        _clip.StorageKey = null;
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Download(TestData.OwnerId));
    }

    [Fact]
    public async Task Share_link_plays_without_logging_in()
    {
        var shared = await OpenShareLink();

        Assert.Equal(ClipStatus.Done, shared.Status);
        Assert.Contains(_clip.StorageKey!, shared.PlaybackUrl);
        Assert.Equal(_clip.ShareExpiresAt, shared.ExpiresAt);
    }

    [Fact]
    public async Task Expired_share_link_is_not_found()
    {
        _clip.ShareExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<NotFoundException>(() => OpenShareLink());
        Assert.Empty(_storage.SignedUrlLifetimes);
    }

    [Fact]
    public async Task Unknown_share_token_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => OpenShareLink("not-a-real-token"));
    }

    [Fact]
    public async Task Playback_url_lasts_an_hour_at_most()
    {
        await OpenShareLink();

        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(_storage.SignedUrlLifetimes));
    }

    [Fact]
    public async Task Playback_url_does_not_outlive_the_share_link()
    {
        _clip.ShareExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        await _db.SaveChangesAsync(Ct);

        await OpenShareLink();

        Assert.InRange(Assert.Single(_storage.SignedUrlLifetimes), TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Share_link_of_an_unfinished_clip_has_nothing_to_play()
    {
        _clip.Status = ClipStatus.Processing;
        _clip.StorageKey = null;
        _clip.ShareExpiresAt = null;
        await _db.SaveChangesAsync(Ct);

        var shared = await OpenShareLink();

        Assert.Null(shared.PlaybackUrl);
        Assert.Empty(_storage.SignedUrlLifetimes);
    }
}
