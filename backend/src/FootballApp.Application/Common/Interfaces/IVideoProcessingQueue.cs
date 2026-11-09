namespace FootballApp.Application.Common.Interfaces;

/// <summary>Queues a freshly uploaded match video for the worker to probe and mark ready (FR-8).</summary>
public interface IVideoProcessingQueue
{
    Task EnqueueAsync(Guid matchId, CancellationToken cancellationToken);
}
