namespace RattoLauncher.Core.AnimeBuilder;

public static class MipChains
{
    public static List<byte[]> Generate(byte[] rgba, int w, int h, int levels)
    {
        if (rgba.Length != (w * h * 4)) { throw new ArgumentException("RGBA must be w*h*4 bytes!"); }

        var result = new List<byte[]>(levels) { rgba };
        
        if (levels == 1) { return result; }
        if ((w & (w - 1)) != 0 || (h & (h - 1)) != 0) { throw new NotSupportedException("Mip generation needs power-of-two sizes"); }

        for (int n = 1; n < levels; n++)
        {
            int lw = Math.Max(w >> n, 1), lh = Math.Max(h >> n, 1);
            int sx = w / lw, sy = h / lh, count = sx * sy;
            var dst = new byte[lw * lh * 4];

            Parallel.For(0, lh, oy =>
            {
                for (int ox = 0; ox < lw; ox++)
                {
                    long r = 0, g = 0, b = 0, a = 0;
                    for (int y = oy * sy; y < (oy + 1) * sy; y++)
                    {
                        int p = (y * w + ox * sx) * 4;
                        for (int x = 0; x < sx; x++, p += 4)
                        {
                            r += rgba[p]; g += rgba[p + 1]; b += rgba[p + 2]; a += rgba[p + 3];
                        }
                    }
                    int d = (oy * lw + ox) * 4;
                    dst[d]     = (byte)((r + count / 2) / count);
                    dst[d + 1] = (byte)((g + count / 2) / count);
                    dst[d + 2] = (byte)((b + count / 2) / count);
                    dst[d + 3] = (byte)((a + count / 2) / count);
                }
            });
            result.Add(dst);
        }
        return result;
    }
}
