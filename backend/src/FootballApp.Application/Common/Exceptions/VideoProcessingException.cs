namespace FootballApp.Application.Common.Exceptions;

/// <summary>
/// ffmpeg/ffprobe failed for a reason that may be temporary (network, storage, timeout), so the job is
/// worth retrying. The message is for logs only — never show it to users.
/// </summary>
public class VideoProcessingException(string message) : Exception(message);
