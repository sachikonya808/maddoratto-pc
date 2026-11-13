using CommunityToolkit.Mvvm.ComponentModel;
using RattoLauncher.Core.Models;

namespace RattoLauncher.App.ViewModels.Pages;

public enum StepStatus { Pending, Running, Done, Failed }

public partial class StepViewModel(ISetupStep step) : ObservableObject
{
    public ISetupStep Step { get; } = step;
    public string Title => Step.Title;

    [ObservableProperty] private StepStatus _status = StepStatus.Pending;
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isIndeterminate;
}
