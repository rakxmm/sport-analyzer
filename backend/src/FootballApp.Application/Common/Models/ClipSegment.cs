namespace FootballApp.Application.Common.Models;

/// <summary>A stretch of the source video, in whole seconds from its start.</summary>
public record ClipSegment(int StartSeconds, int EndSeconds)
{
    public int DurationSeconds => EndSeconds - StartSeconds;
}
