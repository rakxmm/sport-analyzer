using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Tags.Queries.GetTagsForMatch;

public record GetTagsForMatchQuery(Guid MatchId) : IRequest<IReadOnlyList<TagDto>>;

public class GetTagsForMatchQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IRequestHandler<GetTagsForMatchQuery, IReadOnlyList<TagDto>>
{
    public async Task<IReadOnlyList<TagDto>> Handle(GetTagsForMatchQuery request, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .FirstOrDefaultAsync(m => m.Id == request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        if (match.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenAccessException();
        }

        return await db.Tags
            .Where(t => t.MatchId == request.MatchId)
            .OrderBy(t => t.TimestampSeconds)
            .Select(t => TagDto.FromEntity(t))
            .ToListAsync(cancellationToken);
    }
}
