namespace FootballApp.Application.Matches.VideoUpload;

/// <summary>What the browser needs to (re)start uploading parts — CompletedPartNumbers lets it skip parts R2 already has.</summary>
public record UploadSessionDto(
    Guid MatchId,
    string FileName,
    long SizeBytes,
    long PartSizeBytes,
    int PartCount,
    IReadOnlyList<int> CompletedPartNumbers);
