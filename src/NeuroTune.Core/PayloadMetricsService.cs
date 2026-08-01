using System.Text.Json;

namespace NeuroTune;

public sealed class PayloadMetricsService
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _path;

    public PayloadMetricsService(string? path = null) =>
        _path = path ?? Path.Combine(SettingsService.DataDirectory, "payload-metrics.ndjson");

    public void Record(EvidencePayloadReport report, UserSettings settings, string outcome)
    {
        var row = new PayloadMetric(
            Environment.OSVersion.Version.ToString(),
            report.FactCount,
            report.Utf8Bytes,
            settings.Provider.ToString(),
            settings.Model,
            outcome,
            DateTimeOffset.UtcNow);
        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, JsonSerializer.Serialize(row, JsonOptions) + Environment.NewLine);
        }
    }

    private sealed record PayloadMetric(
        string WindowsBuild,
        int FactCount,
        int Utf8Bytes,
        string Provider,
        string Model,
        string Outcome,
        DateTimeOffset RecordedAt);
}
