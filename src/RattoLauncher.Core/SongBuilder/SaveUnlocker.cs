using MadRat.Songs;

namespace RattoLauncher.Core.Services;

public static class SaveUnlocker
{
    public const uint FirstId = 0x66;
    public const uint LastId = 0x3E7;

    public static List<string> UnlockRange(string saveFolder, string soundDatPath,
        uint firstId = FirstId, uint lastId = LastId)
    {
        string save = Path.Combine(saveFolder, "System");
        string bak = save + ".bak";

        if (!File.Exists(soundDatPath))
            throw new FileNotFoundException($"Build the songs first: {soundDatPath} does not exist.");

        if (!File.Exists(bak))
        {
            if (!File.Exists(save))
                throw new FileNotFoundException($"Put your 'System' save file in: {saveFolder}");

            File.Copy(save, bak);
        }

        byte[] data = File.ReadAllBytes(bak);

        var doc = SoundDat.Parse(File.ReadAllBytes(soundDatPath));
        var songs = doc.Records
            .Select((r, index) => (r.Id, Index: index))
            .Where(x => x.Id >= firstId && x.Id <= lastId)
            .Select(x => ($"ID 0x{x.Id:X} = index {x.Index}", x.Index))
            .ToList();

        if (songs.Count == 0)
            throw new InvalidOperationException(
                $"This Sound.dat has no songs with IDs 0x{firstId:X}-0x{lastId:X}.");

        var log = SaveFlags.Unlock(data, songs);

        string tmp = save + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, save, overwrite: true);

        log.Insert(0, $"Unlocked {songs.Count} song(s). Backup: {bak}");
        return log;
    }
}