using FootballApp.Application.Common.Interfaces;

namespace FootballApp.Infrastructure.Storage;

/// <summary>
/// Placeholder for the real Cloudflare R2 (S3-compatible) implementation — swap in the AWS SDK
/// S3 client pointed at the R2 endpoint and generate presigned URLs (NFR-5, NFR-1: EU region bucket).
/// </summary>
public class R2VideoStorageService : IVideoStorageService
{
    public Task<string> GetUploadUrlAsync(string objectKey, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Wire up Cloudflare R2 presigned upload URLs here.");
    }

    public Task<string> GetSignedDownloadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Wire up Cloudflare R2 presigned download URLs here.");
    }
}
