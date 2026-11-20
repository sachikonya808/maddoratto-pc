using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace MadRat.Songs;

public sealed class RhythmChart
{
    public required string Name { get; init; }
    public int NamePos { get; init; }
    public int PayloadStart { get; init; }
    public int Boundary { get; init; }
    public int Table { get; init; }
    public uint[] Offsets { get; init; } = [];
    public int Base { get; init; }
}

public sealed record ChartAddition(byte[][] Tokens, uint Length);

public static class RhythmDat
{
    public const int Header = 0x0C;
    public const int ChartSize = 1600;
    public const int RecordHeader = 0x10;
    public const int Pad = 80;
    public const int NamePtrBack = 12;
    public const int LengthOffset = 12;
    public const int Stride = RecordHeader + 4 * ChartSize + Pad;
    public const double DurationFactor = 720000.0;
    public const char UnknownSymbol = '?';

    private static readonly Regex NameFull = new(@"^BGM(\d{3})_([1-4])(H?)$", RegexOptions.Compiled);

    public static readonly IReadOnlyDictionary<char, byte[]> Symbols = new Dictionary<char, byte[]>
    {
        ['.'] = [],
        ['M'] = [0xE3, 0x83, 0xBB],
        ['O'] = [0xE2, 0x97, 0x8B],
        ['P'] = [0xE2, 0x97, 0x8F],
        ['X'] = [0xE2, 0x97, 0x8B, 0xE2, 0x96, 0xB2, 0x38],
        ['Y'] = [0xE2, 0x97, 0x8F, 0xE2, 0x96, 0xB2, 0x38],
        ['Z'] = [0xE3, 0x83, 0xBB, 0xE2, 0x96, 0xB2, 0x38],
        ['S'] = [0xE2, 0x97, 0x8B, 0xE2, 0x96, 0xB2],
    };

    private static readonly Dictionary<string, char> TokenToSymbol =
        Symbols.ToDictionary(kv => Convert.ToHexString(kv.Value), kv => kv.Key);

    public static (uint Stride, int Count, uint Flag, int Base) ReadHeader(byte[] data)
    {
        if (data.Length < Header) throw new InvalidDataException("file too small for a Rhythm.dat header");

        uint stride = U32(data, 0);
        uint count = U32(data, 4);
        uint flag = U32(data, 8);
        long b = Header + (long)count * stride;

        if (b > data.Length) throw new InvalidDataException("record table runs past the end of the file");

        return (stride, (int)count, flag, (int)b);
    }

    public static List<RhythmChart> ParseDirectory(byte[] data)
    {
        var (stride, count, _, baseOff) = ReadHeader(data);

        if (stride != Stride) throw new InvalidDataException($"unexpected record stride 0x{stride:X}");

        var names = new List<(string Name, int Pos, int End)>();

        for (int i = baseOff; i < data.Length; i++)
        {
            if (data[i] == (byte)'B' && TryReadName(data, i, out string name, out int end))
            {
                names.Add((name, i, end));
                i = end - 1;
            }
        }

        if (names.Count == 0) throw new InvalidDataException("No chart names found");

        if (names[0].Pos != baseOff)
            throw new InvalidDataException(
                $"first chart name at 0x{names[0].Pos:X}, but the header implies the chart region starts at 0x{baseOff:X}");

        if (names.Count != count)
            throw new InvalidDataException($"header says {count} charts, found {names.Count} names");

        var charts = new List<RhythmChart>(count);

        for (int k = 0; k < names.Count; k++)
        {
            var (name, pos, end) = names[k];
            int boundary = k + 1 < names.Count ? names[k + 1].Pos : data.Length;
            int table = Header + k * (int)stride + RecordHeader;

            if (U32(data, table - NamePtrBack) != (uint)(pos - baseOff))
                throw new InvalidDataException($"{name}: name pointer mismatch");

            var offsets = new uint[ChartSize];
            for (int i = 0; i < ChartSize; i++) offsets[i] = U32(data, table + 4 * i);

            var chart = new RhythmChart
            {
                Name = name, NamePos = pos, PayloadStart = end, Boundary = boundary,
                Table = table, Offsets = offsets, Base = baseOff,
            };

            CheckLayout(data, chart);
            charts.Add(chart);
        }

        return charts;
    }

