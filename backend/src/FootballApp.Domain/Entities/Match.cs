using FootballApp.Domain.Common;
using FootballApp.Domain.Enums;

namespace FootballApp.Domain.Entities;

public class Match : BaseEntity
{
    /// <summary>Clerk user ID (subject claim) of the account that owns this match.</summary>
    public string OwnerId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public DateOnly MatchDate { get; set; }

    public VideoStatus VideoStatus { get; set; } = VideoStatus.PendingUpload;

    /// <summary>Object key of the raw video in R2, once uploaded.</summary>
    public string? VideoStorageKey { get; set; }
    public int? DurationSeconds { get; set; }

    /// <summary>Upload-time consent attestation (FR-18) — required before VideoStatus can leave PendingUpload.</summary>
    public bool ConsentConfirmed { get; set; }
    public DateTimeOffset? ConsentConfirmedAt { get; set; }

    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public ICollection<Clip> Clips { get; set; } = new List<Clip>();
}
