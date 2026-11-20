using System.Text;

namespace MadRat.Songs;

public sealed class SongRecord
{
    public const int Langs = 4;

    public uint Id;
    public string Str04 = "";
    public string Str08 = "";
    public string Str0C = "";
    public string Str10 = "";
    public uint[] ChartIds = new uint[8];
    public uint Num34;
    public uint Num38;
    public string[] Title = Blank();
    public string[] Composer = Blank();
    public string[] Composer2 = Blank();
    public uint Bpm;
    public uint Selectable;
    public uint Sort;

    private static string[] Blank() => ["", "", "", ""];

    public SongRecord Copy() => new()
    {
        Id = Id, Str04 = Str04, Str08 = Str08, Str0C = Str0C, Str10 = Str10,
        ChartIds = (uint[])ChartIds.Clone(), Num34 = Num34, Num38 = Num38,
        Title = (string[])Title.Clone(), Composer = (string[])Composer.Clone(),
        Composer2 = (string[])Composer2.Clone(),
        Bpm = Bpm, Selectable = Selectable, Sort = Sort,
    };

    public bool SameAs(SongRecord o) =>
        Id == o.Id && Str04 == o.Str04 && Str08 == o.Str08 && Str0C == o.Str0C && Str10 == o.Str10
        && ChartIds.SequenceEqual(o.ChartIds) && Num34 == o.Num34 && Num38 == o.Num38
        && Title.SequenceEqual(o.Title) && Composer.SequenceEqual(o.Composer)
        && Composer2.SequenceEqual(o.Composer2)
        && Bpm == o.Bpm && Selectable == o.Selectable && Sort == o.Sort;
}

public sealed class SoundDatDocument
{
    public uint Flag = 1;
    public List<SongRecord> Records = [];
}

public sealed class CloneOptions
{
    public required uint SourceId { get; init; }
    public required uint NewId { get; init; }
    public string? Title { get; init; }
    public string? Composer { get; init; }
    public uint? Bpm { get; init; }
    public uint? Sort { get; init; }
    public bool KeepNames { get; init; }
    public bool KeepCharts { get; init; }
    public bool AllowDuplicateId { get; init; }
    public int? InsertAt { get; init; }
    public string? Str04 { get; init; }
    public string? Str08 { get; init; }
    public string? Str0C { get; init; }
    public string? Str10 { get; init; }
    public Dictionary<uint, uint> SetSort { get; init; } = new();
}

public sealed record CloneResult(SongRecord NewRecord, int Index);

public static class SoundDat
{
    public const int Header = 0x0C;
    public const int RecordSize = 0x78;

    private static readonly int[] ChartOffs = Enumerable.Range(0, 8).Select(i => 0x14 + 4 * i).ToArray();
    private static readonly int[] TitleOffs = Enumerable.Range(0, SongRecord.Langs).Select(i => 0x3C + 4 * i).ToArray();
    private static readonly int[] CompOffs = Enumerable.Range(0, SongRecord.Langs).Select(i => 0x4C + 8 * i).ToArray();
    private static readonly int[] Comp2Offs = Enumerable.Range(0, SongRecord.Langs).Select(i => 0x50 + 8 * i).ToArray();

    private static readonly int[] PoolOrder =
        new[] { 0x04, 0x08, 0x0C, 0x10 }.Concat(Enumerable.Range(0, 12).Select(i => 0x3C + 4 * i)).ToArray();

    private static uint U32(byte[] b, int off) => RhythmDat.U32(b, off);


