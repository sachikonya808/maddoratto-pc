namespace RattoLauncher.Core.Models;

public class AppConfig
{
    public string? BaseGame { get; set; }
    public string? Update { get; set; }
    public string? Keys { get; set; }
    public string? InstallationFolder { get; set; }
    public bool SetupCompleted { get; set; } = false;
}