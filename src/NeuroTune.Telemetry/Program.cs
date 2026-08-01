using System.Text.Json;

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

    const string detail =
        "Consent was granted, but the reviewed PawnIO boundary is not approved. No driver was installed, loaded, or invoked.";
    Console.Write(JsonSerializer.Serialize(new[]
    {
        new Capability("SPD and XMP/EXPO profiles", "driverNotApproved", detail),
        new Capability("Memory timings and voltage", "driverNotApproved", detail),
        new Capability("CPU effective clocks, limits, and throttling", "driverNotApproved", detail),
        new Capability("Motherboard temperatures, power, and sensors", "driverNotApproved", detail)
    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
}
catch (Exception exception)
{
    Console.Error.Write(exception.Message);
    Environment.ExitCode = 1;
}

internal sealed record Capability(string Name, string Status, string Detail);
