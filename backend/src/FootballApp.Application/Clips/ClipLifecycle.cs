using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Clips;

/// <summary>
/// Clip status changes: Queued → Processing → Done / Failed (FR-14). Done and Failed are final, which is
/// what makes a retried or duplicated worker job harmless.
/// </summary>
public static class ClipLifecycle
{
    private const int MaxFailureReasonLength = 500;

    /// <summary>Processing counts too: it means an earlier attempt died mid-render and the job is being retried.</summary>
    public static bool IsUnfinished(Clip clip) => clip.Status is ClipStatus.Queued or ClipStatus.Processing;

    public static void MarkProcessing(Clip clip)
    {
        RequireUnfinished(clip, ClipStatus.Processing);
        clip.Status = ClipStatus.Processing;
    }

    /// <summary>The share link starts counting down the moment the clip exists (FR-15).</summary>
    public static void MarkDone(Clip clip, string storageKey, DateTimeOffset now, TimeSpan shareLinkLifetime)
    {
        if (clip.Status != ClipStatus.Processing)
        {
            throw new InvalidOperationException($"A clip cannot go from {clip.Status} to {ClipStatus.Done}.");
        }

        clip.Status = ClipStatus.Done;
        clip.StorageKey = storageKey;
        clip.ShareExpiresAt = now.Add(shareLinkLifetime);
        clip.FailureReason = null;
    }

    public static void MarkFailed(Clip clip, string reason)
    {
        RequireUnfinished(clip, ClipStatus.Failed);
        clip.Status = ClipStatus.Failed;
        clip.FailureReason = reason.Length > MaxFailureReasonLength ? reason[..MaxFailureReasonLength] : reason;
    }

    private static void RequireUnfinished(Clip clip, ClipStatus target)
    {
        if (!IsUnfinished(clip))
        {
            throw new InvalidOperationException($"A clip cannot go from {clip.Status} to {target}.");
        }
    }
}
