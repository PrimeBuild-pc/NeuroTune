namespace NeuroTune;

public enum MeasurementLabel { Baseline, Candidate }
public enum MeasurementSessionState { Prepared, Recording, Captured, Analyzing, Completed, Cancelled, Failed }
public enum ComparisonLevel { Exploratory, Repeated }
public enum ComparisonOutcome { Improvement, Regression, Inconclusive }
public enum ComparisonDecision { InsufficientEvidence, Keep, Rollback }

public sealed record MeasurementWorkload(int ProcessId, string Name, DateTimeOffset StartTimeUtc, string Description);

public sealed class MeasurementSession
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; }
    public Guid? OptimizationRunId { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public DateTimeOffset ProcessStartTimeUtc { get; init; }
    public MeasurementLabel Label { get; init; }
    public int DurationSeconds { get; init; }
    public bool KeepRawTrace { get; init; }
    public MeasurementSessionState State { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? RecordingStartedAtUtc { get; set; }
    public DateTimeOffset? CapturedAtUtc { get; set; }
    public string HardwareFingerprint { get; init; } = "";
    public string ConfigurationFingerprint { get; init; } = "";
    public double? ThermalCelsius { get; init; }
    public double? CpuPerformancePercent { get; init; }
    public string InstanceName { get; init; } = "";
    public TraceReport? Report { get; set; }
    public string? Error { get; set; }
}

public sealed record TraceQuality(
    double DurationMilliseconds,
    long EtlBytes,
    long EventsLost,
    IReadOnlyList<string> MissingProviders,
    double TargetPresencePercent,
    bool IsValid);

public sealed record DistributionMetrics(
    long Count,
    double EventsPerSecond,
    double TotalMicroseconds,
    double P50Microseconds,
    double P95Microseconds,
    double P99Microseconds,
    double MaxMicroseconds);

public sealed record InterruptMetrics(
    string Kind,
    string Module,
    int LogicalProcessor,
    DistributionMetrics Distribution);

public sealed record ProcessorMetrics(
    int LogicalProcessor,
    double InterruptSharePercent,
    double TargetRunningMilliseconds,
    double ReadyOverlapMicroseconds);

public sealed record ThreadSchedulingMetrics(
    string ThreadKey,
    double RunningMilliseconds,
    DistributionMetrics ReadyTime,
    int Migrations,
    IReadOnlyDictionary<int, double> ResidencyMilliseconds);

public sealed record DiagnosticObservation(
    string Title,
    string Category,
    IReadOnlyList<string> EvidenceIds,
    string ObservedMetric,
    string Explanation,
    string VerifiableHypothesis,
    string Confidence);

public sealed record FrameTimeMetrics(
    string Source,
    long SampleCount,
    double CapturedDurationMilliseconds,
    double AverageFps,
    double OnePercentLowFps,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    long StutterCount,
    IReadOnlyList<string> PresentModes);

public sealed class TraceReport
{
    public int SchemaVersion { get; init; } = 1;
    public Guid SessionId { get; init; }
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public string TargetExecutable { get; init; } = "";
    public TraceQuality Quality { get; init; } = new(0, 0, 0, [], 0, false);
    public IReadOnlyList<InterruptMetrics> Interrupts { get; init; } = [];
    public IReadOnlyList<ProcessorMetrics> Processors { get; init; } = [];
    public IReadOnlyList<ThreadSchedulingMetrics> Threads { get; init; } = [];
    public FrameTimeMetrics? FrameTimes { get; init; }
    public IReadOnlyList<DiagnosticObservation> Observations { get; init; } = [];
}

public sealed record ComparisonMetric(
    string EvidenceId,
    double BaselineMedian,
    double CandidateMedian,
    double DeltaPercent,
    ComparisonOutcome Outcome);

public sealed class MeasurementComparison
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; }
    public ComparisonLevel Level { get; init; }
    public IReadOnlyList<Guid> BaselineSessionIds { get; init; } = [];
    public IReadOnlyList<Guid> CandidateSessionIds { get; init; } = [];
    public IReadOnlyList<ComparisonMetric> Metrics { get; init; } = [];
    public IReadOnlyList<string> RejectionReasons { get; init; } = [];
    public ComparisonDecision Recommendation { get; init; }
    public string RecommendationReason { get; init; } = "";
}

