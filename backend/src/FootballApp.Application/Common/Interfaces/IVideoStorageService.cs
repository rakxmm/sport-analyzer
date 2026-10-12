using FootballApp.Application.Common.Models;

namespace FootballApp.Application.Common.Interfaces;

/// <summary>
/// Object storage for raw videos and rendered clips (Cloudflare R2, S3-compatible). Everything stays
/// private; the browser only ever gets signed, time-limited URLs (NFR-5).
/// </summary>
public interface IVideoStorageService
{
    /// <summary>Starts a multipart upload the browser fills part by part via signed URLs; returns its upload ID.</summary>
    Task<string> StartMultipartUploadAsync(string objectKey, string contentType, CancellationToken cancellationToken);

    /// <summary>Signed PUT URL for a single part of a multipart upload.</summary>
    Task<string> GetSignedPartUploadUrlAsync(string objectKey, string uploadId, int partNumber, TimeSpan expiresIn, CancellationToken cancellationToken);

    /// <summary>Parts already stored for the upload, or null if the upload no longer exists (completed, aborted or expired).</summary>
    Task<IReadOnlyList<UploadedPart>?> ListUploadedPartsAsync(string objectKey, string uploadId, CancellationToken cancellationToken);

    Task CompleteMultipartUploadAsync(string objectKey, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken cancellationToken);

    /// <summary>Discards an unfinished upload; a no-op if it no longer exists.</summary>
    Task AbortMultipartUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken);

    /// <summary>Size of a stored object, or null if it does not exist.</summary>
    Task<long?> GetObjectSizeAsync(string objectKey, CancellationToken cancellationToken);

    /// <summary>Signed, expiring GET URL for a raw video or rendered clip/reel.</summary>
    Task<string> GetSignedDownloadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken);
}
