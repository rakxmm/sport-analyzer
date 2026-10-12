using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Application.Matches.VideoUpload;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Matches.Commands.CompleteVideoUpload;

/// <summary>
/// Assembles the uploaded parts into the final raw video in R2 (FR-8). The part list comes from R2
/// itself rather than the client, and is checked against the size declared when the upload started.
/// </summary>
public record CompleteVideoUploadCommand(Guid MatchId) : IRequest<MatchDto>;

public class CompleteVideoUploadCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IVideoStorageService storage) : IRequestHandler<CompleteVideoUploadCommand, MatchDto>
{
    public async Task<MatchDto> Handle(CompleteVideoUploadCommand request, CancellationToken cancellationToken)
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

        var storageKey = match.VideoStorageKey!;
        var sizeBytes = match.VideoSizeBytes!.Value;
        var partSize = match.UploadPartSizeBytes!.Value;

        var parts = await storage.ListUploadedPartsAsync(storageKey, match.UploadId, cancellationToken);
        if (parts is null)
        {
            // Either a previous completion reached R2 but our save failed, or the upload expired.
            if (await storage.GetObjectSizeAsync(storageKey, cancellationToken) == sizeBytes)
            {
                return await MarkUploaded(match, cancellationToken);
            }

            VideoUploadRules.ClearUploadState(match);
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("The upload expired before it was finished. Please upload the video again.");
        }

        var partCount = VideoUploadRules.CalculatePartCount(sizeBytes, partSize);
        var ordered = parts.OrderBy(p => p.PartNumber).ToList();

        var missing = Enumerable.Range(1, partCount).Except(ordered.Select(p => p.PartNumber)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The upload is not finished yet — {missing.Count} of {partCount} parts are still missing.");
        }

        var hasUnexpectedParts = ordered.Count != partCount
            || ordered.Take(partCount - 1).Any(p => p.SizeBytes != partSize)
            || ordered.Sum(p => p.SizeBytes) != sizeBytes;
        if (hasUnexpectedParts)
        {
            throw new InvalidOperationException("The uploaded parts do not match the selected file. Please upload the video again.");
        }

        await storage.CompleteMultipartUploadAsync(storageKey, match.UploadId, ordered, cancellationToken);

        return await MarkUploaded(match, cancellationToken);
    }

    private async Task<MatchDto> MarkUploaded(Match match, CancellationToken cancellationToken)
    {
        match.UploadId = null;
        match.UploadPartSizeBytes = null;
        match.VideoStatus = VideoStatus.Uploaded;
        await db.SaveChangesAsync(cancellationToken);
        return MatchDto.FromEntity(match);
    }
}
