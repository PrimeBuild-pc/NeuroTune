using System.Text.Json;

namespace NeuroTune.Tests;

[TestClass]
public sealed class ContextualReviewTests
{
    [TestMethod]
    public void Action_references_resolve_exactly_and_never_guess_from_titles_or_recommendation_ids()
    {
        const string response = """{"summary":"Checked","findings":[],"recommendations":[{"id":"local-proposal","kind":"executableAction","title":"Game Mode","actionId":"a0001","executionIssue":"forged","reason":"Review"}],"consentQuestion":"Review?"}""";
        var references = new Dictionary<string, string>(StringComparer.Ordinal) { ["a0001"] = "gaming.game-mode" };
        var catalog = new OptimizationCatalog();
        var linked = LlmClient.ParseDiagnosis(response, catalog, originalActionIds: references).Recommendations.Single();
        Assert.AreEqual("gaming.game-mode", linked.ActionId);
        Assert.AreEqual(PlanRecommendationKind.ExecutableAction, linked.Kind);
        Assert.AreEqual("", linked.ExecutionIssue);
        foreach (var id in new[] { "", "a001", "A0001", "f0001", "Game Mode", "system.large-cache-default" })
        {
            var unresolved = LlmClient.ParseDiagnosis(response.Replace("\"actionId\":\"a0001\"", $"\"actionId\":\"{id}\""), catalog, originalActionIds: references).Recommendations.Single();
            Assert.AreEqual(PlanRecommendationKind.ManualGuidance, unresolved.Kind, id);
            Assert.AreEqual("", unresolved.ActionId, id);
            Assert.AreEqual(id.Length == 0 ? "missingActionId" : "unknownActionId", unresolved.ExecutionIssue, id);
        }
        var manual = LlmClient.ParseDiagnosis(response.Replace("executableAction", "manualGuidance").Replace("\"actionId\":\"a0001\",", ""), catalog).Recommendations.Single();
        Assert.AreEqual("", manual.ExecutionIssue, "A model cannot forge locally validated execution metadata.");
    }

    [TestMethod]
    public void Virtual_display_adapters_do_not_certify_Hags_and_firmware_status_never_claims_host_setup_access()
    {
        Assert.IsFalse(OptimizationCatalog.HasPhysicalGraphicsAdapter([@"VMBUS\123", @"ROOT\RDPIDD"]));
        Assert.IsFalse(OptimizationCatalog.HasPhysicalGraphicsAdapter([@"PCI\VEN_1414&DEV_5353", ""]));
        foreach (var vendor in new[] { "10DE", "1002", "8086" }) Assert.IsTrue(OptimizationCatalog.HasPhysicalGraphicsAdapter([$@"PCI\VEN_{vendor}&DEV_1234"]));
        var guest = FirmwareInspection.SetupStatus("Micro-Star International", "Microsoft Corporation Virtual Machine");
        StringAssert.Contains(guest, "not accessible");
        StringAssert.Contains(FirmwareInspection.SetupStatus("Micro-Star International", "Physical desktop"), "No documented, validated MSI setup API");
        var disabled = FirmwareInspection.Read(false);
        Assert.IsFalse(disabled.ReadEnabled); Assert.IsFalse(disabled.WriteSupported); Assert.IsEmpty(disabled.Facts);
    }

    [TestMethod]
    public void Short_MSI_manufacturer_names_are_recognized_without_claiming_setup_access()
    {
        foreach (var board in new[] { "MSI", "MSI MPG X570 GAMING EDGE WIFI (MS-7C37)", "Micro-Star International Co., Ltd." })
            StringAssert.StartsWith(FirmwareInspection.SetupStatus(board, "Desktop"), "MSI board identified.");
        StringAssert.StartsWith(FirmwareInspection.SetupStatus("MSI", "Microsoft Virtual Machine"), "Virtual firmware only:");
        Assert.DoesNotContain("MSI board identified", FirmwareInspection.SetupStatus("Other board", "Desktop"));
    }

