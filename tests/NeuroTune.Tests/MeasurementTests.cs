namespace NeuroTune.Tests;

[TestClass]
public sealed class MeasurementTests
{
    [TestMethod]
    public void Percentiles_use_nearest_rank()
    {
        var distribution = TraceAnalyzer.Describe([1, 2, 3, 4, 100], 1000);

        Assert.AreEqual(3, distribution.P50Microseconds);
        Assert.AreEqual(100, distribution.P95Microseconds);
        Assert.AreEqual(100, distribution.P99Microseconds);
        Assert.AreEqual(5, distribution.Count);
    }

    [TestMethod]
    public void Interval_sweep_counts_only_same_processor_overlap()
    {
        TimeInterval[] ready = [new(0, 10, 2), new(20, 30, 2), new(0, 50, 3)];
        TimeInterval[] interrupt = [new(5, 12, 2), new(18, 24, 2), new(1, 49, 4)];

        Assert.AreEqual(9000, TraceAnalyzer.OverlapMicroseconds(ready, interrupt, 2));
        Assert.AreEqual(0, TraceAnalyzer.OverlapMicroseconds(ready, interrupt, 3));
    }

    [TestMethod]
    public void Repeated_comparison_requires_two_of_three_on_same_side()
    {
        Assert.AreEqual(ComparisonOutcome.Improvement, MeasurementService.RepeatedOutcome(100, [90, 95, 110]));
        Assert.AreEqual(ComparisonOutcome.Regression, MeasurementService.RepeatedOutcome(100, [105, 110, 90]));
        Assert.AreEqual(ComparisonOutcome.Inconclusive, MeasurementService.RepeatedOutcome(100, [90, 100, 110]));
        Assert.AreEqual(ComparisonOutcome.Improvement, MeasurementService.RepeatedOutcome(100, [105, 110, 90], true));
    }

    [TestMethod]
    public void PresentMon_csv_is_aggregated_locally_for_the_selected_executable()
    {
        var rows = Enumerable.Range(0, 121).Select(index => $"game.exe,{(index >= 119 ? 60 : 10)},Hardware Composed: Independent Flip");
        var csv = "ProcessName,MsBetweenPresents,PresentMode\n" + string.Join('\n', rows) + "\nother.exe,1,Composed: Flip";

        var result = PresentMonImporter.Parse(csv, "game");

        Assert.AreEqual(121, result.SampleCount);
        Assert.IsTrue(result.AverageFps is > 90 and < 100);
        Assert.AreEqual(60, result.P99Milliseconds);
        Assert.AreEqual(2, result.StutterCount);
        Assert.AreEqual("Hardware Composed: Independent Flip", result.PresentModes.Single());
        Assert.IsTrue(MeasurementService.FrameDurationMatches(100_000, 120_000));
        Assert.IsFalse(MeasurementService.FrameDurationMatches(100_000, 120_001));
    }

    [TestMethod]
    public void Repeated_comparison_produces_a_conservative_keep_or_rollback_recommendation()
    {
        ComparisonMetric[] improvements =
        [
            new("a", 100, 90, -10, ComparisonOutcome.Improvement),
            new("b", 100, 95, -5, ComparisonOutcome.Improvement),
            new("c", 100, 105, 5, ComparisonOutcome.Regression)
        ];
        Assert.AreEqual(ComparisonDecision.Keep, MeasurementService.Recommend(ComparisonLevel.Repeated, improvements).Decision);
        Assert.AreEqual(ComparisonDecision.Rollback, MeasurementService.Recommend(ComparisonLevel.Repeated, improvements.Reverse().Select((item, index) =>
            index < 2 ? item with { Outcome = ComparisonOutcome.Regression } : item with { Outcome = ComparisonOutcome.Improvement }).ToList()).Decision);
        Assert.AreEqual(ComparisonDecision.InsufficientEvidence, MeasurementService.Recommend(ComparisonLevel.Exploratory, improvements).Decision);
    }

    [TestMethod]
    public void Session_aggregation_uses_median()
    {
        Assert.AreEqual(20, MeasurementService.Median([10, 20, 1000]));
        Assert.AreEqual(15, MeasurementService.Median([10, 20]));
    }

    [TestMethod]
    public void Llm_measurement_evidence_accepts_only_normalized_numbers()
    {
        var profile = new Dictionary<string, string> { ["system:cpu"] = "CPU" };
        var valid = new Dictionary<string, string> { ["measurement:abc:cpu:1:interrupt_share_percent"] = "12.500" };
        var merged = LlmClient.MergeEvidenceFacts(profile, valid);

        Assert.AreEqual("12.500", merged["measurement:abc:cpu:1:interrupt_share_percent"]);
        Assert.ThrowsExactly<InvalidOperationException>(() => LlmClient.MergeEvidenceFacts(profile,
            new Dictionary<string, string> { ["measurement:abc:path"] = @"C:\Users\Lorenzo\capture.etl" }));
        Assert.ThrowsExactly<InvalidOperationException>(() => LlmClient.MergeEvidenceFacts(profile,
            new Dictionary<string, string> { ["measurement:abc:command"] = "wpr -start profile" }));
    }

