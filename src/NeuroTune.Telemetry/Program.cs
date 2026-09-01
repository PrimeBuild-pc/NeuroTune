using System.Text.Json;
using System.Management;
using System.Globalization;

try
{
    if (args.Length != 1 || args[0] != "capabilities")
        throw new InvalidOperationException("Only the capabilities command is supported.");
    if (!Console.IsInputRedirected)
        throw new InvalidOperationException("Telemetry requires the JSON stdin/stdout protocol.");

    var request = await Console.In.ReadToEndAsync();
    using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(request) ? "{}" : request);
    if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Any())
        throw new InvalidOperationException("The capabilities command accepts an empty JSON object only.");

    var capabilities = new List<Capability>();
    var sensors = ReadLibreHardwareMonitor();
    AddSensors("CPU/GPU effective clocks and utilization", ["Clock", "Load"], sensors);
    AddSensors("Temperatures and power", ["Temperature", "Power"], sensors);
    const string driverDetail =
        "The reviewed low-level driver boundary is not approved. NeuroTune did not download, install, load, or invoke a driver.";
    capabilities.AddRange(new[]
    {
        new Capability("SPD and XMP/EXPO profiles", "driverNotApproved", driverDetail),
        new Capability("Memory timings, voltage, and explicit throttling flags", "driverNotApproved", driverDetail)
    });
    Console.Write(JsonSerializer.Serialize(capabilities, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    void AddSensors(string name, string[] types, IReadOnlyList<Sensor> available)
    {
        var matches = available.Where(sensor => types.Contains(sensor.Type, StringComparer.OrdinalIgnoreCase)).Take(40).ToList();
        capabilities.Add(matches.Count == 0
            ? new(name, "unavailable", "LibreHardwareMonitor WMI did not expose these sensors. NeuroTune did not install or start it.")
            : new(name, "supported", "Optional LibreHardwareMonitor WMI samples: " + string.Join("; ", matches.Select(sensor => $"{sensor.Name}={sensor.Value.ToString("0.###", CultureInfo.InvariantCulture)} {Unit(sensor.Type)}"))));
    }
}
catch (Exception exception)
{
    Console.Error.Write(exception.Message);
    Environment.ExitCode = 1;
}

static List<Sensor> ReadLibreHardwareMonitor()
{
    try
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\LibreHardwareMonitor"),
            new ObjectQuery("SELECT Name, SensorType, Value FROM Sensor WHERE Value IS NOT NULL"));
        return searcher.Get().Cast<ManagementBaseObject>().Select(row => new Sensor(
            row["Name"]?.ToString()?.Trim() ?? "Unnamed sensor",
            row["SensorType"]?.ToString()?.Trim() ?? "Unknown",
            Convert.ToDouble(row["Value"] ?? double.NaN, CultureInfo.InvariantCulture)))
            .Where(sensor => double.IsFinite(sensor.Value) && sensor.Name.Length <= 100).Take(80).ToList();
    }
    catch { return []; }
}

static string Unit(string sensorType) => sensorType.ToLowerInvariant() switch
{
    "temperature" => "°C",
    "clock" => "MHz",
    "load" => "%",
    "power" => "W",
    _ => ""
};

internal sealed record Capability(string Name, string Status, string Detail);
internal sealed record Sensor(string Name, string Type, double Value);
