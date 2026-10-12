using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Matches.Commands.AbortVideoUpload;
using FootballApp.Application.Matches.Commands.CompleteVideoUpload;
using FootballApp.Application.Matches.Commands.StartVideoUpload;
using FootballApp.Application.Matches.Queries.GetMatchVideoUrl;
using FootballApp.Application.Matches.Queries.GetVideoPartUploadUrl;
using FootballApp.Application.Matches.VideoUpload;
using FootballApp.Application.Tests.Fakes;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Tests.Matches;

public class VideoUploadTests
{
    private const string OwnerId = "user_owner";
    private const long MiB = 1024 * 1024;
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly TestDbContext _db = TestDbContext.Create();
    private readonly FakeVideoStorage _storage = new();
    private readonly Match _match;

    public VideoUploadTests()
    {
        _match = new Match
        {
            OwnerId = OwnerId,
            Title = "Derby",
            MatchDate = new DateOnly(2026, 10, 1),
            ConsentConfirmed = true,
            ConsentConfirmedAt = DateTimeOffset.UtcNow
        };
        _db.Matches.Add(_match);
        _db.SaveChanges();
    }

    private Task<UploadSessionDto> Start(string fileName = "derby.mp4", long sizeBytes = 100 * MiB, string userId = OwnerId, string contentType = "video/mp4") =>
        new StartVideoUploadCommandHandler(_db, new FakeCurrentUser(userId), _storage)
            .Handle(new StartVideoUploadCommand(_match.Id, fileName, contentType, sizeBytes), Ct);

    private Task<Application.Matches.MatchDto> Complete() =>
        new CompleteVideoUploadCommandHandler(_db, new FakeCurrentUser(OwnerId), _storage)
            .Handle(new CompleteVideoUploadCommand(_match.Id), Ct);

    private void UploadAllParts(UploadSessionDto session)
    {
        for (var part = 1; part <= session.PartCount; part++)
        {
            var size = part < session.PartCount
                ? session.PartSizeBytes
                : session.SizeBytes - session.PartSizeBytes * (session.PartCount - 1);
            _storage.AddPart(_match.UploadId!, part, size);
        }
    }

    [Fact]
    public async Task Start_creates_multipart_upload_and_stores_its_state()
    {
        var session = await Start();

        Assert.Equal(32 * MiB, session.PartSizeBytes);
        Assert.Equal(4, session.PartCount);
        Assert.Empty(session.CompletedPartNumbers);
        Assert.NotNull(_match.UploadId);
        Assert.Equal($"matches/{_match.Id:N}/source.mp4", _match.VideoStorageKey);
        Assert.Equal("derby.mp4", _match.VideoFileName);
        Assert.Equal(VideoStatus.PendingUpload, _match.VideoStatus);
    }

