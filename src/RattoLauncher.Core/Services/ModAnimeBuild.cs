using RattoLauncher.Core.AnimeBuilder;

namespace RattoLauncher.Core.Services;

public sealed class PreflightException(IReadOnlyList<string> problems) : Exception("The animation file failed validation:\n" + string.Join("\n", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;   // so a UI can list them
}

public sealed class ModAnimeBuild : IDisposable
{
    private static readonly string[] KnownAnimeDatHashes =
    {
        "AF78844E423EDDAAB86D385048785A4B751BD581806AB2186CA32DB409EA89A7",

        //PS4 HD using XDelta, while I work out how to do BC1, RGBA8 and Multi Mipmap
        "E0080310F58BD8FDD252EE60E2AB741622A2BA266FE5CEA2C5BDAFA96EEEA05A"
    };

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
                    throw new FileNotFoundException("The ykcmp tool wasn't found. (Download it from https://github.com/iltrof/ykcmp/releases/latest and place it inside your InstallFolder/modtools)", ykcmpExe);
                if (!File.Exists(pristinePath))
                    throw new FileNotFoundException("The original animation file wasn't found.", pristinePath);

                progress?.Report(new("Checking the animation file", 0, 0));

                var yk = new Ykcmp(ykcmpExe);
                var dat = new AnimeDat(pristinePath);

                var problems = dat.Preflight();
                if (problems.Count > 0) { throw new PreflightException(problems); }
                
                var hashProgress = progress is null 
                    ? null
                    : new InlineProgress<double>(f => progress.Report(new ModOperationProgress("Verifying Anime_EN.dat", (int)(f * 1000), 1000)));
                var actual = await Hashing.Sha256Async(pristinePath, hashProgress, ct);

                if (!KnownAnimeDatHashes.Contains(actual, StringComparer.OrdinalIgnoreCase))
                {
                    throw new PreflightException(
                    [
                        "This Anime_EN.dat isn't the version the mod builder supports.",
                        $"Its SHA-256 is {actual}."
                    ]);
                }


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