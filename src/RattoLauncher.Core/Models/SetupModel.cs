namespace RattoLauncher.Core.Models;

public record StepProgress(string Message, double? Fraction = null);

public interface ISetupStep
{
    string Title { get; }
    Task<bool> IsDoneAsync(CancellationToken ct);
    Task RunAsync(IProgress<StepProgress> progress, IProgress<string> log, CancellationToken ct);
}

public class SetupContext
{
    public string? InstallDir { get; private set; } = AppContext.BaseDirectory;
    public string? BaseGamePath { get; private set; }
    public string? GameUpdatePath { get; private set; }
    public string? SystemKeysPath { get; private set; }

    public string EngineDir => Path.Combine(InstallDir, "engine");
    public string GameDataDir => Path.Combine(InstallDir, "game_data");
    public string EngineExec => Path.Combine(EngineDir, "bin", "scarletnx.exe");

    public void Apply(AppConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.InstallationFolder)) { InstallDir = cfg.InstallationFolder; }
        
        BaseGamePath = cfg.BaseGame;
        GameUpdatePath = cfg.Update;
        SystemKeysPath = cfg.Keys;
    }
}