    private static bool TryReadName(byte[] d, int p, out string name, out int end)
    {
        name = "";
        end = 0;

        if (p + 9 > d.Length) return false;
        if (d[p] != 'B' || d[p + 1] != 'G' || d[p + 2] != 'M') return false;
        for (int i = 3; i < 6; i++) if (d[p + i] < '0' || d[p + i] > '9') return false;
        if (d[p + 6] != '_' || d[p + 7] < '1' || d[p + 7] > '4') return false;

        int n = 8;
        if (d[p + n] == 'H') n++;
        if (p + n >= d.Length || d[p + n] != 0) return false;

        name = Encoding.ASCII.GetString(d, p, n);
        end = p + n + 1;
        return true;
    }

    private static void CheckLayout(byte[] data, RhythmChart c)
    {
        var nulls = new List<int>(ChartSize);

        for (int i = c.PayloadStart; i < c.Boundary; i++)
            if (data[i] == 0) nulls.Add(i - c.PayloadStart);

        int length = c.Boundary - c.PayloadStart;

        if (nulls.Count != ChartSize)
            throw new InvalidDataException($"{c.Name}: expected {ChartSize} tokens, found {nulls.Count} terminators");

        if (nulls[^1] != length - 1)
            throw new InvalidDataException($"{c.Name}: payload does not end with a terminator");

        for (int i = 0; i < ChartSize; i++)
        {
            int start = i == 0 ? 0 : nulls[i - 1] + 1;
            uint expected = (uint)(c.PayloadStart + start - c.Base);

            if (c.Offsets[i] != expected)
                throw new InvalidDataException($"{c.Name}: offset table disagrees with payload at token {i}");
        }
    }

    public static RhythmChart FindChart(IEnumerable<RhythmChart> charts, string name)
    {
        var found = charts.Where(c => c.Name == name).ToList();

        if (found.Count != 1)
            throw new InvalidDataException($"{name}: expected exactly one occurrence, found {found.Count}");

        return found[0];
    }

    public static byte[][] DecodeTokens(byte[] data, RhythmChart chart)
    {
        var tokens = new List<byte[]>(ChartSize);
        int start = chart.PayloadStart;

        for (int i = chart.PayloadStart; i < chart.Boundary; i++)
        {
            if (data[i] != 0) continue;
            tokens.Add(data[start..i]);
            start = i + 1;
        }

        return tokens.ToArray();
    }

    public static (string Symbols, List<(int Index, byte[] Token)> Unknown) TokensToSymbols(byte[][] tokens)
    {
        var sb = new StringBuilder(tokens.Length);
        var unknown = new List<(int, byte[])>();

        for (int i = 0; i < tokens.Length; i++)
        {
            if (TokenToSymbol.TryGetValue(Convert.ToHexString(tokens[i]), out char symbol))
            {
                sb.Append(symbol);
            }
            else
            {
                sb.Append(UnknownSymbol);
                unknown.Add((i, tokens[i]));
            }
        }

        return (sb.ToString(), unknown);
    }

    public static byte[][] SymbolsToTokens(string symbols) =>
        symbols.Select(s => Symbols[s]).ToArray();

    public static uint ChartIdFromName(string name)
    {
        var m = NameFull.Match(name);
        if (!m.Success) throw new ArgumentException($"not a chart name: '{name}'");

        return (uint)(int.Parse(m.Groups[1].Value) * 100
                      + (m.Groups[3].Value.Length > 0 ? 10 : 0)
                      + int.Parse(m.Groups[2].Value));
    }

    public static uint StoredLengthField(byte[] data, RhythmChart chart) =>
        U32(data, chart.Table - 4);

    public static uint LengthField(byte[][] tokens, double bpm)
    {
        if (bpm <= 0) throw new ArgumentException("BPM must be positive");

        int used = tokens.Count(t => t.Length != 0);
        return (uint)Math.Round(used * DurationFactor / bpm, MidpointRounding.ToEven);
    }

    public static int NonEmpty(byte[][] tokens) => tokens.Count(t => t.Length != 0);

    private sealed class Entry
    {
        public required string Name;
        public uint Id;
        public required byte[] Record;
        public required byte[] NameBytes;
        public byte[][]? Tokens;
        public byte[]? Payload;
        public uint? Length;
    }

