using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Clips.Commands.CreateClip;

/// <summary>
/// Creates a clip (single tag) or highlight reel (several tags) and queues it for the ffmpeg
/// worker (FR-13, FR-14). All tags must belong to the same match as the caller does not
/// re-derive MatchId from them.
/// </summary>
public record CreateClipCommand(Guid MatchId, ClipType Type, IReadOnlyList<Guid> TagIds) : IRequest<ClipDto>;

public class CreateClipCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClipProcessingQueue processingQueue) : IRequestHandler<CreateClipCommand, ClipDto>
{
    public async Task<ClipDto> Handle(CreateClipCommand request, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .FirstOrDefaultAsync(m => m.Id == request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        if (match.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenAccessException();
        }

        if (request.TagIds.Count == 0)
        {
            throw new InvalidOperationException("At least one tag is required to generate a clip.");
        }

        var tagCount = await db.Tags.CountAsync(t => request.TagIds.Contains(t.Id) && t.MatchId == match.Id, cancellationToken);
        if (tagCount != request.TagIds.Count)
        {
            throw new InvalidOperationException("One or more tags do not belong to this match.");
        }

        var clip = new Clip
        {
            MatchId = match.Id,
            Type = request.Type,
            Status = ClipStatus.Queued
        };

        clip.ClipTags = request.TagIds
            .Select((tagId, index) => new ClipTag { Clip = clip, TagId = tagId, Order = index })
            .ToList();

        db.Clips.Add(clip);
        await db.SaveChangesAsync(cancellationToken);

        await processingQueue.EnqueueAsync(clip.Id, cancellationToken);

        return ClipDto.FromEntity(clip);
    }
}
