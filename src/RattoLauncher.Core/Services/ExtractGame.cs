using RattoLauncher.Core.Models;

namespace RattoLauncher.Core.Services;

public class ExtractGame(SetupContext context) : ISetupStep
{
    public string Title => "Extract the game";

    public Task<bool> IsDoneAsync(CancellationToken ct)
    {
        var dir = Path.Combine(context.InstallDir, "game_data");
        return Task.FromResult(Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.jpg").Any());
    }

    public async Task RunAsync(IProgress<StepProgress> progress, IProgress<string> log, CancellationToken ct)
    {
       var args = new List<string>
        {
            "--data", context.InstallDir,
            "--keys", context.SystemKeysPath,
            "--no-setup",
            "--extract", context.BaseGamePath,
            "--update", context.GameUpdatePath,
            "--out", Path.Combine(context.InstallDir, "game_data"),
        };

        progress.Report(new StepProgress("Extracting the game assets. This can take a while..."));
        var code = await ProcessRunner.RunAsync(context.EngineExec, args, context.EngineDir, log, ct);
        if(code != 0) { throw new InvalidOperationException($"ScarletNX exited with code {code}. Check the log."); }
    }
}
