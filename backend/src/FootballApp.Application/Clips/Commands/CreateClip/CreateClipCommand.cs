using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Clips.Commands.CreateClip;

/// <summary>
/// Creates a clip (single tag) or highlight reel (several tags, joined in the order given) and queues
/// it for the ffmpeg worker (FR-13, FR-14). All tags must belong to the match.
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

        if (match.VideoStatus != VideoStatus.Ready || match.DurationSeconds is null)
        {
            throw new InvalidOperationException("Clips can be generated once the match video has finished processing.");
        }

        if (request.TagIds.Distinct().Count() != request.TagIds.Count)
        {
            throw new InvalidOperationException("A tag can only be included once in a clip.");
        }

        var tagsById = await db.Tags
            .Where(t => request.TagIds.Contains(t.Id) && t.MatchId == match.Id)
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        if (tagsById.Count != request.TagIds.Count)
        {
            throw new InvalidOperationException("One or more tags do not belong to this match.");
        }

        var orderedTags = request.TagIds.Select(id => tagsById[id]).ToList();
        if (ClipRules.Validate(request.Type, orderedTags, match.DurationSeconds.Value) is { } invalidReason)
        {
            throw new InvalidOperationException(invalidReason);
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

        // Queued before it is saved: if the save fails, the job finds no clip and gives up, whereas a
        // saved clip whose job was never queued would sit in Queued forever.
        await processingQueue.EnqueueAsync(clip.Id, cancellationToken);

        db.Clips.Add(clip);
        await db.SaveChangesAsync(cancellationToken);

        return ClipDto.FromEntity(clip);
    }
}
