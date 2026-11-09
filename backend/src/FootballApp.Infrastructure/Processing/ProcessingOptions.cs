using System.ComponentModel.DataAnnotations;

namespace FootballApp.Infrastructure.Processing;

/// <summary>Worker settings (section "Processing").</summary>
public class ProcessingOptions
{
    public const string SectionName = "Processing";

    /// <summary>Executable name or full path. The worker's Docker image has both on PATH.</summary>
    [Required]
    public string FfmpegPath { get; set; } = "ffmpeg";

    [Required]
    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>How many jobs run at once. Encoding is CPU-bound, so keep it near the container's core count.</summary>
    [Range(1, 16)]
    public int WorkerCount { get; set; } = 2;
}