    public static SoundDatDocument Parse(byte[] data)
    {
        if (data.Length < Header) throw new InvalidDataException("file too small");

        uint recSize = U32(data, 0);
        uint count = U32(data, 4);
        uint present = U32(data, 8);

        if (recSize != RecordSize) throw new InvalidDataException($"unexpected record size 0x{recSize:X}");

        long poolBase = Header + (long)count * recSize;

        if (poolBase > data.Length) throw new InvalidDataException("record table runs past end of file");

        string Str(uint rel, string what)
        {
            long pos = poolBase + rel;

            if (pos >= data.Length) throw new InvalidDataException($"{what}: string pointer 0x{rel:X} outside the file");

            int end = Array.IndexOf(data, (byte)0, (int)pos);

            if (end < 0) throw new InvalidDataException($"{what}: unterminated string");

            return Encoding.UTF8.GetString(data, (int)pos, end - (int)pos);
        }

        var doc = new SoundDatDocument { Flag = present };

        for (int i = 0; i < count; i++)
        {
            int o = Header + i * RecordSize;
            uint G(int off) => U32(data, o + off);
            string w = $"record {i}";

            var r = new SongRecord
            {
                Id = G(0x00),
                Str04 = Str(G(0x04), $"{w} str_04"),
                Str08 = Str(G(0x08), $"{w} str_08"),
                Str0C = Str(G(0x0C), $"{w} str_0c"),
                Str10 = Str(G(0x10), $"{w} str_10"),
                ChartIds = ChartOffs.Select(off => G(off)).ToArray(),
                Num34 = G(0x34),
                Num38 = G(0x38),
                Title = TitleOffs.Select(off => Str(G(off), $"{w} title")).ToArray(),
                Composer = CompOffs.Select(off => Str(G(off), $"{w} composer")).ToArray(),
                Composer2 = Comp2Offs.Select(off => Str(G(off), $"{w} composer2")).ToArray(),
                Bpm = G(0x6C),
                Selectable = G(0x70),
                Sort = G(0x74),
            };

            doc.Records.Add(r);
        }

        return doc;
    }

    private static void Check(SongRecord r, int index)
    {
        string w = $"record {index} (id {r.Id})";

        if (r.ChartIds.Length != 8) throw new InvalidDataException($"{w}: chart_ids needs 8 entries");

        foreach (var (key, arr) in new[] { ("title", r.Title), ("composer", r.Composer), ("composer2", r.Composer2) })
            if (arr.Length != SongRecord.Langs) throw new InvalidDataException($"{w}: {key} needs {SongRecord.Langs} entries");

        var all = new[] { r.Str04, r.Str08, r.Str0C, r.Str10 }.Concat(r.Title).Concat(r.Composer).Concat(r.Composer2);

        if (all.Any(s => s.Contains('\0'))) throw new InvalidDataException($"{w}: strings must not contain NUL");
    }

    public static (byte[] Data, List<string> Warnings) Build(SoundDatDocument doc)
    {
        var records = doc.Records;

        for (int i = 0; i < records.Count; i++) Check(records[i], i);

        var warnings = new List<string>();
        var ids = records.Select(r => r.Id).ToList();

        var dupes = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).OrderBy(x => x).ToList();

        if (dupes.Count > 0)
            warnings.Add("duplicate record IDs: " + string.Join(", ", dupes.Select(x => $"0x{x:X}")));

        if (!ids.SequenceEqual(ids.OrderBy(x => x)))
            warnings.Add(
                "records are NOT in ascending ID order: the game looks a song up by ID when it starts it, " +
                "and out-of-order records make it hang (tested: the song with the out-of-order ID, and the " +
                "IDs after it, hang on load)");

        var sel = records.Where(r => r.Selectable == 1).Select(r => r.Sort).OrderBy(x => x).ToList();

        if (!sel.SequenceEqual(Enumerable.Range(1, sel.Count).Select(x => (uint)x)))
            warnings.Add(
                $"sort keys of selectable records are not exactly 1..N (N = {sel.Count}); " +
                "the original file is a clean 1..42 permutation");

        var pool = new MemoryStream();
        var recBytes = new MemoryStream();

        foreach (var r in records)
        {
            var fields = new uint[RecordSize / 4];

            void Put(int off, uint v) => fields[off / 4] = v;

            void AddString(int off, string text)
            {
                Put(off, (uint)pool.Length);
                pool.Write(Encoding.UTF8.GetBytes(text));
                pool.WriteByte(0);
            }

            Put(0x00, r.Id);

            var byOff = new Dictionary<int, string>
            {
                [0x04] = r.Str04, [0x08] = r.Str08, [0x0C] = r.Str0C, [0x10] = r.Str10,
            };

            for (int i = 0; i < SongRecord.Langs; i++)
            {
                byOff[TitleOffs[i]] = r.Title[i];
                byOff[CompOffs[i]] = r.Composer[i];
                byOff[Comp2Offs[i]] = r.Composer2[i];
            }

            foreach (int off in PoolOrder) AddString(off, byOff[off]);

            for (int i = 0; i < 8; i++) Put(ChartOffs[i], r.ChartIds[i]);

            Put(0x34, r.Num34);
            Put(0x38, r.Num38);
            Put(0x6C, r.Bpm);
            Put(0x70, r.Selectable);
            Put(0x74, r.Sort);

            var raw = new byte[RecordSize];
            for (int i = 0; i < fields.Length; i++) RhythmDat.W32(raw, i * 4, fields[i]);
            recBytes.Write(raw);
        }

