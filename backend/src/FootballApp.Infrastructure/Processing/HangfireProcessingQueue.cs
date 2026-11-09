using FootballApp.Application.Common.Interfaces;
using Hangfire;

namespace FootballApp.Infrastructure.Processing;

/// <summary>
/// Puts work on the Hangfire queue kept in Postgres (FR-8, FR-14). The API only enqueues; the jobs run
/// in the separate worker container, which is the one with ffmpeg.
/// </summary>
public class HangfireProcessingQueue(IBackgroundJobClient jobs) : IClipProcessingQueue, IVideoProcessingQueue
{
    Task IClipProcessingQueue.EnqueueAsync(Guid clipId, CancellationToken cancellationToken)
    {
        jobs.Enqueue<ProcessingJobs>(job => job.RenderClipAsync(clipId, null, CancellationToken.None));
        return Task.CompletedTask;
    }

    Task IVideoProcessingQueue.EnqueueAsync(Guid matchId, CancellationToken cancellationToken)
    {
        jobs.Enqueue<ProcessingJobs>(job => job.ProcessUploadedVideoAsync(matchId, null, CancellationToken.None));
        return Task.CompletedTask;
    }
}
