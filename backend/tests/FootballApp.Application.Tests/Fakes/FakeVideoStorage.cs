using FootballApp.Application.Common.Interfaces;
using FootballApp.Application.Common.Models;

namespace FootballApp.Application.Tests.Fakes;

/// <summary>In-memory stand-in for R2 that tracks multipart uploads and completed objects.</summary>
public class FakeVideoStorage : IVideoStorageService
{
    public Dictionary<string, (string Key, List<UploadedPart> Parts)> Uploads { get; } = new();
    public Dictionary<string, long> Objects { get; } = new();
    public List<string> AbortedUploadIds { get; } = new();

    public void AddPart(string uploadId, int partNumber, long sizeBytes) =>
        Uploads[uploadId].Parts.Add(new UploadedPart(partNumber, $"\"etag-{partNumber}\"", sizeBytes));

    public Task<string> StartMultipartUploadAsync(string objectKey, string contentType, CancellationToken cancellationToken)
    {
        var uploadId = Guid.NewGuid().ToString("N");
        Uploads[uploadId] = (objectKey, new List<UploadedPart>());
        return Task.FromResult(uploadId);
    }

    public Task<string> GetSignedPartUploadUrlAsync(string objectKey, string uploadId, int partNumber, TimeSpan expiresIn, CancellationToken cancellationToken) =>
        Task.FromResult($"https://r2.test/{objectKey}?uploadId={uploadId}&partNumber={partNumber}");

    public Task<IReadOnlyList<UploadedPart>?> ListUploadedPartsAsync(string objectKey, string uploadId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UploadedPart>?>(Uploads.TryGetValue(uploadId, out var upload) ? upload.Parts.ToList() : null);

    public Task CompleteMultipartUploadAsync(string objectKey, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken cancellationToken)
    {
        Objects[objectKey] = parts.Sum(p => p.SizeBytes);
        Uploads.Remove(uploadId);
        return Task.CompletedTask;
    }

    public Task AbortMultipartUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken)
    {
        AbortedUploadIds.Add(uploadId);
        Uploads.Remove(uploadId);
        return Task.CompletedTask;
    }

    public Task<long?> GetObjectSizeAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult<long?>(Objects.TryGetValue(objectKey, out var size) ? size : null);

    public Task<string> GetSignedDownloadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        SignedUrlLifetimes.Add(expiresIn);
        return Task.FromResult($"https://r2.test/{objectKey}?signed");
    }

    public Task<string> GetSignedAttachmentUrlAsync(string objectKey, string fileName, TimeSpan expiresIn, CancellationToken cancellationToken) =>
        Task.FromResult($"https://r2.test/{objectKey}?signed&download={fileName}");

    public Task UploadFileAsync(string objectKey, string filePath, string contentType, CancellationToken cancellationToken)
    {
        if (FailNextUpload)
        {
            FailNextUpload = false;
            throw new IOException("Storage is unreachable.");
        }

        Objects[objectKey] = new FileInfo(filePath).Length;
        UploadCount++;
        return Task.CompletedTask;
    }

    public List<TimeSpan> SignedUrlLifetimes { get; } = new();
    public int UploadCount { get; private set; }
    public bool FailNextUpload { get; set; }
}
