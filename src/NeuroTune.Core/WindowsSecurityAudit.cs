using Microsoft.Win32;
using System.Globalization;
using System.Text.Json;

namespace NeuroTune;

// Fixed projections: never export threat resources, exclusion paths, account IDs or event messages.
public sealed record DefenderThreatSummary(string? ThreatId, string Name, int? Severity, bool? IsActive, bool? DidThreatExecute);
public sealed record DefenderStatus(bool? AntivirusEnabled, bool? RealTimeProtectionEnabled, bool? TamperProtected,
    string RunningMode, int? SignatureAgeDays, DateTimeOffset? SignatureUpdatedAtUtc,
    DateTimeOffset? QuickScanEndedAtUtc, DateTimeOffset? FullScanEndedAtUtc);
public sealed record DefenderPreferences(int? CloudReporting, int? SampleSubmission, int? CloudBlockLevel,
    int? ExcludedPathCount, int? ExcludedProcessCount, int? ExcludedExtensionCount);
public sealed record DefenderReport(string Status, DateTimeOffset ReadAtUtc, string ScanState,
    DefenderStatus? Protection, DefenderPreferences? Preferences, IReadOnlyList<DefenderThreatSummary> Threats,
    bool Truncated, string Limitation);

public static class WindowsSecurityAudit
{
    public const string Limitations = "Point-in-time Windows/Defender metadata, not a forensic verdict. Missing/unavailable is not disabled or clean. Threat history may be incomplete; no paths/resources or file contents are exported. Registry snapshots do not establish effective policy or network traffic. Check Windows edition/build, MDM/Group Policy and per-app exceptions in Settings.";
    private static readonly string[] DefenderFields = ["AntivirusEnabled", "RealTimeProtectionEnabled", "IsTamperProtected", "AMRunningMode", "AntivirusSignatureAge", "AntivirusSignatureLastUpdated", "QuickScanEndTime", "FullScanEndTime"];

    // ponytail: latest scan event can be stale; keep manual review until a documented live-state API is available.
    internal const string DefenderReadCommand = """
        $ErrorActionPreference='Stop';
        Import-Module ($PSHOME + '\Modules\Defender\Defender.psd1') -ErrorAction Stop;
        $s=Defender\Get-MpComputerStatus -ErrorAction Stop;
        $p=Defender\Get-MpPreference -ErrorAction Stop;
        $t=@(Defender\Get-MpThreat -ErrorAction Stop | Select-Object -First 41 ThreatID,ThreatName,SeverityID,IsActive,DidThreatExecute);
        $scan='unknown';
        try { $e=Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Windows Defender/Operational';Id=1000,1001,1002} -MaxEvents 1 -ErrorAction Stop; if($e.Id -eq 1000){$scan='busy'} elseif($e.Id -in 1001,1002){$scan='idle'} } catch { }
        $status=[ordered]@{AntivirusEnabled=$s.AntivirusEnabled;RealTimeProtectionEnabled=$s.RealTimeProtectionEnabled;IsTamperProtected=$s.IsTamperProtected;AMRunningMode=$s.AMRunningMode;AntivirusSignatureAge=$s.AntivirusSignatureAge};
        foreach($n in @('AntivirusSignatureLastUpdated','QuickScanEndTime','FullScanEndTime')){ $status[$n]=if($null -eq $s.$n){$null}else{$s.$n.ToUniversalTime().ToString('o')} };
        [ordered]@{scanState=$scan;protection=$status;preferences=[ordered]@{MAPSReporting=$(if($null -ne $p.MAPSReporting){[int]$p.MAPSReporting}else{$null});SubmitSamplesConsent=$(if($null -ne $p.SubmitSamplesConsent){[int]$p.SubmitSamplesConsent}else{$null});CloudBlockLevel=$(if($null -ne $p.CloudBlockLevel){[int]$p.CloudBlockLevel}else{$null});ExcludedPathCount=$(if($p.PSObject.Properties.Name -contains 'ExclusionPath'){@($p.ExclusionPath | Where-Object {$null -ne $_}).Count}else{$null});ExcludedProcessCount=$(if($p.PSObject.Properties.Name -contains 'ExclusionProcess'){@($p.ExclusionProcess | Where-Object {$null -ne $_}).Count}else{$null});ExcludedExtensionCount=$(if($p.PSObject.Properties.Name -contains 'ExclusionExtension'){@($p.ExclusionExtension | Where-Object {$null -ne $_}).Count}else{$null})};threats=$t} | ConvertTo-Json -Depth 5 -Compress
        """;

