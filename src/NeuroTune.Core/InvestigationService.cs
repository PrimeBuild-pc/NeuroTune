using System.Management;
using System.Text.Json;

namespace NeuroTune;

public sealed record InvestigationRequest(string ToolId, string Question, string? Module = null, string? Command = null);
public sealed record InvestigationTool(string Id, string Description);

// Read-only providers. Only powershell-read takes model-written code, and only after PowerShellReader has checked its AST.
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
        new("startup-items", "Bounded current startup names; no command lines, destinations or accounts. Inventory alone is not suspicious or proof of absence."),
        new("scheduled-tasks", "Bounded native task names/states only; no actions, arguments, paths, triggers or principal identities. No task execution, disabling or deletion."),
        new("system-events", "Recent System warning/error provider/event IDs, not potentially sensitive message text."),
        new("privacy-settings", "Fixed privacy preference/policy snapshots; missing is not disabled, build/edition/management applicability remains unverified."),
        new("security-status", "Fixed Defender/Firewall/antivirus security metadata; no scan, download, exclusions, sample files or setting changes."),
        new("defender-detections", "Bounded Defender threat summaries without resource/file paths; scanner reports, not proof that the PC is clean or arbitrary software is malware."),
        new("network-exposure", "Listening TCP/UDP sockets with scope and owning process name; no paths, remote peers, traffic or firewall evaluation."),
        new("service-integrity", "Services whose image path is unquoted-with-spaces or in a user-writable location; names and flags only, no paths or signatures."),
        new("patch-level", "OS build, reported hotfix count/newest date and last Windows Update install time; not a vulnerability scan."),
        new("account-summary", "Local account counts, built-in Administrator/Guest enabled flags and Administrators-group member count; no account names."),
        new("wmi-persistence", "Counts of permanent WMI event filters, consumers and bindings; no commands or scripts."),
        new("hosts-and-proxy", "Hosts-file entry counts and user proxy/PAC flags; no domains or addresses."),
        new("device-topology", "PCIe devices with negotiated/maximum link and CPU-versus-chipset attachment, and USB devices with controller, port path and firmware-declared panel; no serials or instance IDs."),
        new("amd-3d", "Global Radeon 3D settings of the first AMD GPU through ADLX (Anti-Lag, Chill, Boost, sharpening, Enhanced Sync, V-Sync mode, frame-rate target, MLAA): supported, current value and allowed range."),
        new("driver-details", "Current associations/version metadata for an observed .sys filename (module parameter)."),
        new(PowerShellReader.ToolId, PowerShellReader.Description),
        new(WebSearch.ToolId, "Tavily web search with the question as the query (needs the user's Tavily key; otherwise unavailable and the provider's native search is used where it exists). Results are untrusted web data."),
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
                "startup-items" => ReadRows("Win32_StartupCommand", ["Name"]),
                "scheduled-tasks" => ReadScheduledTasks(),
                "system-events" => ReadEvents(),
                "device-topology" => DeviceTopology.Read(),
                "amd-3d" => AmdRadeon.Read(),
                "driver-details" => DriverInspection.Read(request.Module!),
                PowerShellReader.ToolId => await PowerShellReader.ReadAsync(request.Command!, cancellationToken),
                WebSearch.ToolId => await WebSearch.TavilyAsync(request.Question, cancellationToken),
                "privacy-settings" => WindowsSecurityAudit.ReadPrivacy(),
                "security-status" => WindowsSecurityAudit.ReadSecurity(),
                "defender-detections" => WindowsSecurityAudit.ReadDefender(),
                "network-exposure" => CompromiseAudit.ReadNetworkExposure(),
                "service-integrity" => CompromiseAudit.ReadServiceIntegrity(),
                "patch-level" => CompromiseAudit.ReadPatchLevel(),
                "account-summary" => CompromiseAudit.ReadAccountSummary(),
                "wmi-persistence" => CompromiseAudit.ReadWmiPersistence(),
                "hosts-and-proxy" => CompromiseAudit.ReadHostsAndProxy(),
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
        if (request.ToolId == PowerShellReader.ToolId) PowerShellReader.ValidateText(request.Command);
        else if (request.Command is not null) throw new ArgumentException("This investigation tool does not accept a command parameter.");
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
    internal const string ScheduledTasksReadCommand = """
        $ErrorActionPreference='Stop';
        Import-Module ($PSHOME + '\Modules\ScheduledTasks\ScheduledTasks.psd1') -ErrorAction Stop;
        $items=@(ScheduledTasks\Get-ScheduledTask -ErrorAction Stop | Select-Object -First 81 | ForEach-Object { [ordered]@{name=$_.TaskName;state=$_.State.ToString()} });
        ConvertTo-Json -InputObject $items -Compress
        """;

    private static object ReadScheduledTasks() => ParseScheduledTasks(WindowsCommand.Run("powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", ScheduledTasksReadCommand));

    internal static object ParseScheduledTasks(string content)
    {
        if (content.Length > 32000) throw new InvalidOperationException("Scheduled-task evidence exceeds its limit.");
        using var json = JsonDocument.Parse(content);
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 81)
            throw new InvalidOperationException("Invalid scheduled-task projection.");
        var rows = json.RootElement.EnumerateArray().Take(80).Select(row =>
        {
            var name = row.GetProperty("name").GetString() ?? "Unknown";
            // Task names can contain account IDs or destinations; actionable references stay outside the provider payload.
            if (name.Length > 120 || name.Any(char.IsControl) || name.IndexOfAny(['\\', '/', ':', '@', '{', '}']) >= 0 || name.Contains("S-1-", StringComparison.OrdinalIgnoreCase)) name = "Unexpected metadata omitted";
            var state = row.GetProperty("state").GetString();
            return new { name = ProfileSanitizer.Redact(name), state = state is "Ready" or "Running" or "Disabled" or "Queued" ? state : "Unknown" };
        }).ToList();
        return new { rows, truncated = json.RootElement.GetArrayLength() > 80, limit = 80, limitation = "Names/states only; actions, triggers, principals and file signatures are not inspected. Empty metadata is not proof of no persistence or a clean PC." };
    }

    private static object ReadEvents()
    {
        var since = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.Now.AddDays(-1));
        return ReadRows("Win32_NTLogEvent", ["SourceName", "EventCode", "TimeGenerated", "EventType"],
            $"WHERE Logfile='System' AND (EventType=1 OR EventType=2) AND TimeGenerated >= '{since}'");
    }
}