    public static byte[] BuildFile(
        byte[] data,
        List<RhythmChart> charts,
        IDictionary<string, byte[][]>? replacements = null,
        IDictionary<string, ChartAddition>? additions = null,
        IDictionary<string, uint>? lengths = null)
    {
        replacements ??= new Dictionary<string, byte[][]>();
        additions ??= new Dictionary<string, ChartAddition>();
        lengths ??= new Dictionary<string, uint>();

        var (stride, count, flag, _) = ReadHeader(data);

        if (stride != Stride) throw new InvalidDataException($"unexpected record stride 0x{stride:X}");
        if (count != charts.Count) throw new InvalidDataException("chart list does not match the header");

        var names = charts.Select(c => c.Name).ToHashSet();

        var missing = replacements.Keys.Where(n => !names.Contains(n)).OrderBy(n => n).ToList();
        if (missing.Count > 0) throw new InvalidDataException($"Charts not found in file: {string.Join(", ", missing)}");

        var clash = additions.Keys.Where(names.Contains).OrderBy(n => n).ToList();
        if (clash.Count > 0) throw new InvalidDataException($"Charts already exist: {string.Join(", ", clash)}");

        var badLen = lengths.Keys.Where(n => !replacements.ContainsKey(n)).OrderBy(n => n).ToList();
        if (badLen.Count > 0) throw new InvalidDataException($"length given for charts that are not replaced: {string.Join(", ", badLen)}");

        foreach (var (name, tokens) in replacements)
            if (tokens.Length != ChartSize) throw new InvalidDataException($"{name}: expected {ChartSize} tokens");

        foreach (var (name, add) in additions)
            if (add.Tokens.Length != ChartSize) throw new InvalidDataException($"{name}: expected {ChartSize} tokens");

        var entries = new List<Entry>();

        for (int k = 0; k < charts.Count; k++)
        {
            var chart = charts[k];
            int recStart = Header + k * (int)stride;

            if (chart.Table != recStart + RecordHeader)
                throw new InvalidDataException($"{chart.Name}: record is not where the header implies");

            var record = data[recStart..(recStart + (int)stride)];
            uint cid = U32(record, 0);

            if (cid != ChartIdFromName(chart.Name))
                throw new InvalidDataException($"{chart.Name}: stored chart ID {cid} does not match its name");

            entries.Add(new Entry
            {
                Name = chart.Name,
                Id = cid,
                Record = record,
                NameBytes = data[chart.NamePos..chart.PayloadStart],
                Tokens = replacements.TryGetValue(chart.Name, out var t) ? t : null,
                Payload = data[chart.PayloadStart..chart.Boundary],
                Length = lengths.TryGetValue(chart.Name, out var l) ? l : null,
            });
        }

        foreach (var (name, add) in additions)
        {
            var record = new byte[stride];
            W32(record, 0, ChartIdFromName(name));
            W32(record, LengthOffset, add.Length);

            entries.Add(new Entry
            {
                Name = name,
                Id = ChartIdFromName(name),
                Record = record,
                NameBytes = Encoding.ASCII.GetBytes(name + "\0"),
                Tokens = add.Tokens,
                Payload = null,
                Length = null,
            });
        }

        entries = entries.OrderBy(e => e.Id).ToList();

        if (entries.Select(e => e.Id).Distinct().Count() != entries.Count)
            throw new InvalidDataException("duplicate chart IDs");

        int newCount = entries.Count;
        int newBase = Header + newCount * (int)stride;
        var region = new MemoryStream();

        foreach (var e in entries)
        {
            int start = (int)region.Length;
            region.Write(e.NameBytes);
            int payloadStart = (int)region.Length;
            var offsets = new uint[ChartSize];

            if (e.Tokens != null)
            {
                int cursor = payloadStart;

                for (int i = 0; i < ChartSize; i++)
                {
                    offsets[i] = (uint)cursor;
                    cursor += e.Tokens[i].Length + 1;
                    region.Write(e.Tokens[i]);
                    region.WriteByte(0);
                }
            }
            else
            {
                region.Write(e.Payload!);

                uint first = U32(e.Record, RecordHeader);

                for (int i = 0; i < ChartSize; i++)
                    offsets[i] = U32(e.Record, RecordHeader + 4 * i) - first + (uint)payloadStart;
            }

            W32(e.Record, 4, (uint)start);

            for (int i = 0; i < ChartSize; i++) W32(e.Record, RecordHeader + 4 * i, offsets[i]);

            if (e.Length.HasValue) W32(e.Record, LengthOffset, e.Length.Value);
        }

        using var output = new MemoryStream();
        var header = new byte[Header];
        W32(header, 0, stride);
        W32(header, 4, (uint)newCount);
        W32(header, 8, flag);
        output.Write(header);

        foreach (var e in entries) output.Write(e.Record);

        if (output.Length != newBase) throw new InvalidOperationException("internal error: record table size");

        output.Write(region.ToArray());
        return output.ToArray();
    }

