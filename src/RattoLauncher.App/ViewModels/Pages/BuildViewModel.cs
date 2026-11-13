using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RattoLauncher.Core.Models;
using RattoLauncher.Core.Services;

namespace RattoLauncher.App.ViewModels.Pages;

public partial class BuildViewModel : PageViewModel
{
    private const string EngineUrl =
        "https://github.com/scarletnx/nightly/releases/latest/download/ScarletNX-windows-x86_64.zip";

    private readonly SetupContext _ctx = new SetupContext();

    public override string Title => "Build";
    public override string Icon => "🛠"; 

    public ObservableCollection<StepViewModel> Steps { get; }
    public ObservableCollection<string> Log { get; } = new ObservableCollection<string>();

    [ObservableProperty] private string _summary = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunAllCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunAllCommand))]
    private bool _setupDone;

    public BuildViewModel()
    {
        Steps = new ObservableCollection<StepViewModel>
        {
            new StepViewModel(new DownloadEngine(_ctx, EngineUrl, sha256: null)),
            new StepViewModel(new ExtractGame(_ctx)),
            new StepViewModel(new BuildRecomp(_ctx))
        };
        Reload();
    }

    [RelayCommand]
    private void Reload()
    {
        var cfg = ConfigService.Load();
        if (cfg is null || !cfg.SetupCompleted)
        {
            SetupDone = false;
            Summary = "Setup hasn't been completed yet.";
            return;
        }

        _ctx.Apply(cfg);
        SetupDone = true;
        Summary =
            $"Install folder: {_ctx.InstallDir}\n" +
            $"Base game: {_ctx.BaseGamePath}\n" +
            $"Update: {_ctx.GameUpdatePath}\n" +
            $"Keys: {_ctx.SystemKeysPath}";
    }

    private bool CanRun() => SetupDone && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun), IncludeCancelCommand = true)]
    private async Task RunAllAsync(CancellationToken ct)
    {
        Reload();
        if (!SetupDone) { Log.Add("Complete the setup first."); return; }

        IsBusy = true;
        Log.Clear();
        foreach (var s in Steps) { s.Status = StepStatus.Pending; s.Detail = ""; s.Progress = 0; }

        var log = new Progress<string>(line =>
        {
           Log.Add(line);
           if(Log.Count > 1000) Log.RemoveAt(0);
        });

        StepViewModel? current = null;
        try
        {
            foreach (var s in Steps)
            {
                current = s;
                ct.ThrowIfCancellationRequested();

                if (await s.Step.IsDoneAsync(ct))
                {
                    s.Status = StepStatus.Done;
                    s.Detail = "Done!";
                    s.Progress = 1;
                    continue;
                }

                s.Status = StepStatus.Running;
                s.IsIndeterminate = true;
                var progress = new Progress<StepProgress>(p =>
                {
                   s.Detail = p.Message;
                   s.IsIndeterminate = p.Fraction is null;
                   if(p.Fraction is double f) { s.Progress = f; }
                });

                await Task.Run(() => s.Step.RunAsync(progress, log, ct), ct);
                
                s.Status = StepStatus.Done;
                s.IsIndeterminate = false;
                s.Progress = 1;
            }
            Log.Add("All done!");
        }
        catch (OperationCanceledException)
        {
            if (current != null) { current.Status = StepStatus.Pending; current.IsIndeterminate = false; }
            Log.Add("Cancelled.");
        }
        catch (Exception ex)
        {
            if (current != null) { current.Status = StepStatus.Failed; current.IsIndeterminate = false; current.Detail = ex.Message; }
            Log.Add($"ERROR: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
