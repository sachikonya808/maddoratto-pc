using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RattoLauncher.App.Class;
using RattoLauncher.Core;
using RattoLauncher.Core.Models;
using RattoLauncher.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RattoLauncher.App.ViewModels.Pages
{
    internal partial class SongsViewModel : PageViewModel
    {
        public override string Title => "Songs";
        public override Geometry Icon { get; } = Geometry.Parse("M4,6H2V20A2,2 0 0,0 4,22H18V20H4M18,7H15V12.5A2.5,2.5 0 0,1 12.5,15A2.5,2.5 0 0,1 10,12.5A2.5,2.5 0 0,1 12.5,10C13.07,10 13.58,10.19 14,10.5V5H18M20,2H8A2,2 0 0,0 6,4V16A2,2 0 0,0 8,18H20A2,2 0 0,0 22,16V4A2,2 0 0,0 20,2Z");

        public string PackedModsFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RattoLauncher", "mods", "songs");
        private const string OutputModName = "RattoLauncher Songs";


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
        private bool _isReady;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
        private string _modName = "";

        [ObservableProperty] private string _status = "Not prepared yet.";
        [ObservableProperty] private double _progress;
        [ObservableProperty] private bool _isIndeterminate;
        [ObservableProperty] private string _details;

        public ObservableCollection<SongPackage> Packages { get; } = new();
        private SongPackage? _selectedPackage;

        public SongsViewModel()
        {
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            var cfg = ConfigService.Load();
            if (cfg is not { SetupCompleted: true })
            {
                Status = "Complete the setup first.";
                return;
            }

            IsBusy = true; IsReady = false; Details = null;
            try
            {
                Directory.CreateDirectory(PackedModsFolder);
                await LoadPackagesAsync(PackedModsFolder);
            }
            catch (Exception ex)
            {
                Details = $"Failed to load song packages: {ex.Message}";
            }
            finally { IsBusy = false; }
            Status = "Ready";
            IsReady = true;
        }

        // ---- commands -----------------------------------------------------------------

        private bool CanBuild() => !IsBusy && IsReady;
        private bool CanCancel() => IsBusy;

        [RelayCommand]
        private void OpenModsFolder()
        {
            Directory.CreateDirectory(PackedModsFolder);
            Process.Start(new ProcessStartInfo(PackedModsFolder) { UseShellExecute = true });
        }

        [RelayCommand]
        private void OpenLayeredFSFolder()
        {
            var cfg = ConfigService.Load();
            if (cfg is not { SetupCompleted: true })
            {
                Status = "Complete the setup first.";
                return;
            }

            var root = ResolvePaths(cfg).ModsRoot; 
            Directory.CreateDirectory(root); 
            Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
        }

        [RelayCommand]
        private void UnlockSave()
        {
            var cfg = ConfigService.Load();
            if (cfg is not { SetupCompleted: true }) { Status = "Complete the setup first."; return; }

            try
            {
                var folder = Path.Combine(cfg.InstallationFolder, "unlocker");
                Directory.CreateDirectory(folder);

                var sound = Path.Combine(ResolvePaths(cfg).ModsRoot, OutputModName,
                                         "romfs", "Data", "Database", "Sound.dat");

                var log = SaveUnlocker.UnlockRange(folder, sound);
                Status = "Save unlocked. Copy 'System' from the unlocker folder into your game's save folder.";
                Details = string.Join(Environment.NewLine, log);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        [RelayCommand(CanExecute = nameof(CanBuild), IncludeCancelCommand = true)]
        private async Task BuildAsync(CancellationToken ct)
        {
            
            var cfg = ConfigService.Load();
            if (cfg is not { SetupCompleted: true })
            {
                Status = "Complete the setup first.";
                return;
            }

            IsBusy = true; Details = null; Progress = 0;
            var progress = new Progress<ModOperationProgress>(Apply);
            try
            {
                var paths = ResolvePaths(cfg);

                var zips = Packages.Select(p => p.ZipPath).ToList();
                var report = await ModSongsBuild.BuildAsync(paths.RomfsRoot, paths.ModsRoot, OutputModName, zips, progress, ct);

                Progress = 1;
                Status = report.OutputPath is null ? "Nothing was applied. See the details below." : "Songs added.";
                Details = FormatReport(report);
            }
            catch (OperationCanceledException) { Status = "Cancelled."; }
            catch (Exception ex) { ShowError(ex); }
            finally { IsBusy = false; IsIndeterminate = false; }
        }

        [RelayCommand(CanExecute = nameof(CanCancel))]
        private void Cancel()
        {
            BuildCancelCommand.Execute(null);
        }

        // ---- helpers ------------------------------------------------------------------

        private void Apply(ModOperationProgress p)
        {
            Status = p.Stage;
            IsIndeterminate = p.Fraction is null;
            if (p.Fraction is double f) Progress = f;
        }

        private void ShowError(Exception ex)
        {
            var text = ex.Message.Trim();
            var newline = text.IndexOf('\n');
            Status = newline < 0 ? text : text[..newline].TrimEnd();
            Details = text;
        }

        private static string FormatReport(SongBuildReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Applied: {r.Applied.Count}    Skipped: {r.Skipped.Count}");
            if (r.OutputPath is not null) sb.AppendLine($"Output: {r.OutputPath}");
            void Section(string title, IReadOnlyList<string> items)
            {
                if (items.Count == 0) return;
                sb.AppendLine().AppendLine(title);
                foreach (var s in items) sb.AppendLine("  " + s);
            }
            Section("Skipped:", r.Skipped);
            Section("Warnings:", r.Warnings);
            Section("Applied:", r.Applied);
            return sb.ToString();
        }

        public SongPackage? SelectedPackage
        {
            get => _selectedPackage;
            set
            {
                if (_selectedPackage == value) { return; }

                _selectedPackage = value;
                OnPropertyChanged(nameof(SelectedPackage));
            }
        }

        public async Task LoadPackagesAsync(string path)
        {
            var scanner = new SongPackageScanner();

            var packages = await Task.Run(() => scanner.Scan(path));

            Packages.Clear();

            foreach (var package in packages)
            {
                if(package.ImageData != null)
                {
                    using var stream = new MemoryStream(package.ImageData);
                    package.Image = new Bitmap(stream);
                }

                Packages.Add(package);
            }

            SelectedPackage = Packages.FirstOrDefault();
        }

        private sealed record ModPaths(string RomfsRoot, string ModsRoot);
        private static ModPaths ResolvePaths(AppConfig cfg) => new(
            RomfsRoot: Path.Combine(cfg.InstallationFolder, "game_data", "romfs"),
            ModsRoot: Path.Combine(cfg.InstallationFolder, "0100E6701231C000", "0100E6701231C000_data", "mods"));
    }
}
