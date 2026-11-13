namespace RattoLauncher.Core;

public static class Helpers
{
    public static bool DoFileExist(string filePath)
    {
        return File.Exists(filePath);
    }
}