using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Clips.Queries.GetSharedClip;

/// <summary>Resolves a public share link — viewable without logging in (FR-16). No ownership check.</summary>
public record GetSharedClipQuery(string ShareToken) : IRequest<SharedClipDto>;

public record SharedClipDto(Guid Id, ClipType Type, ClipStatus Status, string? PlaybackUrl, DateTimeOffset? ExpiresAt);

public class GetSharedClipQueryHandler(
    IApplicationDbContext db,
    IVideoStorageService storage) : IRequestHandler<GetSharedClipQuery, SharedClipDto>
{
    private static readonly TimeSpan MaxPlaybackUrlLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinPlaybackUrlLifetime = TimeSpan.FromSeconds(1);

    public async Task<SharedClipDto> Handle(GetSharedClipQuery request, CancellationToken cancellationToken)
    {
        var clip = await db.Clips
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ShareToken == request.ShareToken, cancellationToken)
            ?? throw new NotFoundException(nameof(Clip), request.ShareToken);

        var now = DateTimeOffset.UtcNow;
        if (clip.ShareExpiresAt is not null && clip.ShareExpiresAt <= now)
        {
            throw new NotFoundException(nameof(Clip), request.ShareToken);
        }

        string? playbackUrl = null;
        if (clip.Status == ClipStatus.Done && clip.StorageKey is not null)
        {
            // The signed URL must not keep working after the share link itself has expired (FR-15).
            var lifetime = MaxPlaybackUrlLifetime;
            if (clip.ShareExpiresAt is { } expiresAt && expiresAt - now < lifetime)
            {
                lifetime = expiresAt - now;
            }

            if (lifetime < MinPlaybackUrlLifetime)
            {
                lifetime = MinPlaybackUrlLifetime;
            }

            playbackUrl = await storage.GetSignedDownloadUrlAsync(clip.StorageKey, lifetime, cancellationToken);
        }

        return new SharedClipDto(clip.Id, clip.Type, clip.Status, playbackUrl, clip.ShareExpiresAt);
    }
}
