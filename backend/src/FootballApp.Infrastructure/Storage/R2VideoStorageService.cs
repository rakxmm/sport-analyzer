using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Application.Common.Models;
using Microsoft.Extensions.Options;

namespace FootballApp.Infrastructure.Storage;

/// <summary>
/// IVideoStorageService against Cloudflare R2's S3-compatible API. The bucket stays private; the browser
/// only gets presigned URLs (NFR-5).
/// </summary>
public class R2VideoStorageService(IAmazonS3 s3, IOptions<R2Options> options) : IVideoStorageService
{
    private string Bucket => options.Value.BucketName;

    public async Task<string> StartMultipartUploadAsync(string objectKey, string contentType, CancellationToken cancellationToken)
    {
        var response = await s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = Bucket,
            Key = objectKey,
            ContentType = contentType
        }, cancellationToken);

        return response.UploadId;
    }

    public Task<string> GetSignedPartUploadUrlAsync(string objectKey, string uploadId, int partNumber, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        return s3.GetPreSignedURLAsync(CreatePresignRequest(objectKey, HttpVerb.PUT, expiresIn, request =>
        {
            request.UploadId = uploadId;
            request.PartNumber = partNumber;
        }));
    }

    public async Task<IReadOnlyList<UploadedPart>?> ListUploadedPartsAsync(string objectKey, string uploadId, CancellationToken cancellationToken)
    {
        var parts = new List<UploadedPart>();
        string? marker = null;

        try
        {
            ListPartsResponse response;
            do
            {
                response = await s3.ListPartsAsync(new ListPartsRequest
                {
                    BucketName = Bucket,
                    Key = objectKey,
                    UploadId = uploadId,
                    PartNumberMarker = marker
                }, cancellationToken);

                parts.AddRange((response.Parts ?? []).Select(p =>
                    new UploadedPart(p.PartNumber ?? 0, p.ETag, p.Size ?? 0)));

                marker = response.NextPartNumberMarker?.ToString();
            }
            while (response.IsTruncated == true);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchUpload" || ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return parts;
    }

    public async Task CompleteMultipartUploadAsync(string objectKey, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken cancellationToken)
    {
        await s3.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = Bucket,
            Key = objectKey,
            UploadId = uploadId,
            PartETags = parts.Select(p => new PartETag(p.PartNumber, p.ETag)).ToList()
        }, cancellationToken);
    }

    public async Task AbortMultipartUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken)
    {
        try
        {
            await s3.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
            {
                BucketName = Bucket,
                Key = objectKey,
                UploadId = uploadId
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchUpload" || ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone — nothing to discard.
        }
    }

    public async Task<long?> GetObjectSizeAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await s3.GetObjectMetadataAsync(Bucket, objectKey, cancellationToken);
            return metadata.ContentLength;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<string> GetSignedDownloadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        return s3.GetPreSignedURLAsync(CreatePresignRequest(objectKey, HttpVerb.GET, expiresIn, _ => { }));
    }

    private GetPreSignedUrlRequest CreatePresignRequest(string objectKey, HttpVerb verb, TimeSpan expiresIn, Action<GetPreSignedUrlRequest> configure)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = objectKey,
            Verb = verb,
            Expires = DateTime.UtcNow.Add(expiresIn),
            // The SDK signs https URLs unless told otherwise; only a local S3 emulator runs on plain http.
            Protocol = options.Value.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? Protocol.HTTP
                : Protocol.HTTPS
        };
        configure(request);
        return request;
    }
}
