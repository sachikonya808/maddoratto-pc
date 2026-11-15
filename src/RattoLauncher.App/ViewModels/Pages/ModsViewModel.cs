using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RattoLauncher.Core.Models;

namespace RattoLauncher.App.ViewModels.Pages;

public partial class ModsViewModel : PageViewModel
{
    public override string Title => "Mods";
    public override Geometry Icon { get; } = Geometry.Parse("M20.5,11H19V7C19,5.89 18.1,5 17,5H13V3.5A2.5,2.5 0 0,0 10.5,1A2.5,2.5 0 0,0 8,3.5V5H4A2,2 0 0,0 2,7V10.8H3.5C5,10.8 6.2,12 6.2,13.5C6.2,15 5,16.2 3.5,16.2H2V20A2,2 0 0,0 4,22H7.8V20.5C7.8,19 9,17.8 10.5,17.8C12,17.8 13.2,19 13.2,20.5V22H17A2,2 0 0,0 19,20V16H20.5A2.5,2.5 0 0,0 23,13.5A2.5,2.5 0 0,0 20.5,11Z");

    private readonly SetupContext _sctx = new SetupContext();

    [ObservableProperty] bool isBusy;
    [ObservableProperty] string status = "";
    public ObservableCollection<string> Log { get; } = new();
    CancellationTokenSource? _cts;

    [RelayCommand]
    async Task InitAsync()
    {
        IsBusy = true; _cts = new();
        try
        {
            var progress = new Progress<int>(n => Status = $"Indexing {n}/1074");   // created on the UI thread, so Report posts back to it
            await _service.InitializeAsync(romfsRoot, pristinePath, ykcmpExe, cacheDir, progress, _cts.Token);
            Status = "Ready";
        }
        catch (OperationCanceledException) { Status = "Cancelled"; }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    async Task BuildAsync()
    {
        IsBusy = true; _cts = new(); Log.Clear();
        try
        {
            var r = await _service.BuildAsync(pngFolder, modsRoot, modName, _encoder, _cts.Token);
            foreach (var s in r.Applied) Log.Add("OK    " + s);
            foreach (var s in r.Skipped) Log.Add("SKIP  " + s);
            Status = r.OutputPath is null ? "Nothing written" : "Done";
        }
        catch (OperationCanceledException) { Status = "Cancelled"; }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand] void Cancel() => _cts?.Cancel();
}