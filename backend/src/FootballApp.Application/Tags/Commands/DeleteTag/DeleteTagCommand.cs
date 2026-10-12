using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Tags.Commands.DeleteTag;

/// <summary>Tags remain editable/deletable until a clip is exported from them (FR-12).</summary>
public record DeleteTagCommand(Guid TagId) : IRequest;

public class DeleteTagCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IRequestHandler<DeleteTagCommand>
{
    public async Task Handle(DeleteTagCommand request, CancellationToken cancellationToken)
    {
        var tag = await db.Tags
            .Include(t => t.Match)
            .FirstOrDefaultAsync(t => t.Id == request.TagId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tag), request.TagId);

        if (tag.Match!.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenAccessException();
        }

        db.Tags.Remove(tag);
        await db.SaveChangesAsync(cancellationToken);
    }
}
