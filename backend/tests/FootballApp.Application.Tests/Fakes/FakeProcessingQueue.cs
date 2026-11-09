using FootballApp.Application.Common.Interfaces;

namespace FootballApp.Application.Tests.Fakes;

/// <summary>Records what was queued instead of running anything.</summary>
public class FakeProcessingQueue : IClipProcessingQueue, IVideoProcessingQueue
{
    public List<Guid> ClipIds { get; } = new();
    public List<Guid> MatchIds { get; } = new();
    public bool Fail { get; set; }

    Task IClipProcessingQueue.EnqueueAsync(Guid clipId, CancellationToken cancellationToken)
    {
        ThrowIfFailing();
        ClipIds.Add(clipId);
        return Task.CompletedTask;
    }

    Task IVideoProcessingQueue.EnqueueAsync(Guid matchId, CancellationToken cancellationToken)
    {
        ThrowIfFailing();
        MatchIds.Add(matchId);
        return Task.CompletedTask;
    }

    private void ThrowIfFailing()
    {
        if (Fail)
        {
            throw new IOException("The queue is unreachable.");
        }
    }
}
