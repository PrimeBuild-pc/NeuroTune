using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeuroTune.Tests;

[TestClass]
public sealed class AuditChecklistTests
{
    [TestMethod]
    public void Checklist_covers_five_domains_with_registered_readers_without_changing_authority()
    {
        Assert.HasCount(15, AuditChecklist.Checks);
        Assert.AreEqual(15, AuditChecklist.Checks.Select(check => check.Id).Distinct().Count());
        Assert.AreEqual(5, AuditChecklist.Checks.Select(check => check.Area).Distinct().Count());
        foreach (var tool in AuditChecklist.Checks.SelectMany(check => check.ToolIds))
            Assert.IsTrue(InvestigationService.Tools.Any(item => item.Id == tool));
        Assert.IsFalse(AuditChecklist.Required(InvestigationMode.MeasuredOptimization, new()));
        Assert.IsTrue(AuditChecklist.Required(InvestigationMode.MeasuredOptimization, new() { Priority = OptimizationPriority.PrivacySecurity }));
        Assert.IsTrue(AuditChecklist.Required(InvestigationMode.AuditOnly, new()));
        Assert.IsFalse(InvestigationService.Tools.Any(tool => tool.Id is "defender-scan" or "tronscript"));
    }

    [TestMethod]
    public void Missing_unrelated_imported_unknown_and_uncited_data_never_pass_as_verified_coverage()
    {
        var facts = new Dictionary<string, string>
        {
            ["system:cpu"] = "CPU",
            ["support:fake:provenance"] = "Unverified report",
            ["audit:privacy.example"] = "DWord snapshot=0",
            ["audit:privacy.other"] = "Not configured; effective state unknown",
            ["investigation:memory-pressure:test:turn1"] = "{\"rows\":[{\"pressure\":\"Unknown\"}],\"truncated\":false}",
            ["investigation:storage-health:test:turn1"] = "{\"rows\":[{\"status\":\"OK\"}],\"truncated\":true}",
            ["investigation:system-events:test:turn1"] = "Unavailable: Access denied",
            ["audit:security.firewall-profiles"] = "{\"Enabled\":\"NotConfigured\"}",
            ["audit:security.secure-boot"] = "{}"
        };
        var result = new DiagnosisResult
        {
            AuditCoverage = [
            Row("privacy", "audit:privacy.example"), Row("antivirus", "system:cpu"), Row("devices", "support:fake:provenance"),
            Row("memory", "investigation:memory-pressure:test:turn1"), Row("storage", "investigation:storage-health:test:turn1"),
            Row("firewall", "audit:security.firewall-profiles"), Row("boot-isolation", "audit:security.secure-boot"),
            new() { CheckId = "events", Status = AuditCheckStatus.Unavailable, EvidenceIds = ["investigation:system-events:test:turn1"], Assessment = "Windows reader failed" }
        ]
        };
        AuditChecklist.Normalize(result, facts);
        Assert.HasCount(15, result.AuditCoverage);
        foreach (var id in new[] { "privacy", "memory", "storage", "firewall", "boot-isolation" }) Assert.AreEqual(AuditCheckStatus.Partial, result.AuditCoverage.Single(row => row.CheckId == id).Status);
        foreach (var id in new[] { "antivirus", "devices", "scheduled-tasks" }) Assert.AreEqual(AuditCheckStatus.NotChecked, result.AuditCoverage.Single(row => row.CheckId == id).Status);
        Assert.AreEqual(AuditCheckStatus.Unavailable, result.AuditCoverage.Single(row => row.CheckId == "events").Status);
        Assert.IsFalse(result.AuditCoverageComplete);
        AuditChecklist.ValidatePersisted(result, facts);
        Assert.ThrowsExactly<InvalidOperationException>(() => AuditChecklist.Normalize(new() { AuditCoverage = [Row("privacy", "invented:fact")] }, facts));
        Assert.ThrowsExactly<InvalidOperationException>(() => AuditChecklist.ValidateShape(new() { AuditCoverage = [Row("privacy", "audit:privacy.example"), Row("privacy", "audit:privacy.example")] }));
        Assert.ThrowsExactly<InvalidOperationException>(() => AuditChecklist.ValidateShape(new() { AuditCoverage = [new() { CheckId = "arbitrary-shell", Assessment = "Launch" }] }));
    }

