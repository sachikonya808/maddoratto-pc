using BCnEncoder.Encoder;

namespace RattoLauncher.Core.AnimeBuilder;

public enum EncodeQuality { Fast, Balanced, Quality }

public sealed class BcnEncoder : IBcEncoder
{
    private readonly BcEncoder _enc = new();

    public BcnEncoder(EncodeQuality quality = EncodeQuality.Quality)
    {
        _enc.OutputOptions.Quality = quality switch
        {
            EncodeQuality.Fast => CompressionQuality.Fast,
            EncodeQuality.Balanced => CompressionQuality.Balanced,
            _ => CompressionQuality.BestQuality
        };
        _enc.OutputOptions.GenerateMipMaps = false;
    }

    public byte[] EncodeBc7(byte[] rgba, int width, int height)
    {
        _enc.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc7;
        return _enc.EncodeToRawBytes(rgba, width, height, PixelFormat.Rgba32, 0, out _, out _);
    }

    public byte[] EncodeBc1(byte[] rgba, int width, int height)
    {
        _enc.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc1;
        return _enc.EncodeToRawBytes(rgba, width, height, PixelFormat.Rgba32, 0, out _, out _);
    }

    public byte[] EncodeBc3(byte[] rgba, int width, int height)
    {
        _enc.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc3;
        return _enc.EncodeToRawBytes(rgba, width, height, PixelFormat.Rgba32, 0, out _, out _);
    }
}