    [TestMethod]
    public void Evidence_components_remove_path_and_argument_characters()
    {
        var component = TraceAnalyzer.EvidencePart(@"C:\Driver Files\gpu.sys --flag");

        Assert.DoesNotContain("\\", component);
        Assert.DoesNotContain(" ", component);
        Assert.AreEqual("c__driver_files_gpu.sys_--flag", component);
    }

    [TestMethod]
    public void Measurement_state_machine_keeps_cancelled_analysis_retryable()
    {
        Assert.IsTrue(MeasurementStateMachine.CanTransition(MeasurementSessionState.Captured, MeasurementSessionState.Analyzing));
        Assert.IsTrue(MeasurementStateMachine.CanTransition(MeasurementSessionState.Analyzing, MeasurementSessionState.Captured));
        Assert.IsTrue(MeasurementStateMachine.CanTransition(MeasurementSessionState.Recording, MeasurementSessionState.Cancelled));
        Assert.IsFalse(MeasurementStateMachine.CanTransition(MeasurementSessionState.Completed, MeasurementSessionState.Recording));
        Assert.IsFalse(MeasurementStateMachine.CanTransition(MeasurementSessionState.Cancelled, MeasurementSessionState.Analyzing));
    }

    [TestMethod]
    public void Gpu_candidate_preview_returns_three_distinct_physical_cores_without_enabling_apply()
    {
        var sessions = Enumerable.Range(0, 3).Select(index => Baseline(index)).ToList();
        CpuTopologyEntry[] cpus =
        [
            new(0, 0, 0, 0, 0, 0), new(0, 1, 0, 1, 0, 0), new(0, 2, 1, 0, 0, 0),
            new(0, 3, 2, 0, 0, 0), new(0, 4, 3, 0, 0, 0)
        ];
        var gpu = new GpuDeviceTopology("gpu-key", "Test GPU", "AMD", "1.2.3", @"PCI\VEN_1002", @"SYSTEM\gpu", true);

        var result = new HardwareTopologyService().Generate(new("gpu-key", sessions.Select(item => item.Id).ToList()), sessions,
            new MachineTopology(cpus, [gpu]));

        Assert.HasCount(3, result.Candidates);
        Assert.AreEqual(3, result.Candidates.Select(item => item.PhysicalCore).Distinct().Count());
        Assert.IsTrue(result.Candidates.All(item => !item.ApplyEnabled && item.DevicePolicy == 4));
        Assert.AreEqual((byte)1, result.Candidates[0].LogicalProcessor);

        static MeasurementSession Baseline(int index) => new()
        {
            Id = Guid.NewGuid(),
            ProcessName = "game",
            Label = MeasurementLabel.Baseline,
            DurationSeconds = 180,
            HardwareFingerprint = "hardware",
            ConfigurationFingerprint = "configuration",
            State = MeasurementSessionState.Completed,
            Report = new TraceReport
            {
                Quality = new(180_000, 1, 0, [], 100, true),
                Processors =
                [
                    new(0, 20 + index, 20, 20), new(1, 1 + index, 2, 2), new(2, 5 + index, 5, 5),
                    new(3, 2 + index, 3, 3), new(4, 9 + index, 9, 9)
                ]
            }
        };
    }

    [TestMethod]
    public void Gpu_affinity_policy_snapshot_classifies_only_exact_registry_types_as_restorable()
    {
        var missing = new RegistryValueSnapshot(false, "None", "", 0);
        var mask = new RegistryValueSnapshot(true, "Binary", "08", 1);
        var dwordMask = new RegistryValueSnapshot(true, "DWord", "00000008", 4);
        var qwordMask = new RegistryValueSnapshot(true, "QWord", "0000000000000008", 8);
        var policy = new RegistryValueSnapshot(true, "DWord", "00000004", 4);

        Assert.AreEqual("windowsDefault", HardwareTopologyService.PolicyState(missing, missing));
        Assert.AreEqual("configured", HardwareTopologyService.PolicyState(mask, policy));
        Assert.AreEqual("configured", HardwareTopologyService.PolicyState(dwordMask, policy));
        Assert.AreEqual("configured", HardwareTopologyService.PolicyState(qwordMask, policy));
        Assert.AreEqual("unsupported", HardwareTopologyService.PolicyState(new(true, "String", "", 0), policy));
        Assert.AreEqual("unsupported", HardwareTopologyService.PolicyState(new(true, "Binary", new string('F', 18), 9), policy));
    }
}
