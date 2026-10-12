using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Matches.Queries.GetMatchById;

public record GetMatchByIdQuery(Guid MatchId) : IRequest<MatchDto>;

public class GetMatchByIdQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IRequestHandler<GetMatchByIdQuery, MatchDto>
{
    public async Task<MatchDto> Handle(GetMatchByIdQuery request, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .FirstOrDefaultAsync(m => m.Id == request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.Match), request.MatchId);

        if (match.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenAccessException();
        }

        return MatchDto.FromEntity(match);
    }
}
