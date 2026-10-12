using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Matches;

public record MatchDto(
    Guid Id,
    string Title,
    DateOnly MatchDate,
    VideoStatus VideoStatus,
    int? DurationSeconds,
    string? VideoFileName,
    long? VideoSizeBytes,
    bool UploadInProgress,
    DateTimeOffset CreatedAt)
{
    public static MatchDto FromEntity(Match match) => new(
        match.Id,
        match.Title,
        match.MatchDate,
        match.VideoStatus,
        match.DurationSeconds,
        match.VideoFileName,
        match.VideoSizeBytes,
        match.UploadId != null,
        match.CreatedAt);
}
