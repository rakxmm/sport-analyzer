using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Matches;

public record MatchDto(
    Guid Id,
    string Title,
    DateOnly MatchDate,
    VideoStatus VideoStatus,
    int? DurationSeconds,
    DateTimeOffset CreatedAt)
{
    public static MatchDto FromEntity(Match match) => new(
        match.Id,
        match.Title,
        match.MatchDate,
        match.VideoStatus,
        match.DurationSeconds,
        match.CreatedAt);
}