public sealed record MeasurementStartRequest(
    int ProcessId,
    DateTimeOffset ProcessStartTimeUtc,
    MeasurementLabel Label,
    int DurationSeconds = 180,
    bool KeepRawTrace = false,
    Guid? OptimizationRunId = null);

public sealed record MeasurementIdRequest(Guid SessionId, Guid? OptimizationRunId = null);
public sealed record FrameTimeImportRequest(Guid SessionId, string Csv, Guid? OptimizationRunId = null);
public sealed record MeasurementCompareRequest(IReadOnlyList<Guid> BaselineSessionIds, IReadOnlyList<Guid> CandidateSessionIds,
    Guid? OptimizationRunId = null);

public sealed record CpuTopologyEntry(
    ushort ProcessorGroup,
    byte LogicalProcessor,
    byte PhysicalCore,
    byte SmtIndex,
    byte EfficiencyClass,
    byte CacheCluster);

public sealed record GpuDeviceTopology(
    string DeviceKey,
    string Name,
    string Vendor,
    string DriverVersion,
    string DeviceInstanceId,
    string AffinityRegistryPath,
    bool PhysicalHost);

public sealed record MachineTopology(
    IReadOnlyList<CpuTopologyEntry> Processors,
    IReadOnlyList<GpuDeviceTopology> Gpus);

public sealed record CandidateAction(
    string CandidateId,
    string Action,
    string DeviceKey,
    string DeviceName,
    ushort ProcessorGroup,
    byte LogicalProcessor,
    byte PhysicalCore,
    byte SmtIndex,
    byte EfficiencyClass,
    byte CacheCluster,
    string AssignmentSetOverrideHex,
    int DevicePolicy,
    double InterruptSharePercent,
    double TargetRunningMilliseconds,
    double ReadyOverlapMicroseconds,
    IReadOnlyList<string> EvidenceIds,
    bool ApplyEnabled,
    string GateReason);

public sealed record GpuCandidateRequest(string DeviceKey, IReadOnlyList<Guid> BaselineSessionIds);
public sealed record GpuCandidateSet(string HardwareFingerprint, IReadOnlyList<Guid> BaselineSessionIds, IReadOnlyList<CandidateAction> Candidates);
public sealed record GpuAffinityInspectRequest(string DeviceKey);
public sealed record RegistryValueSnapshot(bool Exists, string Kind, string HexValue, int ByteLength);
public sealed record GpuAffinityPolicySnapshot(
    string DeviceKey,
    string DeviceName,
    string State,
    RegistryValueSnapshot AssignmentSetOverride,
    RegistryValueSnapshot DevicePolicy,
    bool Restorable,
    bool ApplyEnabled,
    string GateReason);

internal readonly record struct TimeInterval(double StartMilliseconds, double EndMilliseconds, int LogicalProcessor);

internal static class MeasurementStateMachine
{
    public static bool CanTransition(MeasurementSessionState from, MeasurementSessionState to) => (from, to) switch
    {
        (MeasurementSessionState.Prepared, MeasurementSessionState.Recording or MeasurementSessionState.Cancelled or MeasurementSessionState.Failed) => true,
        (MeasurementSessionState.Recording, MeasurementSessionState.Captured or MeasurementSessionState.Cancelled or MeasurementSessionState.Failed) => true,
        (MeasurementSessionState.Captured, MeasurementSessionState.Analyzing or MeasurementSessionState.Cancelled or MeasurementSessionState.Failed) => true,
        (MeasurementSessionState.Analyzing, MeasurementSessionState.Completed or MeasurementSessionState.Captured or MeasurementSessionState.Failed) => true,
        (MeasurementSessionState.Failed, MeasurementSessionState.Analyzing or MeasurementSessionState.Cancelled) => true,
        (MeasurementSessionState.Completed, MeasurementSessionState.Failed) => true,
        _ => false
    };
}
