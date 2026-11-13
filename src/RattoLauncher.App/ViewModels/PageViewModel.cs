using Avalonia.Media;

namespace RattoLauncher.App.ViewModels;

public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public abstract Geometry Icon { get; }
}