    [Fact]
    public async Task Start_rejects_other_users()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Start(userId: "user_intruder"));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("")]
    public async Task Start_rejects_non_video_files(string contentType)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Start(contentType: contentType));
    }

    [Fact]
    public async Task Start_rejects_files_over_the_size_limit()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Start(sizeBytes: VideoUploadRules.MaxVideoSizeBytes + 1));
    }

    [Fact]
    public async Task Start_rejects_matches_without_consent()
    {
        _match.ConsentConfirmed = false;
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Start());
    }

    [Fact]
    public async Task Start_with_the_same_file_resumes_and_reports_completed_parts()
    {
        await Start();
        var uploadId = _match.UploadId!;
        _storage.AddPart(uploadId, 2, 32 * MiB);
        _storage.AddPart(uploadId, 1, 32 * MiB);

        var resumed = await Start();

        Assert.Equal(uploadId, _match.UploadId);
        Assert.Equal([1, 2], resumed.CompletedPartNumbers);
        Assert.Empty(_storage.AbortedUploadIds);
    }

    [Fact]
    public async Task Start_with_a_different_file_aborts_the_old_upload()
    {
        await Start();
        var oldUploadId = _match.UploadId!;

        await Start(fileName: "other.mov", sizeBytes: 50 * MiB);

        Assert.Contains(oldUploadId, _storage.AbortedUploadIds);
        Assert.NotEqual(oldUploadId, _match.UploadId);
        Assert.Equal("other.mov", _match.VideoFileName);
    }

    [Fact]
    public async Task Start_restarts_when_the_old_upload_expired()
    {
        await Start();
        var expiredUploadId = _match.UploadId!;
        _storage.Uploads.Remove(expiredUploadId);

        var session = await Start();

        Assert.NotEqual(expiredUploadId, _match.UploadId);
        Assert.Empty(session.CompletedPartNumbers);
    }

    [Fact]
    public async Task Start_is_rejected_once_the_video_is_uploaded()
    {
        UploadAllParts(await Start());
        await Complete();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Start());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Part_url_rejects_part_numbers_outside_the_upload(int partNumber)
    {
        await Start();
        var handler = new GetVideoPartUploadUrlQueryHandler(_db, new FakeCurrentUser(OwnerId), _storage);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new GetVideoPartUploadUrlQuery(_match.Id, partNumber), Ct));
    }

    [Fact]
    public async Task Part_url_is_signed_for_the_requested_part()
    {
        await Start();
        var handler = new GetVideoPartUploadUrlQueryHandler(_db, new FakeCurrentUser(OwnerId), _storage);

        var result = await handler.Handle(new GetVideoPartUploadUrlQuery(_match.Id, 4), Ct);

        Assert.Contains("partNumber=4", result.Url);
    }

    [Fact]
    public async Task Complete_marks_the_video_uploaded()
    {
        UploadAllParts(await Start());

        var result = await Complete();

        Assert.Equal(VideoStatus.Uploaded, result.VideoStatus);
        Assert.Null(_match.UploadId);
        Assert.Equal(100 * MiB, _storage.Objects[_match.VideoStorageKey!]);
    }

    [Fact]
    public async Task Complete_rejects_missing_parts()
    {
        await Start();
        _storage.AddPart(_match.UploadId!, 1, 32 * MiB);

        await Assert.ThrowsAsync<InvalidOperationException>(Complete);
        Assert.Equal(VideoStatus.PendingUpload, _match.VideoStatus);
    }

    [Fact]
    public async Task Complete_rejects_parts_that_do_not_add_up_to_the_file_size()
    {
        await Start();
        for (var part = 1; part <= 4; part++)
        {
            _storage.AddPart(_match.UploadId!, part, 32 * MiB);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(Complete);
    }

    [Fact]
    public async Task Complete_recovers_when_the_upload_was_already_assembled()
    {
        UploadAllParts(await Start());
        await _storage.CompleteMultipartUploadAsync(_match.VideoStorageKey!, _match.UploadId!,
            (await _storage.ListUploadedPartsAsync(_match.VideoStorageKey!, _match.UploadId!, Ct))!, Ct);

        var result = await Complete();

        Assert.Equal(VideoStatus.Uploaded, result.VideoStatus);
    }

    [Fact]
    public async Task Complete_clears_an_expired_upload()
    {
        await Start();
        _storage.Uploads.Remove(_match.UploadId!);

        await Assert.ThrowsAsync<InvalidOperationException>(Complete);
        Assert.Null(_match.UploadId);
        Assert.Null(_match.VideoFileName);
    }

    [Fact]
    public async Task Abort_discards_parts_and_clears_upload_state()
    {
        await Start();
        var uploadId = _match.UploadId!;
        var handler = new AbortVideoUploadCommandHandler(_db, new FakeCurrentUser(OwnerId), _storage);

        var result = await handler.Handle(new AbortVideoUploadCommand(_match.Id), Ct);

        Assert.Contains(uploadId, _storage.AbortedUploadIds);
        Assert.False(result.UploadInProgress);
        Assert.Null(_match.VideoStorageKey);
    }

    [Fact]
    public async Task Video_url_requires_a_finished_upload()
    {
        await Start();
        var handler = new GetMatchVideoUrlQueryHandler(_db, new FakeCurrentUser(OwnerId), _storage);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new GetMatchVideoUrlQuery(_match.Id), Ct));
    }

    [Fact]
    public async Task Video_url_is_signed_for_the_owner_only()
    {
        UploadAllParts(await Start());
        await Complete();

        var url = await new GetMatchVideoUrlQueryHandler(_db, new FakeCurrentUser(OwnerId), _storage)
            .Handle(new GetMatchVideoUrlQuery(_match.Id), Ct);
        Assert.Contains("signed", url.Url);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            new GetMatchVideoUrlQueryHandler(_db, new FakeCurrentUser("user_intruder"), _storage)
                .Handle(new GetMatchVideoUrlQuery(_match.Id), Ct));
    }
}
