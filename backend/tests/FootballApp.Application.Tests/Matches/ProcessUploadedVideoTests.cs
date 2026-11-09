using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Models;
using FootballApp.Application.Matches.Commands.FailVideoProcessing;
using FootballApp.Application.Matches.Commands.ProcessUploadedVideo;
using FootballApp.Application.Tests.Fakes;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Tests.Matches;

public class ProcessUploadedVideoTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly TestDbContext _db = TestDbContext.Create();
    private readonly FakeVideoStorage _storage = new();
    private readonly FakeVideoProcessor _processor = new();
    private readonly Match _match = TestData.UploadedMatch();

    public ProcessUploadedVideoTests()
    {
        _db.Matches.Add(_match);
        _db.SaveChanges();
    }

    private Task Process(Guid? matchId = null) =>
        new ProcessUploadedVideoCommandHandler(_db, _storage, _processor)
            .Handle(new ProcessUploadedVideoCommand(matchId ?? _match.Id), Ct);

    private Task GiveUp() =>
        new FailVideoProcessingCommandHandler(_db).Handle(new FailVideoProcessingCommand(_match.Id), Ct);

    [Fact]
    public async Task Uploaded_video_becomes_ready_with_its_duration_in_whole_seconds()
    {
        _processor.ProbeResult = new VideoProbeResult(5400.7, HasVideo: true, HasAudio: true);

        await Process();

        Assert.Equal(VideoStatus.Ready, _match.VideoStatus);
        Assert.Equal(5400, _match.DurationSeconds);
        Assert.Null(_match.VideoFailureReason);
    }

    [Fact]
    public async Task A_file_that_is_not_a_video_fails_with_a_reason_for_the_owner()
    {
        _processor.ProbeError = new InvalidVideoException("The uploaded file is not a playable video.");

        await Process();

        Assert.Equal(VideoStatus.Failed, _match.VideoStatus);
        Assert.Equal("The uploaded file is not a playable video.", _match.VideoFailureReason);
        Assert.Null(_match.DurationSeconds);
    }

    [Fact]
    public async Task A_file_with_sound_only_fails()
    {
        _processor.ProbeResult = new VideoProbeResult(180, HasVideo: false, HasAudio: true);

        await Process();

        Assert.Equal(VideoStatus.Failed, _match.VideoStatus);
        Assert.NotNull(_match.VideoFailureReason);
    }

    [Fact]
    public async Task A_video_shorter_than_a_second_fails()
    {
        _processor.ProbeResult = new VideoProbeResult(0.04, HasVideo: true, HasAudio: false);

        await Process();

        Assert.Equal(VideoStatus.Failed, _match.VideoStatus);
    }

    [Fact]
    public async Task A_temporary_failure_is_left_for_the_queue_to_retry()
    {
        _processor.ProbeError = new VideoProcessingException("connection reset");

        await Assert.ThrowsAsync<VideoProcessingException>(() => Process());
        Assert.Equal(VideoStatus.Processing, _match.VideoStatus);

        _processor.ProbeError = null;
        await Process();

        Assert.Equal(VideoStatus.Ready, _match.VideoStatus);
    }

    [Theory]
    [InlineData(VideoStatus.Ready)]
    [InlineData(VideoStatus.Failed)]
    public async Task Running_the_job_again_for_a_finished_video_changes_nothing(VideoStatus status)
    {
        int? duration = status == VideoStatus.Ready ? 90 : null;
        _match.VideoStatus = status;
        _match.DurationSeconds = duration;
        await _db.SaveChangesAsync(Ct);

        await Process();

        Assert.Equal(status, _match.VideoStatus);
        Assert.Equal(duration, _match.DurationSeconds);
        Assert.Equal(0, _processor.ProbeCount);
    }

    [Fact]
    public async Task A_job_that_overtakes_the_upload_being_saved_is_retried_later()
    {
        _match.VideoStatus = VideoStatus.PendingUpload;
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Process());

        Assert.Equal(VideoStatus.PendingUpload, _match.VideoStatus);
        Assert.Equal(0, _processor.ProbeCount);
    }

    [Fact]
    public async Task A_deleted_match_is_ignored()
    {
        await Process(Guid.NewGuid());

        Assert.Equal(0, _processor.ProbeCount);
    }

    [Theory]
    [InlineData(VideoStatus.Uploaded)]
    [InlineData(VideoStatus.Processing)]
    public async Task Giving_up_marks_the_video_failed(VideoStatus status)
    {
        _match.VideoStatus = status;
        await _db.SaveChangesAsync(Ct);

        await GiveUp();

        Assert.Equal(VideoStatus.Failed, _match.VideoStatus);
        Assert.Equal(FailVideoProcessingCommandHandler.Reason, _match.VideoFailureReason);
    }

    [Theory]
    [InlineData(VideoStatus.PendingUpload)]
    [InlineData(VideoStatus.Ready)]
    public async Task Giving_up_leaves_other_videos_alone(VideoStatus status)
    {
        _match.VideoStatus = status;
        await _db.SaveChangesAsync(Ct);

        await GiveUp();

        Assert.Equal(status, _match.VideoStatus);
        Assert.Null(_match.VideoFailureReason);
    }
}
