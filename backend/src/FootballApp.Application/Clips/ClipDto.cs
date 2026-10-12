using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Clips;

public record ClipDto(
    Guid Id,
    Guid MatchId,
    ClipType Type,
    ClipStatus Status,
    string ShareToken,
    DateTimeOffset? ShareExpiresAt)
{
    public static ClipDto FromEntity(Clip clip) => new(
        clip.Id,
        clip.MatchId,
        clip.Type,
        clip.Status,
        clip.ShareToken,
        clip.ShareExpiresAt);
}
