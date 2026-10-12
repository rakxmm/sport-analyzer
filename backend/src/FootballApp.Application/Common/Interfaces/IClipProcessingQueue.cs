namespace FootballApp.Application.Common.Interfaces;

/// <summary>
/// Queues a clip/reel for the ffmpeg worker (FR-14). Implemented in Infrastructure — Hangfire to start.
/// </summary>
public interface IClipProcessingQueue
{
    Task EnqueueAsync(Guid clipId, CancellationToken cancellationToken);
}
