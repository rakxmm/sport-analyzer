using FootballApp.Application.Common.Interfaces;
using FootballApp.Application.Common.Models;

namespace FootballApp.Application.Tests.Fakes;

/// <summary>Stands in for ffmpeg: "renders" a small placeholder file and remembers what it was asked to cut.</summary>
public class FakeVideoProcessor : IVideoProcessor
{
    public VideoProbeResult ProbeResult { get; set; } = new(5400.7, HasVideo: true, HasAudio: true);
    public Exception? ProbeError { get; set; }
    public Exception? RenderError { get; set; }

    public int ProbeCount { get; private set; }
    public List<IReadOnlyList<ClipSegment>> Renders { get; } = new();
    public List<string> DeletedFiles { get; } = new();

    public Task<VideoProbeResult> ProbeAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        ProbeCount++;
        return ProbeError is null ? Task.FromResult(ProbeResult) : Task.FromException<VideoProbeResult>(ProbeError);
    }

    public Task<RenderedVideo> RenderAsync(string sourceUrl, IReadOnlyList<ClipSegment> segments, CancellationToken cancellationToken)
    {
        Renders.Add(segments);
        if (RenderError is not null)
        {
            return Task.FromException<RenderedVideo>(RenderError);
        }

        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, new byte[1024]);
        return Task.FromResult(new RenderedVideo(path, () =>
        {
            File.Delete(path);
            DeletedFiles.Add(path);
        }));
    }
}
