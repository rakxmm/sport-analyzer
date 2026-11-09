using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Matches.VideoProcessing;

/// <summary>
/// Match video status after the upload: Uploaded → Processing → Ready / Failed. Ready and Failed are
/// final, which is what makes a retried or duplicated worker job harmless.
/// </summary>
public static class VideoLifecycle
{
    public const int MaxFailureReasonLength = 500;

    /// <summary>Processing counts too: it means an earlier attempt died halfway and the job is being retried.</summary>
    public static bool IsAwaitingProcessing(Match match) =>
        match.VideoStatus is VideoStatus.Uploaded or VideoStatus.Processing;

    public static void MarkProcessing(Match match)
    {
        RequireAwaitingProcessing(match, VideoStatus.Processing);
        match.VideoStatus = VideoStatus.Processing;
    }

    public static void MarkReady(Match match, int durationSeconds)
    {
        if (match.VideoStatus != VideoStatus.Processing)
        {
            throw new InvalidOperationException($"A match video cannot go from {match.VideoStatus} to {VideoStatus.Ready}.");
        }

        if (durationSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "A ready video is at least one second long.");
        }

        match.VideoStatus = VideoStatus.Ready;
        match.DurationSeconds = durationSeconds;
        match.VideoFailureReason = null;
    }

    public static void MarkFailed(Match match, string reason)
    {
        RequireAwaitingProcessing(match, VideoStatus.Failed);
        match.VideoStatus = VideoStatus.Failed;
        match.VideoFailureReason = reason.Length > MaxFailureReasonLength ? reason[..MaxFailureReasonLength] : reason;
    }

    private static void RequireAwaitingProcessing(Match match, VideoStatus target)
    {
        if (!IsAwaitingProcessing(match))
        {
            throw new InvalidOperationException($"A match video cannot go from {match.VideoStatus} to {target}.");
        }
    }
}
