using System.Diagnostics;

namespace BoltZip.Core.Infrastructure;

/// <summary>
/// Minimal helper for invoking short-lived external processes (e.g. PowerShell CIM
/// queries used for best-effort hardware discovery). Never throws; returns an empty
/// string on any failure or timeout.
/// </summary>
internal static class ProcessRunner
{
    public static async Task<string> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken,
        int timeoutMs = 8000)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return string.Empty;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return string.Empty;
            }

            return await stdoutTask.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best-effort cleanup only
        }
    }
}
