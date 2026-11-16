using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace RattoLauncher.Core.AnimeBuilder;

public sealed record TextureMetadata(
    string Hash,
    TexKind Kind,
    byte Format,
    int Width,
    int Height,
    int LogicalWidth,
    int LogicalHeight,
    bool SingleLevel,
    int[] Blocks
);

public sealed class TextureIndex
{
    public Dictionary<string, List<int>> ByHash { get; } = new();
    public Dictionary<string, TextureMetadata> Metadata { get; } = new();

    public static string HashOf(TexKind kind, int w, int h, byte[] data)
    {
        using var ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Span<byte> hdr = stackalloc byte[5];
        hdr[0] = (byte)kind;

        BinaryPrimitives.WriteUInt16LittleEndian(hdr[1..], (ushort)w);
        BinaryPrimitives.WriteUInt16LittleEndian(hdr[3..], (ushort)h);

        ih.AppendData(hdr);
        ih.AppendData(data);

        return Convert.ToHexString(ih.GetHashAndReset().AsSpan(0, 8)).ToLowerInvariant();
    }

    public static string Fingerprint(string datPath)
    {
        using var fs = File.OpenRead(datPath);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    public static TextureIndex LoadOrBuild(AnimeDat dat, Ykcmp yk, string cacheDir, IProgress<int>? progress = null)
    {
        Directory.CreateDirectory(cacheDir);
        string fp = Fingerprint(dat.FilePath);
        string cache = Path.Combine(cacheDir, $"index_{fp[..16]}.json");

        if (File.Exists(cache))
        {
            var c = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(cache));
            if (c != null && c.Fingerprint == fp)
            {
                var idx = new TextureIndex();

                foreach (var (h, list) in c.Textures)
                {
                    idx.ByHash[h] = list.ToList();
                }

                idx.FillMetadata(dat);
                idx.ExportMetadata(cacheDir, fp);

                return idx;
            }
        }

        var hashes = new string[dat.Textures.Count];
        int done = 0;

        Parallel.For(0, hashes.Length, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, g =>
        {
            var info = dat.Info(g);
            var data = yk.Decompress(dat.BlockBytes(g));

            hashes[g] = HashOf(info.Kind, info.W, info.H, data);
            progress?.Report(Interlocked.Increment(ref done));
        });

        var index = new TextureIndex();
        for (int g = 0; g < hashes.Length; g++)
        {
            if (!index.ByHash.TryGetValue(hashes[g], out var l)) { index.ByHash[hashes[g]] = l = new List<int>(); }
            l.Add(g);
        }

        File.WriteAllText(cache, JsonSerializer.Serialize(new CacheFile
        {
            Fingerprint = fp,
            Textures = index.ByHash.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray())
        }));

        index.FillMetadata(dat);
        index.ExportMetadata(cacheDir, fp);

        return index;
    }

    void FillMetadata(AnimeDat dat)
    {
        Metadata.Clear();
        foreach (var (hash, blocks) in ByHash)
        {
            var i = dat.Info(blocks[0]);
            var (ew, eh) = i.Elements(i.W, i.H);
            bool single = i.DSize == (uint)(ew * eh * i.BytesPerElement);
            Metadata[hash] = new TextureMetadata(hash, i.Kind, i.Fmt, i.W, i.H, i.LogW, i.LogH, single, blocks.ToArray());
        }
    }

    private static readonly JsonSerializerOptions meta_WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    void ExportMetadata(string cacheDir, string fingerprint)
    {
        try
        {
            var rows = Metadata.Values.OrderBy(m => m.Blocks[0]).Select(m =>
                new TextureMetadata(m.Hash, m.Kind, m.Format, m.Width, m.Height, m.LogicalWidth, m.LogicalHeight, m.SingleLevel, m.Blocks)
            );

            var doc = new
            {
                Fingerprint = fingerprint,
                UniqueTextures = Metadata.Count,
                Blocks = ByHash.Values.Sum(l => l.Count),
                Textures = rows
            };

            string path = Path.Combine(cacheDir, "texture_metadata.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(doc, meta_WriteOptions));
            File.Move(path + ".tmp", path, overwrite: true);
        } catch { }
    }

    sealed class CacheFile
    {
        public string Fingerprint { get; set; } = "";
        public Dictionary<string, int[]> Textures { get; set; } = new();
    }
}
