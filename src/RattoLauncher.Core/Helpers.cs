namespace RattoLauncher.Core;

public static class Helpers
{
    public static bool DoFileExist(string filePath)
    {
        return File.Exists(filePath);
    }

    public static void TryDelete(string p)
    {
        try
        {
            File.Delete(p);
        }
        catch { }
    }

    public static string Tmp() => Path.Combine(Path.GetTempPath(), "mrdmod_" + Guid.NewGuid().ToString("N"));

    public static int RoundUp(int value, int multiple) => (value + multiple - 1) / multiple * multiple;

    public static int Pow2Ceil(int n)
    {
        int p = 1;
        while (p < n) p <<= 1;
        return p;
    }

    public static byte[] SwapRb(byte[] px)
    {
        var o = (byte[])px.Clone();

        for (int i = 0; i + 3 < o.Length; i += 4)
        { 
            (o[i], o[i + 2]) = (o[i + 2], o[i]);
        }
        
        return o;
    }

    public static (byte[] rgba, int w, int h) PadTo4(byte[] src, int w, int h)
    {
        int pw = (w + 3) / 4 * 4, ph = (h + 3) / 4 * 4;
        var dst = new byte[pw * ph * 4];
        for (int y = 0; y < ph; y++){
            for (int x = 0; x < pw; x++) 
            {
                Buffer.BlockCopy(src, (Math.Min(y, h - 1) * w + Math.Min(x, w - 1)) * 4, dst, (y * pw + x) * 4, 4);
            }
        }
        return (dst, pw, ph);
    }
}