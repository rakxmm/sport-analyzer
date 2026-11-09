using System.ComponentModel;
using System.Diagnostics;
using FootballApp.Application.Common.Exceptions;

namespace FootballApp.Infrastructure.Processing;

internal record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ProcessRunner
{
    /// <summary>
    /// Runs a command to completion and captures its output. Arguments are passed as a list, never
    /// through a shell. The process is killed if the caller cancels or the timeout runs out.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new VideoProcessingException($"Could not start '{fileName}' — is ffmpeg installed and on PATH? {ex.Message}");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        // Both pipes are drained while waiting, otherwise a chatty process blocks on a full buffer.
        var standardOutput = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var standardError = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Exited on its own in the meantime.
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new VideoProcessingException($"'{fileName}' did not finish within {timeout.TotalMinutes:0} minutes and was stopped.");
        }
    }
}
