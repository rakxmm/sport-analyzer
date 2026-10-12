namespace FootballApp.Application.Common.Interfaces;

/// <summary>
/// Issues signed, time-limited R2 URLs so raw video and rendered clips stay private by default (NFR-5).
/// Implemented in Infrastructure against Cloudflare R2's S3-compatible API.
/// </summary>
public interface IVideoStorageService
{
    /// <summary>Signed PUT/tus destination the client uploads the raw match video to.</summary>
    Task<string> GetUploadUrlAsync(string objectKey, CancellationToken cancellationToken);

    /// <summary>Signed, expiring GET URL for a raw video or rendered clip/reel.</summary>
    Task<string> GetSignedDownloadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken);
}
