using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace RattoLauncher.Core.AnimeBuilder;

public sealed class TextureIndex
{
    public Dictionary<string, List<int>> ByHash { get; } = new();

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

        return index;
    }

    sealed class CacheFile
    {
        public string Fingerprint { get; set; } = "";
        public Dictionary<string, int[]> Textures { get; set; } = new();
    }
}
