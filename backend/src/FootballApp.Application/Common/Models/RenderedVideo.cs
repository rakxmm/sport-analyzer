namespace FootballApp.Application.Common.Models;

/// <summary>A rendered clip on the worker's local disk. Disposing it removes the file and its scratch space.</summary>
public sealed class RenderedVideo(string filePath, Action cleanup) : IDisposable
{
    public string FilePath { get; } = filePath;

    public void Dispose() => cleanup();
}
