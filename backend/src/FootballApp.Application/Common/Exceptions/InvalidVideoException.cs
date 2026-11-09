namespace FootballApp.Application.Common.Exceptions;

/// <summary>
/// The source file itself is the problem (not a video, corrupt, no picture), so retrying cannot help.
/// The message is shown to the match owner.
/// </summary>
public class InvalidVideoException(string message) : Exception(message);
