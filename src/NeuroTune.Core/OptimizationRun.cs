using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuroTune;

public enum OptimizationRunState
{
    Draft,
    Scanned,
    Hypothesizing,
    ProposalReady,
    BaselinePending,
    BaselineReady,
    Approved,
    Applying,
    RestartPending,
    CandidatePending,
    Evaluating,
    DecisionPending,
    RollingBack,
    RecoveryRequired,
    Completed,
    Failed
}

public enum OptimizationRunDecision { Undecided, Keep, Rollback }

public sealed record OptimizationRunTransition(
    DateTimeOffset AtUtc,
    OptimizationRunState From,
    OptimizationRunState To,
    string Reason);

public sealed class OptimizationRun
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public OptimizationRunState State { get; set; } = OptimizationRunState.Draft;
    public TuningGoals Goals { get; init; } = new();
    public Dictionary<string, string> EvidenceFacts { get; set; } = [];
    public DiagnosisResult? Diagnosis { get; set; }
    public List<PlannerAuditEntry> PlannerAudit { get; set; } = [];
    public string PlannerStopReason { get; set; } = "";
    public bool UsedLocalFallback { get; set; }
    public List<string> RequestedProbeIds { get; set; } = [];
    public List<string> ApprovedActionIds { get; set; } = [];
    public bool HighRiskConfirmed { get; set; }
    public List<Guid> BaselineSessionIds { get; set; } = [];
    public List<Guid> CandidateSessionIds { get; set; } = [];
    public Guid? OperationId { get; set; }
    public string BootIdAtApply { get; set; } = "";
    public MeasurementComparison? Comparison { get; set; }
    public OptimizationRunDecision Decision { get; set; }
    public string? Error { get; set; }
    public List<OptimizationRunTransition> Transitions { get; set; } = [];

    [JsonIgnore]
    public string DirectoryPath { get; set; } = "";

    public bool RequiresRecovery => State is OptimizationRunState.Applying or OptimizationRunState.RollingBack or OptimizationRunState.RecoveryRequired;
}

public static class OptimizationRunStateMachine
{
    public static bool IsTerminal(OptimizationRunState state) =>
        state is OptimizationRunState.Completed or OptimizationRunState.Failed;

    public static bool CanTransition(OptimizationRunState from, OptimizationRunState to) => (from, to) switch
    {
        (OptimizationRunState.Draft, OptimizationRunState.Scanned) => true,
        (OptimizationRunState.Scanned, OptimizationRunState.Hypothesizing) => true,
        (OptimizationRunState.Hypothesizing, OptimizationRunState.ProposalReady or OptimizationRunState.Failed) => true,
        (OptimizationRunState.ProposalReady, OptimizationRunState.BaselinePending or OptimizationRunState.BaselineReady or OptimizationRunState.Completed or OptimizationRunState.Failed) => true,
        (OptimizationRunState.BaselinePending, OptimizationRunState.BaselineReady or OptimizationRunState.Failed) => true,
        (OptimizationRunState.BaselineReady, OptimizationRunState.Approved or OptimizationRunState.Failed) => true,
        (OptimizationRunState.Approved, OptimizationRunState.Applying or OptimizationRunState.Failed) => true,
        (OptimizationRunState.Applying, OptimizationRunState.RestartPending or OptimizationRunState.CandidatePending or OptimizationRunState.RollingBack or OptimizationRunState.RecoveryRequired or OptimizationRunState.Failed) => true,
        (OptimizationRunState.RestartPending, OptimizationRunState.CandidatePending or OptimizationRunState.RollingBack or OptimizationRunState.RecoveryRequired) => true,
        (OptimizationRunState.CandidatePending, OptimizationRunState.Evaluating or OptimizationRunState.RollingBack or OptimizationRunState.RecoveryRequired) => true,
        (OptimizationRunState.Evaluating, OptimizationRunState.DecisionPending or OptimizationRunState.RollingBack or OptimizationRunState.RecoveryRequired) => true,
        (OptimizationRunState.DecisionPending, OptimizationRunState.Completed or OptimizationRunState.RollingBack or OptimizationRunState.RecoveryRequired) => true,
        (OptimizationRunState.Completed, OptimizationRunState.RollingBack) => true,
        (OptimizationRunState.RollingBack, OptimizationRunState.Completed or OptimizationRunState.RecoveryRequired) => true,
        (OptimizationRunState.RecoveryRequired, OptimizationRunState.RestartPending or OptimizationRunState.CandidatePending or OptimizationRunState.RollingBack or OptimizationRunState.Completed) => true,
        _ => false
    };
}

