using System;
using System.Diagnostics;
using Avalonia.Media;
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
    public override Geometry Icon { get; } = Geometry.Parse("M10 16.5L16 12L10 7.5M22 12C22 6.46 17.54 2 12 2C10.83 2 9.7 2.19 8.62 2.56L9.32 4.5C10.17 4.16 11.06 3.97 12 3.97C16.41 3.97 20.03 7.59 20.03 12C20.03 16.41 16.41 20.03 12 20.03C7.59 20.03 3.97 16.41 3.97 12C3.97 11.06 4.16 10.12 4.5 9.28L2.56 8.62C2.19 9.7 2 10.83 2 12C2 17.54 6.46 22 12 22C17.54 22 22 17.54 22 12M5.47 3.97C6.32 3.97 7 4.68 7 5.47C7 6.32 6.32 7 5.47 7C4.68 7 3.97 6.32 3.97 5.47C3.97 4.68 4.68 3.97 5.47 3.97Z");

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