    public static DefenderReport ReadDefender()
    {
        try { return ParseDefender(WindowsCommand.Run("powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", DefenderReadCommand)); }
        catch (Exception error) { return UnavailableDefender(error.GetType().Name); }
    }

    internal static DefenderReport UnavailableDefender(string reason) => new("unavailable", DateTimeOffset.UtcNow, "unknown", null, null, [], false,
        $"Defender reader unavailable ({reason}); no scan or setting change was attempted. {Limitations}");

    internal static DefenderReport ParseDefender(string content)
    {
        if (content.Length > 128_000) throw new InvalidOperationException("Defender evidence exceeds its limit.");
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        var s = root.GetProperty("protection");
        var p = root.GetProperty("preferences");
        if (s.ValueKind != JsonValueKind.Object || p.ValueKind != JsonValueKind.Object || !DefenderFields.All(name => s.TryGetProperty(name, out _)))
            throw new InvalidOperationException("Incomplete Defender status projection.");
        var threats = root.GetProperty("threats");
        if (threats.ValueKind != JsonValueKind.Array || threats.GetArrayLength() > 41)
            throw new InvalidOperationException("Invalid Defender detection projection.");
        var summaries = threats.EnumerateArray().Take(40).Select(row => new DefenderThreatSummary(
            Unsigned(row, "ThreatID")?.ToString(CultureInfo.InvariantCulture), Metadata(Text(row, "ThreatName")), Number(row, "SeverityID"), Boolean(row, "IsActive"), Boolean(row, "DidThreatExecute"))).ToList();
        var mode = Text(s, "AMRunningMode");
        if (mode is not ("Normal" or "Passive Mode" or "SxS Passive Mode" or "EDR Block Mode")) mode = "Unknown";
        var scan = Text(root, "scanState");
        if (scan is not ("busy" or "idle")) scan = "unknown";
        return new("observed", DateTimeOffset.UtcNow, scan,
            new(Boolean(s, "AntivirusEnabled"), Boolean(s, "RealTimeProtectionEnabled"), Boolean(s, "IsTamperProtected"), mode,
                Number(s, "AntivirusSignatureAge"), Timestamp(s, "AntivirusSignatureLastUpdated"), Timestamp(s, "QuickScanEndTime"), Timestamp(s, "FullScanEndTime")),
            new(Number(p, "MAPSReporting"), Number(p, "SubmitSamplesConsent"), Number(p, "CloudBlockLevel"), Number(p, "ExcludedPathCount"), Number(p, "ExcludedProcessCount"), Number(p, "ExcludedExtensionCount")),
            summaries, threats.GetArrayLength() > 40, Limitations + " Scan state is inferred from the latest Defender scan event and may be stale; command completion is not a clean-PC verdict.");
    }

