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
}
