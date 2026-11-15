using System.Diagnostics;

namespace RattoLauncher.Core.Services;

public static class ProcessRunner
{
    public static async Task<int> RunAsync(
        string exePath, IEnumerable<string> args,
        string workingDir, IProgress<string> output, CancellationToken ct
    )
    {
        var psi = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var a in args) { psi.ArgumentList.Add(a); }

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.Report(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.Report(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var reg = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });

        await process.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        return process.ExitCode;
    }
}
