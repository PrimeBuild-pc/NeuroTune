using System.Management;
using System.Text.Json;

namespace NeuroTune;

public sealed record InvestigationRequest(string ToolId, string Question, string? Module = null);
public sealed record InvestigationTool(string Id, string Description);

// Read-only providers, never a model-supplied shell/query/path. The model chooses domains and questions, not executable code.
public static class InvestigationService
{
    public static IReadOnlyList<InvestigationTool> Tools { get; } = [
        new("processor-times", "Fresh per-core busy/idle/DPC/ISR counters; no new ETW capture."),
        new("memory-pressure", "Fresh available/committed memory and paging-rate counters."),
        new("storage-health", "Windows-reported disk status/capacity and free-space metadata; no stress test."),
        new("display-configuration", "Windows-reported GPU driver/display mode, resolution and refresh metadata."),
        new("device-health", "Current device status/error codes; no identifiers, disabling or driver execution."),
        new("network-counters", "Local adapter traffic/error counters; no ping, IP/MAC/SSID or network changes."),
        new("service-state", "Current service name/state/start mode, without paths or account identities."),
        new("system-events", "Recent System warning/error provider/event IDs, not potentially sensitive message text."),
        new("driver-details", "Current associations/version metadata for an observed .sys filename (module parameter)."),
    ];
    public static async Task<IReadOnlyDictionary<string, string>> ReadAsync(InvestigationRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (MeasurementService.HasActiveRecording()) throw new InvalidOperationException("Read-only AI investigation is paused while recording.");
        var id = "investigation:" + request.ToolId + (request.Module is null ? "" : ":" + request.Module);
        try
        {
            object result = request.ToolId switch
            {
                "processor-times" => await new PerformanceSnapshotService().CollectProcessorsAsync(),
                "memory-pressure" => ReadRows("Win32_PerfFormattedData_PerfOS_Memory", ["AvailableMBytes", "PercentCommittedBytesInUse", "PagesInputPersec", "PageReadsPersec"]),
                "storage-health" => new { disks = ReadRows("Win32_DiskDrive", ["Model", "Status", "Size"]), volumes = ReadRows("Win32_LogicalDisk", ["DeviceID", "Size", "FreeSpace", "DriveType"]) },
                "display-configuration" => ReadRows("Win32_VideoController", ["Name", "DriverVersion", "CurrentRefreshRate", "CurrentHorizontalResolution", "CurrentVerticalResolution"]),
                "device-health" => ReadRows("Win32_PnPEntity", ["Name", "Status", "ConfigManagerErrorCode"]),
                "network-counters" => ReadRows("Win32_PerfFormattedData_Tcpip_NetworkInterface", ["Name", "BytesReceivedPersec", "BytesSentPersec", "PacketsOutboundErrors", "PacketsReceivedErrors"]),
                "service-state" => ReadRows("Win32_Service", ["Name", "State", "StartMode"]),
                "system-events" => ReadEvents(),
                "driver-details" => DriverInspection.Read(request.Module!),
                _ => new { status = "unavailable", reason = "No safe reader exists for this requested tool. Suggest a specific manual follow-up; no arbitrary command or query was executed." }
            };
            cancellationToken.ThrowIfCancellationRequested();
            var value = ProfileSanitizer.Redact(JsonSerializer.Serialize(new { readAtUtc = DateTimeOffset.UtcNow, source = "Current local read-only observation, not capture-time causal proof", data = result }));
            if (value.Length > 20_000) return new Dictionary<string, string> { [id] = "Unavailable: result exceeds the bounded evidence size. Ask a narrower supported question; no partial JSON was substituted." };
            return new Dictionary<string, string> { [id] = value };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new Dictionary<string, string> { [id] = $"Unavailable: {error.GetType().Name}. This is not a measured zero or proof that the component is absent." };
        }
    }
    internal static void Validate(InvestigationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ToolId) || request.ToolId.Length > 80 || request.ToolId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') ||
            string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 800)
            throw new ArgumentException("Invalid read-only investigation request.");
        if (request.ToolId == "driver-details") DriverInspection.ValidateModule(request.Module ?? "");
        else if (request.Module is not null) throw new ArgumentException("This investigation tool does not accept a module parameter.");
    }
    private static object ReadRows(string table, string[] columns, string where = "")
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"), new ObjectQuery($"SELECT {string.Join(',', columns)} FROM {table} {where}"),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(20) });
        using var rows = searcher.Get();
        var values = new List<Dictionary<string, string>>(); var truncated = false;
        foreach (ManagementBaseObject row in rows)
        {
            using (row)
            {
                if (values.Count == 80) { truncated = true; break; }
                values.Add(columns.ToDictionary(column => column, column => row[column]?.ToString() is { } text ? text[..Math.Min(250, text.Length)] : "Unknown"));
            }
        }
        return new { rows = values, truncated, limit = 80, limitation = "Empty/unavailable fields do not prove absence. Windows metadata, not a stress test or tuning verdict." };
    }
    private static object ReadEvents()
    {
        var since = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.Now.AddDays(-1));
        return ReadRows("Win32_NTLogEvent", ["SourceName", "EventCode", "TimeGenerated", "EventType"],
            $"WHERE Logfile='System' AND (EventType=1 OR EventType=2) AND TimeGenerated >= '{since}'");
    }
}
