using FootballApp.Domain.Common;
using FootballApp.Domain.Enums;

namespace FootballApp.Domain.Entities;

public class Clip : BaseEntity
{
    public Guid MatchId { get; set; }
    public Match? Match { get; set; }

    public ClipType Type { get; set; }
    public ClipStatus Status { get; set; } = ClipStatus.Queued;

    /// <summary>Tags included in this clip — a single tag for SingleClip, several for a HighlightReel.</summary>
    public ICollection<ClipTag> ClipTags { get; set; } = new List<ClipTag>();

    /// <summary>Object key of the rendered clip in R2, once processing completes.</summary>
    public string? StorageKey { get; set; }

    /// <summary>Opaque token used to build the public, unauthenticated share link (FR-16).</summary>
    public string ShareToken { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? ShareExpiresAt { get; set; }

    public string? FailureReason { get; set; }
}
