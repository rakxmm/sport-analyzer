using FootballApp.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Clips.Commands.FailClip;

/// <summary>
/// Worker job, last resort: the queue gave up on rendering this clip, so stop showing it as in progress.
/// Does nothing if the clip already reached Done or Failed.
/// </summary>
public record FailClipCommand(Guid ClipId) : IRequest;

public class FailClipCommandHandler(IApplicationDbContext db) : IRequestHandler<FailClipCommand>
{
    public const string Reason = "The clip could not be generated. Please try again.";

    public async Task Handle(FailClipCommand request, CancellationToken cancellationToken)
    {
        var clip = await db.Clips.FirstOrDefaultAsync(c => c.Id == request.ClipId, cancellationToken);
        if (clip is null || !ClipLifecycle.IsUnfinished(clip))
        {
            return;
        }

        ClipLifecycle.MarkFailed(clip, Reason);
        await db.SaveChangesAsync(cancellationToken);
    }
}
