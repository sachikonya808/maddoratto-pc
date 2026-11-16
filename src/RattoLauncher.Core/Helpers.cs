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
}