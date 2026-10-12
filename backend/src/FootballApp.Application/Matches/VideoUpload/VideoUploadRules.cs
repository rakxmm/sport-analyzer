using FootballApp.Domain.Entities;

namespace FootballApp.Application.Matches.VideoUpload;

public static class VideoUploadRules
{
    /// <summary>Comfortably above the ~15 GB full-match target in NFR-3.</summary>
    public const long MaxVideoSizeBytes = 20L * 1024 * 1024 * 1024;

    // S3/R2 allow at most 10,000 parts and require every part but the last to be the same size.
    // 32 MiB keeps a 20 GiB file at ~640 parts while a dropped part only costs 32 MiB of re-upload.
    private const long MinPartSizeBytes = 32L * 1024 * 1024;
    private const int MaxPartCount = 10_000;
    private const long MiB = 1024 * 1024;

    public static long CalculatePartSize(long sizeBytes)
    {
        var minimumForPartLimit = (sizeBytes + MaxPartCount - 1) / MaxPartCount;
        var roundedToMiB = (minimumForPartLimit + MiB - 1) / MiB * MiB;
        return Math.Max(MinPartSizeBytes, roundedToMiB);
    }

    public static int CalculatePartCount(long sizeBytes, long partSizeBytes) =>
        (int)((sizeBytes + partSizeBytes - 1) / partSizeBytes);

    public static string BuildStorageKey(Guid matchId, string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        var isSafe = extension.Length is > 0 and <= 10 && extension.All(char.IsAsciiLetterOrDigit);
        return $"matches/{matchId:N}/source{(isSafe ? "." + extension : string.Empty)}";
    }

    public static void ClearUploadState(Match match)
    {
        match.UploadId = null;
        match.UploadPartSizeBytes = null;
        match.VideoStorageKey = null;
        match.VideoFileName = null;
        match.VideoContentType = null;
        match.VideoSizeBytes = null;
        match.VideoLastModifiedMs = null;
    }
}
