using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RattoLauncher.App.ViewModels;
using RattoLauncher.App.Views;
using RattoLauncher.Core.Services;

namespace RattoLauncher.App;

public partial class App : Application
{
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
                desktop.MainWindow = new MainWindow { DataContext = new MainViewModel() };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}