    [TestMethod]
    public void Historical_detections_and_model_claimed_consent_cannot_certify_a_scan_or_complete_audit()
    {
        var facts = new Dictionary<string, string> { ["audit:security.defender"] = "{\"scanState\":\"idle\",\"threats\":[]}" };
        var result = new DiagnosisResult { AuditCoverage = [Row("antivirus-scan", "audit:security.defender"), new() { CheckId = "scheduled-tasks", Status = AuditCheckStatus.ConsentDenied, Assessment = "Model claims user refused" }] };
        AuditChecklist.Normalize(result, facts);
        Assert.IsTrue(result.AuditCoverage.All(row => row.Status == AuditCheckStatus.NotChecked));
        Assert.IsFalse(result.AuditCoverageComplete);
        var parsed = LlmClient.ParseDiagnosis("{\"summary\":\"Model claims complete\",\"consentQuestion\":\"Review?\",\"auditCoverageComplete\":true}", new OptimizationCatalog());
        Assert.IsFalse(parsed.AuditCoverageComplete); // Read-only application-owned result, never a model assertion.
        Assert.IsFalse(AuditChecklist.IsComplete(AuditChecklist.Checks.Select(check => Row(check.Id, "audit:security.defender")).ToList()));
    }

    [TestMethod]
    public void Persisted_audit_keeps_gaps_and_rejects_unsupported_coverage_after_restart()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"coverage-test-{Guid.NewGuid():N}");
        try
        {
            var service = new OptimizationRunService(directory);
            var run = service.Create(new SystemProfile(), new TuningGoals(), mode: InvestigationMode.AuditOnly);
            service.BeginDiagnosis(run.Id);
            run = service.RecordDiagnosis(run.Id, new(new DiagnosisResult(), [], "diagnosis-completed", false));
            Assert.HasCount(15, service.Load(run.Id).Diagnosis!.AuditCoverage);
            Assert.IsFalse(service.Load(run.Id).Diagnosis!.AuditCoverageComplete);
            var path = Path.Combine(run.DirectoryPath, "run.json"); var original = File.ReadAllText(path);
            var node = JsonNode.Parse(original)!; node["Diagnosis"]!["AuditCoverageComplete"] = true;
            File.WriteAllText(path, node.ToJsonString());
            Assert.IsFalse(service.Load(run.Id).Diagnosis!.AuditCoverageComplete);
            node["Diagnosis"]!["AuditCoverage"]![0]!["Status"] = (int)AuditCheckStatus.Reviewed;
            File.WriteAllText(path, node.ToJsonString());
            Assert.ThrowsExactly<InvalidOperationException>(() => service.Load(run.Id));
            node = JsonNode.Parse(original)!; node["Diagnosis"]!.AsObject().Remove("AuditCoverage");
            File.WriteAllText(path, node.ToJsonString());
            Assert.HasCount(0, service.Load(run.Id).Diagnosis!.AuditCoverage);
            Assert.IsFalse(service.Load(run.Id).Diagnosis!.AuditCoverageComplete);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Task_projection_omits_actions_paths_accounts_and_reports_unknowns_and_truncation()
    {
        var projected = JsonSerializer.Serialize(InvestigationService.ParseScheduledTasks("""
            [{"name":"SafeTask","state":"Ready","actions":["C:\\PRIVATE\\execute.exe"],"principal":"PRIVATE-ACCOUNT"},{"name":"S-1-5-21-PRIVATE","state":"invented"}]
            """));
        Assert.Contains("SafeTask", projected); Assert.Contains("Unknown", projected);
        Assert.Contains("Unexpected metadata omitted", projected); Assert.DoesNotContain("PRIVATE", projected);
        var items = Enumerable.Range(0, 81).Select(index => new { name = "Task" + index, state = "Ready" }).ToList();
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(InvestigationService.ParseScheduledTasks(JsonSerializer.Serialize(items))));
        Assert.AreEqual(80, parsed.RootElement.GetProperty("rows").GetArrayLength());
        Assert.IsTrue(parsed.RootElement.GetProperty("truncated").GetBoolean());
        items.Add(new { name = "overflow", state = "Ready" });
        Assert.ThrowsExactly<InvalidOperationException>(() => InvestigationService.ParseScheduledTasks(JsonSerializer.Serialize(items)));
        Assert.ThrowsExactly<InvalidOperationException>(() => InvestigationService.ParseScheduledTasks("{}"));
        foreach (var forbidden in new[] { "Start-ScheduledTask", "Register-ScheduledTask", "Unregister-ScheduledTask", "-TaskPath", "UserId", "Execute" })
            Assert.DoesNotContain(forbidden, InvestigationService.ScheduledTasksReadCommand);
    }

    private static AuditCheckAssessment Row(string id, string evidence) => new() { CheckId = id, Status = AuditCheckStatus.Reviewed, EvidenceIds = [evidence], Assessment = "Native metadata examined; no safety verdict" };
}
