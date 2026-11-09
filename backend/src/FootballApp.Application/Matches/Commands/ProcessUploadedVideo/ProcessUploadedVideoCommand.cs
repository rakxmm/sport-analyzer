using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Application.Matches.VideoProcessing;
using FootballApp.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Matches.Commands.ProcessUploadedVideo;

/// <summary>
/// Worker job: reads the uploaded video's length and moves the match Uploaded → Ready, or to Failed if
/// the file is not a usable video. Not exposed through the API, so there is no owner check. Safe to run
/// more than once for the same match.
/// </summary>
public record ProcessUploadedVideoCommand(Guid MatchId) : IRequest;

public class ProcessUploadedVideoCommandHandler(
    IApplicationDbContext db,
    IVideoStorageService storage,
    IVideoProcessor processor) : IRequestHandler<ProcessUploadedVideoCommand>
{
    private static readonly TimeSpan SourceUrlLifetime = TimeSpan.FromMinutes(30);

    public async Task Handle(ProcessUploadedVideoCommand request, CancellationToken cancellationToken)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == request.MatchId, cancellationToken);
        if (match is null)
        {
            return;
        }

        if (match.VideoStatus == VideoStatus.PendingUpload)
        {
            // The API queues this job right before it saves the Uploaded status, so the job can get here
            // first. Throwing makes the queue try again a little later.
            throw new InvalidOperationException($"Match {match.Id} is not marked as uploaded yet.");
        }

        if (!VideoLifecycle.IsAwaitingProcessing(match))
        {
            return;
        }

        VideoLifecycle.MarkProcessing(match);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var sourceUrl = await storage.GetSignedDownloadUrlAsync(match.VideoStorageKey!, SourceUrlLifetime, cancellationToken);
            var probe = await processor.ProbeAsync(sourceUrl, cancellationToken);

            var durationSeconds = (int)Math.Min(Math.Floor(probe.DurationSeconds), int.MaxValue);
            if (!probe.HasVideo || durationSeconds < 1)
            {
                throw new InvalidVideoException("The uploaded file does not contain any video.");
            }

            VideoLifecycle.MarkReady(match, durationSeconds);
        }
        catch (InvalidVideoException ex)
        {
            VideoLifecycle.MarkFailed(match, ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
