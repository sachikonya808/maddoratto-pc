using RattoLauncher.Core.AnimeBuilder;

namespace RattoLauncher.Core.Services;

public sealed class PreflightException(IReadOnlyList<string> problems) : Exception("The animation file failed validation:\n" + string.Join("\n", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;   // so a UI can list them
}

public sealed class ModAnimeBuild : IDisposable
{
    private AnimeDat? _dat;
    private TextureIndex? _index;
    private Ykcmp? _yk;
    private string _rel = "";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsReady => Volatile.Read(ref _index) != null;

    public Task InitializeAsync(string layeredfsRoot, string pristinePath, string ykcmpExe, string cacheDir,
                                IProgress<ModOperationProgress>? progress = null, CancellationToken ct = default) => Task.Run(async () =>
        {
            await _gate.WaitAsync(ct);
            try
            {
                ResetState();

                if (!File.Exists(ykcmpExe))
                    throw new FileNotFoundException("The ykcmp tool wasn't found.", ykcmpExe);
                if (!File.Exists(pristinePath))
                    throw new FileNotFoundException("The original animation file wasn't found.", pristinePath);

                progress?.Report(new("Checking the animation file", 0, 0));

                var yk = new Ykcmp(ykcmpExe);
                var dat = new AnimeDat(pristinePath);

                var problems = dat.Preflight();
                if (problems.Count > 0) { throw new PreflightException(problems); }
                
                // TODO: compare TextureIndex.Fingerprint against a hard-coded pristine value

                ct.ThrowIfCancellationRequested();

                var rel = Path.GetRelativePath(layeredfsRoot, pristinePath).Replace('\\', '/');
                var index = TextureIndex.LoadOrBuild(dat, yk, cacheDir, Adapt(progress, "Indexing textures"));

                _yk = yk; 
                _dat = dat; 
                _rel = rel; 
                _index = index;
            }
            finally { _gate.Release(); }
        }, ct);

    public Task<BuildReport> BuildAsync(string pngFolder, string modsRoot, string modName, IBcEncoder enc,
                                        IProgress<ModOperationProgress>? progress = null, CancellationToken ct = default) => Task.Run(async () =>
        {
            await _gate.WaitAsync(ct);

            try
            {
                if (_dat is null || _index is null || _yk is null)
                    throw new InvalidOperationException("Prepare the animation tools first.");
                if (!Directory.Exists(pngFolder))
                    throw new DirectoryNotFoundException($"Folder not found: {pngFolder}");
                if (string.IsNullOrWhiteSpace(modName) || modName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new ArgumentException("Enter a valid mod name.", nameof(modName));

                var pngs = Directory.EnumerateFiles(pngFolder, "*.png").ToList();
                if (pngs.Count == 0) { throw new InvalidOperationException("That folder has no .png files."); }

                return new Builder(_dat, _index, _yk, enc, _rel).Build(pngs, modsRoot, modName, progress: progress, ct: ct);
            }
            finally { _gate.Release(); }
        }, ct);

    public void Dispose()
    {
        ResetState();
        _gate.Dispose();
    }

    private void ResetState()
    {
        _dat = null; 
        _yk = null; 
        _index = null; 
        _rel = "";
    }

    private static IProgress<int>? Adapt(IProgress<ModOperationProgress>? p, string stage) =>
        p is null ? null : new InlineProgress<int>(pct => p.Report(new ModOperationProgress(stage, pct, 100)));
}