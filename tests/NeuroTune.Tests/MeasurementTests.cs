namespace NeuroTune.Tests;

[TestClass]
public sealed class MeasurementTests
{
    [TestMethod]
    [DoNotParallelize] // This fixture shares the recording store inspected by planner tests.
    public void Repeated_stop_of_a_finalized_session_does_not_restart_Wpr_or_corrupt_the_report()
    {
        var id = Guid.NewGuid();
        var directory = Path.Combine(MeasurementService.MeasurementsDirectory, id.ToString("D"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "session.json"), System.Text.Json.JsonSerializer.Serialize(new MeasurementSession
            { Id = id, State = MeasurementSessionState.Completed, Report = new TraceReport { Quality = new(30_000, 1, 0, [], 0, true) } }));
            var finalized = new MeasurementService().Stop(id);
            Assert.AreEqual(MeasurementSessionState.Completed, finalized.State);
            Assert.IsNotNull(finalized.Report);
            Assert.IsNull(finalized.Error);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

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
        Assert.AreEqual(ComparisonOutcome.Inconclusive, MeasurementService.RepeatedOutcome(100, [90]));
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
    [DataRow("MsBetweenPresents")]
    [DataRow("MsBetweenPresents,ProcessName")]
    public void PresentMon_import_rejects_missing_process_identity_in_headers_or_rows(string header)
    {
        var rows = string.Join('\n', Enumerable.Repeat("10", 3000));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PresentMonImporter.Parse(header + "\n" + rows, "selected-game"),
            "Unattributed frames must not become verified measurements for the selected process.");
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
        Assert.AreEqual(ComparisonDecision.Rollback, MeasurementService.Recommend(ComparisonLevel.Repeated, improvements).Decision);
        Assert.AreEqual(ComparisonDecision.Keep, MeasurementService.Recommend(ComparisonLevel.Repeated, improvements.Take(2).ToList()).Decision);
        Assert.AreEqual(ComparisonDecision.InsufficientEvidence, MeasurementService.Recommend(ComparisonLevel.Repeated, []).Decision);
        Assert.AreEqual(ComparisonDecision.Rollback, MeasurementService.Recommend(ComparisonLevel.Repeated, improvements.Reverse().Select((item, index) =>
            index < 2 ? item with { Outcome = ComparisonOutcome.Regression } : item with { Outcome = ComparisonOutcome.Improvement }).ToList()).Decision);
        Assert.AreEqual(ComparisonDecision.InsufficientEvidence, MeasurementService.Recommend(ComparisonLevel.Exploratory, improvements).Decision);
    }

    [TestMethod]
    public void Comparison_never_recommends_keep_when_new_driver_latency_and_faults_regress()
    {
        var baseline = Enumerable.Range(0, 3).Select(_ => Session(MeasurementLabel.Baseline)).ToList();
        var candidate = Enumerable.Range(0, 3).Select(_ => Session(MeasurementLabel.Candidate)).ToList();
        var result = MeasurementService.Compare(new(baseline.Select(item => item.Id).ToList(), candidate.Select(item => item.Id).ToList()), baseline, candidate);
        Assert.AreNotEqual(ComparisonDecision.Keep, result.Recommendation);
        Assert.IsTrue(result.Metrics.Any(metric => metric.Outcome == ComparisonOutcome.Regression));

        static MeasurementSession Session(MeasurementLabel label) => new()
        {
            Id = Guid.NewGuid(),
            Label = label,
            ProcessName = "game",
            HardFaultsEnabled = true,
            HardwareFingerprint = "same",
            ConfigurationFingerprint = "same",
            State = MeasurementSessionState.Completed,
            Report = new TraceReport
            {
                SchemaVersion = 2,
                Quality = new(30_000, 1, 0, [], 100, true),
                Interrupts = label == MeasurementLabel.Baseline
                    ? [new("dpc", "common.sys", 0, TraceAnalyzer.Describe([100], 30_000))]
                    : [new("dpc", "common.sys", 0, TraceAnalyzer.Describe([50], 30_000)),
                        new("dpc", "new.sys", 0, TraceAnalyzer.Describe([100_000], 30_000))],
                HardFaults = label == MeasurementLabel.Baseline ? []
                    : [new("process-1", "game", TraceAnalyzer.Describe([500_000], 30_000))]
            }
        };
    }

