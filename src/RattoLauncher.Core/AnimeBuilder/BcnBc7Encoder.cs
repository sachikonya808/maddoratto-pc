using BCnEncoder.Encoder;

namespace RattoLauncher.Core.AnimeBuilder;

public sealed class BcnBc7Encoder : IBcEncoder
{
    private readonly BcEncoder _enc = new();

    public BcnBc7Encoder(CompressionQuality quality = CompressionQuality.BestQuality)
    {
        _enc.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc7;
        _enc.OutputOptions.Quality = quality;
        _enc.OutputOptions.GenerateMipMaps = false;
    }

    public byte[] EncodeBc7(byte[] rgba, int width, int height)
    {
        return _enc.EncodeToRawBytes(rgba, width, height, PixelFormat.Rgba32, 0, out _, out _);
    }
}
