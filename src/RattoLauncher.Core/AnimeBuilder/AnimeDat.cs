using System.Buffers.Binary;
using System.Text;

namespace RattoLauncher.Core.AnimeBuilder;

public enum TexKind : byte { Bc7 = 0, Bc1 = 1, Rgba8 = 2 }

public sealed record FadEntry(string Name, long Size, long Offset);

public readonly record struct TexRef(int Fad, int Local, int RecordPos);

public readonly record struct TexInfo(
    uint Stride, uint R20, int LogW, int LogH, byte Fmt, int W, int H, uint W32, uint H32,
    uint DSize, uint CSize, byte BhLog)
{
    public TexKind Kind => Fmt == 3 ? TexKind.Bc1
        : (Fmt == 0 && (double)DSize / (W * H) >= 3.9) ? TexKind.Rgba8 : TexKind.Bc7;

    public int BytesPerElement => Kind switch { TexKind.Bc1 => 8, TexKind.Rgba8 => 4, _ => 16 };

    public (int ew, int eh) Elements(int w, int h) => Kind == TexKind.Rgba8 ? (w, h) : ((w + 3) / 4, (h + 3) / 4);

    public int BlockHeightGobs => 1 << Math.Min((int)BhLog, 4);
}

public sealed class AnimeDat
{
    static ReadOnlySpan<byte> Magic => "YKCMP_V1"u8;

    public string FilePath { get; }
    public byte[] Data { get; }
    public List<FadEntry> Entries { get; } = new();
    public List<int[]> FadRecords { get; } = new();
    public List<int> FadFirstGlobal { get; } = new();
    public List<TexRef> Textures { get; } = new();

    public static uint U32(byte[] b, long o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)o));
    public static ulong U64(byte[] b, long o) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan((int)o));

    public AnimeDat(string path)
    {
        FilePath = path;
        Data = File.ReadAllBytes(path);
        if (Data.Length < 0x10 || Encoding.ASCII.GetString(Data, 0, 8) != "PS_FS_V1") { throw new InvalidDataException($"{path}: not a PS_FS_V1 file"); }
        
        uint n = U32(Data, 8);
        for (int i = 0; i < n; i++)
        {
            int e = 0x10 + i * 0x40;
            int len = Array.IndexOf(Data, (byte)0, e, 0x30) - e;

            if (len < 0) { len = 0x30; }
            
            string name = Encoding.ASCII.GetString(Data, e, len);
            Entries.Add(new FadEntry(name, (long)U64(Data, e + 0x30), (long)U64(Data, e + 0x38)));
        }

        for (int fi = 0; fi < Entries.Count; fi++)
        {
            var (_, size, off) = Entries[fi];
            var fad = Data.AsSpan((int)off, (int)size);
            var recs = new List<int>();

            int pos = 0;
            while (true)
            {
                int i = fad[pos..].IndexOf(Magic);
                if (i < 0) break;
                recs.Add(pos + i - 192);
                pos += i + 8;
            }
            FadRecords.Add(recs.ToArray());
            FadFirstGlobal.Add(Textures.Count);

            for (int k = 0; k < recs.Count; k++)
            {
                Textures.Add(new TexRef(fi, k, recs[k]));
            }
        }
    }

    public long RecordPos(int g) => Entries[Textures[g].Fad].Offset + Textures[g].RecordPos;
    public long BlockPos(int g) => RecordPos(g) + 192;
    public int BlockLength(int g) => (int)U32(Data, BlockPos(g) + 12);
    public byte[] BlockBytes(int g) => Data.AsSpan((int)BlockPos(g), BlockLength(g)).ToArray();

    public TexInfo Info(int g)
    {
        long r = RecordPos(g);

        return new TexInfo(
            Stride: U32(Data, r + 8),
            R20: U32(Data, r + 0x20),

            LogW: BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan((int)r + 0x28)),
            LogH: BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan((int)r + 0x2a)),

            Fmt: Data[r + 0x33],

            W: BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan((int)r + 0x34)),
            H: BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan((int)r + 0x36)),

            W32: U32(Data, r + 0x58),
            H32: U32(Data, r + 0x5c),

            DSize: U32(Data, r + 0x6c),
            CSize: U32(Data, r + 0x70),

            BhLog: Data[r + 0x74]
        );
    }

    public List<string> Preflight()
    {
        var bad = new List<string>();
        for (int i = 0; i + 1 < Entries.Count; i++)
        {
            long expect = Entries[i].Offset + (Entries[i].Size / 0x200 + 1) * 0x200;
            if (Entries[i + 1].Offset != expect) { bad.Add($"Entry {i}: Next entry is not at offset + (size/0x200+1)*0x200"); }
        }

        for (int fi = 0; fi < FadRecords.Count; fi++)
        {
            var recs = FadRecords[fi];
            long fadOff = Entries[fi].Offset;

            for (int k = 0; k + 1 < recs.Length; k++)
            {
                if (recs[k] + U32(Data, fadOff + recs[k] + 8) != recs[k + 1]) 
                { 
                    bad.Add($"{Entries[fi].Name} stride chain broken at {k}"); 
                    break;
                }
            }
        }

        for (int g = 0; g < Textures.Count; g++)
        {
            long expect = (192 + BlockLength(g) + 15) / 16 * 16;
            if (expect != Info(g).Stride)
            {
                bad.Add($"block {g}: stride != roundup(192 + block, 16)"); 
                break;
            }
        }
        return bad;
    }
}
