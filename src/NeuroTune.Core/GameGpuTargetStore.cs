using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NeuroTune;

internal sealed record GameGpuTarget(string Id, string ExecutableName, string ExecutablePath);

internal static class GameGpuTargetStore
{
    private static readonly string PathName = Path.Combine(SettingsService.DataDirectory, "game-gpu-targets.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Register(IEnumerable<string> executablePaths)
    {
        try
        {
            WithLock(() =>
            {
                var targets = LoadCore(PathName).ToDictionary(target => target.Id, StringComparer.OrdinalIgnoreCase);
                foreach (var path in executablePaths.Select(Normalize).Where(path => path is not null).Cast<string>())
                {
                    var target = new GameGpuTarget(CreateId(path), Path.GetFileName(path), path);
                    if (targets.TryGetValue(target.Id, out var existing) && !existing.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("A per-app GPU target ID collision was detected.");
                    if (targets.Count < 20 || targets.ContainsKey(target.Id)) targets[target.Id] = target;
                }
                // ponytail: twenty stable targets bound planner size; add explicit target management if real libraries need rotation.
                var saved = targets.Values.OrderBy(target => target.ExecutableName, StringComparer.OrdinalIgnoreCase).ToList();
                Directory.CreateDirectory(SettingsService.DataDirectory);
                WriteAtomic(PathName, saved);
            });
        }
        catch (Exception exception) { Console.Error.WriteLine($"Per-app GPU target cache was ignored: {exception.Message}"); }
    }

    public static IReadOnlyList<GameGpuTarget> Load()
    {
        try { return WithLock(() => LoadCore(PathName)); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Per-app GPU target cache was ignored: {exception.Message}");
            return [];
        }
    }

    internal static IReadOnlyList<GameGpuTarget> LoadFrom(string path) => LoadCore(path);

    internal static string CreateId(string path) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..16].ToLowerInvariant();

    private static List<GameGpuTarget> LoadCore(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var targets = JsonSerializer.Deserialize<List<GameGpuTarget>>(File.ReadAllText(path)) ?? [];
            if (targets.Count > 20 || targets.Any(target => target.Id != CreateId(target.ExecutablePath) ||
                target.ExecutableName != Path.GetFileName(target.ExecutablePath) || target.ExecutablePath.Length > 2_048))
                throw new InvalidOperationException("The per-app GPU target store is invalid.");
            return targets;
        }
        catch (Exception exception)
        {
            try { File.Move(path, $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"); }
            catch { /* A disposable cache must never block the agent if quarantine is unavailable. */ }
            Console.Error.WriteLine($"Corrupt per-app GPU target cache quarantined or ignored: {path}: {exception.Message}");
            return [];
        }
    }

    private static void WriteAtomic(string path, IReadOnlyList<GameGpuTarget> targets)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, targets, JsonOptions);
            stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    private static string? Normalize(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return full.Length <= 2_048 && Path.GetExtension(full).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(full)
                ? full : null;
        }
        catch { return null; }
    }

    private static T WithLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, @"Global\NeuroTuneGameGpuTargets");
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Timed out waiting for the per-app GPU target cache lock.");
        }
        catch (AbandonedMutexException) { }
        try { return action(); }
        finally { mutex.ReleaseMutex(); }
    }

    private static void WithLock(Action action) => WithLock(() => { action(); return true; });
}
