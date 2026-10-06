using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeuroTune.Tests;

[TestClass]
public sealed class PrivacySecurityTests
{
    internal const string DefenderJson = """
        {"scanState":"idle","protection":{"AntivirusEnabled":true,"RealTimeProtectionEnabled":true,"IsTamperProtected":true,"AMRunningMode":"Normal","AntivirusSignatureAge":0,"AntivirusSignatureLastUpdated":"2026-01-01T10:00:00Z","QuickScanEndTime":null,"FullScanEndTime":null,"ComputerID":"PRIVATE-ACCOUNT"},"preferences":{"MAPSReporting":2,"SubmitSamplesConsent":1,"CloudBlockLevel":2,"ExcludedPathCount":1,"ExcludedProcessCount":0,"ExcludedExtensionCount":0,"ExclusionPath":["C:\\secret\\excluded"]},"threats":[{"ThreatID":42,"ThreatName":"Test:Win32/Example","SeverityID":4,"IsActive":true,"DidThreatExecute":false,"Resources":["C:\\secret\\malware.exe"],"ProcessName":"PRIVATE-COMMAND-LINE"}]}
        """;

    [TestMethod]
    public void Audit_mode_is_persisted_independently_of_focus_and_cannot_be_promoted_to_a_writer()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"audit-test-{Guid.NewGuid():N}");
        try
        {
            var service = new OptimizationRunService(directory);
            var run = service.Create(new SystemProfile { Cpu = "CPU" }, new TuningGoals { Priority = OptimizationPriority.Balanced }, mode: InvestigationMode.AuditOnly);
            service.BeginDiagnosis(run.Id);
            var diagnosis = new DiagnosisResult { Recommendations = [new() { Id = "action", Kind = PlanRecommendationKind.ExecutableAction, ActionId = "gaming.game-mode" }] };
            run = service.RecordDiagnosis(run.Id, new(diagnosis, [], "diagnosis-completed", false));
            Assert.AreEqual(OptimizationRunState.ProposalReady, run.State);
            Assert.AreEqual(InvestigationMode.AuditOnly, service.Load(run.Id).Mode);
            Assert.AreEqual(PlanRecommendationKind.ManualGuidance, run.Diagnosis!.Recommendations.Single().Kind);
            Assert.AreEqual("", run.Diagnosis.Recommendations.Single().ActionId);
            Assert.ThrowsExactly<InvalidOperationException>(() => service.Approve(run.Id, ["gaming.game-mode"], false, new OptimizationCatalog()));
            Assert.ThrowsExactly<InvalidOperationException>(() => service.BeginApply(run.Id, Guid.NewGuid()));
            Assert.ThrowsExactly<InvalidOperationException>(() => service.AttachMeasurement(run.Id, new MeasurementSession { OptimizationRunId = run.Id }));
            var path = Path.Combine(run.DirectoryPath, "run.json");
            var original = File.ReadAllText(path);
            foreach (var field in new[] { "Mode", "SchemaVersion", "State", "ApprovedActionIds" })
            {
                var node = JsonNode.Parse(original)!;
                if (field == "Mode") node.AsObject().Remove(field);
                if (field == "SchemaVersion") node[field] = 1;
                if (field == "State") node[field] = (int)OptimizationRunState.BaselineReady;
                if (field == "ApprovedActionIds") node[field] = new JsonArray("gaming.game-mode");
                File.WriteAllText(path, node.ToJsonString());
                Assert.ThrowsExactly<InvalidOperationException>(() => service.Load(run.Id));
            }
            File.WriteAllText(path, original);
            Assert.AreEqual(OptimizationRunState.Completed, service.Dismiss(run.Id).State);
            Assert.ThrowsExactly<InvalidOperationException>(() => service.Create(new SystemProfile(), new TuningGoals(), [new MeasurementSession()], mode: InvestigationMode.AuditOnly));
            Assert.ThrowsExactly<InvalidOperationException>(() => service.Create(new SystemProfile(), new TuningGoals(), mode: (InvestigationMode)99));
            var legacy = service.Create(new SystemProfile(), new TuningGoals { Priority = OptimizationPriority.PrivacySecurity });
            var legacyPath = Path.Combine(legacy.DirectoryPath, "run.json");
            var journal = JsonNode.Parse(File.ReadAllText(legacyPath))!;
            journal["SchemaVersion"] = 1; journal.AsObject().Remove("Mode");
            File.WriteAllText(legacyPath, journal.ToJsonString());
            Assert.AreEqual(InvestigationMode.MeasuredOptimization, service.Load(legacy.Id).Mode);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Defender_projection_omits_private_resources_and_preserves_unknown_and_truncation()
    {
        var report = WindowsSecurityAudit.ParseDefender(DefenderJson);
        Assert.AreEqual("observed", report.Status);
        Assert.AreEqual("idle", report.ScanState);
        Assert.IsTrue(report.Threats.Single().IsActive);
        Assert.AreEqual("42", report.Threats.Single().ThreatId);
        var prepared = JsonSerializer.Serialize(report);
        foreach (var secret in new[] { "PRIVATE-ACCOUNT", "secret", "PRIVATE-COMMAND-LINE", "ExclusionPath", "Resources", "ProcessName" })
            Assert.DoesNotContain(secret, prepared);
        var node = JsonNode.Parse(DefenderJson)!;
        node["threats"]![0]!["ThreatID"] = ulong.MaxValue;
        Assert.AreEqual("18446744073709551615", WindowsSecurityAudit.ParseDefender(node.ToJsonString()).Threats.Single().ThreatId); // Exact even beyond JS integer precision.
        node["protection"]!["AMRunningMode"] = "invented";
        node["protection"]!["RealTimeProtectionEnabled"] = "false";
        node["threats"]![0]!["ThreatName"] = "C:\\Users\\private\\malware.exe";
        report = WindowsSecurityAudit.ParseDefender(node.ToJsonString());
        Assert.AreEqual("Unknown", report.Protection!.RunningMode);
        Assert.IsNull(report.Protection.RealTimeProtectionEnabled);
        Assert.AreEqual("Unexpected metadata omitted", report.Threats.Single().Name);
        node["threats"] = new JsonArray(Enumerable.Range(0, 41).Select(_ => JsonNode.Parse("{\"ThreatID\":1}")).ToArray());
        report = WindowsSecurityAudit.ParseDefender(node.ToJsonString());
        Assert.HasCount(40, report.Threats); Assert.IsTrue(report.Truncated);
        node["threats"]!.AsArray().Add(new JsonObject());
        Assert.ThrowsExactly<InvalidOperationException>(() => WindowsSecurityAudit.ParseDefender(node.ToJsonString()));
        Assert.AreEqual("unavailable", WindowsSecurityAudit.UnavailableDefender("Test").Status);
        Assert.IsNull(WindowsSecurityAudit.UnavailableDefender("Test").Protection);
    }

    [TestMethod]
    public async Task Defender_scan_requires_separate_consent_and_journals_uncertain_completion_without_repeating_it()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"defender-test-{Guid.NewGuid():N}");
        var report = WindowsSecurityAudit.ParseDefender(DefenderJson);
        var calls = new List<(string Command, TimeSpan Timeout)>();
        var fail = false; var recording = false; var measured = false; var hold = false;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        DefenderScanService? service = null;
        service = new(directory, false, () => report, (command, timeout) =>
        {
            Assert.AreEqual("Running", service!.Current()!.State); // Pre-command journal must exist.
            calls.Add((command, timeout));
            if (hold) { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Mock scan release timed out"); }
            return fail ? Task.FromException<string>(new TimeoutException("PRIVATE-PATH must not escape")) : Task.FromResult("Untrusted raw output must not become evidence");
        }, () => recording, () => measured, "Local\\NeuroTuneDefenderTest-" + Guid.NewGuid().ToString("N"));
        var request = new DefenderScanRequest("quick", true, true, true);
        try
        {
            foreach (var invalid in new[] { request with { ScanConsent = false }, request with { RemediationConsent = false }, request with { NetworkConsent = false }, request with { ScanType = "quick;Remove-Item" } })
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(invalid));
            Assert.IsFalse(Directory.Exists(directory)); Assert.HasCount(0, calls);
            recording = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(request));
            recording = false; measured = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(request));
            measured = false;
            foreach (var invalid in new[] { report with { Status = "unavailable" }, report with { ScanState = "busy" }, report with { ScanState = "unknown" }, report with { Protection = report.Protection! with { RunningMode = "Passive Mode" } } })
                Assert.ThrowsExactly<InvalidOperationException>(() => DefenderScanService.ValidateProtection(invalid));
            var result = await service.StartAsync(request);
            Assert.AreEqual("CommandReturned", result.Operation.State);
            Assert.IsTrue(result.Report.Threats.Single().IsActive); // Successful command does not equal clean PC.
            Assert.DoesNotContain("Untrusted raw", JsonSerializer.Serialize(result));
            Assert.Contains("QuickScan", calls[0].Command); Assert.AreEqual(TimeSpan.FromHours(1), calls[0].Timeout);
            fail = true;
            result = await service.StartAsync(request with { ScanType = "full" });
            Assert.AreEqual("InterruptedOrFailed", result.Operation.State);
            Assert.DoesNotContain("PRIVATE-PATH", result.Operation.Detail);
            Assert.Contains("FullScan", calls[1].Command); Assert.AreEqual(TimeSpan.FromHours(24), calls[1].Timeout);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(request));
            Assert.HasCount(2, calls);
            Assert.ThrowsExactly<InvalidOperationException>(() => service.AcknowledgeInterruption(false));
            report = report with { ScanState = "unknown" };
            Assert.ThrowsExactly<InvalidOperationException>(() => service.AcknowledgeInterruption(true));
            report = report with { ScanState = "idle" };
            Assert.AreEqual("ReviewedAfterInterruption", service.AcknowledgeInterruption(true).State);
            Assert.ThrowsExactly<InvalidOperationException>(() => service.AcknowledgeInterruption(true));
            fail = false; hold = true;
            var pending = service.StartAsync(request);
            try
            {
                Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(request));
                Assert.ThrowsExactly<InvalidOperationException>(() => service.AcknowledgeInterruption(true));
            }
            finally { release.Set(); await pending; }
            Assert.HasCount(3, calls);
            foreach (var command in calls.Select(call => call.Command))
                foreach (var forbidden in new[] { "Set-MpPreference", "Remove-MpThreat", "-DisableRemediation", "-ScanPath", "-AsJob", "-ExecutionPolicy", "Invoke-Expression" })
                    Assert.DoesNotContain(forbidden, command);
            var path = Path.Combine(directory, "current.json");
            File.WriteAllText(path, "{}");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(request));
            Assert.HasCount(3, calls);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task Privacy_focus_does_not_start_scanners_or_turn_absence_into_disabled()
    {
        Assert.Contains("not execution permissions", LlmClient.AnalysisFocus(OptimizationPriority.PrivacySecurity));
        Assert.Contains("WINDOWS PRIVACY AND SECURITY", LlmClient.AnalysisFocus(OptimizationPriority.PrivacySecurity));
        var restored = JsonSerializer.Deserialize<TuningGoals>("{\"Priority\":6}")!;
        restored.Validate(); Assert.AreEqual(OptimizationPriority.PrivacySecurity, restored.Priority);
        Assert.Contains("effective state unknown", WindowsSecurityAudit.DescribeRegistryValue(RegistryValueType.DWord, null));
        Assert.DoesNotContain("private", WindowsSecurityAudit.DescribeRegistryValue(RegistryValueType.String, "C:\\Users\\private"));
        Assert.Contains("DWord snapshot=0", WindowsSecurityAudit.DescribeRegistryValue(RegistryValueType.DWord, 0));
        Assert.AreEqual(ProbeCatalog.PrivacySecurityRegistry.Count, ProbeCatalog.PrivacySecurityRegistry.Select(probe => probe.StableEvidenceId).Distinct().Count());
        Assert.IsFalse(InvestigationService.Tools.Any(tool => tool.Id.Contains("scan", StringComparison.Ordinal)));
        var unsupported = await InvestigationService.ReadAsync(new("defender-scan", "Scan without consent"));
        Assert.Contains("unavailable", unsupported.Values.Single());
        Assert.ThrowsExactly<ArgumentException>(() => InvestigationService.Validate(new("defender-detections", "Read threats", "C:\\private")));
        var facts = LlmClient.BuildEvidenceFacts(new SystemProfile { PrivacySecurity = new() { ["security.defender"] = "unavailable" } });
        Assert.AreEqual("unavailable", facts["audit:security.defender"]);
        Assert.AreEqual(EvidencePrivacy.SoftwareInventory, LlmClient.ClassifyEvidence("audit:security.defender"));
        Assert.AreEqual(EvidencePrivacy.SoftwareInventory, LlmClient.ClassifyEvidence("investigation:security-status:request:turn1"));
        Assert.IsTrue(LlmClient.SelectInitialEvidence(facts, []).ContainsKey("audit:security.defender"));
    }
}