    public static void Verify(
        byte[] original,
        List<RhythmChart> oldCharts,
        byte[] result,
        IDictionary<string, byte[][]>? replacements = null,
        IDictionary<string, ChartAddition>? additions = null,
        IDictionary<string, uint>? lengths = null)
    {
        replacements ??= new Dictionary<string, byte[][]>();
        additions ??= new Dictionary<string, ChartAddition>();
        lengths ??= new Dictionary<string, uint>();

        var (stride, oldCount, flag, _) = ReadHeader(original);
        var (newStride, newCount, newFlag, newBase) = ReadHeader(result);

        if (newStride != stride || newFlag != flag) throw new InvalidDataException("header stride/flag changed");
        if (newCount != oldCount + additions.Count) throw new InvalidDataException("chart count is wrong");

        var newCharts = ParseDirectory(result);
        var byOld = oldCharts.ToDictionary(c => c.Name);
        var oldIndex = oldCharts.Select((c, i) => (c.Name, i)).ToDictionary(x => x.Name, x => x.i);

        var expectedIds = byOld.Keys.Concat(additions.Keys).Select(ChartIdFromName).OrderBy(x => x).ToList();

        if (!newCharts.Select(c => ChartIdFromName(c.Name)).SequenceEqual(expectedIds))
            throw new InvalidDataException("charts are not in ascending chart-ID order");

        var newNames = newCharts.Select(c => c.Name).ToHashSet();

        if (!newNames.SetEquals(byOld.Keys.Concat(additions.Keys)))
            throw new InvalidDataException("chart list is wrong");

        if (!newCharts.Where(c => byOld.ContainsKey(c.Name)).Select(c => c.Name)
                .SequenceEqual(oldCharts.Select(c => c.Name)))
            throw new InvalidDataException("existing charts changed order");

        for (int k = 0; k < newCharts.Count; k++)
        {
            var nc = newCharts[k];
            var rec = result[(Header + k * (int)stride)..(Header + (k + 1) * (int)stride)];

            if (U32(rec, 0) != ChartIdFromName(nc.Name))
                throw new InvalidDataException($"{nc.Name}: record chart ID wrong");

            if (additions.TryGetValue(nc.Name, out var add))
            {
                if (!SameTokens(DecodeTokens(result, nc), add.Tokens))
                    throw new InvalidDataException($"{nc.Name}: tokens differ from expectation");

                if (StoredLengthField(result, nc) != add.Length)
                    throw new InvalidDataException($"{nc.Name}: length field wrong");

                if (!rec[8..12].All(b => b == 0) || !rec[(RecordHeader + 4 * ChartSize)..].All(b => b == 0))
                    throw new InvalidDataException($"{nc.Name}: record padding is not zero");

                continue;
            }

            var old = byOld[nc.Name];
            var expected = replacements.TryGetValue(nc.Name, out var r) ? r : DecodeTokens(original, old);

            if (!SameTokens(DecodeTokens(result, nc), expected))
                throw new InvalidDataException($"{nc.Name}: tokens differ from expectation");

            int kOld = oldIndex[old.Name];
            var oldRec = original[(Header + kOld * (int)stride)..(Header + (kOld + 1) * (int)stride)];
            var allowed = new List<(int, int)> { (4, 8), (RecordHeader, RecordHeader + 4 * ChartSize) };

            if (lengths.ContainsKey(nc.Name)) allowed.Add((12, 16));

            if (!RecordDiffOk(oldRec, rec, allowed))
                throw new InvalidDataException($"{nc.Name}: record bytes changed outside pointers/offsets");

            if (lengths.TryGetValue(nc.Name, out var len) && StoredLengthField(result, nc) != len)
                throw new InvalidDataException($"{nc.Name}: length field wrong");
        }

        long accounted = newBase + newCharts.Sum(c => (long)(c.Boundary - c.NamePos));

        if (result.Length != accounted) throw new InvalidDataException("unaccounted bytes in the file");
    }

