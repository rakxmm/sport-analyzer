using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Clips.Queries.GetClipsForMatch;

public record GetClipsForMatchQuery(Guid MatchId) : IRequest<IReadOnlyList<ClipDto>>;

public class GetClipsForMatchQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IRequestHandler<GetClipsForMatchQuery, IReadOnlyList<ClipDto>>
{
    public async Task<IReadOnlyList<ClipDto>> Handle(GetClipsForMatchQuery request, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .FirstOrDefaultAsync(m => m.Id == request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        if (match.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenAccessException();
        }

        return await db.Clips
            .Where(c => c.MatchId == request.MatchId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => ClipDto.FromEntity(c))
            .ToListAsync(cancellationToken);
    }
}
