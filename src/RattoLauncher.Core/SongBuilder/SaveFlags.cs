using System.Text;

namespace MadRat.Songs;

public static class SaveFlags
{
    public const int OpenBase = 500;
    public const int PlayedBase = 600;

    private static readonly byte[] Tag = Encoding.ASCII.GetBytes("maFlag");

    public static int PayloadOffset(byte[] save)
    {
        int tag = save.AsSpan().IndexOf(Tag);

        if (tag < 0) throw new InvalidDataException("maFlag tag not found: is this the 'System' save file?");

        int po = tag + Tag.Length;

        if (po + 1 < save.Length && save[po] == 0x01 && save[po + 1] == 0x07) po += 2;
        if (po < save.Length && save[po] == 0x00) po += 1;

        return po + 8;
    }

    public static (byte Old, byte Now) SetBit(byte[] save, int payloadOffset, int flag)
    {
        int index = payloadOffset + (flag >> 3);

        if (index >= save.Length) throw new InvalidDataException($"flag {flag} is outside the save file");

        byte old = save[index];
        save[index] = (byte)(old | (1 << (flag & 7)));
        return (old, save[index]);
    }

    public static bool IsSet(byte[] save, int flag)
    {
        int index = PayloadOffset(save) + (flag >> 3);
        return index < save.Length && (save[index] & (1 << (flag & 7))) != 0;
    }

    public static List<string> Unlock(byte[] save, IEnumerable<(string Label, int Index)> songs)
    {
        int po = PayloadOffset(save);
        var log = new List<string> { $"payload at 0x{po:X}" };

        foreach (var (label, index) in songs)
        {
            foreach (int baseFlag in new[] { OpenBase, PlayedBase })
            {
                int flag = baseFlag + index;
                var (old, now) = SetBit(save, po, flag);
                log.Add($"  {label}: flag {flag} -> byte {flag >> 3} bit {flag & 7}: 0x{old:X2} -> 0x{now:X2}");
            }
        }

        return log;
    }
}
