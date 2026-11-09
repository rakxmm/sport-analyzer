using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Models;

namespace FootballApp.Application.Common.Interfaces;

/// <summary>
/// Inspects and cuts video (ffprobe/ffmpeg in Infrastructure). Only the worker calls it; the source is
/// read through a signed URL so a full match never has to be downloaded first.
/// </summary>
public interface IVideoProcessor
{
    /// <exception cref="InvalidVideoException">The source is not a readable video file.</exception>
    Task<VideoProbeResult> ProbeAsync(string sourceUrl, CancellationToken cancellationToken);

    /// <summary>Cuts the segments out of the source and joins them, in the given order, into one local MP4.</summary>
    /// <exception cref="InvalidVideoException">The source is not a readable video file.</exception>
    Task<RenderedVideo> RenderAsync(string sourceUrl, IReadOnlyList<ClipSegment> segments, CancellationToken cancellationToken);
}
