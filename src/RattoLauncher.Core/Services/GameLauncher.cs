using System.Diagnostics;

namespace RattoLauncher.Core.Services;

public static class GameLauncher
{
    public static Process Start(string exePath, Action<int> onExited, IEnumerable<string>? args = null)
    {
        var psi = new ProcessStartInfo(exePath){
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false
        };

        if (args != null) foreach (var a in args) psi.ArgumentList.Add(a);
        
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Exited += (_, _) => onExited(proc.ExitCode);
        proc.Start();
        return proc;
    }
}