        var output = new byte[Header];
        RhythmDat.W32(output, 0, RecordSize);
        RhythmDat.W32(output, 4, (uint)records.Count);
        RhythmDat.W32(output, 8, doc.Flag);

        return (output.Concat(recBytes.ToArray()).Concat(pool.ToArray()).ToArray(), warnings);
    }

    public static (byte[] Data, List<string> Warnings) BuildChecked(SoundDatDocument doc, byte[]? rhythmDat = null,
        string rhythmName = "Rhythm.dat")
    {
        var (data, warnings) = Build(doc);
        var again = Parse(data);

        if (again.Records.Count != doc.Records.Count || again.Flag != doc.Flag ||
            !again.Records.Zip(doc.Records, (a, b) => a.SameAs(b)).All(x => x))
            throw new InvalidOperationException("internal error: rebuilt file does not match the input");

        if (rhythmDat != null)
        {
            var have = RhythmDat.ChartIds(rhythmDat);

            foreach (var r in doc.Records)
            {
                var missing = r.ChartIds.Where(c => c != 0 && !have.Contains(c)).ToList();

                if (missing.Count > 0)
                    warnings.Add(
                        $"record id 0x{r.Id:X}: chart IDs [{string.Join(", ", missing)}] do not exist in " +
                        $"{rhythmName} (selecting this song would have no chart to load)");
            }
        }

        return (data, warnings);
    }

    public static CloneResult Clone(SoundDatDocument doc, CloneOptions o)
    {
        var records = doc.Records;
        var src = records.Where(r => r.Id == o.SourceId).ToList();

        if (src.Count != 1) throw new InvalidDataException($"expected exactly one record with id 0x{o.SourceId:X}");

        if (records.Any(r => r.Id == o.NewId) && !o.AllowDuplicateId)
            throw new InvalidDataException($"id 0x{o.NewId:X} already exists (allow duplicates only for experiments)");

        var n = src[0].Copy();
        n.Id = o.NewId;
        string name = $"BGM{o.NewId:D3}";

        if (!o.KeepNames)
        {
            n.Str04 = n.Str08 = n.Str0C = name;
            n.Str10 = name + "_Select";
        }

        if (!o.KeepCharts)
            n.ChartIds = src[0].ChartIds.Select(old => old == 0 ? 0u : o.NewId * 100 + old % 100).ToArray();

        if (o.Str04 != null) n.Str04 = o.Str04;
        if (o.Str08 != null) n.Str08 = o.Str08;
        if (o.Str0C != null) n.Str0C = o.Str0C;
        if (o.Str10 != null) n.Str10 = o.Str10;

        if (o.Title != null) n.Title = Enumerable.Repeat(o.Title, SongRecord.Langs).ToArray();

        if (o.Composer != null)
        {
            n.Composer = Enumerable.Repeat(o.Composer, SongRecord.Langs).ToArray();
            n.Composer2 = Enumerable.Repeat("", SongRecord.Langs).ToArray();
        }

        if (o.Bpm.HasValue) n.Bpm = o.Bpm.Value;

        n.Sort = o.Sort ?? records.Where(r => r.Selectable == 1).Max(r => r.Sort) + 1;
        n.Selectable = 1;

        foreach (var (rid, value) in o.SetSort)
        {
            var target = records.Where(r => r.Id == rid).ToList();

            if (target.Count != 1)
                throw new InvalidDataException($"set-sort: expected exactly one record with id 0x{rid:X}");

            target[0].Sort = value;
        }

        int position;

        if (o.InsertAt is null)
        {
            position = records.FindIndex(r => r.Id > n.Id);
            if (position < 0) position = records.Count;
        }
        else
        {
            if (o.InsertAt < 0 || o.InsertAt > records.Count)
                throw new ArgumentException($"insert-at must be between 0 and {records.Count}");

            position = o.InsertAt.Value;
        }

        records.Insert(position, n);
        return new CloneResult(n, position);
    }

    public static int IndexOfId(SoundDatDocument doc, uint id)
    {
        int i = doc.Records.FindIndex(r => r.Id == id);

        if (i < 0) throw new InvalidDataException($"ID 0x{id:X} is not in this Sound.dat");

        return i;
    }
}
