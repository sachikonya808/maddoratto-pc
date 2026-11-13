using System.Text.Json;
using RattoLauncher.Core.Models;

namespace RattoLauncher.Core.Services;

public static class ConfigService
{
    private static readonly string AppDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RattoLauncher");
    private static readonly string ConfigPath = Path.Combine(AppDirectory, "config.json");
    private static readonly JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static AppConfig? Load()
    {
        if (!Helpers.DoFileExist(ConfigPath)) { return null; }

        try
        {
            string json = File.ReadAllText(ConfigPath);

            return JsonSerializer.Deserialize<AppConfig>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(AppConfig appConfig)
    {
        Directory.CreateDirectory(AppDirectory);

        string json = JsonSerializer.Serialize(appConfig, jsonSerializerOptions);

        File.WriteAllText(ConfigPath, json);
    }
}
