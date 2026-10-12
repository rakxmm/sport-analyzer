using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Application.Matches.VideoUpload;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Matches.Commands.StartVideoUpload;

/// <summary>
/// Starts uploading the raw match video straight to R2 as a multipart upload (FR-6). Calling it again
/// with the same file resumes the unfinished upload — e.g. after a page reload — instead of starting over.
/// </summary>
public record StartVideoUploadCommand(Guid MatchId, string FileName, string ContentType, long SizeBytes) : IRequest<UploadSessionDto>;

public class StartVideoUploadCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IVideoStorageService storage) : IRequestHandler<StartVideoUploadCommand, UploadSessionDto>
{
    public async Task<UploadSessionDto> Handle(StartVideoUploadCommand request, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .FirstOrDefaultAsync(m => m.Id == request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        if (match.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenAccessException();
        }

        if (!match.ConsentConfirmed)
        {
            throw new InvalidOperationException("Upload consent must be confirmed before a video can be uploaded.");
        }

        if (match.VideoStatus != VideoStatus.PendingUpload)
        {
            throw new InvalidOperationException("A video has already been uploaded for this match.");
        }

        var fileName = Path.GetFileName(request.FileName?.Trim() ?? string.Empty);
        if (fileName.Length is 0 or > 255)
        {
            throw new InvalidOperationException("File name must be between 1 and 255 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.ContentType)
            || !request.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || request.ContentType.Length > 100)
        {
            throw new InvalidOperationException("Only video files can be uploaded.");
        }

        if (request.SizeBytes <= 0 || request.SizeBytes > VideoUploadRules.MaxVideoSizeBytes)
        {
            throw new InvalidOperationException(
                $"Video size must be between 1 byte and {VideoUploadRules.MaxVideoSizeBytes / (1024 * 1024 * 1024)} GB.");
        }

        if (match.UploadId is not null)
        {
            var isSameFile = match.VideoFileName == fileName && match.VideoSizeBytes == request.SizeBytes;
            var existingParts = isSameFile
                ? await storage.ListUploadedPartsAsync(match.VideoStorageKey!, match.UploadId, cancellationToken)
                : null;

            if (existingParts is not null)
            {
                return ToSession(match, existingParts.Select(p => p.PartNumber));
            }

            // A different file was picked, or R2 already discarded the unfinished upload (it expires
            // incomplete multipart uploads) — either way start from scratch.
            await storage.AbortMultipartUploadAsync(match.VideoStorageKey!, match.UploadId, cancellationToken);
        }

        var storageKey = VideoUploadRules.BuildStorageKey(match.Id, fileName);
        var contentType = request.ContentType.ToLowerInvariant();

        match.UploadId = await storage.StartMultipartUploadAsync(storageKey, contentType, cancellationToken);
        match.UploadPartSizeBytes = VideoUploadRules.CalculatePartSize(request.SizeBytes);
        match.VideoStorageKey = storageKey;
        match.VideoFileName = fileName;
        match.VideoContentType = contentType;
        match.VideoSizeBytes = request.SizeBytes;

        await db.SaveChangesAsync(cancellationToken);

        return ToSession(match, []);
    }

    private static UploadSessionDto ToSession(Match match, IEnumerable<int> completedPartNumbers)
    {
        var partSize = match.UploadPartSizeBytes!.Value;
        var size = match.VideoSizeBytes!.Value;
        return new UploadSessionDto(
            match.Id,
            match.VideoFileName!,
            size,
            partSize,
            VideoUploadRules.CalculatePartCount(size, partSize),
            completedPartNumbers.Order().ToList());
    }
}
