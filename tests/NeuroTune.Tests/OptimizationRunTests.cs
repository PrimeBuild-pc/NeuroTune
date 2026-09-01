namespace NeuroTune.Tests;

[TestClass]
public sealed class OptimizationRunTests
{
    [TestMethod]
    public void Run_persists_the_closed_loop_and_rejects_repeating_a_write()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neurotune-run-{Guid.NewGuid():N}");
        try
        {
            var service = new OptimizationRunService(directory);
            var run = service.Create(Profile(), new TuningGoals { Games = ["Example Game"] });
            service.BeginDiagnosis(run.Id);
            run = service.RecordDiagnosis(run.Id, Outcome());
            Assert.AreEqual(OptimizationRunState.BaselinePending, run.State);

            var baselines = Enumerable.Range(0, 3).Select(_ => Session(run.Id, MeasurementLabel.Baseline)).ToList();
            foreach (var baseline in baselines) run = service.AttachMeasurement(run.Id, baseline);
            Assert.AreEqual(OptimizationRunState.BaselineReady, run.State);
            service.Approve(run.Id, ["gaming.game-mode"], false, new OptimizationCatalog());
            var operationId = Guid.NewGuid();
            run = service.BeginApply(run.Id, operationId);

            var reloaded = new OptimizationRunService(directory).Load(run.Id);
            Assert.AreEqual(OptimizationRunState.Applying, reloaded.State);
            Assert.IsTrue(reloaded.RequiresRecovery);
            Assert.AreEqual(operationId, reloaded.OperationId);
            Assert.AreEqual("CPU", reloaded.EvidenceFacts["system:cpu"]);
            Assert.ThrowsExactly<InvalidOperationException>(() => service.BeginApply(run.Id, Guid.NewGuid()));

            service.RecordApplyCompleted(run.Id, false);
            var candidates = Enumerable.Range(0, 3).Select(_ => Session(run.Id, MeasurementLabel.Candidate)).ToList();
            foreach (var candidate in candidates) service.AttachMeasurement(run.Id, candidate);
            var baselineIds = baselines.Select(item => item.Id).ToList();
            var candidateIds = candidates.Select(item => item.Id).ToList();
            Assert.ThrowsExactly<InvalidOperationException>(() => service.RecordComparison(run.Id, new MeasurementComparison
            {
                Id = Guid.NewGuid(),
                Level = ComparisonLevel.Exploratory,
                BaselineSessionIds = baselineIds,
                CandidateSessionIds = candidateIds,
                Metrics = [new("comparison:test", 10, 9, -10, ComparisonOutcome.Improvement)]
            }));
            var comparison = new MeasurementComparison
            {
                Id = Guid.NewGuid(),
                Level = ComparisonLevel.Repeated,
                BaselineSessionIds = baselineIds,
                CandidateSessionIds = candidateIds,
                Metrics = [new("comparison:test", 10, 9, -10, ComparisonOutcome.Improvement)]
            };
            service.RecordComparison(run.Id, comparison);
            run = service.Keep(run.Id);

            Assert.AreEqual(OptimizationRunState.Completed, run.State);
            Assert.AreEqual(OptimizationRunDecision.Keep, run.Decision);
            Assert.AreEqual(OptimizationRunState.RollingBack, service.BeginRollback(run.Id).State);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Run_rejects_skipping_baseline_and_hides_no_pending_write_as_failed()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neurotune-run-{Guid.NewGuid():N}");
        try
        {
            var service = new OptimizationRunService(directory);
            var run = service.Create(Profile(), new TuningGoals());
            service.BeginDiagnosis(run.Id);
            run = service.RecordDiagnosis(run.Id, Outcome());

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                service.Approve(run.Id, ["gaming.game-mode"], false, new OptimizationCatalog()));
            Assert.IsFalse(OptimizationRunStateMachine.CanTransition(
                OptimizationRunState.Applying, OptimizationRunState.Completed));
            Assert.IsTrue(OptimizationRunStateMachine.CanTransition(
                OptimizationRunState.Applying, OptimizationRunState.RecoveryRequired));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Provider_failure_remains_retryable_without_repeating_the_diagnosis_transition()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neurotune-run-{Guid.NewGuid():N}");
        try
        {
            var service = new OptimizationRunService(directory);
            var run = service.Create(Profile(), new TuningGoals());
            service.BeginDiagnosis(run.Id);
            run = service.RecordDiagnosisAttemptFailure(run.Id, "temporary provider failure");

            Assert.AreEqual(OptimizationRunState.Hypothesizing, run.State);
            Assert.AreEqual("temporary provider failure", run.Error);
            var retried = service.BeginDiagnosis(run.Id);
            Assert.HasCount(run.Transitions.Count, retried.Transitions);
            Assert.AreEqual(OptimizationRunState.BaselinePending, service.RecordDiagnosis(run.Id, Outcome()).State);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Restart_resume_requires_a_different_verified_boot_id()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neurotune-run-{Guid.NewGuid():N}");
        try
        {
            var service = new OptimizationRunService(directory);
            var run = service.Create(Profile(), new TuningGoals());
            service.BeginDiagnosis(run.Id);
            service.RecordDiagnosis(run.Id, Outcome());
            service.AttachMeasurement(run.Id, Session(run.Id, MeasurementLabel.Baseline));
            service.Approve(run.Id, ["gaming.hags"], false, new OptimizationCatalog());
            service.BeginApply(run.Id, Guid.NewGuid(), "Unavailable");
            service.RecordApplyCompleted(run.Id, true);

            Assert.ThrowsExactly<InvalidOperationException>(() => service.ResumeAfterRestart(run.Id, "new-boot"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Run_recovery_handles_prewrite_failure_and_interrupted_rollback()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neurotune-run-{Guid.NewGuid():N}");
        try
        {
            var service = new OptimizationRunService(directory);
            var failed = ReadyToApply(service);
            service.BeginApply(failed.Id, Guid.NewGuid(), "boot-a");
            failed = service.RecordApplyFailure(failed.Id, null, "availability failed");
            Assert.AreEqual(OptimizationRunState.Failed, failed.State);

            var rolling = ReadyToApply(service);
            service.BeginApply(rolling.Id, Guid.NewGuid(), "boot-a");
            service.RecordApplyCompleted(rolling.Id, false);
            var first = service.BeginRollback(rolling.Id);
            var resumed = service.BeginRollback(rolling.Id);
            Assert.AreEqual(OptimizationRunState.RollingBack, first.State);
            Assert.HasCount(first.Transitions.Count, resumed.Transitions);
            Assert.AreEqual(OptimizationRunState.Completed, service.RecordRollbackCompleted(rolling.Id).State);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Run_reconciles_a_completed_linked_measurement_and_rejects_overlap()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neurotune-run-{Guid.NewGuid():N}");
        try
        {
            var service = new OptimizationRunService(directory);
            var run = service.Create(Profile(), new TuningGoals());
            Assert.ThrowsExactly<InvalidOperationException>(() => service.Create(Profile(), new TuningGoals()));
            service.BeginDiagnosis(run.Id);
            service.RecordDiagnosis(run.Id, Outcome());

            var session = Session(run.Id, MeasurementLabel.Baseline);
            run = service.ReconcileMeasurements(run.Id, [session]);
            Assert.AreEqual(OptimizationRunState.BaselineReady, run.State);
            Assert.AreEqual(session.Id, run.BaselineSessionIds.Single());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Run_list_skips_a_corrupt_journal_but_strict_operations_still_fail()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neurotune-run-{Guid.NewGuid():N}");
        try
        {
            var runId = Guid.NewGuid();
            var runDirectory = Path.Combine(directory, runId.ToString("D"));
            Directory.CreateDirectory(runDirectory);
            File.WriteAllText(Path.Combine(runDirectory, "run.json"), "not-json");
            var service = new OptimizationRunService(directory);
            Assert.IsEmpty(service.List());
            Assert.ThrowsExactly<InvalidOperationException>(() => service.Load(runId));
            Assert.ThrowsExactly<InvalidOperationException>(() => service.Create(Profile(), new TuningGoals()));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Run_uses_only_valid_baselines_and_releases_failed_run_measurements()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neurotune-run-{Guid.NewGuid():N}");
        try
        {
            var service = new OptimizationRunService(directory);
            var provisionalRunId = Guid.NewGuid();
            var baseline = Session(provisionalRunId, MeasurementLabel.Baseline);
            var candidate = Session(provisionalRunId, MeasurementLabel.Candidate);
            var run = service.Create(Profile(), new TuningGoals(), [baseline, candidate]);

            Assert.AreEqual(baseline.Id, run.BaselineSessionIds.Single());
            Assert.DoesNotContain(candidate.Id, run.CandidateSessionIds);
            service.BeginDiagnosis(run.Id);
            service.RecordDiagnosisFailure(run.Id, "provider unavailable");
            Assert.IsFalse(service.IsMeasurementReferenced(baseline.Id));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Run_history_remains_loadable_when_an_approved_dynamic_action_is_no_longer_registered()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neurotune-run-{Guid.NewGuid():N}");
        try
        {
            var service = new OptimizationRunService(directory);
            var run = ReadyToApply(service);
            var path = Path.Combine(directory, run.Id.ToString("D"), "run.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("gaming.game-mode", "gaming.gpu-deadbeef.high", StringComparison.Ordinal));

            Assert.AreEqual("gaming.gpu-deadbeef.high", service.List().Single().ApprovedActionIds.Single());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static OptimizationRun ReadyToApply(OptimizationRunService service)
    {
        var run = service.Create(Profile(), new TuningGoals());
        service.BeginDiagnosis(run.Id);
        service.RecordDiagnosis(run.Id, Outcome());
        service.AttachMeasurement(run.Id, Session(run.Id, MeasurementLabel.Baseline));
        return service.Approve(run.Id, ["gaming.game-mode"], false, new OptimizationCatalog());
    }

    private static SystemProfile Profile() => new() { Cpu = "CPU" };

    private static DiagnosisResult Diagnosis() => new()
    {
        Summary = "Evidence checked",
        ConsentQuestion = "Apply selected actions?"
    };

    private static PlannerDiagnosisOutcome Outcome() => new(Diagnosis(), [], "diagnosis-completed", false);

    private static MeasurementSession Session(Guid runId, MeasurementLabel label) => new()
    {
        Id = Guid.NewGuid(),
        OptimizationRunId = runId,
        Label = label,
        State = MeasurementSessionState.Completed,
        Report = new TraceReport { Quality = new(180_000, 1, 0, [], 100, true) }
    };
}
