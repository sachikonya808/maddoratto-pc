using System.Buffers.Binary;
using System.Text.RegularExpressions;
using StbImageSharp;

namespace RattoLauncher.Core.AnimeBuilder;

public sealed record TextureJob(string Hash, int NameW, int NameH, string PngPath);

public sealed class BuildReport
{
    public List<string> Applied { get; } = new();
    public List<string> Skipped { get; } = new();
    public string? OutputPath { get; set; }
}

public sealed class Builder
{
    static readonly Regex NameRx = new(@"^([0-9a-fA-F]{16})_(\d+)x(\d+)\.png$", RegexOptions.Compiled);

    readonly AnimeDat _pristine;
    readonly Ykcmp _yk;
    readonly IBcEncoder _enc;
    readonly TextureIndex _index;
    readonly string _rel;

    public Builder(AnimeDat pristine, TextureIndex index, Ykcmp yk, IBcEncoder encoder, string relative) 
    { 
        _pristine = pristine; 
        _index = index; 
        _yk = yk; 
        _enc = encoder;
        _rel = relative;
    }

    public static TextureJob? ParseName(string pngPath)
    {
        var m = NameRx.Match(Path.GetFileName(pngPath));

        return m.Success ? new TextureJob(m.Groups[1].Value.ToLowerInvariant(), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), pngPath) : null;
    }

    byte[] EncodeLevel(TexKind kind, bool bgra, byte[] rgba, int w, int h)
    {
        if (kind != TexKind.Rgba8 && (w % 4 != 0 || h % 4 != 0)) { (rgba, w, h) = Helpers.PadTo4(rgba, w, h); }

        return kind switch
        {
            TexKind.Bc7 => _enc.EncodeBc7(rgba, w, h),
            TexKind.Bc3 => _enc.EncodeBc3(rgba, w, h),
            TexKind.Bc1 => _enc.EncodeBc1(rgba, w, h),
            TexKind.Rgba8 => bgra ? Helpers.SwapRb(rgba) : rgba,
            _ => throw new NotSupportedException($"Unsupported texture kind! {kind}")
        };
    }

    byte[] Pack(TexInfo info, TexKind kind, byte[] rgba, int w, int h, List<Level> plan, CancellationToken ct)
    {
        var levels = MipChains.Generate(rgba, w, h, plan.Count);
        var data = new byte[plan.Sum(l => l.Size)];

        for (int n = 0; n < plan.Count; n++)
        {
            ct.ThrowIfCancellationRequested();

            var lv = plan[n];
            byte[] lin = EncodeLevel(kind, info.Bgra, levels[n], lv.W, lv.H);

            if(lin.Length != (lv.Ew * lv.Eh * info.BytesPerElement)) { throw new InvalidOperationException($"Encoder returned the wrong number of bytes for level {n}"); }

            byte[] tiled = BlockLinear.ToSwitch(lin, lv.Ew, lv.Eh, info.BytesPerElement, lv.Wg, lv.Bh);
            
            Buffer.BlockCopy(tiled, 0, data, lv.Offset, tiled.Length);
        }

        return data;
    }

    public BuildReport Build(IEnumerable<string> pngPaths, string modsRoot, string modName, int ykLevel = 1,
                             IProgress<ModOperationProgress>? progress = null, CancellationToken ct = default)
    {
        var report = new BuildReport();
        var repl = new Dictionary<int, Replacement>();
        var blockCache = new Dictionary<string, byte[]>();

        var list = pngPaths as IReadOnlyList<string> ?? pngPaths.ToList();
        int done = 0;

        foreach (var path in list)
        {
            ct.ThrowIfCancellationRequested();
            string file = Path.GetFileName(path);
            progress?.Report(new ModOperationProgress($"Processing {file}", done++, list.Count));
            var job = ParseName(path);

            if (job == null) 
            { 
                report.Skipped.Add($"{file}: Name is not <16 hex>_<w>x<h>.png"); 
                continue; 
            }

            if (!_index.ByHash.TryGetValue(job.Hash, out var copies)) 
            { 
                report.Skipped.Add($"{file}: Hash not found in this Anime_EN.dat"); 
                continue; 
            }

            var info = _pristine.Info(copies[0]);
            if ((info.W, info.H) != (job.NameW, job.NameH)) 
            { 
                report.Skipped.Add($"{file}: Size in the name is {job.NameW}x{job.NameH}, Texture is {info.W}x{info.H}"); 
                continue; 
            }

            TexKind kind;
            try { kind = info.Kind; }
            catch (NotSupportedException ex) { report.Skipped.Add($"{file}: {ex.Message}"); continue; }

            ImageResult img;
            try
            {
                using var fs = File.OpenRead(path); 
                img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                report.Skipped.Add($"{file}: Couldn't read the PNG ({ex.Message})");
                continue;
            }
            
            bool okSize = (img.Width, img.Height) == (info.W, info.H) || (img.Width, img.Height) == (info.LogW, info.LogH);
            if (!okSize) 
            { 
                report.Skipped.Add($"{file}: PNG is {img.Width}x{img.Height}; expected {info.W}x{info.H} or {info.LogW}x{info.LogH}"); 
                continue; 
            }

            bool resized = (img.Width, img.Height) != (info.W, info.H);

            if (resized && info.Levels > 1)
            {
                report.Skipped.Add($"{file}: Mip chains can't be resized (PNG must be {info.W}x{info.H})");
                continue;
            }

            var target = info with { W = img.Width, H = img.Height };
            var plan = TexInfo.Plan(target);
            int newDSize = plan.Sum(l => l.Size);

            if (!resized && (uint)newDSize != info.DSize)
            {
                report.Skipped.Add($"{file}: Layout of this texture is not understood (blocks 139 and 294 are known cases)");
                continue;
            }

            if (!blockCache.TryGetValue(job.Hash, out var blk))
            {
                byte[] data = Pack(info, kind, img.Data, img.Width, img.Height, plan, ct);

                blk = _yk.Compress(data, ykLevel, ct);
                if (blk.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(blk.AsSpan(12)) != blk.Length || !blk.AsSpan(0, 8).SequenceEqual("YKCMP_V1"u8)) 
                {
                    throw new InvalidOperationException("ykcmp output does not look like a game block");
                }

                if (!_yk.Decompress(blk, ct).AsSpan().SequenceEqual(data)) { throw new InvalidOperationException("Round trip failed"); }
                
                blockCache[job.Hash] = blk;
            }

            foreach (int g in copies)
            {
                repl[g] = new Replacement(NewRecord(_pristine, g, blk, img.Width, img.Height, newDSize), blk);
            }

            report.Applied.Add($"{file}: {copies.Count} copy/copies (blocks {string.Join(",", copies)}), {info.W}x{info.H} -> {img.Width}x{img.Height}, {info.Levels}");
        }

        if (repl.Count == 0) { return report; }
        ct.ThrowIfCancellationRequested();

        progress?.Report(new ModOperationProgress("Writing the mod file", 0, 0));
        string outPath = Path.Combine(modsRoot, modName, "romfs", _rel);
        string full = Path.GetFullPath(outPath);
        string root = Path.GetFullPath(modsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, Path.GetFullPath(_pristine.FilePath), StringComparison.OrdinalIgnoreCase)) 
        {    
            throw new InvalidOperationException("Refusing to write outside the mods folder");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        
        string tmp = outPath + ".tmp";

        try {
            using (var fs = File.Create(tmp)) { AnimeDatWriter.Write(_pristine, repl, fs, s => report.Skipped.Add(s)); }
            File.Move(tmp, outPath, overwrite: true);
        }
        catch
        {
            Helpers.TryDelete(tmp);
            throw;
        }

        report.OutputPath = outPath;
        return report;
    }

    static byte[] NewRecord(AnimeDat src, int g, byte[] newBlock, int w, int h, int dsize)
    {
        var r = src.Data.AsSpan((int)src.RecordPos(g), 192).ToArray();
        int oldBlock = src.BlockLength(g);
        int delta = newBlock.Length - oldBlock;

        uint Get(int o) => BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(o));
        void Put(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(o), v);
        
        Put(0x08, (uint)((192 + newBlock.Length + 15) / 16 * 16));
        Put(0x20, (uint)(Get(0x20) + delta));
        
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x34), (ushort)w);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x36), (ushort)h);
        
        Put(0x58, (uint)w); Put(0x5c, (uint)h);
        Put(0x6c, (uint)dsize);
        Put(0x70, (uint)(Get(0x70) + delta));
        
        return r;
    }
}