    private static bool SameTokens(byte[][] a, byte[][] b)
    {
        if (a.Length != b.Length) return false;

        for (int i = 0; i < a.Length; i++)
            if (!a[i].AsSpan().SequenceEqual(b[i])) return false;

        return true;
    }

    private static bool RecordDiffOk(byte[] old, byte[] now, List<(int, int)> allowed)
    {
        if (old.Length != now.Length) return false;

        var mask = new bool[old.Length];

        foreach (var (a, b) in allowed)
            for (int i = a; i < b; i++) mask[i] = true;

        for (int i = 0; i < old.Length; i++)
            if (!mask[i] && old[i] != now[i]) return false;

        return true;
    }

    public static string RenderChartText(byte[] data, RhythmChart chart, bool allowUnknown, Action<string>? warn = null)
    {
        var (symbols, unknown) = TokensToSymbols(DecodeTokens(data, chart));

        if (unknown.Count > 0)
        {
            var kinds = unknown
                .GroupBy(u => Convert.ToHexString(u.Token))
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key} x{g.Count()} (first at token {g.First().Index})");

            string message = $"{chart.Name} contains {unknown.Count} unrecognised token(s): {string.Join("; ", kinds)}";

            if (!allowUnknown)
                throw new InvalidDataException(
                    message + $". Nothing was written. Add the missing form to Symbols in RhythmDat.cs, or pass " +
                    $"--allow-unknown to write '{UnknownSymbol}' placeholders (those cannot be re-imported).");

            warn?.Invoke($"WARNING: {message} -> written as '{UnknownSymbol}'");
        }

        string block = chart.Name.Split('_')[1].Replace("H", "");
        var sb = new StringBuilder();
        sb.Append($"=== {block} ===");

        for (int row = 0; row < ChartSize / 4; row++)
        {
            sb.Append('\n');
            sb.Append(symbols, row * 4, 4);
        }

        return sb.ToString();
    }

    public static SortedDictionary<int, string> ReadTextCharts(string text)
    {
        var blocks = new SortedDictionary<int, StringBuilder>();
        StringBuilder? current = null;
        var heading = new Regex(@"^===\s*([1-4])\s*===$");
        int lineNumber = 0;

        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            string line = raw.Trim();

            if (line.Length == 0) continue;

            var m = heading.Match(line);

            if (m.Success)
            {
                int number = int.Parse(m.Groups[1].Value);

                if (blocks.ContainsKey(number)) throw new InvalidDataException($"Duplicate block {number}");

                current = blocks[number] = new StringBuilder();
                continue;
            }

            if (current == null)
                throw new InvalidDataException($"Unexpected text before first block at line {lineNumber}");

            if (line.Length != 4)
                throw new InvalidDataException($"Line {lineNumber}: expected 4 characters, got {line.Length}");

            foreach (char ch in line)
            {
                if (ch == UnknownSymbol)
                    throw new InvalidDataException(
                        $"Line {lineNumber}: '{UnknownSymbol}' marks a token the exporter did not recognise; " +
                        "replace it with a valid symbol before importing");

                if (!Symbols.ContainsKey(ch))
                    throw new InvalidDataException($"Invalid character '{ch}' at line {lineNumber}");
            }

            current.Append(line);
        }

        if (blocks.Count == 0) throw new InvalidDataException("No chart blocks found");

        var result = new SortedDictionary<int, string>();

        foreach (var (number, sb) in blocks)
        {
            if (sb.Length != ChartSize)
                throw new InvalidDataException($"Block {number}: expected {ChartSize} symbols, got {sb.Length}");

            result[number] = sb.ToString();
        }

        return result;
    }


    internal static uint U32(byte[] b, int off) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(off));

    internal static void W32(byte[] b, int off, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(off), v);

    public static HashSet<uint> ChartIds(byte[] data)
    {
        var (stride, count, _, _) = ReadHeader(data);
        var ids = new HashSet<uint>();

        for (int k = 0; k < count; k++) ids.Add(U32(data, Header + k * (int)stride));

        return ids;
    }
}
