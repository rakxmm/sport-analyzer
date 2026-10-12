using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Tags;

public record TagDto(
    Guid Id,
    Guid MatchId,
    TagType Type,
    int TimestampSeconds,
    int LeadInSeconds,
    int LeadOutSeconds,
    string? Note)
{
    public static TagDto FromEntity(Tag tag) => new(
        tag.Id,
        tag.MatchId,
        tag.Type,
        tag.TimestampSeconds,
        tag.LeadInSeconds,
        tag.LeadOutSeconds,
        tag.Note);
}
