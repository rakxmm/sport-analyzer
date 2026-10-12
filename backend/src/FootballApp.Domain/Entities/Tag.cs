using FootballApp.Domain.Common;
using FootballApp.Domain.Enums;

namespace FootballApp.Domain.Entities;

public class Tag : BaseEntity
{
    public Guid MatchId { get; set; }
    public Match? Match { get; set; }

    public TagType Type { get; set; }
    public int TimestampSeconds { get; set; }

    /// <summary>Seconds to include before the timestamp when a clip is generated (FR-11).</summary>
    public int LeadInSeconds { get; set; } = 5;
    /// <summary>Seconds to include after the timestamp when a clip is generated (FR-11).</summary>
    public int LeadOutSeconds { get; set; } = 3;

    public string? Note { get; set; }
}
