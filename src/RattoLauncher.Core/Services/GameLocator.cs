namespace RattoLauncher.Core.Services;

public static class GameLocator
{
    public const string TitleID = "0100E6701231C000";
    
    public static string? FindGameExecutable(string installFolder)
    {
        var dir = Path.Combine(installFolder, TitleID);
        if (!Directory.Exists(dir)) { return null; }
        
        var baseExe = Path.Combine(dir, $"{TitleID}.exe");
        return Helpers.DoFileExist(baseExe) ? baseExe : null;
    }
}
