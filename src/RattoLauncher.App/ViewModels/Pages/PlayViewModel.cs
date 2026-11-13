using System;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RattoLauncher.Core.Services;

namespace RattoLauncher.App.ViewModels.Pages;

public enum LauncherState { NeedsSetup, Ready, Running }

public partial class PlayViewModel : PageViewModel
{
    private string? _exePath;
    private Process? _game;

    public override string Title => "Play";
    public override string Icon => "▶";

    public event Action? BuildRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeadlineText), nameof(StatusText), nameof(ButtonText))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    private LauncherState _state = LauncherState.NeedsSetup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _error;

    public string HeadlineText => State switch
    {
        LauncherState.Ready => "Ready to RAT?",
        LauncherState.NeedsSetup => "Setup needed",
        _ => "Running..."
    };

    public string StatusText => Error ?? State switch
    {
        LauncherState.Ready => "Your game is ready to launch!",
        LauncherState.Running => "Game is already running!",
        _ => "Your game hasn't been built yet! (Open the Build tab)"
    };

    public string ButtonText => State switch
    {
        LauncherState.Ready => "PLAY",
        LauncherState.Running => "RUNNING",
        _ => "BUILD"
    };

    public PlayViewModel() => Refresh();

    public void Refresh()
    {
        if (State == LauncherState.Running) { return; }
        
        Error = null;
        var cfg = ConfigService.Load();
        _exePath = cfg is { SetupCompleted: true }
            ? GameLocator.FindGameExecutable(cfg.InstallationFolder)
            : null;

        State = _exePath is null ? LauncherState.NeedsSetup : LauncherState.Ready;
    }

    private bool CanAct() => State != LauncherState.Running;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void PrimaryAction()
    {
        if (State == LauncherState.NeedsSetup) { BuildRequested?.Invoke(); }
        else if(State == LauncherState.Ready) { Launch(); }
    }

    private void Launch()
    {
        if (_exePath is null) { return; }
        
        Error = null;
        try
        {
            _game = GameLauncher.Start(_exePath, code => Dispatcher.UIThread.Post(() => OnGameExited(code)));
            State = LauncherState.Running;
        }
        catch (Exception ex)
        {
            Error = $"Couldn't start the game: {ex.Message}";
        }
    }

    private void OnGameExited(int exitCode)
    {
        _game?.Dispose();
        _game = null;

        State = LauncherState.Ready;
        Refresh();
        
        if(exitCode != 0) { Error = $"The game closed with exit code {exitCode}. Check the logs folder for more info!"; }
    }
}
