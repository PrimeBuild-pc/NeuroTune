using Microsoft.Win32;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NeuroTune;

internal sealed record GamingContext(
    List<string> Launchers,
    List<string> Games,
    List<string> Executables,
    List<string> GraphicsApis,
    List<string> GpuPreferences,
    List<string> Displays,
    List<string> ActiveGpuMappings);

internal static class GamingContextDetector
{
    private sealed record Game(string Name, string Launcher, string? Directory, string? Executable);
    private static readonly string[] ApiNames = ["d3d12.dll", "d3d11.dll", "dxgi.dll", "vulkan-1.dll", "opengl32.dll"];

    public static GamingContext Collect(IReadOnlyList<string> installedSoftware, bool registerTargets = false)
    {
        var launchers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var games = new List<Game>();
        var steamRoot = RegistryText(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath") ??
            RegistryText(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        if (steamRoot is not null)
        {
            launchers.Add("Steam: detected");
            try { games.AddRange(ReadSteam(steamRoot)); }
            catch { /* Detection remains best-effort and never blocks the system scan. */ }
        }
        var epicRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (Directory.Exists(epicRoot))
        {
            launchers.Add("Epic Games Launcher: detected");
            try { games.AddRange(Directory.EnumerateFiles(epicRoot, "*.item").Take(500).Select(ParseEpic).Where(game => game is not null).Cast<Game>()); }
            catch { /* Detection remains best-effort and never blocks the system scan. */ }
        }
        foreach (var name in new[] { "Xbox", "GOG Galaxy", "Battle.net", "EA app", "Ubisoft Connect" })
            if (installedSoftware.Any(item => item.Contains(name, StringComparison.OrdinalIgnoreCase))) launchers.Add($"{name}: detected");

        var running = RunningExecutables();
        var executables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            var executable = game.Executable;
            if (executable is null && game.Directory is not null)
                executable = running.FirstOrDefault(path => IsBelow(path, game.Directory));
            if (executable is not null && File.Exists(executable)) executables[Path.GetFileName(executable)] = executable;
        }
        var preferences = ReadGpuPreferences(executables);
        if (registerTargets) GameGpuTargetStore.Register(executables.Values);
        var apiSignals = executables.SelectMany(pair => DetectGraphicsApis(pair.Value)
            .Select(api => $"{pair.Key}: {api}")).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(200).ToList();
        var adapters = SystemProfiler.Query("SELECT Name, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, VideoModeDescription FROM Win32_VideoController",
            row => (Name: row["Name"]?.ToString() ?? "Unknown GPU", Driver: row["DriverVersion"]?.ToString() ?? "Unavailable",
                Width: Convert.ToUInt32(row["CurrentHorizontalResolution"] ?? 0), Height: Convert.ToUInt32(row["CurrentVerticalResolution"] ?? 0),
                Refresh: Convert.ToUInt32(row["CurrentRefreshRate"] ?? 0), Mode: row["VideoModeDescription"]?.ToString() ?? "Unavailable"));
        var displays = adapters.Select(adapter => $"{adapter.Name}: {adapter.Width}x{adapter.Height} @ {adapter.Refresh} Hz; {adapter.Mode}")
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        var active = adapters.Where(adapter => adapter.Width > 0 && adapter.Height > 0)
            .Select(adapter => $"Active display pipeline: {adapter.Name}; driver {adapter.Driver}; {adapter.Width}x{adapter.Height} @ {adapter.Refresh} Hz")
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        return new(launchers.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            games.Select(game => $"{game.Name} ({game.Launcher})").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(500).ToList(),
            executables.Keys.Order(StringComparer.OrdinalIgnoreCase).Take(200).ToList(), apiSignals, preferences, displays, active);
    }

    internal static (string Name, string InstallDirectory)? ParseSteamManifest(string text)
    {
        var name = Regex.Match(text, "\\\"name\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        var directory = Regex.Match(text, "\\\"installdir\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        return name.Length > 0 && directory.Length > 0 ? (name, directory) : null;
    }

    internal static IReadOnlyList<string> DetectGraphicsApis(string executable)
    {
        try
        {
            using var stream = File.OpenRead(executable);
            // ponytail: import-name scanning is bounded to PE headers/early sections; parse the PE import table if this misses real games.
            var length = (int)Math.Min(stream.Length, 2 * 1024 * 1024);
            var bytes = new byte[length];
            _ = stream.ReadAtLeast(bytes, length, false);
            var text = Encoding.Latin1.GetString(bytes);
            return ApiNames.Where(api => text.Contains(api, StringComparison.OrdinalIgnoreCase))
                .Select(api => api switch
                {
                    "d3d12.dll" => "Direct3D 12 import signal",
                    "d3d11.dll" => "Direct3D 11 import signal",
                    "dxgi.dll" => "DXGI import signal",
                    "vulkan-1.dll" => "Vulkan import signal",
                    _ => "OpenGL import signal"
                }).ToList();
        }
        catch { return []; }
    }

    private static IEnumerable<Game> ReadSteam(string root)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
            foreach (Match match in Regex.Matches(File.ReadAllText(libraryFile), "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\", StringComparison.Ordinal));
        foreach (var library in libraries.Where(Directory.Exists).Take(32))
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf").Take(500))
            {
                (string Name, string InstallDirectory)? parsed;
                try { parsed = ParseSteamManifest(File.ReadAllText(manifest)); }
                catch { continue; }
                if (parsed is { } item) yield return new(item.Name, "Steam", Path.Combine(steamApps, "common", item.InstallDirectory), null);
            }
        }
    }

    private static Game? ParseEpic(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var name = root.TryGetProperty("DisplayName", out var displayName) ? displayName.GetString()?.Trim() : null;
            var location = root.TryGetProperty("InstallLocation", out var installLocation) ? installLocation.GetString() : null;
            var launch = root.TryGetProperty("LaunchExecutable", out var launchExecutable) ? launchExecutable.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) return null;
            var executable = !string.IsNullOrWhiteSpace(location) && !string.IsNullOrWhiteSpace(launch) ? Path.Combine(location, launch) : null;
            return new(name, "Epic Games", location, executable);
        }
        catch { return null; }
    }

    private static List<string> ReadGpuPreferences(Dictionary<string, string> executables)
    {
        var result = new List<string>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            if (key is null) return result;
            foreach (var path in key.GetValueNames().Take(500))
            {
                var executable = Path.GetFileName(path);
                if (executable.Length == 0) continue;
                var value = key.GetValue(path)?.ToString() ?? "Unavailable";
                result.Add($"{executable}: {GpuPreference(value)}");
                if (File.Exists(path)) executables.TryAdd(executable, path);
            }
        }
        catch { }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(500).ToList();
    }

    private static string GpuPreference(string value) => value.Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase)
        ? "High performance" : value.Contains("GpuPreference=1", StringComparison.OrdinalIgnoreCase) ? "Power saving" : "Windows default/unspecified";

    private static string? RegistryText(RegistryKey hive, string path, string name)
    {
        try { using var key = hive.OpenSubKey(path); return key?.GetValue(name)?.ToString(); }
        catch { return null; }
    }

    private static List<string> RunningExecutables()
    {
        var result = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            try { if (process.MainModule?.FileName is { } path) result.Add(path); }
            catch { }
            finally { process.Dispose(); }
        }
        return result;
    }

    private static bool IsBelow(string path, string directory)
    {
        try
        {
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
