using System.Security.Principal;

namespace NeuroTune;

public static class LogService
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(SettingsService.DataDirectory, "NeuroTune.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(SettingsService.DataDirectory);
            foreach (var identity in new[] { Environment.UserName, Environment.MachineName })
                if (!string.IsNullOrWhiteSpace(identity)) message = message.Replace(identity, "[redatto]", StringComparison.OrdinalIgnoreCase);
            lock (Gate) File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O}\t{message}{Environment.NewLine}");
        }
        catch { }
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
