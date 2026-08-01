using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuroTune;

internal static class TelemetryProcessClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static List<TelemetryCapability> QueryCapabilities(bool consent)
    {
        if (!consent)
            return Unavailable("Optional telemetry is off. Enable its separate consent in Settings to query the isolated adapter.");

        var executable = FindExecutable();
        if (executable is null)
            return Unavailable("The isolated telemetry adapter is unavailable. No driver was installed or loaded.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable, "capabilities")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        process.StandardInput.Write("{}");
        process.StandardInput.Close();
        if (!process.WaitForExit(2_000))
        {
            process.Kill(true);
            return Unavailable("The isolated telemetry adapter timed out and its process tree was terminated.");
        }
        if (process.ExitCode != 0)
            return Unavailable("The isolated telemetry adapter failed closed. No driver was installed or loaded.");

        try
        {
            return JsonSerializer.Deserialize<List<TelemetryCapability>>(process.StandardOutput.ReadToEnd(), JsonOptions)
                ?? Unavailable("The isolated telemetry adapter returned no capabilities.");
        }
        catch (JsonException)
        {
            return Unavailable("The isolated telemetry adapter returned an invalid response and was ignored.");
        }
    }

    private static string? FindExecutable()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "NeuroTune.Telemetry.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "NeuroTune.Telemetry",
                "bin", "Release", "net8.0-windows", "NeuroTune.Telemetry.exe"))
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static List<TelemetryCapability> Unavailable(string detail) =>
    [
        new("SPD and XMP/EXPO profiles", TelemetryStatus.Unavailable, detail),
        new("Memory timings and voltage", TelemetryStatus.Unavailable, detail),
        new("CPU effective clocks, limits, and throttling", TelemetryStatus.Unavailable, detail),
        new("Motherboard temperatures, power, and sensors", TelemetryStatus.Unavailable, detail)
    ];
}
