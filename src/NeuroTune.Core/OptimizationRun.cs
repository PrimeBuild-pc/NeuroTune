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

public enum OptimizationRunDecision { Undecided, Keep, Rollback, Declined }

public sealed record OptimizationRunTransition(
    DateTimeOffset AtUtc,
    OptimizationRunState From,
    OptimizationRunState To,
    string Reason);

public sealed class OptimizationRun
{
    public int SchemaVersion { get; init; } = 2;
    public InvestigationMode Mode { get; init; } = InvestigationMode.MeasuredOptimization;
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public OptimizationRunState State { get; set; } = OptimizationRunState.Draft;
    public TuningGoals Goals { get; init; } = new();
    public Dictionary<string, string> EvidenceFacts { get; set; } = [];
    public List<SupportingAttachmentInfo> SupportingAttachments { get; set; } = [];
    public DiagnosisResult? Diagnosis { get; set; }
    public List<PlannerAuditEntry> PlannerAudit { get; set; } = [];
    public string PlannerStopReason { get; set; } = "";
    public bool UsedLocalFallback { get; set; }
    public List<string> RequestedProbeIds { get; set; } = [];
    public List<string> ApprovedActionIds { get; set; } = [];
    public bool HighRiskConfirmed { get; set; }
    public List<Guid> BaselineSessionIds { get; set; } = [];
    public List<Guid> CandidateSessionIds { get; set; } = [];
    public List<Guid> DiagnosticSessionIds { get; set; } = [];
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
        (OptimizationRunState.Scanned or OptimizationRunState.Hypothesizing or OptimizationRunState.BaselinePending or OptimizationRunState.BaselineReady or OptimizationRunState.Approved, OptimizationRunState.Completed) => true,
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
    public static readonly string RunsDirectory = Path.Combine(JournalStorage.UserDirectory, "runs");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly JournalStorage _storage;

    public OptimizationRunService() : this(RunsDirectory, privileged: true) { }
    internal OptimizationRunService(string directory) : this(directory, privileged: false) { }

    private OptimizationRunService(string directory, bool privileged)
    {
        _storage = new(directory, privileged);
        _directory = _storage.DirectoryPath;
    }

