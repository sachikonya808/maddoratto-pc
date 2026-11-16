using System.Text;
using RattoLauncher.Core.Services;

namespace RattoLauncher.Core.AnimeBuilder;

public static class BlockLinear
{
    public static byte[] ToSwitch(byte[] lin, int ew, int eh, int B, int bhg)
    {
        int wg = (ew * B + 63) / 64;
        int rows = (eh + 8 * bhg - 1) / (8 * bhg) * (8 * bhg);
        var outb = new byte[(long)rows * wg * 64];

        for (int y = 0; y < eh; y++) 
        {
            for (int x = 0 ; x < ew ; x++)
            {
                long xb = (long)x * B;
                long gob = (y / (8 * bhg)) * 512L * bhg * wg + (xb / 64) * 512L * bhg + ((y % (8 * bhg)) / 8) * 512L;
                long xr = xb % 64; int yr = y % 8;
                long off = gob + (xr / 32) * 256 + (yr / 2) * 64 + ((xr % 32) / 16) * 32 + (yr % 2) * 16 + (xr % 16);
                Buffer.BlockCopy(lin, (y * ew + x) * B, outb, (int)off, B);
            }
        }
        return outb;
    }
}

public interface IBcEncoder
{
    byte[] EncodeBc7(byte[] rgba, int width, int height);
}

public sealed class Ykcmp
{
    readonly string _exe;

    public Ykcmp(string exePath) { _exe = exePath; }

    public byte[] Decompress(byte[] block, CancellationToken ct = default)
    {
        string src = Helpers.Tmp(), dst = Helpers.Tmp();
        try
        {
            File.WriteAllBytes(src, block);
            RunDynamis(ct, "-a", "0x0", src, dst);

            return File.ReadAllBytes(dst);
        }
        finally
        {
            Helpers.TryDelete(src);
            Helpers.TryDelete(dst);
        }
    }

    public byte[] Compress(byte[] raw, int level = 1, CancellationToken ct = default)
    {
        string src = Helpers.Tmp(), dst = Helpers.Tmp();

        try
        {
            File.WriteAllBytes(src, raw);
            RunDynamis(ct, "-cl", level.ToString(), src, dst);

            return File.ReadAllBytes(dst);
        }
        finally
        {
            Helpers.TryDelete(src);
            Helpers.TryDelete(dst);
        }
    }

    private void RunDynamis(CancellationToken ct, params string[] args)
    {
        var output = new StringBuilder();
        var lines = new InlineProgress<string>(l => { lock (output) output.AppendLine(l); });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));

        int code;
        try
        {
            code = ProcessRunner.RunAsync(_exe, args, Path.GetDirectoryName(_exe)!, lines, timeout.Token)
                                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"{Path.GetFileName(_exe)} didn't finish in time.");
        }

        if (code != 0) { throw new InvalidOperationException($"{Path.GetFileName(_exe)} failed (exit code {code}):\n{output.ToString().Trim()}"); }
    }
}