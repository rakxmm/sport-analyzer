using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Clips.Queries.GetSharedClip;

/// <summary>Resolves a public share link — viewable without logging in (FR-16). No ownership check.</summary>
public record GetSharedClipQuery(string ShareToken) : IRequest<SharedClipDto>;

public record SharedClipDto(Guid Id, ClipType Type, ClipStatus Status, string? PlaybackUrl);

public class GetSharedClipQueryHandler(
    IApplicationDbContext db,
    IVideoStorageService storage) : IRequestHandler<GetSharedClipQuery, SharedClipDto>
{
    public async Task<SharedClipDto> Handle(GetSharedClipQuery request, CancellationToken cancellationToken)
    {
        var clip = await db.Clips
            .FirstOrDefaultAsync(c => c.ShareToken == request.ShareToken, cancellationToken)
            ?? throw new NotFoundException(nameof(Clip), request.ShareToken);

        if (clip.ShareExpiresAt is not null && clip.ShareExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new NotFoundException(nameof(Clip), request.ShareToken);
        }

        string? playbackUrl = null;
        if (clip.Status == ClipStatus.Done && clip.StorageKey is not null)
        {
            playbackUrl = await storage.GetSignedDownloadUrlAsync(clip.StorageKey, TimeSpan.FromHours(1), cancellationToken);
        }

        return new SharedClipDto(clip.Id, clip.Type, clip.Status, playbackUrl);
    }
}
