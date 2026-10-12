using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Application.Matches.VideoUpload;
using FootballApp.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Matches.Commands.AbortVideoUpload;

/// <summary>Cancels an unfinished video upload and discards the parts already stored in R2.</summary>
public record AbortVideoUploadCommand(Guid MatchId) : IRequest<MatchDto>;

public class AbortVideoUploadCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IVideoStorageService storage) : IRequestHandler<AbortVideoUploadCommand, MatchDto>
{
    public async Task<MatchDto> Handle(AbortVideoUploadCommand request, CancellationToken cancellationToken)
    {
        var match = await db.Matches
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

        await storage.AbortMultipartUploadAsync(match.VideoStorageKey!, match.UploadId, cancellationToken);

        VideoUploadRules.ClearUploadState(match);
        await db.SaveChangesAsync(cancellationToken);

        return MatchDto.FromEntity(match);
    }
}
