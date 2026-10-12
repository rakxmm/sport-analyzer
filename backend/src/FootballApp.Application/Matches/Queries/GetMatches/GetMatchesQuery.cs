using FootballApp.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Matches.Queries.GetMatches;

/// <summary>Lists matches owned by the current Clerk user (FR-5).</summary>
public record GetMatchesQuery : IRequest<IReadOnlyList<MatchDto>>;

public class GetMatchesQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IRequestHandler<GetMatchesQuery, IReadOnlyList<MatchDto>>
{
    public async Task<IReadOnlyList<MatchDto>> Handle(GetMatchesQuery request, CancellationToken cancellationToken)
    {
        return await db.Matches
            .Where(m => m.OwnerId == currentUser.UserId)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => MatchDto.FromEntity(m))
            .ToListAsync(cancellationToken);
    }
}
