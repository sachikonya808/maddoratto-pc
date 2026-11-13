namespace RattoLauncher.App.ViewModels;

public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public abstract string Icon { get; }
}