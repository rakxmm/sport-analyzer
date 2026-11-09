using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Matches.Queries.GetMatchVideoUrl;

/// <summary>Signed playback URL for the owner's raw match video — never a public bucket URL (NFR-5).</summary>
public record GetMatchVideoUrlQuery(Guid MatchId) : IRequest<VideoUrlDto>;

public record VideoUrlDto(string Url, DateTimeOffset ExpiresAt);

public class GetMatchVideoUrlQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IVideoStorageService storage) : IRequestHandler<GetMatchVideoUrlQuery, VideoUrlDto>
{
    // The player keeps issuing range requests while seeking, so the URL has to last a whole tagging session.
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromHours(4);

    public async Task<VideoUrlDto> Handle(GetMatchVideoUrlQuery request, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        if (match.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenAccessException();
        }

        if (match.VideoStatus == VideoStatus.PendingUpload || match.VideoStorageKey is null)
        {
            throw new InvalidOperationException("This match has no uploaded video yet.");
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(UrlLifetime);
        var url = await storage.GetSignedDownloadUrlAsync(match.VideoStorageKey, UrlLifetime, cancellationToken);

        return new VideoUrlDto(url, expiresAt);
    }
}