    [TestMethod]
    public void Coverage_changes_or_missing_fault_collection_never_turn_into_zero_cost_keep()
    {
        var baseline = Enumerable.Range(0, 3).Select(_ => Session(MeasurementLabel.Baseline)).ToList();
        var candidate = Enumerable.Range(0, 3).Select(_ => Session(MeasurementLabel.Candidate)).ToList();
        var request = new MeasurementCompareRequest(baseline.Select(item => item.Id).ToList(), candidate.Select(item => item.Id).ToList());
        var comparison = MeasurementService.Compare(request, baseline, candidate);
        Assert.AreEqual(ComparisonDecision.InsufficientEvidence, comparison.Recommendation);
        Assert.IsTrue(comparison.RejectionReasons.Any(reason => reason.Contains("coverage", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(comparison.Metrics.Any(metric => metric.EvidenceId.Contains("extra.sys", StringComparison.Ordinal)));
        candidate[0] = Session(MeasurementLabel.Candidate, hardFaults: true);
        request = request with { CandidateSessionIds = candidate.Select(item => item.Id).ToList() };
        comparison = MeasurementService.Compare(request, baseline, candidate);
        Assert.AreEqual(ComparisonDecision.InsufficientEvidence, comparison.Recommendation);
        Assert.IsTrue(comparison.RejectionReasons.Any(reason => reason.Contains("Hard-fault", StringComparison.Ordinal)));

        static MeasurementSession Session(MeasurementLabel label, bool hardFaults = false) => new()
        {
            Id = Guid.NewGuid(),
            Label = label,
            ProcessName = "game",
            HardFaultsEnabled = hardFaults,
            State = MeasurementSessionState.Completed,
            Report = new TraceReport
            {
                Quality = new(30_000, 1, 0, [], 100, true),
                Interrupts = label == MeasurementLabel.Baseline
                    ? [new("dpc", "common.sys", 0, TraceAnalyzer.Describe([100], 30_000))]
                    : [new("dpc", "common.sys", 0, TraceAnalyzer.Describe([50], 30_000)),
                        new("dpc", "extra.sys", 0, TraceAnalyzer.Describe([40], 30_000))]
            }
        };
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

    [TestMethod]
    public void Interrupt_completion_and_nested_intervals_do_not_shift_or_double_count_overlap()
    {
        Assert.AreEqual(8, TraceAnalyzer.InterruptStartMilliseconds(10, 2));
        Assert.AreEqual(0, TraceAnalyzer.InterruptStartMilliseconds(1, 2));
        TimeInterval[] ready = [new(0, 10, 0), new(2, 8, 0)];
        TimeInterval[] interrupts = [new(5, 9, 0), new(6, 7, 0), new(0, 10, 1)];
        Assert.AreEqual(4000, TraceAnalyzer.OverlapMicroseconds(ready, interrupts, 0));
    }

    [TestMethod]
    public void Processor_counter_delta_uses_actual_window_and_rejects_reset_or_missing_coordinates()
    {
        var before = new PerformanceSnapshotService.ProcessorCounters(100_000_000, 20_000_000, 100_000, 200_000);
        var after = new PerformanceSnapshotService.ProcessorCounters(120_000_000, 25_000_000, 120_000, 210_000);
        var sample = PerformanceSnapshotService.ProcessorDelta("1,3", before, after)!;
        Assert.AreEqual((ushort)1, sample.ProcessorGroup);
        Assert.AreEqual((byte)3, sample.LogicalProcessor);
        Assert.AreEqual(2000, sample.WindowMilliseconds);
        Assert.AreEqual(1500, sample.BusyMilliseconds);
        Assert.AreEqual(500, sample.IdleMilliseconds);
        Assert.AreEqual(2, sample.DpcMilliseconds);
        Assert.AreEqual(1, sample.IsrMilliseconds);
        Assert.IsNull(PerformanceSnapshotService.ProcessorDelta("_Total", before, after));
        Assert.IsNull(PerformanceSnapshotService.ProcessorDelta("0,0", after, before));
        Assert.IsNull(PerformanceSnapshotService.ProcessorDelta("0,0", before, before));
    }

    [TestMethod]
    public void New_trace_evidence_survives_frame_import_and_does_not_export_fault_process_names()
    {
        var report = new TraceReport
        {
            SchemaVersion = 2,
            HardFaults = [new("process-1", "private-application.exe", new(2, 1, 8, 4, 4, 4, 4))],
            LongestSpikes = [new("dpc", "test.sys", 0, 8, 2000)],
            Limitations = ["Interrupt-to-process latency unavailable"]
        };
        report = report.WithObservations([]).WithFrameTimes(new("test", 1, 1, 1, 1, 1, 1, 1, 0, []));
        Assert.HasCount(1, report.HardFaults);
        Assert.HasCount(1, report.LongestSpikes);
        Assert.HasCount(1, report.Limitations);
        var facts = MeasurementService.BuildNormalizedEvidence([new MeasurementSession
        {
            Id = Guid.NewGuid(), SystemWide = true, HardFaultsEnabled = true,
            State = MeasurementSessionState.Completed, Report = report
        }]);
        Assert.IsTrue(facts.Keys.Any(key => key.EndsWith(":fault:process-1:count")));
        Assert.DoesNotContain("private-application", string.Join(" ", facts));
    }

    [TestMethod]
    public void Firmware_read_opt_out_returns_no_facts_or_interfaces()
    {
        var result = FirmwareInspection.Read(false);
        Assert.IsFalse(result.ReadEnabled);
        Assert.IsFalse(result.WriteSupported);
        Assert.HasCount(0, result.Facts);
        Assert.HasCount(0, result.Interfaces);
        Assert.AreEqual("Unknown", FirmwareInspection.ObservedBoolean(null));
        Assert.AreEqual("Unknown", FirmwareInspection.ObservedBoolean("false"));
        Assert.AreEqual("Yes", FirmwareInspection.ObservedBoolean(true));
        Assert.AreEqual("No", FirmwareInspection.ObservedBoolean(false));
        Assert.AreEqual("https://download.msi.com/archive/mnu_exe/mb/E7C37v1.1.pdf",
            FirmwareInspection.GuidanceUrl("Micro-Star International Co., Ltd. MPG X570 GAMING EDGE WIFI (MS-7C37) 1.0"));
        Assert.DoesNotContain("E7C37", FirmwareInspection.GuidanceUrl("MSI MPG X570 GAMING PLUS (MS-7C37)"));
    }

    [TestMethod]
    public void Driver_inspection_accepts_only_module_names_and_local_windows_driver_paths()
    {
        DriverInspection.ValidateModule("dxgkrnl.sys");
        foreach (var invalid in new[] { @"..\gpu.sys", "../gpu.sys", @"\\host\gpu.sys", "gpu.sys:stream", "gpu.sys --flag", "Unknown", "..sys" })
            Assert.ThrowsExactly<ArgumentException>(() => DriverInspection.ValidateModule(invalid));
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.AreEqual(Path.Combine(windows, @"System32\drivers\dxgkrnl.sys"), DriverInspection.ResolveLocalDriverPath(@"\SystemRoot\System32\drivers\dxgkrnl.sys"));
        Assert.IsNull(DriverInspection.ResolveLocalDriverPath(@"\\host\share\gpu.sys"));
        Assert.IsNull(DriverInspection.ResolveLocalDriverPath(@"\??\UNC\host\gpu.sys"));
        Assert.IsNull(DriverInspection.ResolveLocalDriverPath(@"\SystemRoot\..\outside.sys"));
    }

    [TestMethod]
    public void Reused_identities_and_changed_kernel_ranges_are_not_misattributed()
    {
        var start = Microsoft.Diagnostics.Tracing.TraceEventOpcode.Start;
        var stop = Microsoft.Diagnostics.Tracing.TraceEventOpcode.Stop;
        var initial = Microsoft.Diagnostics.Tracing.TraceEventOpcode.DataCollectionStart;
        var final = Microsoft.Diagnostics.Tracing.TraceEventOpcode.DataCollectionStop;
        Assert.IsFalse(TraceAnalyzer.IdentityWasReused([(initial, 0), (final, 100)]));
        Assert.IsFalse(TraceAnalyzer.IdentityWasReused([(start, 5), (stop, 20)]));
        Assert.IsTrue(TraceAnalyzer.IdentityWasReused([(initial, 0), (stop, 20), (start, 30)]));
        Assert.IsTrue(TraceAnalyzer.IdentityWasReused([(stop, 20), (start, 30)]));
        Assert.IsTrue(TraceAnalyzer.IdentityWasReused([(start, 5), (start, 30)]));
        TraceAnalyzer.ImageRange[] stable = [new(100, 200, "gpu.sys"), new(100, 200, "GPU.sys")];
        Assert.AreEqual("GPU.sys", TraceAnalyzer.ResolveModule(stable, 150));
        Assert.AreEqual("Unknown", TraceAnalyzer.ResolveModule(stable, 200));
        Assert.AreEqual("Unknown", TraceAnalyzer.ResolveModule([.. stable, new(120, 180, "other.sys")], 150));
        Assert.AreEqual("Unknown", TraceAnalyzer.ResolveModule([.. stable, new(100, 200, "gpu.sys", true)], 150));
    }

    [TestMethod]
    public void Comparison_uses_captured_duration_and_includes_threads_beyond_top_ten()
    {
        var sessions = new[] { Session(180_000), Session(180_000), Session(30_000) };
        Assert.IsFalse(MeasurementService.CapturedDurationsMatch(sessions));
        Assert.IsTrue(MeasurementService.CapturedDurationsMatch([Session(180_000), Session(181_000)]));
        Assert.IsFalse(MeasurementService.CapturedDurationsMatch([Session(double.NaN)]));
        var report = new TraceReport
        {
            Threads = Enumerable.Range(0, 11).Select(index => new ThreadSchedulingMetrics($"thread-{index}", 10,
                TraceAnalyzer.Describe([index == 10 ? 1000 : 1], 1000), index, new Dictionary<int, double>())).ToList(),
            Interrupts = [new("dpc", "gpu.sys", 0, TraceAnalyzer.Describe([10], 1000)),
                new("dpc", "gpu.sys", 1, TraceAnalyzer.Describe([20], 1000))]
        };
        var metrics = MeasurementService.SessionMetrics(new MeasurementSession { Report = report });
        Assert.AreEqual(1000, metrics["target:worst_thread_ready_p99_us"]);
        Assert.AreEqual(55, metrics["target:migrations"]);
        Assert.AreEqual(20, metrics["interrupt:dpc:gpu.sys:worst_core_p99_us"]);

        static MeasurementSession Session(double duration) => new()
        {
            DurationSeconds = 180,
            Report = new TraceReport { Quality = new(duration, 1, 0, [], 100, true) }
        };
    }
}
