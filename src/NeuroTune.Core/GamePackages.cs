using System.Text.Json;

namespace NeuroTune;

// F9: games/<id>.json describe executables, config files, anti-cheat limits and benchmark methods. They are model
// context (evidence), never commands; a matching package is attached when its game is running, detected or named.
internal static class GamePackages
{
    internal sealed record Package(string Id, string[] Aliases, string[] Executables, string Json);

    internal static IReadOnlyList<Package> All { get; } = typeof(GamePackages).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith("NeuroTune.games.", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal).Select(Load).ToList();

    private static Package Load(string resource)
    {
        using var stream = typeof(GamePackages).Assembly.GetManifestResourceStream(resource)!;
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        string[] List(string name) => root.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).ToArray();
        return new(root.GetProperty("id").GetString()!, List("aliases"), List("executables"), root.GetRawText());
    }

    internal static Dictionary<string, string> Evidence(SystemProfile profile, TuningGoals? goals)
    {
        var executables = (profile.GameExecutables ?? []).Concat((profile.TopProcesses ?? []).Select(process => process.Split(' ')[0]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = (profile.DetectedGames ?? []).Concat(goals?.Games ?? []).Append(goals?.GameContext?.Game ?? "").Where(name => name.Length > 0).ToList();
        return All.Where(package => package.Executables.Any(executables.Contains) ||
                package.Aliases.Any(alias => names.Any(name => name.Contains(alias, StringComparison.OrdinalIgnoreCase))))
            .ToDictionary(package => $"game-package:{package.Id}", package => package.Json, StringComparer.Ordinal);
    }
}
