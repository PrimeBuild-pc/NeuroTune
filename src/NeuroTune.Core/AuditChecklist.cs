using System.Text.Json;

namespace NeuroTune;

public enum AuditCheckStatus { NotChecked, Reviewed, Partial, Unavailable, ConsentDenied }

public sealed class AuditCheckAssessment
{
    public string CheckId { get; set; } = "";
    public AuditCheckStatus Status { get; set; }
    public List<string> EvidenceIds { get; set; } = [];
    public string Assessment { get; set; } = "";
    public string Label => AuditChecklist.Checks.FirstOrDefault(check => check.Id == CheckId)?.Label ?? CheckId;
    public string Area => AuditChecklist.Checks.FirstOrDefault(check => check.Id == CheckId)?.Area ?? "unknown";
}

public sealed record AuditCheckDefinition(string Id, string Area, string Label, string[] EvidencePrefixes, string[] ToolIds);

// Mandatory report coverage, not a tweak recipe or permission to execute tools.
public static class AuditChecklist
{
    public static IReadOnlyList<AuditCheckDefinition> Checks { get; } =
    [
        new("privacy", "privacy", "Privacy · preferenze e policy", ["audit:privacy.", "investigation:privacy-settings:"], ["privacy-settings"]),
        new("antivirus", "protections", "Antivirus · modalità, firme e protezioni", ["audit:security.defender", "audit:security.antivirus-products", "investigation:security-status:"], ["security-status"]),
        new("firewall", "protections", "Firewall · policy e limiti dello stato effettivo", ["audit:security.firewall-", "investigation:security-status:"], ["security-status"]),
        new("smartscreen", "protections", "SmartScreen · preferenze e policy", ["audit:privacy.HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\\EnableSmartScreen", "audit:privacy.HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\SmartScreenEnabled", "investigation:privacy-settings:"], ["privacy-settings"]),
        new("updates", "protections", "Aggiornamenti · policy, non prova di patch installate", ["audit:privacy.HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate", "investigation:privacy-settings:"], ["privacy-settings"]),
        new("boot-isolation", "protections", "Secure Boot e isolamento · metadati Windows", ["audit:security.secure-boot", "audit:security.device-guard", "investigation:security-status:"], ["security-status"]),
        new("detections", "detections", "Rilevamenti · sintesi dello scanner", ["audit:security.defender", "investigation:defender-detections:", "investigation:security-status:"], ["defender-detections"]),
        new("antivirus-scan", "detections", "Scansione antivirus · consenso e copertura separati", [], []),
        new("startup", "persistence", "Avvio automatico · inventario", ["startup:", "investigation:startup-items:"], ["startup-items"]),
        new("scheduled-tasks", "persistence", "Attività pianificate · inventario", ["investigation:scheduled-tasks:"], ["scheduled-tasks"]),
        new("services", "persistence", "Servizi · stato e avvio", ["service:", "investigation:service-state:"], ["service-state"]),
        new("events", "health", "Eventi Windows · errori e avvisi recenti", ["investigation:system-events:"], ["system-events"]),
        new("storage", "health", "Storage · stato e spazio, non test di superficie", ["storage:", "investigation:storage-health:"], ["storage-health"]),
        new("devices", "health", "Dispositivi · stato e codici di errore", ["device-issue:", "investigation:device-health:"], ["device-health"]),
        new("memory", "health", "Memoria · pressione e paging", ["hardware:Memory pressure sample", "investigation:memory-pressure:"], ["memory-pressure"]),
    ];

    public static bool Required(InvestigationMode mode, TuningGoals goals) => mode == InvestigationMode.AuditOnly || goals.Priority == OptimizationPriority.PrivacySecurity;

    public static IReadOnlyList<string> Missing(DiagnosisResult diagnosis) => Checks.Where(check =>
        !(diagnosis.AuditCoverage ?? []).Any(row => row.CheckId == check.Id)).Select(check => check.Id).ToList();

