using FootballApp.Application.Common.Interfaces;
using FootballApp.Application.Matches.VideoProcessing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Matches.Commands.FailVideoProcessing;

/// <summary>
/// Worker job, last resort: the queue gave up on processing this match's video, so stop showing it as
/// in progress. Does nothing if the video already reached Ready or Failed.
/// </summary>
public record FailVideoProcessingCommand(Guid MatchId) : IRequest;

public class FailVideoProcessingCommandHandler(IApplicationDbContext db) : IRequestHandler<FailVideoProcessingCommand>
{
    public const string Reason = "The video could not be processed. Please try again later.";

    public async Task Handle(FailVideoProcessingCommand request, CancellationToken cancellationToken)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == request.MatchId, cancellationToken);
        if (match is null || !VideoLifecycle.IsAwaitingProcessing(match))
        {
            return;
        }

        VideoLifecycle.MarkFailed(match, Reason);
        await db.SaveChangesAsync(cancellationToken);
    }
}
