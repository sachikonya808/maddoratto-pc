using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MadRat.Songs;
using RattoLauncher.Core.Models;

namespace RattoLauncher.Core.Services;

public sealed record SongBuildReport(
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Warnings,
    string? OutputPath
);

public static class ModSongsBuild
{
    private const uint TemplateId = 0x0F;
    private const uint FirstSongId = 0x66;
    private const uint MaxSongId = 999;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task<SongBuildReport> BuildAsync(
        string romfsRoot,
        string modsRoot,
        string modName,
        IEnumerable<string> zipPaths,
        IProgress<ModOperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        ValidateModName(modName);

        var zips = zipPaths
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return await Task.Run(() => Build(romfsRoot, modsRoot, modName, zips, progress, ct), ct);
    }

    // ------------------------------------------------------------------------------------------

    private static SongBuildReport Build(
        string romfsRoot, string modsRoot, string modName,
        List<string> zips, IProgress<ModOperationProgress>? progress, CancellationToken ct)
    {
        string soundPath = Path.Combine(romfsRoot, "Data", "Database", "Sound.dat");
        string rhythmPath = Path.Combine(romfsRoot, "Data", "Database", "Rhythm.dat");

        var problems = new List<string>();
        if (!File.Exists(soundPath)) problems.Add($"Original Sound.dat not found: {soundPath}");
        if (!File.Exists(rhythmPath)) problems.Add($"Original Rhythm.dat not found: {rhythmPath}");
        if (problems.Count > 0) throw new PreflightException(problems);

        progress?.Report(new ModOperationProgress("Reading the original song data", 0, 0));

        byte[] soundBytes = File.ReadAllBytes(soundPath);
        byte[] rhythmBytes = File.ReadAllBytes(rhythmPath);

        SoundDatDocument soundDoc;
        List<RhythmChart> charts;

        try
        {
            soundDoc = SoundDat.Parse(soundBytes);
            charts = RhythmDat.ParseDirectory(rhythmBytes);
        }
        catch (InvalidDataException e)
        {
            throw new PreflightException(new[] { $"The original Sound.dat / Rhythm.dat could not be read: {e.Message}" });
        }

        if (soundDoc.Records.Count(r => r.Id == TemplateId) != 1)
            problems.Add($"Sound.dat has no (or several) record(s) with the template ID 0x{TemplateId:X}.");

        if (soundDoc.Records.Any(r => r.Id >= FirstSongId && r.Id <= MaxSongId))
            problems.Add($"Sound.dat already contains song IDs 0x{FirstSongId:X}-0x{MaxSongId:X}; it does not look like the original file.");

        if (charts.Any(c =>
        {
            uint n = RhythmDat.ChartIdFromName(c.Name) / 100;
            return n >= FirstSongId && n <= MaxSongId;
        }))
            problems.Add($"Rhythm.dat already contains charts for BGM{FirstSongId:D3}-BGM{MaxSongId:D3}; it does not look like the original file.");

        if (problems.Count > 0) throw new PreflightException(problems);

        string finalDir = Path.Combine(modsRoot, modName);
        string stagingDir = finalDir + ".tmp";
        string stagedDatabase = Path.Combine(stagingDir, "romfs", "Data", "Database");
        string stagedSound = Path.Combine(stagingDir, "romfs", "Data", "Sound");

        var applied = new List<string>();
        var skipped = new List<string>();
        var additions = new Dictionary<string, ChartAddition>();
        uint nextId = FirstSongId;

        try
        {
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
            Directory.CreateDirectory(stagedDatabase);
            Directory.CreateDirectory(stagedSound);

            for (int i = 0; i < zips.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                string zipPath = zips[i];
                string zipName = Path.GetFileName(zipPath);
                progress?.Report(new ModOperationProgress($"Adding {zipName}", i, zips.Count));

                if (nextId > MaxSongId)
                {
                    skipped.Add($"{zipName}: no free song ID left (the limit is BGM{MaxSongId:D3})");
                    continue;
                }

                uint id = nextId;
                string target = $"BGM{id:D3}";
                PreparedSong song;

                try
                {
                    song = Prepare(zipPath, target, rhythmBytes, charts);

                    SoundDat.Clone(soundDoc, new CloneOptions
                    {
                        SourceId = TemplateId,
                        NewId = id,
                        Title = song.Title,
                        Composer = song.Composer,
                        Bpm = song.Bpm,
                        Str0C = $"BGM{song.BaseId:D3}",
                        NormalDifficulty = song.NormalDifficulty,
                        HardDifficulty = song.HardDifficulty
                    });
                }
                catch (Exception e) when (e is PackProblem or InvalidDataException or System.Text.Json.JsonException
                                              or ArgumentException or KeyNotFoundException or IOException)
                {
                    skipped.Add($"{zipName}: {e.Message}");
                    continue;
                }

                foreach (var (name, add) in song.Charts) additions.Add(name, add);

                using (var zip = ZipFile.OpenRead(zipPath))
                    Find(zip, "song.acb")!.ExtractToFile(Path.Combine(stagedSound, target + ".acb"), overwrite: true);

                applied.Add($"{target} (ID 0x{id:X2}): \"{song.Title}\" from {zipName}");
                nextId++;
            }

            ct.ThrowIfCancellationRequested();

            if (applied.Count == 0)
            {
                if (Directory.Exists(finalDir)) Directory.Delete(finalDir, true);
                return new SongBuildReport(applied, skipped, [], null);
            }

            progress?.Report(new ModOperationProgress("Writing Sound.dat and Rhythm.dat", 0, 0));

            byte[] rhythmOut = RhythmDat.BuildFile(rhythmBytes, charts, null, additions, null);
            RhythmDat.Verify(rhythmBytes, charts, rhythmOut, null, additions, null);

            var (soundOut, warnings) = SoundDat.BuildChecked(soundDoc, rhythmOut);

            File.WriteAllBytes(Path.Combine(stagedDatabase, "Rhythm.dat"), rhythmOut);
            File.WriteAllBytes(Path.Combine(stagedDatabase, "Sound.dat"), soundOut);

            ct.ThrowIfCancellationRequested();

            if (Directory.Exists(finalDir)) Directory.Delete(finalDir, true);
            Directory.Move(stagingDir, finalDir);

            return new SongBuildReport(applied, skipped, warnings, finalDir);
        }
        finally
        {
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }


    private sealed record PreparedSong(
        string Title, string Composer, uint Bpm, int BaseId, Dictionary<string, ChartAddition> Charts, uint? NormalDifficulty, uint? HardDifficulty);

    private sealed class PackProblem(string message) : Exception(message);

    private static PreparedSong Prepare(string zipPath, string target, byte[] rhythmBytes, List<RhythmChart> charts)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        var packEntry = Find(zip, "pack.json") ?? throw new PackProblem("pack.json is missing");

        SongMetadata? meta;
        using (var s = packEntry.Open())
            meta = JsonSerializer.Deserialize<SongMetadata>(s, JsonOptions);

        if (meta is null) throw new PackProblem("pack.json is empty");

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(meta.Title)) missing.Add("Title");
        if (meta.Composer is null) missing.Add("Composer");
        if (meta.BPM is null) missing.Add("BPM");
        if (meta.BaseID is null) missing.Add("BaseID");
        if (missing.Count > 0) throw new PackProblem($"pack.json has no value for: {string.Join(", ", missing)}");

