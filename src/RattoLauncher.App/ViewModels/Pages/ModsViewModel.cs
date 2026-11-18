using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RattoLauncher.Core;
using RattoLauncher.Core.AnimeBuilder;
using RattoLauncher.Core.Models;
using RattoLauncher.Core.Services;

namespace RattoLauncher.App.ViewModels.Pages;

public partial class ModsViewModel : PageViewModel
{
    private readonly ModAnimeBuild _anime = new();

    public override string Title => "Mods";
    public override Geometry Icon { get; } = Geometry.Parse("M20.5,11H19V7C19,5.89 18.1,5 17,5H13V3.5A2.5,2.5 0 0,0 10.5,1A2.5,2.5 0 0,0 8,3.5V5H4A2,2 0 0,0 2,7V10.8H3.5C5,10.8 6.2,12 6.2,13.5C6.2,15 5,16.2 3.5,16.2H2V20A2,2 0 0,0 4,22H7.8V20.5C7.8,19 9,17.8 10.5,17.8C12,17.8 13.2,19 13.2,20.5V22H17A2,2 0 0,0 19,20V16H20.5A2.5,2.5 0 0,0 23,13.5A2.5,2.5 0 0,0 20.5,11Z");

    public string PackedModsFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RattoLauncher", "mods");
    private const string OutputModName = "RattoLauncher Textures";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InitializeCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private bool _isReady;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private string _modName = "";

    [ObservableProperty] private string _status = "Not prepared yet.";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private string _details;

    // ---- commands -----------------------------------------------------------------

    private bool CanInitialize() => !IsBusy;

    private bool CanBuild() => !IsBusy && IsReady;

    private bool CanCancel() => IsBusy;

    [RelayCommand]
    private void OpenModsFolder()
    {
        Directory.CreateDirectory(PackedModsFolder);
        Process.Start(new ProcessStartInfo(PackedModsFolder) { UseShellExecute = true }); 
    }

    [RelayCommand(CanExecute = nameof(CanInitialize), IncludeCancelCommand = true)]
    private async Task InitializeAsync(CancellationToken ct)
    {
        var cfg = ConfigService.Load();
        if (cfg is not { SetupCompleted: true })
        {
            Status = "Complete the setup first.";
            return;
        }

        IsBusy = true; IsReady = false; Details = null; Progress = 0;
        var progress = new Progress<ModOperationProgress>(Apply);
        try
        {
            var paths = ResolvePaths(cfg);
            await _anime.InitializeAsync(paths.RomfsRoot, paths.PristineDat, paths.YkcmpExe, paths.CacheDir, progress, ct);
            IsReady = true;
            Progress = 1;
            Status = "Ready.";
        }
        catch (OperationCanceledException) { Status = "Cancelled."; }
        catch (PreflightException ex)
        {
            Status = "The animation file didn't pass validation.";
            Details = string.Join("\n", ex.Problems);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { IsBusy = false; IsIndeterminate = false; }
    }

    [RelayCommand(CanExecute = nameof(CanBuild), IncludeCancelCommand = true)]
    private async Task BuildAsync(CancellationToken ct)
    {
        var cfg = ConfigService.Load();
        if (cfg is not { SetupCompleted: true })
        {
            Status = "Complete the setup first.";
            return;
        }

        IsBusy = true; Details = null; Progress = 0;
        var progress = new Progress<ModOperationProgress>(Apply);
        try
        {
            var paths = ResolvePaths(cfg);
            var report = await _anime.BuildAsync(PackedModsFolder, paths.ModsRoot, OutputModName, CreateEncoder(), progress, ct);

            Progress = 1;
            Status = report.OutputPath is null
                ? "Nothing was applied. See the details below."
                : "Mod built.";
            Details = FormatReport(report);
        }
        catch (OperationCanceledException) { Status = "Cancelled."; }
        catch (Exception ex) { ShowError(ex); }
        finally { IsBusy = false; IsIndeterminate = false; }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        InitializeCancelCommand.Execute(null);
        BuildCancelCommand.Execute(null);
    }

    // ---- helpers ------------------------------------------------------------------

    private void Apply(ModOperationProgress p)
    {
        Status = p.Stage;
        IsIndeterminate = p.Fraction is null;
        if (p.Fraction is double f) Progress = f;
    }

    private void ShowError(Exception ex)
    {
        var text = ex.Message.Trim();
        var newline = text.IndexOf('\n');
        Status = newline < 0 ? text : text[..newline].TrimEnd();
        Details = text;
    }

    private static string FormatReport(BuildReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Applied: {r.Applied.Count}    Skipped: {r.Skipped.Count}");
        if (r.OutputPath is not null) sb.AppendLine($"Output: {r.OutputPath}");

        if (r.Skipped.Count > 0)
        {
            sb.AppendLine().AppendLine("Skipped:");
            foreach (var s in r.Skipped) sb.AppendLine("  " + s);
        }
        if (r.Applied.Count > 0)
        {
            sb.AppendLine().AppendLine("Applied:");
            foreach (var a in r.Applied) sb.AppendLine("  " + a);
        }
        return sb.ToString();
    }

    private sealed record ModPaths(string RomfsRoot, string PristineDat, string YkcmpExe, string CacheDir, string ModsRoot);

    private static ModPaths ResolvePaths(AppConfig cfg)
    {
        return new ModPaths(
            RomfsRoot: Path.Combine(cfg.InstallationFolder, "game_data", "romfs"),
            PristineDat: Path.Combine(cfg.InstallationFolder, "game_data", "romfs", "Data", "Anime", "Anime_EN.dat"),
            YkcmpExe: Path.Combine(cfg.InstallationFolder, "modtools", "ykcmp.exe"),
            CacheDir: Path.Combine(cfg.InstallationFolder, "cache"),
            ModsRoot: Path.Combine(cfg.InstallationFolder, "0100E6701231C000", "0100E6701231C000_data", "mods")
        );
    }

    private static IBcEncoder CreateEncoder() => new BcnEncoder();
}