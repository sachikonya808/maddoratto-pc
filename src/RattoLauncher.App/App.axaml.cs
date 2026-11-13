using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RattoLauncher.App.ViewModels;
using RattoLauncher.App.Views;
using RattoLauncher.Core.Services;

namespace RattoLauncher.App;

public partial class App : Application
{
    public static MainWindow CreateMainWindow() => new() { DataContext = new MainViewModel() };

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var config = ConfigService.Load();

            if (config is null || !config.SetupCompleted)
            {
                desktop.MainWindow = new SetupWindow { DataContext = new SetupViewModel() };
            }
            else
            {
                desktop.MainWindow = CreateMainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}