        int bpm = meta.BPM!.Value;
        int baseId = meta.BaseID!.Value;

        if (bpm <= 0) throw new PackProblem("BPM must be greater than 0");
        if (baseId is < 0 or > 999) throw new PackProblem("BaseID must be between 0 and 999");

        uint? normalDifficulty = CheckLevel(meta.NormalDifficulty, "NormalDifficulty");
        uint? hardDifficulty = CheckLevel(meta.HardDifficulty, "HardDifficulty");

        if (Find(zip, "song.acb") is null) throw new PackProblem("song.acb is missing");

        var additions = new Dictionary<string, ChartAddition>();

        if (meta.CustomCharts)
        {
            var normal = Find(zip, "normal.txt")
                         ?? throw new PackProblem("normal.txt is missing (required when CustomCharts is true)");
            var hard = Find(zip, "hard.txt")
                       ?? throw new PackProblem("hard.txt is missing (required when CustomCharts is true)");

            foreach (var (entry, suffix) in new[] { (normal, ""), (hard, "H") })
            {
                try
                {
                    string text = ReadText(entry);

                    foreach (var (number, symbols) in RhythmDat.ReadTextCharts(text))
                    {
                        var tokens = RhythmDat.SymbolsToTokens(symbols);
                        additions[$"{target}_{number}{suffix}"] =
                            new ChartAddition(tokens, RhythmDat.LengthField(tokens, bpm));
                    }
                }
                catch (InvalidDataException e)
                {
                    throw new PackProblem($"{entry.Name}: {e.Message}");
                }
            }
        }
        else
        {
            string from = $"BGM{baseId:D3}";
            var sources = charts.Where(c => c.Name.StartsWith(from + "_", StringComparison.Ordinal)).ToList();

            if (sources.Count == 0)
                throw new PackProblem($"the original game has no charts for {from} (BaseID {baseId})");

            foreach (var c in sources)
            {
                additions[target + c.Name[from.Length..]] = new ChartAddition(
                    RhythmDat.DecodeTokens(rhythmBytes, c),
                    RhythmDat.StoredLengthField(rhythmBytes, c));
            }
        }

        return new PreparedSong(meta.Title!, meta.Composer!, (uint)bpm, baseId, additions, normalDifficulty, hardDifficulty);
    }

    private static ZipArchiveEntry? Find(ZipArchive zip, string name) =>
        zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void ValidateModName(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName) || modName is "." or ".." ||
            modName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"'{modName}' is not a valid mod folder name.", nameof(modName));
    }

    private static uint? CheckLevel(int? level, string name)
    {
        if(level is null) { return null; }
        if(level is < 1 or > 5) { throw new PackProblem($"{name} must be between 1 and 5"); }
        
        return (uint)level.Value;
    }
}
