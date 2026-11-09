using FootballApp.Application.Clips.Commands.FailClip;
using FootballApp.Application.Clips.Commands.RenderClip;
using FootballApp.Application.Matches.Commands.FailVideoProcessing;
using FootballApp.Application.Matches.Commands.ProcessUploadedVideo;
using Hangfire;
using Hangfire.Server;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FootballApp.Infrastructure.Processing;

/// <summary>
/// The Hangfire jobs. The API only refers to these methods to put them on the queue; the worker is the
/// one that runs them. Each job just hands over to an Application command, which is written so that
/// running it twice does no harm.
/// </summary>
public class ProcessingJobs(ISender sender, IServiceScopeFactory scopeFactory, ILogger<ProcessingJobs> logger)
{
    // Three runs in total. The pauses cover a short storage or database blip; after that the owner is
    // better served by a clear Failed status than by a clip that stays "processing" for an hour.
    private const int Retries = 2;
    private const int FirstRetryDelaySeconds = 15;
    private const int SecondRetryDelaySeconds = 60;

    [AutomaticRetry(Attempts = Retries, DelaysInSeconds = [FirstRetryDelaySeconds, SecondRetryDelaySeconds])]
    [JobDisplayName("Process uploaded video of match {0}")]
    public async Task ProcessUploadedVideoAsync(Guid matchId, PerformContext? context, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new ProcessUploadedVideoCommand(matchId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && IsLastAttempt(context))
        {
            logger.LogError(ex, "Giving up on processing the video of match {MatchId}", matchId);
            await SendInNewScope(new FailVideoProcessingCommand(matchId));
            throw;
        }
    }

    [AutomaticRetry(Attempts = Retries, DelaysInSeconds = [FirstRetryDelaySeconds, SecondRetryDelaySeconds])]
    [JobDisplayName("Render clip {0}")]
    public async Task RenderClipAsync(Guid clipId, PerformContext? context, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new RenderClipCommand(clipId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && IsLastAttempt(context))
        {
            logger.LogError(ex, "Giving up on rendering clip {ClipId}", clipId);
            await SendInNewScope(new FailClipCommand(clipId));
            throw;
        }
    }

    /// <summary>Hangfire counts the retries it has scheduled so far in the job's "RetryCount" parameter.</summary>
    private static bool IsLastAttempt(PerformContext? context) =>
        context is null || context.GetJobParameter<int>("RetryCount") >= Retries;

    /// <summary>A fresh DbContext, because the one the failed command used may still hold its half-made changes.</summary>
    private async Task SendInNewScope(IRequest command)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, CancellationToken.None);
    }
}
