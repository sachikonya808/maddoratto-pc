using System.Security.Cryptography;

namespace RattoLauncher.Core.Services;

public static class Hashing
{
    public static async Task<string> Sha256Async(string path, IProgress<double>? progress, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        using var sha = SHA256.Create();
        var buffer = new byte[1 << 20];

        long done = 0, total = fs.Length;

        int n;
        while ((n = await fs.ReadAsync(buffer, ct)) > 0)
        {
            sha.TransformBlock(buffer, 0, n, null, 0);
            done += n;
            progress?.Report((double)done / total);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }
}