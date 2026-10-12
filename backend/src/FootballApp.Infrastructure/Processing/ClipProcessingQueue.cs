using FootballApp.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FootballApp.Infrastructure.Processing;

/// <summary>
/// Placeholder for the real background queue — swap in Hangfire's BackgroundJob.Enqueue to hand the
/// clip off to the Dockerized ffmpeg worker (FR-14).
/// </summary>
public class ClipProcessingQueue(ILogger<ClipProcessingQueue> logger) : IClipProcessingQueue
{
    public Task EnqueueAsync(Guid clipId, CancellationToken cancellationToken)
    {
        logger.LogWarning("ClipProcessingQueue is a stub — clip {ClipId} was not actually queued for processing.", clipId);
        return Task.CompletedTask;
    }
}
