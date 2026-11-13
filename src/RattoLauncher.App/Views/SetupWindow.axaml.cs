using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RattoLauncher.Core.Models;
using RattoLauncher.Core.Services;
using Avalonia.Interactivity;
using System.Linq;

namespace RattoLauncher.App.Views;

public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        if(sender is not Button button || button.Parent is not Grid grid) { return; }

        var textBox = grid.Children.OfType<TextBox>().FirstOrDefault();
        if(textBox is null) { return; }

        string pickerTitle = button.Tag?.ToString() ?? "Select a folder";

        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = pickerTitle,
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            textBox.Text = folders[0].Path.LocalPath;
        }
    }

    private async void FilePicker_Click(object? sender, RoutedEventArgs e)
    {
        if(sender is not Button button || button.Parent is not Grid grid) { return; }

        var textBox = grid.Children.OfType<TextBox>().FirstOrDefault();
        if(textBox is null) { return; }

        string pickerTitle = button.Tag?.ToString() ?? "Oh no! If you see this, this is a bug!";

        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = pickerTitle,
                AllowMultiple = false
            }
        );

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            textBox.Text = path;
        }
    }

    private void FinishButton_Click(object? sender, RoutedEventArgs e)
    {
        var config = new AppConfig
        {
            BaseGame = BaseGameTextBox.Text ?? "",
            Update = GameUpdateTextBox.Text ?? "",
            Keys = SystemKeysTextBox.Text ?? "",
            InstallationFolder = InstallationPathTextBox.Text ?? "",
            SetupCompleted = true
        };

        ConfigService.Save(config);

        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();

            desktop.MainWindow = mainWindow;
            mainWindow.Show();

            Close();
        }
    }
}