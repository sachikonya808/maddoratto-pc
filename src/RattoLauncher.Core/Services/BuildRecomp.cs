using RattoLauncher.Core.Models;

namespace RattoLauncher.Core.Services;

public class BuildRecomp(SetupContext context) : ISetupStep
{
    public string Title => "Build the game";

    public Task<bool> IsDoneAsync(CancellationToken ct)
    {
        var dir = Path.Combine(context.InstallDir, "0100E6701231C000");
        return Task.FromResult(Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.exe").Any());
    }

    public async Task RunAsync(IProgress<StepProgress> progress, IProgress<string> log, CancellationToken ct)
    {
        var args = new List<string>
        {
            "--data", context.InstallDir,
            "--no-setup",
            "--recompile", Path.Combine(context.InstallDir, "game_data"),
            "--outdir", context.InstallDir
        };

        progress.Report(new StepProgress("Recompiling. This can take a while..."));
        var code = await ProcessRunner.RunAsync(context.EngineExec, args, context.EngineDir, log, ct);
        if(code != 0) { throw new InvalidOperationException($"ScarletNX exited with code {code}. Check the log."); }
    }
}
