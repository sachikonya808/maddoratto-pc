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

        
    public static void Run(string _exe, params string[] args)
    {
        var psi = new ProcessStartInfo(_exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        
        foreach (var a in args) psi.ArgumentList.Add(a);
        
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start ykcmp");
        
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        
        p.WaitForExit();
        
        if (p.ExitCode != 0) throw new InvalidOperationException($"ykcmp failed ({p.ExitCode}): {se.Result} {so.Result}");
    }
}
