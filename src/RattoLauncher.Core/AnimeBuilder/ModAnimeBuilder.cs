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

public sealed class ModAnimeBuilder
{
    static readonly Regex NameRx = new(@"^([0-9a-fA-F]{16})_(\d+)x(\d+)\.png$", RegexOptions.Compiled);

    readonly AnimeDat _pristine;
    readonly Ykcmp _yk;
    readonly IBcEncoder _enc;
    readonly TextureIndex _index;
    readonly string _rel;

    public ModAnimeBuilder(AnimeDat pristine, TextureIndex index, Ykcmp yk, IBcEncoder encoder, string relative) 
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

    public BuildReport Build(IEnumerable<string> pngPaths, string modsRoot, string modName, int ykLevel = 1)
    {
        var report = new BuildReport();
        var repl = new Dictionary<int, Replacement>();
        var blockCache = new Dictionary<string, byte[]>();

        foreach (var path in pngPaths)
        {
            string file = Path.GetFileName(path);
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

            if (info.Kind != TexKind.Bc7) 
            { 
                report.Skipped.Add($"{file}: Only BC7 textures are supported for now (this one is {info.Kind})"); 
                continue; 
            }

            ImageResult img;
            using (var fs = File.OpenRead(path)) { img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha); }

            bool okSize = (img.Width, img.Height) == (info.W, info.H) || (img.Width, img.Height) == (info.LogW, info.LogH);
            if (!okSize) 
            { 
                report.Skipped.Add($"{file}: PNG is {img.Width}x{img.Height}; expected {info.W}x{info.H} or {info.LogW}x{info.LogH}"); 
                continue; 
            }

            var (ew0, eh0) = info.Elements(info.W, info.H);
            if (info.DSize != (uint)(ew0 * eh0 * info.BytesPerElement)) 
            { 
                report.Skipped.Add($"{file}: Existing texture is not a single plain level (mip chain / odd padding)"); 
                continue; 
            }

            var (ew, eh) = info.Elements(img.Width, img.Height);
            int bhg = info.BlockHeightGobs;
            if (ew % 8 != 0 || eh % (8 * bhg) != 0) 
            { 
                report.Skipped.Add($"{file}: Size not supported yet (needs element grid multiple of 8 x {8 * bhg})"); 
                continue; 
            }

            if (!blockCache.TryGetValue(job.Hash, out var blk))
            {
                byte[] lin = _enc.EncodeBc7(img.Data, img.Width, img.Height);
                if (lin.Length != ew * eh * info.BytesPerElement) { throw new InvalidOperationException("Encoder returned the wrong number of bytes"); }

                byte[] data = BlockLinear.ToSwitch(lin, ew, eh, info.BytesPerElement, bhg);
                if (data.Length != lin.Length) 
                { 
                    report.Skipped.Add($"{file}: Block-linear data would need padding"); 
                    continue; 
                }

                blk = _yk.Compress(data, ykLevel);
                if (blk.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(blk.AsSpan(12)) != blk.Length || !blk.AsSpan(0, 8).SequenceEqual("YKCMP_V1"u8)) 
                {
                    throw new InvalidOperationException("ykcmp output does not look like a game block");
                }

                if (!_yk.Decompress(blk).AsSpan().SequenceEqual(data)) { throw new InvalidOperationException("Round trip failed"); }
                
                blockCache[job.Hash] = blk;
            }

            foreach (int g in copies)
            {
                repl[g] = new Replacement(NewRecord(_pristine, g, blk, img.Width, img.Height, ew * eh * info.BytesPerElement), blk);
            }

            report.Applied.Add($"{file}: {copies.Count} copy/copies (blocks {string.Join(",", copies)}), {info.W}x{info.H} -> {img.Width}x{img.Height}");
        }

        if (repl.Count == 0) { return report; }

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