public sealed class OptimizationRunService
{
    public static readonly string RunsDirectory = Path.Combine(SettingsService.DataDirectory, "runs");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public OptimizationRunService(string? directory = null) => _directory = directory ?? RunsDirectory;

    public OptimizationRun Create(SystemProfile profile, TuningGoals goals,
        IReadOnlyCollection<MeasurementSession>? baselineSessions = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(goals);
        goals.Validate();
        baselineSessions = (baselineSessions ?? []).Where(session => session.Label == MeasurementLabel.Baseline &&
            session.State == MeasurementSessionState.Completed && session.Report?.Quality.IsValid == true).ToList();
        var evidence = LlmClient.MergeEvidenceFacts(LlmClient.BuildEvidenceFacts(profile),
            MeasurementService.BuildNormalizedEvidence(baselineSessions));
        var run = new OptimizationRun
        {
            Goals = goals,
            EvidenceFacts = evidence.ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.Ordinal),
            BaselineSessionIds = baselineSessions.Select(session => session.Id).Distinct().ToList(),
            State = OptimizationRunState.Scanned
        };
        run.Transitions.Add(new(run.UpdatedAtUtc, OptimizationRunState.Draft,
            OptimizationRunState.Scanned, "Captured sanitized local scan evidence"));
        WithLock(() =>
        {
            if (ListCore(strict: true).Any(existing => !OptimizationRunStateMachine.IsTerminal(existing.State)))
                throw new InvalidOperationException("Finish or recover the active optimization run before creating another one.");
            Save(run);
        });
        return run;
    }

    public OptimizationRun BeginDiagnosis(Guid id) => WithLock(() =>
    {
        var run = LoadCore(id);
        if (run.State == OptimizationRunState.Hypothesizing) return run;
        if (run.State != OptimizationRunState.Scanned)
            throw new InvalidOperationException("This optimization run is not ready for provider diagnosis.");
        Move(run, OptimizationRunState.Hypothesizing, "Started bounded provider diagnosis");
        Save(run);
        return run;
    });

    public OptimizationRun RecordDiagnosis(Guid id, PlannerDiagnosisOutcome outcome) => WithLock(() =>
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var run = LoadExpected(id, OptimizationRunState.Hypothesizing);
        run.Diagnosis = outcome.Diagnosis;
        run.PlannerAudit = outcome.Audit.ToList();
        run.PlannerStopReason = outcome.StopReason;
        run.UsedLocalFallback = outcome.UsedLocalFallback;
        run.RequestedProbeIds = outcome.Audit.Where(entry => entry.Accepted && entry.Kind == "requestEvidence")
            .SelectMany(entry => entry.EvidenceIds).Distinct(StringComparer.Ordinal).ToList();
        Move(run, OptimizationRunState.ProposalReady, "Provider proposal passed local validation");
        Move(run, run.BaselineSessionIds.Count > 0 ? OptimizationRunState.BaselineReady : OptimizationRunState.BaselinePending,
            run.BaselineSessionIds.Count > 0 ? "Existing quality-valid Baseline linked" : "A quality-valid Baseline is required before approval");
        Save(run);
        return run;
    });

    public OptimizationRun RecordDiagnosisFailure(Guid id, string error) => Advance(id,
        OptimizationRunState.Hypothesizing, OptimizationRunState.Failed, "Provider diagnosis failed",
        run => run.Error = BoundError(error));

    public OptimizationRun RecordDiagnosisAttemptFailure(Guid id, string error, PlannerDiagnosisOutcome? outcome = null) => WithLock(() =>
    {
        var run = LoadExpected(id, OptimizationRunState.Hypothesizing);
        run.Error = BoundError(error);
        if (outcome is not null)
        {
            run.PlannerAudit = outcome.Audit.ToList();
            run.PlannerStopReason = outcome.StopReason;
            run.UsedLocalFallback = true;
        }
        run.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Save(run);
        return run;
    });

    public OptimizationRun AttachMeasurement(Guid id, MeasurementSession session) => WithLock(() =>
    {
        ArgumentNullException.ThrowIfNull(session);
        var run = LoadCore(id);
        if (session.OptimizationRunId != id || session.State != MeasurementSessionState.Completed ||
            session.Report?.Quality.IsValid != true)
            throw new InvalidOperationException("The linked measurement is not a completed, quality-valid session from this optimization run.");
        if (session.Label == MeasurementLabel.Baseline)
        {
            if (run.State is not (OptimizationRunState.BaselinePending or OptimizationRunState.BaselineReady))
                throw new InvalidOperationException("This optimization run is not accepting Baseline measurements.");
            if (!run.BaselineSessionIds.Contains(session.Id)) run.BaselineSessionIds.Add(session.Id);
            if (run.State == OptimizationRunState.BaselinePending)
                Move(run, OptimizationRunState.BaselineReady, "Quality-valid Baseline measurement linked");
        }
        else
        {
            if (run.State != OptimizationRunState.CandidatePending)
                throw new InvalidOperationException("This optimization run is not accepting Candidate measurements.");
            if (!run.CandidateSessionIds.Contains(session.Id)) run.CandidateSessionIds.Add(session.Id);
        }
        run.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Save(run);
        return run;
    });

    public OptimizationRun Approve(Guid id, IEnumerable<string> actionIds, bool highRiskConfirmed,
        OptimizationCatalog catalog) => Advance(id, OptimizationRunState.BaselineReady,
        OptimizationRunState.Approved, "User approved selected capabilities", run =>
        {
            if (run.Diagnosis is null)
                throw new InvalidOperationException("A locally validated diagnosis is required before approval.");
            var actions = actionIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(catalog.Get).ToList();
            if (actions.Count == 0) throw new InvalidOperationException("Select at least one optimization capability.");
            if (actions.Any(action => action.Risk == RiskLevel.High) && !highRiskConfirmed)
                throw new InvalidOperationException("High-risk capabilities require separate confirmation.");
            run.ApprovedActionIds = actions.Select(action => action.Id).ToList();
            run.HighRiskConfirmed = highRiskConfirmed;
        });

    public OptimizationRun BeginApply(Guid id, Guid operationId, string? bootIdAtApply = null) => Advance(id,
        OptimizationRunState.Approved, OptimizationRunState.Applying,
        "Started transactional capability apply", run =>
        {
            if (operationId == Guid.Empty) throw new InvalidOperationException("The operation ID was invalid.");
            run.OperationId = operationId;
            run.BootIdAtApply = bootIdAtApply ?? CurrentBootId();
        });

    public OptimizationRun RecordApplyCompleted(Guid id, bool restartRequired) => Advance(id,
        OptimizationRunState.Applying,
        restartRequired ? OptimizationRunState.RestartPending : OptimizationRunState.CandidatePending,
        restartRequired ? "Applied capabilities require a Windows restart" : "Applied capabilities are ready for Candidate measurement");

    public OptimizationRun RecordApplyFailure(Guid id, OperationManifest? manifest, string error)
    {
        if (manifest is null || manifest.Actions.All(action => !action.Attempted && !action.Applied) ||
            !manifest.HasPendingRollback && manifest.Status.Contains("rollback completed", StringComparison.OrdinalIgnoreCase))
            return Advance(id, OptimizationRunState.Applying, OptimizationRunState.Failed,
                manifest is null ? "Capability apply stopped before an operation journal was created" :
                manifest.Actions.Count == 0 ? "Capability apply stopped before the first system write" :
                "Capability apply failed and automatic rollback completed",
                run => run.Error = BoundError(error));
        return RequireRecovery(id, error);
    }

    public OptimizationRun RequireRecovery(Guid id, string error) => WithLock(() =>
    {
        var run = LoadCore(id);
        if (run.State == OptimizationRunState.RecoveryRequired)
        {
            run.Error = BoundError(error);
            run.UpdatedAtUtc = DateTimeOffset.UtcNow;
            Save(run);
            return run;
        }
        if (!OptimizationRunStateMachine.CanTransition(run.State, OptimizationRunState.RecoveryRequired))
            throw new InvalidOperationException("This optimization run cannot enter recovery from its current state.");
        run.Error = BoundError(error);
        Move(run, OptimizationRunState.RecoveryRequired, "A write may be incomplete; only reconciliation or rollback is allowed");
        Save(run);
        return run;
    });

    public OptimizationRun RecordComparison(Guid id, MeasurementComparison comparison) => WithLock(() =>
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var run = LoadExpected(id, OptimizationRunState.CandidatePending);
        if (comparison.Level != ComparisonLevel.Repeated || comparison.RejectionReasons.Count > 0 || comparison.Metrics.Count == 0 ||
            !SameIds(comparison.BaselineSessionIds, run.BaselineSessionIds) ||
            !SameIds(comparison.CandidateSessionIds, run.CandidateSessionIds))
            throw new InvalidOperationException("A quality-valid repeated 3+3 comparison matching this optimization run is required.");
        run.Comparison = comparison;
        Move(run, OptimizationRunState.Evaluating, "Valid Baseline/Candidate comparison recorded");
        Move(run, OptimizationRunState.DecisionPending, "Comparison is ready for an explicit Keep or Rollback decision");
        Save(run);
        return run;
    });

    public OptimizationRun Keep(Guid id) => Advance(id, OptimizationRunState.DecisionPending,
        OptimizationRunState.Completed, "User kept the measured candidate", run => run.Decision = OptimizationRunDecision.Keep);

    public OptimizationRun BeginRollback(Guid id) => WithLock(() =>
    {
        var run = LoadCore(id);
        if (run.State == OptimizationRunState.RollingBack) return run;
        if (run.State == OptimizationRunState.Completed)
        {
            if (run.Decision != OptimizationRunDecision.Keep)
                throw new InvalidOperationException("Only a kept optimization run can be reverted after completion.");
            if (ListCore(strict: true).Any(other => other.Id != run.Id && !OptimizationRunStateMachine.IsTerminal(other.State)))
                throw new InvalidOperationException("Finish or recover the active optimization run before reverting a kept run.");
        }
        if (!OptimizationRunStateMachine.CanTransition(run.State, OptimizationRunState.RollingBack))
            throw new InvalidOperationException("This optimization run cannot roll back from its current state.");
        run.Decision = OptimizationRunDecision.Rollback;
        Move(run, OptimizationRunState.RollingBack, "User requested rollback");
        Save(run);
        return run;
    });

    public OptimizationRun RecordRollbackCompleted(Guid id) => Advance(id,
        OptimizationRunState.RollingBack, OptimizationRunState.Completed, "Rollback completed and verified",
        run => run.Decision = OptimizationRunDecision.Rollback);

    public OptimizationRun Load(Guid id) => WithLock(() => LoadCore(id));

    public OptimizationRun ResumeAfterRestart(Guid id, string? currentBootId = null)
    {
        var run = Load(id);
        var currentBoot = currentBootId ?? CurrentBootId();
        if (run.State != OptimizationRunState.RestartPending)
            throw new InvalidOperationException("The optimization run is not waiting for a restart.");
        if (run.BootIdAtApply.Length == 0 || run.BootIdAtApply == "Unavailable" ||
            currentBoot == "Unavailable" || currentBoot == run.BootIdAtApply)
            throw new InvalidOperationException("A Windows restart has not been verified for this optimization run.");
        return Advance(id, OptimizationRunState.RestartPending, OptimizationRunState.CandidatePending,
            "Verified Windows restart; candidate measurement is ready");
    }

    public bool IsMeasurementReferenced(Guid sessionId) => WithLock(() => ListCore(strict: true).Any(run =>
        run.State != OptimizationRunState.Failed &&
        (run.BaselineSessionIds.Contains(sessionId) || run.CandidateSessionIds.Contains(sessionId))));

    public OptimizationRun ReconcileMeasurements(Guid id, IEnumerable<MeasurementSession> sessions)
    {
        var run = Load(id);
        foreach (var session in sessions.Where(session => session.OptimizationRunId == id &&
            session.State == MeasurementSessionState.Completed && session.Report?.Quality.IsValid == true))
        {
            if (session.Label == MeasurementLabel.Baseline &&
                run.State is OptimizationRunState.BaselinePending or OptimizationRunState.BaselineReady ||
                session.Label == MeasurementLabel.Candidate && run.State == OptimizationRunState.CandidatePending)
                run = AttachMeasurement(id, session);
        }
        return run;
    }

    public IReadOnlyList<OptimizationRun> List() => WithLock(() => ListCore());

    private IReadOnlyList<OptimizationRun> ListCore(bool strict = false)
    {
        if (!Directory.Exists(_directory)) return [];
        var runs = new List<OptimizationRun>();
        foreach (var path in Directory.GetFiles(_directory, "run.json", SearchOption.AllDirectories))
        {
            try { runs.Add(LoadPath(path)); }
            catch (InvalidOperationException exception) when (!strict)
            {
                Console.Error.WriteLine(exception.Message);
            }
        }
        return runs.OrderByDescending(run => run.UpdatedAtUtc).ToList();
    }

    private OptimizationRun Advance(Guid id, OptimizationRunState expected, OptimizationRunState next,
        string reason, Action<OptimizationRun>? update = null) => WithLock(() =>
    {
        var run = LoadExpected(id, expected);
        update?.Invoke(run);
        Move(run, next, reason);
        Save(run);
        return run;
    });

    private static void Move(OptimizationRun run, OptimizationRunState next, string reason)
    {
        if (!OptimizationRunStateMachine.CanTransition(run.State, next))
            throw new InvalidOperationException($"Invalid optimization run transition: {run.State} -> {next}.");
        reason = reason?.Trim() ?? "";
        if (reason.Length is 0 or > 500) throw new InvalidOperationException("The optimization run transition reason was invalid.");
        var previous = run.State;
        run.State = next;
        run.UpdatedAtUtc = DateTimeOffset.UtcNow;
        run.Transitions.Add(new(run.UpdatedAtUtc, previous, next, reason));
    }

    private OptimizationRun LoadExpected(Guid id, OptimizationRunState expected)
    {
        var run = LoadCore(id);
        if (run.State != expected)
            throw new InvalidOperationException($"Optimization run {id} is {run.State}, not {expected}; the step will not be repeated.");
        return run;
    }

    private static string BoundError(string? error)
    {
        error = error?.Trim() ?? "Unknown error";
        return error.Length <= 2_000 ? error : error[..2_000];
    }

    private static bool SameIds(IEnumerable<Guid> left, IEnumerable<Guid> right) =>
        left.ToHashSet().SetEquals(right);

    private OptimizationRun LoadCore(Guid id)
    {
        if (id == Guid.Empty) throw new InvalidOperationException("The optimization run ID was invalid.");
        var path = Path.Combine(_directory, id.ToString("D"), "run.json");
        if (!File.Exists(path)) throw new InvalidOperationException("The optimization run was not found.");
        return LoadPath(path);
    }

    private OptimizationRun LoadPath(string path)
    {
        try
        {
            var run = JsonSerializer.Deserialize<OptimizationRun>(File.ReadAllText(path));
            if (run is null) throw new JsonException("The optimization run was empty.");
            run.DirectoryPath = Path.GetDirectoryName(path)!;
            Validate(run);
            return run;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"The optimization run journal is corrupt: {path}", exception);
        }
    }

    private void Save(OptimizationRun run)
    {
        Validate(run);
        run.DirectoryPath = Path.Combine(_directory, run.Id.ToString("D"));
        Directory.CreateDirectory(run.DirectoryPath);
        var path = Path.Combine(run.DirectoryPath, "run.json");
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, run, JsonOptions);
            stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    private static void Validate(OptimizationRun run)
    {
        if (run.SchemaVersion != 1 || run.Id == Guid.Empty)
            throw new InvalidOperationException("The optimization run schema or ID was invalid.");
        run.Goals.Validate();
        if (!LlmClient.MeasureEvidence(run.EvidenceFacts).FitsSinglePass)
            throw new InvalidOperationException("The optimization run evidence exceeded the single-pass limit.");
        if (run.State != OptimizationRunState.Draft && run.EvidenceFacts.Count == 0)
            throw new InvalidOperationException("The optimization run has no sanitized evidence.");
        var knownProbeIds = run.EvidenceFacts.Keys.ToHashSet(StringComparer.Ordinal);
        if (run.RequestedProbeIds.Any(probe => !knownProbeIds.Contains(probe)))
            throw new InvalidOperationException("The optimization run referenced an unknown probe.");
        if (run.RequestedProbeIds.Count > PlannerProtocol.MaxTurns * PlannerProtocol.MaxEvidencePerTurn || run.ApprovedActionIds.Count > 100 ||
            run.BaselineSessionIds.Count > 20 || run.CandidateSessionIds.Count > 20 || run.Transitions.Count > 200 ||
            run.PlannerAudit.Count > PlannerProtocol.MaxTurns || run.PlannerStopReason.Length > 500 ||
            run.PlannerAudit.Any(entry => entry.Reason.Length > 500 || entry.EvidenceIds.Count > PlannerProtocol.MaxEvidencePerTurn ||
                entry.EvidenceIds.Any(id => id.Length is 0 or > 500) ||
                entry.Accepted && entry.EvidenceIds.Any(id => !run.EvidenceFacts.ContainsKey(id))) ||
            run.BaselineSessionIds.Count != run.BaselineSessionIds.Distinct().Count() ||
            run.CandidateSessionIds.Count != run.CandidateSessionIds.Distinct().Count() ||
            run.BaselineSessionIds.Intersect(run.CandidateSessionIds).Any() ||
            run.ApprovedActionIds.Count != run.ApprovedActionIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() ||
            run.RequestedProbeIds.Concat(run.ApprovedActionIds).Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 120) ||
            run.Error?.Length > 2_000 || run.BootIdAtApply.Length > 100)
            throw new InvalidOperationException("The optimization run contained invalid or excessive data.");
    }

    public static string CurrentBootId() => SystemProfiler.Query(
        "SELECT LastBootUpTime FROM Win32_OperatingSystem",
        row => row["LastBootUpTime"]?.ToString() ?? "").FirstOrDefault(value => value.Length > 0) ?? "Unavailable";

    private static T WithLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, @"Global\NeuroTuneOptimizationRuns");
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Timed out waiting for the optimization-run journal lock.");
        }
        catch (AbandonedMutexException) { }
        try { return action(); }
        finally { mutex.ReleaseMutex(); }
    }

    private static void WithLock(Action action) => WithLock(() => { action(); return true; });
}