    public static Dictionary<string, string> ReadPrivacy()
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["privacy.limitations"] = Limitations };
        foreach (var probe in ProbeCatalog.PrivacySecurityRegistry)
        {
            var value = "Unavailable";
            try
            {
                using var key = (probe.Hive == RegistryProbeHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine).OpenSubKey(probe.Path);
                var observed = key?.GetValue(probe.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                value = DescribeRegistryValue(probe.ExpectedType, observed);
            }
            catch (Exception error) { value = $"Unavailable ({error.GetType().Name})"; }
            facts["privacy." + probe.ProfileKey] = value + "; " + probe.Interpretation;
        }
        return facts;
    }

    internal static string DescribeRegistryValue(RegistryValueType type, object? value) => (type, value) switch
    {
        (_, null) => "Not configured; effective state unknown",
        (RegistryValueType.DWord, int number) => $"DWord snapshot={number.ToString(CultureInfo.InvariantCulture)}",
        (RegistryValueType.String, string text) when text is "Allow" or "Deny" or "Warn" or "Block" or "Off" or "On" => "String snapshot=" + text,
        _ => "Unexpected value/type; effective state unknown; value omitted"
    };

    public static Dictionary<string, string> ReadSecurity()
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["security.limitations"] = Limitations };
        facts["security.defender"] = JsonSerializer.Serialize(ReadDefender());
        facts["security.antivirus-products"] = ReadAntivirusProducts();
        facts["security.firewall-active-profiles"] = ReadFirewall();
        try
        {
            var state = WindowsCommand.Run("powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Confirm-SecureBootUEFI -ErrorAction Stop").Trim();
            facts["security.secure-boot"] = state is "True" or "False" ? state : "Unavailable";
        }
        catch (Exception error) { facts["security.secure-boot"] = $"Unavailable ({error.GetType().Name}); not disabled."; }
        var guard = SystemProfiler.Query(@"root\Microsoft\Windows\DeviceGuard", "SELECT VirtualizationBasedSecurityStatus,SecurityServicesRunning FROM Win32_DeviceGuard",
            row => new { vbsStatus = row["VirtualizationBasedSecurityStatus"]?.ToString() ?? "Unknown", servicesRunning = row["SecurityServicesRunning"] as uint[] });
        facts["security.device-guard"] = guard.Count == 0 ? "Unavailable; not disabled." : JsonSerializer.Serialize(guard.Take(1));
        return facts;
    }

    public static Dictionary<string, string> ReadAll()
    {
        var facts = ReadPrivacy();
        foreach (var fact in ReadSecurity()) facts.Add(fact.Key, fact.Value);
        return facts;
    }

    private static string ReadAntivirusProducts()
    {
        var products = SystemProfiler.Query(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct",
            row => new { name = Metadata(row["displayName"]?.ToString()), reportedState = row["productState"]?.ToString() ?? "Unknown" });
        return products.Count == 0 ? "Unavailable/empty Security Center inventory; not proof of absent antivirus." : JsonSerializer.Serialize(new { products = products.Take(20), truncated = products.Count > 20, limitation = "Registered products, not proof of effective protection; productState is undecoded metadata." });
    }

    private static string ReadFirewall()
    {
        try
        {
            var content = WindowsCommand.Run("powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                "Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop | ForEach-Object { [ordered]@{Name=$_.Name;Enabled=$(if($null -eq $_.Enabled){$null}else{$_.Enabled.ToString()});DefaultInboundAction=$_.DefaultInboundAction;DefaultOutboundAction=$_.DefaultOutboundAction} } | ConvertTo-Json -Compress");
            if (content.Length > 4_000) throw new InvalidOperationException("Firewall evidence limit.");
            using var json = JsonDocument.Parse(content);
            var rows = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement.EnumerateArray().ToList() : [json.RootElement];
            if (rows.Count is 0 or > 3) throw new InvalidOperationException("Firewall profile projection unavailable.");
            return JsonSerializer.Serialize(new { profiles = rows.Select(row => new { name = Metadata(Text(row, "Name")), enabled = Text(row, "Enabled") is "True" or "False" or "NotConfigured" ? Text(row, "Enabled") : "Unknown", defaultInbound = Number(row, "DefaultInboundAction"), defaultOutbound = Number(row, "DefaultOutboundAction") }), limitation = "ActiveStore profile policies only; active connection/interface mapping and BFE/Mpssvc service state are not verified. Enabled policy alone does not attest effective protection." });
        }
        catch (Exception error) { return $"Unavailable ({error.GetType().Name}); not evidence that Firewall is disabled."; }
    }

    private static string? Text(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool? Boolean(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    private static int? Number(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 ? number : null;
    private static ulong? Unsigned(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number) ? number : null;
    private static DateTimeOffset? Timestamp(JsonElement row, string name) => Text(row, name) is { Length: <= 64 } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) && time.Year > 2000 ? time.ToUniversalTime() : null;
    private static string Metadata(string? text) => string.IsNullOrWhiteSpace(text) ? "Unknown" : text.Length > 120 || text.Any(char.IsControl) || text.Contains('\\') || text.Contains('@') || text.Contains("://", StringComparison.Ordinal) ? "Unexpected metadata omitted" : ProfileSanitizer.Redact(text);
}