    public OptimizationRun Create(SystemProfile profile, TuningGoals goals,
        IReadOnlyCollection<MeasurementSession>? baselineSessions = null, IReadOnlyList<SupportingAttachment>? attachments = null, bool imagesConfirmed = false,
        InvestigationMode mode = InvestigationMode.MeasuredOptimization)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(goals);
        goals.Validate();
        if (!Enum.IsDefined(mode) || mode == InvestigationMode.AuditOnly && baselineSessions?.Count > 0)
            throw new InvalidOperationException("Audit-only runs cannot contain measurements, and the investigation mode must be known.");
        baselineSessions = (baselineSessions ?? []).Where(session => session.Label == MeasurementLabel.Baseline &&
            session.State == MeasurementSessionState.Completed && session.Report?.Quality.IsValid == true).ToList();
        var supporting = NeuroTune.SupportingAttachments.Normalize(attachments, imagesConfirmed);
        var evidence = NeuroTune.SupportingAttachments.MergeEvidence(LlmClient.MergeEvidenceFacts(LlmClient.BuildEvidenceFacts(profile),
            MeasurementService.BuildNormalizedEvidence(baselineSessions)), supporting);
        var run = new OptimizationRun
        {
            Goals = goals,
            Mode = mode,
            EvidenceFacts = evidence.ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.Ordinal),
            SupportingAttachments = NeuroTune.SupportingAttachments.Describe(supporting),
            BaselineSessionIds = baselineSessions.Where(session => !session.SystemWide).Select(session => session.Id).Distinct().ToList(),
            DiagnosticSessionIds = baselineSessions.Where(session => session.SystemWide).Select(session => session.Id).Distinct().ToList(),
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
        run.EvidenceFacts = LlmClient.AppendInvestigationEvidence(run.EvidenceFacts, outcome.AdditionalEvidence).ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.Ordinal);
        run.Diagnosis = outcome.Diagnosis;
        if (AuditChecklist.Required(run.Mode, run.Goals)) AuditChecklist.Normalize(run.Diagnosis, run.EvidenceFacts);
        if (run.Mode == InvestigationMode.AuditOnly) LlmClient.MakeAuditOnly(run.Diagnosis);
        run.PlannerAudit = outcome.Audit.ToList();
        run.PlannerStopReason = outcome.StopReason;
        run.UsedLocalFallback = outcome.UsedLocalFallback;
        run.RequestedProbeIds = outcome.Audit.Where(entry => entry.Accepted && entry.Kind is "requestEvidence" or "requestInvestigation")
            .SelectMany(entry => entry.EvidenceIds).Distinct(StringComparer.Ordinal).ToList();
        Move(run, OptimizationRunState.ProposalReady, "Provider proposal passed local validation");
        if (run.Mode != InvestigationMode.AuditOnly)
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
            run.EvidenceFacts = LlmClient.AppendInvestigationEvidence(run.EvidenceFacts, outcome.AdditionalEvidence).ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.Ordinal);
            run.PlannerAudit = outcome.Audit.ToList();
            run.PlannerStopReason = outcome.StopReason;
            run.UsedLocalFallback = true;
        }
        run.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Save(run);
        return run;
    });

    public OptimizationRun Dismiss(Guid id) => WithLock(() =>
    {
        var run = LoadCore(id);
        if (OptimizationRunStateMachine.IsTerminal(run.State)) return run;
        if (run.OperationId is not null || run.RequiresRecovery || run.State is not (OptimizationRunState.Scanned or OptimizationRunState.Hypothesizing or OptimizationRunState.ProposalReady or OptimizationRunState.BaselinePending or OptimizationRunState.BaselineReady or OptimizationRunState.Approved))
            throw new InvalidOperationException("A run with an operation or pending recovery cannot be dismissed; use verified rollback/recovery.");
        run.Decision = OptimizationRunDecision.Declined;
        Move(run, OptimizationRunState.Completed, "Diagnosis dismissed without system changes");
        Save(run);
        return run;
    });

    public OptimizationRun AttachMeasurement(Guid id, MeasurementSession session) => WithLock(() =>
    {
        ArgumentNullException.ThrowIfNull(session);
        var run = LoadCore(id);
        if (run.Mode == InvestigationMode.AuditOnly || session.SystemWide || session.OptimizationRunId != id || session.State != MeasurementSessionState.Completed ||
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
            if (run.Mode == InvestigationMode.AuditOnly || run.Diagnosis is null)
                throw new InvalidOperationException("A measured optimization diagnosis is required before approval; audit-only runs cannot approve writes.");
            var actions = actionIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(catalog.Get).ToList();
            if (actions.Count == 0) throw new InvalidOperationException("Select at least one optimization capability.");
            OptimizationCatalog.ValidateSelection(actions);
            if (actions.Any(action => action.Risk == RiskLevel.High) && !highRiskConfirmed)
                throw new InvalidOperationException("High-risk capabilities require separate confirmation.");
            run.ApprovedActionIds = actions.Select(action => action.Id).ToList();
            run.HighRiskConfirmed = highRiskConfirmed;
        });

    public OptimizationRun BeginApply(Guid id, Guid operationId, string? bootIdAtApply = null) => Advance(id,
        OptimizationRunState.Approved, OptimizationRunState.Applying,
        "Started transactional capability apply", run =>
        {
            if (run.Mode == InvestigationMode.AuditOnly || operationId == Guid.Empty) throw new InvalidOperationException("Audit-only runs cannot apply, and the operation ID must be valid.");
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
        _storage.CheckLegacyJournals();
        var runs = new List<OptimizationRun>();
        foreach (var directory in _storage.Directories())
        {
            try { runs.Add(LoadPath(Path.Combine(directory, "run.json"))); }
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
        _storage.CheckLegacyJournals();
        var path = Path.Combine(_directory, id.ToString("D"), "run.json");
        _storage.CheckPath(path);
        if (!File.Exists(path)) throw new InvalidOperationException("The optimization run was not found.");
        return LoadPath(path);
    }

    private OptimizationRun LoadPath(string path)
    {
        try
        {
            var content = _storage.Read(path);
            using var json = JsonDocument.Parse(content);
            if (!json.RootElement.TryGetProperty("SchemaVersion", out _) || !json.RootElement.TryGetProperty("Id", out _) ||
                !json.RootElement.TryGetProperty("State", out _)) throw new JsonException("Missing required run fields.");
            if (json.RootElement.GetProperty("SchemaVersion").GetInt32() == 2 && !json.RootElement.TryGetProperty("Mode", out _))
                throw new JsonException("Missing required investigation mode.");
            var run = JsonSerializer.Deserialize<OptimizationRun>(content);
            if (run is null) throw new JsonException("The optimization run was empty.");
            run.DirectoryPath = Path.GetDirectoryName(path)!;
            if (!Guid.TryParseExact(Path.GetFileName(run.DirectoryPath), "D", out var directoryId) || run.Id != directoryId ||
                !Path.GetDirectoryName(run.DirectoryPath)!.Equals(_directory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The optimization run ID does not match its directory.");
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
        _storage.Write(Path.Combine(run.DirectoryPath, "run.json"), run, JsonOptions);
    }

    private static void Validate(OptimizationRun run)
    {
        if (run.SchemaVersion is not (1 or 2) || !Enum.IsDefined(run.Mode) || run.SchemaVersion == 1 && run.Mode != InvestigationMode.MeasuredOptimization || run.Id == Guid.Empty || !Enum.IsDefined(run.State) || !Enum.IsDefined(run.Decision) ||
            run.Goals is null || run.EvidenceFacts is null || run.RequestedProbeIds is null || run.ApprovedActionIds is null ||
            run.BaselineSessionIds is null || run.CandidateSessionIds is null || run.DiagnosticSessionIds is null ||
            run.Transitions is null || run.PlannerAudit is null || run.PlannerStopReason is null || run.BootIdAtApply is null ||
            run.OperationId == Guid.Empty || run.BaselineSessionIds.Concat(run.CandidateSessionIds).Concat(run.DiagnosticSessionIds).Contains(Guid.Empty))
            throw new InvalidOperationException("The optimization run schema or ID was invalid.");
        run.Goals.Validate();
        if (run.Diagnosis is not null) AuditChecklist.ValidatePersisted(run.Diagnosis, run.EvidenceFacts);
        if (run.Mode == InvestigationMode.AuditOnly &&
            (run.OperationId is not null || run.ApprovedActionIds.Count > 0 || run.HighRiskConfirmed ||
             run.BaselineSessionIds.Count + run.CandidateSessionIds.Count + run.DiagnosticSessionIds.Count > 0 || run.Comparison is not null ||
             run.Decision is not (OptimizationRunDecision.Undecided or OptimizationRunDecision.Declined) ||
             run.State is not (OptimizationRunState.Draft or OptimizationRunState.Scanned or OptimizationRunState.Hypothesizing or OptimizationRunState.ProposalReady or OptimizationRunState.Completed or OptimizationRunState.Failed) ||
             run.Transitions.Any(step => step.To is not (OptimizationRunState.Scanned or OptimizationRunState.Hypothesizing or OptimizationRunState.ProposalReady or OptimizationRunState.Completed or OptimizationRunState.Failed)) ||
             run.Diagnosis?.Recommendations.Any(item => item.Kind == PlanRecommendationKind.ExecutableAction) == true))
            throw new InvalidOperationException("Audit-only journals cannot contain optimization approval, measurements or writes.");
        for (var index = 0; index < run.Transitions.Count; index++)
        {
            var transition = run.Transitions[index];
            if (transition is null || transition.Reason is null || transition.Reason.Length is 0 or > 500 ||
                !OptimizationRunStateMachine.CanTransition(transition.From, transition.To) ||
                transition.From != (index == 0 ? OptimizationRunState.Draft : run.Transitions[index - 1].To))
                throw new InvalidOperationException("The run transition history is invalid.");
        }
        if (run.Transitions.Count > 0 && run.Transitions[^1].To != run.State)
            throw new InvalidOperationException("The run state does not match its transition history.");
        if (!LlmClient.MeasureEvidence(run.EvidenceFacts).FitsSinglePass)
            throw new InvalidOperationException("The optimization run evidence exceeded the single-pass limit.");
        if (run.SupportingAttachments is null || run.SupportingAttachments.Count > NeuroTune.SupportingAttachments.MaxFiles ||
            run.SupportingAttachments.Count(item => item?.Kind == "image") > NeuroTune.SupportingAttachments.MaxImages ||
            run.SupportingAttachments.Select(item => item?.Id).Distinct().Count() != run.SupportingAttachments.Count ||
            run.SupportingAttachments.Any(item => item is null || !Guid.TryParseExact(item.Id, "D", out _) || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 120 ||
                item.Kind is not ("report" or "image") || item.ContentType != (item.Kind == "image" ? "image/png" : "text/plain") ||
                item.Bytes is < 1 or > NeuroTune.SupportingAttachments.MaxImageBytes || item.Sha256 is null || item.Sha256.Length != 64 || item.Sha256.Any(character => !Uri.IsHexDigit(character)) ||
                !run.EvidenceFacts.ContainsKey($"support:{item.Id}:provenance")))
            throw new InvalidOperationException("Invalid persisted supporting attachment metadata.");
        if (run.State != OptimizationRunState.Draft && run.EvidenceFacts.Count == 0)
            throw new InvalidOperationException("The optimization run has no sanitized evidence.");
        var knownProbeIds = run.EvidenceFacts.Keys.ToHashSet(StringComparer.Ordinal);
        if (run.RequestedProbeIds.Any(probe => !knownProbeIds.Contains(probe)))
            throw new InvalidOperationException("The optimization run referenced an unknown probe.");
        if (run.RequestedProbeIds.Count > PlannerProtocol.MaxTurns * PlannerProtocol.MaxEvidencePerTurn || run.ApprovedActionIds.Count > 100 ||
            run.BaselineSessionIds.Count > 20 || run.CandidateSessionIds.Count > 20 || run.DiagnosticSessionIds.Count > 20 || run.Transitions.Count > 200 ||
            run.PlannerAudit.Count > PlannerProtocol.MaxTurns || run.PlannerStopReason.Length > 500 ||
            run.PlannerAudit.Any(entry => entry.Reason.Length > 500 || entry.EvidenceIds.Count > PlannerProtocol.MaxEvidencePerTurn ||
                entry.EvidenceIds.Any(id => id.Length is 0 or > 500) ||
                entry.Accepted && entry.EvidenceIds.Any(id => !run.EvidenceFacts.ContainsKey(id))) ||
            run.BaselineSessionIds.Count != run.BaselineSessionIds.Distinct().Count() ||
            run.CandidateSessionIds.Count != run.CandidateSessionIds.Distinct().Count() ||
            run.BaselineSessionIds.Intersect(run.CandidateSessionIds).Any() ||
            run.ApprovedActionIds.Count != run.ApprovedActionIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() ||
            run.DiagnosticSessionIds.Count != run.DiagnosticSessionIds.Distinct().Count() ||
            run.DiagnosticSessionIds.Intersect(run.BaselineSessionIds.Concat(run.CandidateSessionIds)).Any() ||
            run.RequestedProbeIds.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 500) ||
            run.ApprovedActionIds.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 120) ||
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