    [TestMethod]
    public void Inbox_Microsoft_driver_dates_do_not_create_age_based_conflicts()
    {
        var profile = new SystemProfile
        {
            CollectedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
            RelevantDrivers = ["Hyper-V | Microsoft | 10.0.26200.0 | 06/21/2006"],
            DeviceIssues = ["Hyper-V | Error 37"]
        };
        Assert.IsFalse(ConflictAnalyzer.Analyze(profile, new()).Any(item => item.Id == "stale-driver-device-error"));
        profile.RelevantDrivers[0] = "Adapter | Example vendor | 1.0 | 01/01/2010";
        Assert.IsTrue(ConflictAnalyzer.Analyze(profile, new()).Any(item => item.Id == "stale-driver-device-error"));
    }

    [TestMethod]
    public void Idle_differences_never_become_workload_results_or_Keep_authority()
    {
        var before = Enumerable.Range(0, 3).Select(_ => Session(MeasurementLabel.Baseline)).ToList();
        var after = Enumerable.Range(0, 3).Select(_ => Session(MeasurementLabel.Candidate)).ToList();
        var request = new MeasurementCompareRequest(before.Select(item => item.Id).ToList(), after.Select(item => item.Id).ToList());
        var result = MeasurementService.Compare(request, before, after);
        Assert.IsEmpty(result.RejectionReasons); Assert.IsTrue(result.DiagnosticOnly);
        Assert.AreEqual(ComparisonLevel.Exploratory, result.Level);
        Assert.AreEqual(ComparisonDecision.InsufficientEvidence, result.Recommendation);
        Assert.IsNotEmpty(result.Metrics);
        Assert.IsFalse(result.Metrics.Any(item => item.EvidenceId.Contains("target:", StringComparison.Ordinal)));
        Assert.IsNotEmpty(MeasurementService.Compare(request with { OptimizationRunId = Guid.NewGuid() }, before, after).RejectionReasons);
        Assert.IsNotEmpty(MeasurementService.Compare(request, before, [Session(MeasurementLabel.Candidate, systemWide: false)]).RejectionReasons);
        Assert.IsNotEmpty(MeasurementService.Compare(request, before, [Session(MeasurementLabel.Candidate, conditions: "Different scene")]).RejectionReasons);
        Assert.IsNotEmpty(MeasurementService.Compare(request, [Session(MeasurementLabel.Baseline, conditions: "")], [Session(MeasurementLabel.Candidate, conditions: "")]).RejectionReasons);
        var saved = JsonSerializer.Deserialize<MeasurementSession>(JsonSerializer.Serialize(before[0]))!;
        Assert.AreEqual("Idle", saved.Conditions); Assert.AreEqual(OptimizationPriority.SystemLatency, saved.AnalysisPreset); Assert.AreEqual(before[0].Report!.Quality.DurationMilliseconds, saved.Report!.Quality.DurationMilliseconds); Assert.IsTrue(saved.Report.Quality.IsValid);

        static MeasurementSession Session(MeasurementLabel label, bool systemWide = true, string conditions = "Idle") => new()
        {
            Id = Guid.NewGuid(),
            SchemaVersion = 3,
            Label = label,
            SystemWide = systemWide,
            Conditions = conditions,
            AnalysisPreset = OptimizationPriority.SystemLatency,
            ProcessName = "System-wide",
            HardwareFingerprint = "hardware",
            ConfigurationFingerprint = "configuration",
            HardFaultsEnabled = true,
            State = MeasurementSessionState.Completed,
            DurationSeconds = 30,
            Report = new TraceReport
            {
                SchemaVersion = 2,
                Quality = new(30_000, 1, 0, [], 100, true),
                Interrupts = [new("dpc", "fixture.sys", 0, TraceAnalyzer.Describe([label == MeasurementLabel.Baseline ? 100 : 50], 30_000))]
            }
        };
    }
}
