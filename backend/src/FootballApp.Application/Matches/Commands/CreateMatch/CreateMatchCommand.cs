using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using MediatR;

namespace FootballApp.Application.Matches.Commands.CreateMatch;

/// <summary>
/// Creates the match record and returns an upload target for the raw video (FR-6).
/// The client's consent checkbox (FR-9) must be true or this is rejected.
/// </summary>
public record CreateMatchCommand(string Title, DateOnly MatchDate, bool ConsentConfirmed) : IRequest<MatchDto>;

public class CreateMatchCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IRequestHandler<CreateMatchCommand, MatchDto>
{
    public async Task<MatchDto> Handle(CreateMatchCommand request, CancellationToken cancellationToken)
    {
        if (!request.ConsentConfirmed)
        {
            throw new InvalidOperationException("Upload consent must be confirmed before a match can be created.");
        }

        var match = new Match
        {
            OwnerId = currentUser.UserId!,
            Title = request.Title,
            MatchDate = request.MatchDate,
            ConsentConfirmed = true,
            ConsentConfirmedAt = DateTimeOffset.UtcNow
        };

        db.Matches.Add(match);
        await db.SaveChangesAsync(cancellationToken);

        return MatchDto.FromEntity(match);
    }
}
