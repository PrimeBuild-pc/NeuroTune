using System.Reflection;
using System.Text.Json;

namespace NeuroTune;

internal static class ComponentBaselineCatalog
{
    private static readonly BaselineData Data = Load();

    public static Dictionary<string, string> Compare(IReadOnlyDictionary<string, string> identities)
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        var cpuMatch = $"{identities.GetValueOrDefault("CPU specification ID")}|{identities.GetValueOrDefault("CPU model")}";
        results["CPU"] = Describe(Data.Cpu.FirstOrDefault(item => item.Match == cpuMatch), cpuMatch);

        foreach (var (key, value) in identities.Where(item => item.Key.StartsWith("DIMM ", StringComparison.Ordinal) && item.Key.EndsWith(" specification ID", StringComparison.Ordinal)))
            results[key[..^" specification ID".Length]] = Describe(Data.Memory.FirstOrDefault(item => item.Match == value), value);
        return results;
    }

    private static string Describe(Baseline? baseline, string identity) => baseline is null
        ? $"Baseline unavailable: no exact local match for {identity}"
        : $"{baseline.VendorSpecificationId}; {baseline.Reference}; source={baseline.Source}";

    private static BaselineData Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith("component-baselines.v1.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("The component baseline catalog is unavailable.");
        var data = JsonSerializer.Deserialize<BaselineData>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The component baseline catalog is invalid.");
        return data.SchemaVersion == 1 ? data : throw new InvalidOperationException("The component baseline catalog version is unsupported.");
    }

    private sealed class BaselineData
    {
        public int SchemaVersion { get; set; }
        public List<Baseline> Cpu { get; set; } = [];
        public List<Baseline> Memory { get; set; } = [];
    }

    private sealed class Baseline
    {
        public string Match { get; set; } = "";
        public string VendorSpecificationId { get; set; } = "";
        public string Reference { get; set; } = "";
        public string Source { get; set; } = "";
    }
}
