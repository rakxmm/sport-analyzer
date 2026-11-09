using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Clips.Queries.GetClipDownloadUrl;

/// <summary>
/// Signed URL the owner's browser saves the finished clip from (FR-16). Unlike the share link it does
/// not expire with the clip — only the URL itself is short-lived.
/// </summary>
public record GetClipDownloadUrlQuery(Guid ClipId) : IRequest<ClipDownloadUrlDto>;

public record ClipDownloadUrlDto(string Url, string FileName, DateTimeOffset ExpiresAt);

public class GetClipDownloadUrlQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IVideoStorageService storage) : IRequestHandler<GetClipDownloadUrlQuery, ClipDownloadUrlDto>
{
    // Only has to be valid when the download starts.
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(10);

    public async Task<ClipDownloadUrlDto> Handle(GetClipDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        var clip = await db.Clips
            .AsNoTracking()
            .Include(c => c.Match)
            .FirstOrDefaultAsync(c => c.Id == request.ClipId, cancellationToken)
            ?? throw new NotFoundException(nameof(Clip), request.ClipId);

        if (clip.Match!.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenAccessException();
        }

        if (clip.Status != ClipStatus.Done || clip.StorageKey is null)
        {
            throw new InvalidOperationException("This clip is not ready to download yet.");
        }

        var fileName = ClipRules.BuildDownloadFileName(clip.Match.Title, clip.Type);
        var expiresAt = DateTimeOffset.UtcNow.Add(UrlLifetime);
        var url = await storage.GetSignedAttachmentUrlAsync(clip.StorageKey, fileName, UrlLifetime, cancellationToken);

        return new ClipDownloadUrlDto(url, fileName, expiresAt);
    }
}