    internal static void ValidateShape(DiagnosisResult diagnosis)
    {
        diagnosis.AuditCoverage ??= [];
        if (diagnosis.AuditCoverage.Count > Checks.Count || diagnosis.AuditCoverage.Any(row => row is null ||
            !Checks.Any(check => check.Id == row.CheckId) || !Enum.IsDefined(row.Status) || row.Assessment is null ||
            row.Assessment.Length is 0 or > 1000 || row.EvidenceIds is null || row.EvidenceIds.Count > 40 ||
            row.EvidenceIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 500)) ||
            diagnosis.AuditCoverage.Select(row => row.CheckId).Distinct(StringComparer.Ordinal).Count() != diagnosis.AuditCoverage.Count)
            throw new InvalidOperationException("Invalid audit checklist rows, status or evidence references.");
    }

    public static void Normalize(DiagnosisResult diagnosis, IReadOnlyDictionary<string, string> provided)
    {
        ValidateShape(diagnosis);
        var normalized = new List<AuditCheckAssessment>();
        foreach (var check in Checks)
        {
            var row = diagnosis.AuditCoverage.SingleOrDefault(item => item.CheckId == check.Id) ?? new()
            { CheckId = check.Id, Status = AuditCheckStatus.NotChecked, Assessment = "The AI did not assess this required check. No verification is claimed." };
            if (row.EvidenceIds.Any(id => !provided.ContainsKey(id))) throw new InvalidOperationException("Audit coverage cited evidence not provided to the investigator.");
            if (check.Id == "antivirus-scan")
            {
                // Scan journals are not linked to audit evidence yet; timestamps/history cannot prove this scan's coverage.
                row = new() { CheckId = check.Id, Status = AuditCheckStatus.NotChecked, Assessment = "No consented scanner operation is linked to this audit. Status/history is not proof of a scan performed for this report. Use the separate Windows scanner controls and review Windows Security." };
            }
            else if (row.Status == AuditCheckStatus.ConsentDenied)
            {
                row = new() { CheckId = check.Id, Status = AuditCheckStatus.NotChecked, Assessment = "No application-owned consent denial was recorded for this check; the model cannot invent user consent or refusal." };
            }
            else if (row.Status != AuditCheckStatus.NotChecked && (row.EvidenceIds.Count == 0 ||
                row.EvidenceIds.Any(id => !Matches(check, id))))
            {
                row = new() { CheckId = check.Id, Status = AuditCheckStatus.NotChecked, Assessment = "No provided native evidence supports this check; unrelated metadata or imported reports cannot verify it." };
            }
            else if (row.Status == AuditCheckStatus.Reviewed && (row.EvidenceIds.Any(id => HasGap(provided[id])) ||
                provided.Keys.Any(id => Matches(check, id) && !row.EvidenceIds.Contains(id, StringComparer.Ordinal))))
            {
                row.Status = AuditCheckStatus.Partial;
                row.Assessment = "Native evidence contains unknown/unavailable/empty/truncated or uncited relevant fields. " + row.Assessment[..Math.Min(900, row.Assessment.Length)];
            }
            row.EvidenceIds = row.Status == AuditCheckStatus.NotChecked ? [] : row.EvidenceIds.Distinct(StringComparer.Ordinal).ToList();
            normalized.Add(row);
        }
        diagnosis.AuditCoverage = normalized;
    }

    public static bool IsComplete(IReadOnlyList<AuditCheckAssessment>? rows) => rows is not null && rows.Count == Checks.Count &&
        Checks.All(check => rows.Count(row => row is not null && row.CheckId == check.Id && row.Status == AuditCheckStatus.Reviewed &&
            row.EvidenceIds is { Count: > 0 } && row.EvidenceIds.All(id => Matches(check, id))) == 1);

    internal static void ValidatePersisted(DiagnosisResult diagnosis, IReadOnlyDictionary<string, string> facts)
    {
        ValidateShape(diagnosis);
        if (diagnosis.AuditCoverage.Count == 0) return; // Legacy reports are incomplete, not retroactively verified.
        if (Missing(diagnosis).Count > 0 || diagnosis.AuditCoverage.Any(row => row.Status == AuditCheckStatus.ConsentDenied ||
            row.CheckId == "antivirus-scan" && row.Status != AuditCheckStatus.NotChecked ||
            row.EvidenceIds.Any(id => !facts.ContainsKey(id) || !Matches(Checks.Single(check => check.Id == row.CheckId), id)) ||
            row.Status != AuditCheckStatus.NotChecked && row.EvidenceIds.Count == 0 ||
            row.Status == AuditCheckStatus.Reviewed && (row.EvidenceIds.Any(id => HasGap(facts[id])) ||
                facts.Keys.Any(id => Matches(Checks.Single(check => check.Id == row.CheckId), id) && !row.EvidenceIds.Contains(id, StringComparer.Ordinal)))))
            throw new InvalidOperationException("Persisted audit coverage is unsupported or incomplete.");
    }

    private static bool Matches(AuditCheckDefinition check, string id) => !id.EndsWith(".limitations", StringComparison.Ordinal) &&
        check.EvidencePrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal));

    private static bool HasGap(string value)
    {
        try { using var json = JsonDocument.Parse(value); return HasGap(json.RootElement); }
        catch (JsonException) { return string.IsNullOrWhiteSpace(value) || new[] { "Unknown", "Unavailable", "Not configured", "NotConfigured", "Unexpected" }.Any(marker => value.StartsWith(marker, StringComparison.OrdinalIgnoreCase)); }
    }

    private static bool HasGap(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => true,
        JsonValueKind.String => HasGap(value.GetString() ?? "Unknown"),
        JsonValueKind.Array => value.GetArrayLength() == 0 || value.EnumerateArray().Any(HasGap),
        JsonValueKind.Object => !value.EnumerateObject().Any() || value.EnumerateObject().Any(property =>
            property.Name.Equals("truncated", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.True ||
            property.Name is not ("limitation" or "limitations" or "source" or "readAtUtc" or "Limitation" or "ReadAtUtc") && HasGap(property.Value)),
        _ => false
    };
}
