using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Application.Matches.VideoUpload;
using FootballApp.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Matches.Queries.GetVideoPartUploadUrl;

/// <summary>Signs a PUT URL for one part of the match's in-progress video upload (FR-6, NFR-5).</summary>
public record GetVideoPartUploadUrlQuery(Guid MatchId, int PartNumber) : IRequest<PartUploadUrlDto>;

public record PartUploadUrlDto(int PartNumber, string Url);

public class GetVideoPartUploadUrlQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IVideoStorageService storage) : IRequestHandler<GetVideoPartUploadUrlQuery, PartUploadUrlDto>
{
    // Signed per part right before it is sent, so this only needs to outlive one slow part upload.
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromHours(1);

    public async Task<PartUploadUrlDto> Handle(GetVideoPartUploadUrlQuery request, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        if (match.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenAccessException();
        }

        if (match.UploadId is null)
        {
            throw new InvalidOperationException("No video upload is in progress for this match.");
        }

        var partCount = VideoUploadRules.CalculatePartCount(match.VideoSizeBytes!.Value, match.UploadPartSizeBytes!.Value);
        if (request.PartNumber < 1 || request.PartNumber > partCount)
        {
            throw new InvalidOperationException($"Part number must be between 1 and {partCount}.");
        }

        var url = await storage.GetSignedPartUploadUrlAsync(
            match.VideoStorageKey!, match.UploadId, request.PartNumber, UrlLifetime, cancellationToken);

        return new PartUploadUrlDto(request.PartNumber, url);
    }
}
