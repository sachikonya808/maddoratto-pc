using System.Buffers.Binary;

namespace RattoLauncher.Core.AnimeBuilder;

public sealed record Replacement(byte[] Record, byte[] Block);

public static class AnimeDatWriter
{
    sealed class FadPlan
    {
        public byte[] Head = null!;
        public int[] Strides = null!;
        public int TailStart;
        public long Size;
    }

    public static void Write(AnimeDat src, IReadOnlyDictionary<int, Replacement> repl, Stream output, Action<string>? log = null)
    {
        var plans = new Dictionary<int, FadPlan>();
        foreach (int fi in repl.Keys.Select(g => src.Textures[g].Fad).Distinct())
        {
            plans[fi] = PlanFad(src, fi, repl, log);
        }

        int n = src.Entries.Count;
        var sizes = new long[n];
        var offsets = new long[n];
        long pos = src.Entries[0].Offset;

        for (int i = 0; i < n; i++)
        {
            sizes[i] = plans.TryGetValue(i, out var p) ? p.Size : src.Entries[i].Size;
            offsets[i] = pos;
            pos += (sizes[i] / 0x200 + 1) * 0x200;
        }

        var hdr = src.Data.AsSpan(0, (int)src.Entries[0].Offset).ToArray();
        for (int i = 0; i < n; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(hdr.AsSpan(0x10 + i * 0x40 + 0x30), (ulong)sizes[i]);
            BinaryPrimitives.WriteUInt64LittleEndian(hdr.AsSpan(0x10 + i * 0x40 + 0x38), (ulong)offsets[i]);
        }
        output.Write(hdr);

        long written = hdr.Length;
        for (int i = 0; i < n; i++)
        {
            if (written != offsets[i]) { throw new InvalidOperationException("Internal error: entry offset"); }
            if (plans.TryGetValue(i, out var plan)) { WriteFad(src, i, plan, repl, output); }
            else { output.Write(src.Data, (int)src.Entries[i].Offset, (int)src.Entries[i].Size); }

            written += sizes[i];

            if (i < n - 1) 
            { 
                Zeros(output, offsets[i + 1] - written);
                written = offsets[i + 1];
            }
        }

        var last = src.Entries[^1];
        long tailStart = last.Offset + last.Size;
        output.Write(src.Data, (int)tailStart, (int)(src.Data.Length - tailStart));
    }

    static FadPlan PlanFad(AnimeDat src, int fi, IReadOnlyDictionary<int, Replacement> repl, Action<string>? log)
    {
        var e = src.Entries[fi];
        var recs = src.FadRecords[fi];
        int n = recs.Length;
        long fadOff = e.Offset;
        int first = src.FadFirstGlobal[fi];

        var strides = new int[n];
        for (int k = 0; k < n; k++)
        {
            strides[k] = (int)AnimeDat.U32(src.Data, fadOff + recs[k] + 8);
        }
        int tailStart = recs[n - 1] + strides[n - 1];
        var head = src.Data.AsSpan((int)fadOff, recs[0]).ToArray();

        int t0 = FindTable(head, recs);
        if (t0 < 0) { throw new InvalidDataException($"{e.Name}: Could not find exactly one 32-byte entry table"); }
        
        int match = 0;
        for (int k = 0; k + 1 < n; k++)
        {
            if(AnimeDat.U32(head, t0 + 32 * k + 0x18) == (uint)strides[k + 1]) { match++; }
        }
        if (match < n - 3) { throw new InvalidDataException($"{e.Name} Table does not follow the +0x18 = next stride relation"); }

        var newStrides = (int[])strides.Clone();
        for (int k = 0; k < n; k++)
        {
            if(repl.TryGetValue(first + k, out var r)) { newStrides[k] = (int)AnimeDat.U32(r.Record, 8); }
        }

        int p = recs[0];
        for (int k = 0; k < n; k++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(t0 + 32 * k), (uint)p);
            
            if (k < n - 1) { BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(t0 + 32 * k + 0x18), (uint)newStrides[k + 1]); }
            
            p += newStrides[k];
        }

        if (repl.ContainsKey(first + n - 1)) { log?.Invoke($"WARNING: Last texture of {e.Name} changed; table field +0x18 of the last entry left as is (meaning unknown)"); }
        
        long size = head.Length + newStrides.Sum(s => (long)s) + (e.Size - tailStart);

        return new FadPlan { Head = head, Strides = newStrides, TailStart = tailStart, Size = size };
    }

    static void WriteFad(AnimeDat src, int fi, FadPlan plan, IReadOnlyDictionary<int, Replacement> repl, Stream output)
    {
        var e = src.Entries[fi];
        var recs = src.FadRecords[fi];
        int first = src.FadFirstGlobal[fi];

        output.Write(plan.Head);
        for (int k = 0; k < recs.Length; k++)
        {
            if (repl.TryGetValue(first + k, out var r))
            {
                output.Write(r.Record);
                output.Write(r.Block);
                Zeros(output, plan.Strides[k] - r.Record.Length - r.Block.Length);
            }
            else
            {
                output.Write(src.Data, (int)(e.Offset + recs[k]), plan.Strides[k]);
            }
        }
        output.Write(src.Data, (int)(e.Offset + plan.TailStart), (int)(e.Size - plan.TailStart));
    }

    static int FindTable(byte[] head, int[] recs)
    {
        int found = -1, count = 0;
        for (int p = 0; p + 4 <= head.Length; p += 4)
        {
            if (p == 0x24 || AnimeDat.U32(head, p) != (uint)recs[0] || p + 32L * recs.Length > head.Length) { continue; }
            
            bool all = true;
            for (int k = 0; k < recs.Length && all; k++) 
            { 
                all = AnimeDat.U32(head, p + 32 * k) == (uint)recs[k]; 
            }

            if (all) 
            { 
                found = p; 
                count++; 
            }
        }
        return count == 1 ? found : -1;
    }

    static void Zeros(Stream s, long count)
    {
        if (count < 0) { throw new InvalidOperationException("negative padding"); }

        var buf = new byte[Math.Min(count, 1 << 16)];
        while (count > 0) 
        { 
            int c = (int)Math.Min(count, buf.Length); 
            s.Write(buf, 0, c); 
            count -= c; 
        }
    }
}
