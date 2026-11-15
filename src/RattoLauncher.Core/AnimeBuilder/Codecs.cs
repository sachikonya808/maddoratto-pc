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

    public byte[] Decompress(byte[] block)
    {
        string src = Helpers.Tmp(), dst = Helpers.Tmp();
        try
        {
            File.WriteAllBytes(src, block);
            ProcessRunner.Run(_exe, "-a", "0x0", src, dst);

            return File.ReadAllBytes(dst);
        }
        finally
        {
            Helpers.TryDelete(src);
            Helpers.TryDelete(dst);
        }
    }

    public byte[] Compress(byte[] raw, int level = 1)
    {
        string src = Helpers.Tmp(), dst = Helpers.Tmp();

        try
        {
            File.WriteAllBytes(src, raw);
            ProcessRunner.Run(_exe, "-cl", level.ToString(), src, dst);

            return File.ReadAllBytes(dst);
        }
        finally
        {
            Helpers.TryDelete(src);
            Helpers.TryDelete(dst);
        }
    }
}
