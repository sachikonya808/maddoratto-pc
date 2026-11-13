namespace RattoLauncher.App.ViewModels.Pages;

public abstract class PlaceholderPageViewModel : PageViewModel
{
    public string Message => $"{Title} is coming soon...";
}

// Placeholders! To make a real one, delete the placeholder class create the real file (ie. Delete PlayViewModel, then
// create: ViewModels/Pages/PlayViewModel.cs, Views/Pages/PlayView.axaml and Views/Pages/PlayView.axaml.cs)
// Finally, add the data template to MainWindow.axaml

public class ConfigViewModel : PlaceholderPageViewModel
{
    public override string Title => "Configuration";
    public override string Icon => "⚙";
}

public class ModsViewModel : PlaceholderPageViewModel
{
    public override string Title => "Mods";
    public override string Icon => "✚";
}

public class AboutViewModel : PlaceholderPageViewModel
{
    public override string Title => "About";
    public override string Icon => "ℹ";
}