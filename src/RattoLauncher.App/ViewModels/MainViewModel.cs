using System.Collections.Generic;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using RattoLauncher.App.ViewModels.Pages;

namespace RattoLauncher.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public IReadOnlyList<PageViewModel> Pages { get; }

    [ObservableProperty]
    private PageViewModel _currentPage;

    public MainViewModel()
    {
        var play = new PlayViewModel();
        var build = new BuildViewModel();

        Pages = new PageViewModel[]
        {
            play, build, new ConfigViewModel(), new ModsViewModel(), new AboutViewModel()
        };
        play.BuildRequested += () => CurrentPage = build;

        _currentPage = Pages[0];
    }

    partial void OnCurrentPageChanged(PageViewModel value)
    {
        if (value is PlayViewModel p) { p.Refresh(); }
    }
}
