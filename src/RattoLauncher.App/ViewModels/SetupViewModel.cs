using CommunityToolkit.Mvvm.ComponentModel;

namespace RattoLauncher.App.ViewModels;

public partial class SetupViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Setup { get; set; } = "Welcome to Avalonia!";
}
