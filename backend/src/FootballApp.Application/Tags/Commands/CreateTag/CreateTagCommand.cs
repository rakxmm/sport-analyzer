using FootballApp.Application.Clips;
using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Tags.Commands.CreateTag;

/// <summary>Tags an event at a timestamp in a match's video (FR-10).</summary>
public record CreateTagCommand(
    Guid MatchId,
    TagType Type,
    int TimestampSeconds,
    int LeadInSeconds,
    int LeadOutSeconds,
    string? Note) : IRequest<TagDto>;

public class CreateTagCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IRequestHandler<CreateTagCommand, TagDto>
{
    public async Task<TagDto> Handle(CreateTagCommand request, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .FirstOrDefaultAsync(m => m.Id == request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        if (match.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenAccessException();
        }

        if (request.TimestampSeconds < 0
            || (match.DurationSeconds is { } duration && !ClipRules.IsWithinVideo(request.TimestampSeconds, duration)))
        {
            throw new InvalidOperationException("The tag must be placed within the video.");
        }

        if (request.LeadInSeconds is < 0 or > ClipRules.MaxLeadSeconds
            || request.LeadOutSeconds is < 0 or > ClipRules.MaxLeadSeconds)
        {
            throw new InvalidOperationException($"Lead-in and lead-out must be between 0 and {ClipRules.MaxLeadSeconds} seconds.");
        }

        var tag = new Tag
        {
            MatchId = match.Id,
            Type = request.Type,
            TimestampSeconds = request.TimestampSeconds,
            LeadInSeconds = request.LeadInSeconds,
            LeadOutSeconds = request.LeadOutSeconds,
            Note = request.Note
        };

        db.Tags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);

        return TagDto.FromEntity(tag);
    }
}
