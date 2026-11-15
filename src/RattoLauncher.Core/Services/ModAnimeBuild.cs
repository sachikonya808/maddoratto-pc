using RattoLauncher.Core.AnimeBuilder;

namespace RattoLauncher.Core.Services;

public sealed class ModAnimeBuild
{
    AnimeDat? _dat; TextureIndex? _index; Ykcmp? _yk; string _rel = "";
    readonly SemaphoreSlim _gate = new(1, 1);          // one heavy operation at a time

    public bool IsReady => _index != null;

    public Task InitializeAsync(string romfsRoot, string pristinePath, string ykcmpExe, string cacheDir,
                                IProgress<int>? progress = null, CancellationToken ct = default)
        => Task.Run(async () =>
        {
            await _gate.WaitAsync(ct);
            try
            {
                _yk = new Ykcmp(ykcmpExe);
                _dat = new AnimeDat(pristinePath);
                var problems = _dat.Preflight();
                if (problems.Count > 0) throw new InvalidDataException(string.Join("\n", problems));
                // TODO: compare TextureIndex.Fingerprint against your hard-coded pristine value
                _rel = Path.GetRelativePath(romfsRoot, pristinePath).Replace('\\', '/');
                _index = TextureIndex.LoadOrBuild(_dat, _yk, cacheDir, progress);
            }
            finally { _gate.Release(); }
        }, ct);

    public Task<BuildReport> BuildAsync(string pngFolder, string modsRoot, string modName, IBcEncoder enc, CancellationToken ct = default)
        => Task.Run(async () =>
        {
            await _gate.WaitAsync(ct);
            try
            {
                var pngs = Directory.EnumerateFiles(pngFolder, "*.png").ToList();
                return new ModAnimeBuilder(_dat!, _index!, _yk!, enc, _rel).Build(pngs, modsRoot, modName);
            }
            finally { _gate.Release(); }
        }, ct);
}
