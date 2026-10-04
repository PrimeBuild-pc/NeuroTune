using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuroTune;

public sealed class MeasurementService
{
    public static readonly string MeasurementsDirectory = Path.Combine(SettingsService.DataDirectory, "measurements");
    private const string SessionFileName = "session.json";
    private const string TraceFileName = "capture.etl";
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly Action<string>? _progress;

    public MeasurementService(Action<string>? progress = null)
    {
        _progress = progress;
        RecoverAndPurge();
    }

    public IReadOnlyList<MeasurementWorkload> Workloads()
    {
        var result = new List<MeasurementWorkload>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id <= 4 || process.HasExited || string.IsNullOrWhiteSpace(process.ProcessName)) continue;
                    var description = process.MainModule?.FileVersionInfo.FileDescription;
                    result.Add(new(process.Id, process.ProcessName, process.StartTime.ToUniversalTime(),
                        string.IsNullOrWhiteSpace(description) ? process.ProcessName : description));
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
        }
        return result.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.ProcessId).ToList();
    }

    public MeasurementSession Start(MeasurementStartRequest request, string wprProfilePath)
    {
        if (request.DurationSeconds is < 30 or > 600) throw new ArgumentOutOfRangeException(nameof(request.DurationSeconds), "Duration must be between 30 and 600 seconds.");
        if (!File.Exists(wprProfilePath)) throw new FileNotFoundException("The embedded NeuroTune WPR profile is missing.", wprProfilePath);
        if (request.SystemWide && request.OptimizationRunId is not null)
            throw new InvalidOperationException("System-wide monitoring is diagnostic only. Select a repeatable workload for an optimization run.");
        var workload = request.SystemWide ? new MeasurementWorkload(0, "System-wide", DateTimeOffset.MinValue, "System diagnostics") : Workloads().SingleOrDefault(item => item.ProcessId == request.ProcessId &&
            Math.Abs((item.StartTimeUtc - request.ProcessStartTimeUtc).TotalSeconds) < 1)
            ?? throw new InvalidOperationException("The selected process ended or its identity changed. Refresh the process list.");
        var id = Guid.NewGuid();
        _progress?.Invoke("Reading hardware and configuration fingerprints locally (WMI)…");
        var environment = CaptureEnvironment();
        var session = new MeasurementSession
        {
            SchemaVersion = 2,
            SystemWide = request.SystemWide,
            HardFaultsEnabled = true,
            Id = id,
            OptimizationRunId = request.OptimizationRunId,
            ProcessId = workload.ProcessId,
            ProcessName = workload.Name,
            ProcessStartTimeUtc = workload.StartTimeUtc,
            Label = request.Label,
            DurationSeconds = request.DurationSeconds,
            KeepRawTrace = request.KeepRawTrace,
            State = MeasurementSessionState.Prepared,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            HardwareFingerprint = environment.HardwareFingerprint,
            ConfigurationFingerprint = environment.ConfigurationFingerprint,
            ThermalCelsius = environment.ThermalCelsius,
            CpuPerformancePercent = environment.CpuPerformancePercent,
            InstanceName = $"NeuroTune-{id:N}"
        };
        Directory.CreateDirectory(SessionDirectory(id));
        Save(session);
        try
        {
            _progress?.Invoke("Waiting for the capture lock and starting Windows Performance Recorder…");
            WithCaptureMutex(() =>
            {
                if (ListWithoutRecovery().Any(item => item.Id != id && item.State == MeasurementSessionState.Recording))
                    throw new InvalidOperationException("Another NeuroTune measurement is already recording.");
                RunWpr(["-start", $"{Path.GetFullPath(wprProfilePath)}!NeuroTuneLatency", "-instancename", session.InstanceName]);
                Transition(session, MeasurementSessionState.Recording);
                session.RecordingStartedAtUtc = DateTimeOffset.UtcNow;
                Save(session);
            });
            _progress?.Invoke("WPR is recording. The capture countdown starts now.");
            return session;
        }
        catch (Exception exception)
        {
            Transition(session, MeasurementSessionState.Failed);
            session.Error = exception.Message;
            Save(session);
            throw;
        }
    }

    public MeasurementSession Stop(Guid id)
    {
        var session = Load(id);
        _progress?.Invoke("Stopping WPR and flushing captured events to the local ETL; this can take several seconds…");
        WithCaptureMutex(() =>
        {
            session = Load(id); // Watchdog, polling recovery and UI race: recheck and persist under the recorder lock.
            if (session.State is MeasurementSessionState.Captured or MeasurementSessionState.Analyzing or MeasurementSessionState.Completed) return;
            if (session.State != MeasurementSessionState.Recording) throw new InvalidOperationException("Only a recording session can be stopped.");
            try
            {
                RunWpr(["-stop", TracePath(id), "-instancename", session.InstanceName]);
                Transition(session, MeasurementSessionState.Captured);
                session.CapturedAtUtc = DateTimeOffset.UtcNow;
                session.Error = null;
                Save(session);
            }
            catch (Exception exception)
            {
                Transition(session, MeasurementSessionState.Failed);
                session.Error = exception.Message;
                Save(session);
                throw;
            }
        });
        return session;
    }

    public MeasurementSession Cancel(Guid id)
    {
        var session = Load(id);
        _progress?.Invoke("Cancelling the named WPR capture; waiting for Windows to release the recorder…");
        WithCaptureMutex(() =>
        {
            session = Load(id);
            if (session.State == MeasurementSessionState.Recording)
                RunWpr(["-cancel", "-instancename", session.InstanceName]);
            _progress?.Invoke("Deleting this session's incomplete local data…");
            Transition(session, MeasurementSessionState.Cancelled);
            session.Error = null;
            DeleteDirectory(id);
        });
        return session;
    }

    public MeasurementSession Analyze(Guid id, CancellationToken cancellationToken = default)
    {
        var session = Load(id);
        if (session.State is not (MeasurementSessionState.Captured or MeasurementSessionState.Failed or MeasurementSessionState.Analyzing))
            throw new InvalidOperationException("The session does not contain a captured trace that can be analyzed.");
        if (!File.Exists(TracePath(id))) throw new InvalidOperationException("The captured ETL is unavailable.");
        Transition(session, MeasurementSessionState.Analyzing);
        session.Error = null;
        Save(session);
        try
        {
            session.Report = new TraceAnalyzer().Analyze(TracePath(id), session, cancellationToken);
            Transition(session, MeasurementSessionState.Completed);
            Save(session);
            if (!session.KeepRawTrace) File.Delete(TracePath(id));
            return session;
        }
        catch (OperationCanceledException)
        {
            Transition(session, MeasurementSessionState.Captured);
            session.Error = null;
            Save(session);
            throw;
        }
        catch (Exception exception)
        {
            Transition(session, MeasurementSessionState.Failed);
            session.Error = exception.Message;
            Save(session);
            throw;
        }
    }

    public IReadOnlyList<MeasurementSession> List() => Directory.Exists(MeasurementsDirectory)
        ? Directory.EnumerateDirectories(MeasurementsDirectory).Select(Path.GetFileName).Select(name => Guid.TryParse(name, out var id) ? TryLoad(id) : null)
            .Where(session => session is not null).Cast<MeasurementSession>().OrderByDescending(session => session.CreatedAtUtc).ToList()
        : [];

    public MeasurementSession ImportFrameTimes(FrameTimeImportRequest request)
    {
        var session = Load(request.SessionId);
        if (request.OptimizationRunId is { } runId)
        {
            var run = new OptimizationRunService().Load(runId);
            if (session.OptimizationRunId is { } linkedRunId && linkedRunId != runId ||
                !run.BaselineSessionIds.Contains(session.Id) && !run.CandidateSessionIds.Contains(session.Id))
                throw new InvalidOperationException("The frame-time import does not match the measurement's optimization run.");
            var acceptsImport = session.Label == MeasurementLabel.Baseline &&
                    run.State is OptimizationRunState.BaselinePending or OptimizationRunState.BaselineReady ||
                session.Label == MeasurementLabel.Candidate && run.State == OptimizationRunState.CandidatePending;
            if (!acceptsImport)
                throw new InvalidOperationException("Frame-time evidence cannot change after this optimization-run stage.");
        }
        else if (session.OptimizationRunId is not null || new OptimizationRunService().IsMeasurementReferenced(session.Id))
            throw new InvalidOperationException("A run-linked frame-time import must include its optimization run ID.");
        if (session.State != MeasurementSessionState.Completed || session.Report is null)
            throw new InvalidOperationException("Frame-time evidence can be attached only to a completed measurement.");
        var metrics = PresentMonImporter.Parse(request.Csv, session.ProcessName);
        var expectedDuration = session.Report.Quality.DurationMilliseconds;
        if (!FrameDurationMatches(expectedDuration, metrics.CapturedDurationMilliseconds))
            throw new InvalidOperationException("The PresentMon duration differs from the ETW measurement by more than 20%.");
        session.Report = session.Report.WithFrameTimes(metrics);
        Save(session);
        return session;
    }

    internal static bool FrameDurationMatches(double expectedMilliseconds, double capturedMilliseconds) =>
        expectedMilliseconds > 0 && capturedMilliseconds > 0 &&
        Math.Abs(capturedMilliseconds - expectedMilliseconds) / expectedMilliseconds <= .20;

    public void Delete(Guid id)
    {
        var session = Load(id);
        if (session.State == MeasurementSessionState.Recording) throw new InvalidOperationException("Cancel the active recording before deleting it.");
        if (new OptimizationRunService().IsMeasurementReferenced(id))
            throw new InvalidOperationException("This measurement is referenced by an optimization run and cannot be deleted.");
        DeleteDirectory(id);
    }

    public MeasurementComparison Compare(MeasurementCompareRequest request)
    {
        return Compare(request, request.BaselineSessionIds.Distinct().Select(Load).ToList(),
            request.CandidateSessionIds.Distinct().Select(Load).ToList());
    }

    internal static MeasurementComparison Compare(MeasurementCompareRequest request,
        IReadOnlyList<MeasurementSession> baseline, IReadOnlyList<MeasurementSession> candidate)
    {
        if (baseline.Count == 0 || candidate.Count == 0) throw new InvalidOperationException("Select at least one baseline and one candidate session.");
        var all = baseline.Concat(candidate).ToList();
        var reasons = new List<string>();
        if (all.Any(item => item.SystemWide)) reasons.Add("System-wide captures are diagnostic snapshots, not matched workload benchmarks.");
        if (all.Select(item => item.Report?.SchemaVersion).Distinct().Count() != 1) reasons.Add("Analyzer schema versions differ; recapture matching sessions.");
        if (baseline.Any(item => item.Label != MeasurementLabel.Baseline) || candidate.Any(item => item.Label != MeasurementLabel.Candidate)) reasons.Add("Session labels do not match their comparison side.");
        if (all.Any(item => item.State != MeasurementSessionState.Completed || item.Report is null)) reasons.Add("Every session must have a completed report.");
        if (all.Any(item => item.Report?.Quality.IsValid != true)) reasons.Add("Every session must pass the trace quality gate.");
        if (all.Select(item => item.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) reasons.Add("Sessions target different executables.");
        if (all.Select(item => item.HardwareFingerprint).Distinct(StringComparer.Ordinal).Count() != 1) reasons.Add("Hardware fingerprints do not match.");
        if (all.Select(item => item.ConfigurationFingerprint).Distinct(StringComparer.Ordinal).Count() != 1) reasons.Add("Relevant configurations do not match.");
        var thermal = all.Select(item => item.ThermalCelsius).OfType<double>().ToList();
        if (thermal.Count > 0 && thermal.Count != all.Count)
            reasons.Add("Thermal telemetry availability changed between sessions.");
        else if (thermal.Count > 0 && thermal.Max() - thermal.Min() > 10)
            reasons.Add("Thermal samples differ by more than 10 °C.");
        var performance = all.Select(item => item.CpuPerformancePercent).OfType<double>().ToList();
        if (performance.Count > 0 && performance.Count != all.Count)
            reasons.Add("CPU performance telemetry availability changed between sessions.");
        else if (performance.Count > 0 && performance.Max() - performance.Min() > 15)
            reasons.Add("CPU performance state differs by more than 15 percentage points.");
        var frameTimeCount = all.Count(item => item.Report?.FrameTimes is not null);
        if (frameTimeCount > 0 && frameTimeCount != all.Count) reasons.Add("Frame-time evidence is missing from part of the comparison.");
        if (all.Select(item => item.HardFaultsEnabled).Distinct().Count() != 1)
            reasons.Add("Hard-fault collection availability changed between sessions.");
        if (!CapturedDurationsMatch(all)) reasons.Add("Actual captured durations are unavailable or differ by more than 10%.");
        if (reasons.Count > 0) return NewComparison(request, ComparisonLevel.Exploratory, [], reasons);

        var level = baseline.Count >= 3 && candidate.Count >= 3 ? ComparisonLevel.Repeated : ComparisonLevel.Exploratory;
        var baselineFacts = baseline.Select(SessionMetrics).ToList();
        var candidateFacts = candidate.Select(SessionMetrics).ToList();
        var facts = baselineFacts.Concat(candidateFacts).ToList();
        if (facts.Any(item => item.Values.Any(value => !double.IsFinite(value) || value < 0)))
            return NewComparison(request, ComparisonLevel.Exploratory, [], ["Comparison metrics contain invalid values."]);
        var allKeys = facts.SelectMany(item => item.Keys).Distinct(StringComparer.Ordinal).Order().ToList();
        var keys = allKeys.Where(key => facts.All(item => item.ContainsKey(key))).ToList();
        var missing = allKeys.Except(keys).ToList();
        if (missing.Count > 0)
            reasons.Add($"Metric coverage changed; missing values are not zero: {string.Join(", ", missing.Take(20))}.");
        var comparisonId = Guid.NewGuid();
        var metrics = keys.Select(key =>
        {
            var baselineValues = baselineFacts.Where(item => item.ContainsKey(key)).Select(item => item[key]).ToList();
            var candidateValues = candidateFacts.Where(item => item.ContainsKey(key)).Select(item => item[key]).ToList();
            var before = Median(baselineValues); var after = Median(candidateValues);
            var delta = before == 0 ? (after == 0 ? 0 : 100) : (after - before) / Math.Abs(before) * 100;
            var higherIsBetter = key.EndsWith("_fps", StringComparison.Ordinal);
            var outcome = level == ComparisonLevel.Repeated
                ? RepeatedOutcome(before, candidateValues, higherIsBetter)
                : after == before ? ComparisonOutcome.Inconclusive :
                    after > before == higherIsBetter ? ComparisonOutcome.Improvement : ComparisonOutcome.Regression;
            return new ComparisonMetric($"comparison:{comparisonId}:{key}:median_delta_percent", before, after, delta, outcome);
        }).ToList();
        var recommendation = Recommend(level, metrics);
        if (reasons.Count > 0 && recommendation.Decision != ComparisonDecision.Rollback)
            recommendation = (ComparisonDecision.InsufficientEvidence, "Coverage changed; a favorable Keep recommendation is not justified.");
        return new MeasurementComparison
        {
            Id = comparisonId,
            Level = level,
            BaselineSessionIds = request.BaselineSessionIds,
            CandidateSessionIds = request.CandidateSessionIds,
            Metrics = metrics,
            RejectionReasons = reasons,
            Recommendation = recommendation.Decision,
            RecommendationReason = recommendation.Reason
        };
    }

    public MachineTopology Topology() => new HardwareTopologyService().Collect();

    public GpuCandidateSet GpuAffinityCandidates(GpuCandidateRequest request)
    {
        var sessions = request.BaselineSessionIds.Distinct().Select(Load).ToList();
        return new HardwareTopologyService().Generate(request, sessions);
    }

    public GpuAffinityPolicySnapshot GpuAffinityPolicy(GpuAffinityInspectRequest request) =>
        new HardwareTopologyService().InspectGpuAffinity(request.DeviceKey);

    public void Watchdog(Guid id, CancellationToken cancellationToken = default)
    {
        var session = TryLoad(id);
        if (session?.State != MeasurementSessionState.Recording || session.RecordingStartedAtUtc is null) return;
        var deadline = session.RecordingStartedAtUtc.Value.AddSeconds(session.DurationSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(1000, Math.Max(50, (deadline - DateTimeOffset.UtcNow).TotalMilliseconds))));
            if (TryLoad(id)?.State != MeasurementSessionState.Recording) return;
        }
        if (TryLoad(id)?.State == MeasurementSessionState.Recording) Stop(id);
    }

    public IReadOnlyDictionary<string, string> BuildNormalizedEvidence(IEnumerable<Guid> ids) =>
        BuildNormalizedEvidence(ids.Distinct().Select(Load));

    public static IReadOnlyDictionary<string, string> BuildNormalizedEvidence(IEnumerable<MeasurementSession> sessions)
    {
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var session in sessions.Where(item => item.State == MeasurementSessionState.Completed && item.Report is not null))
        {
            var report = session.Report!;
            facts[$"measurement:{session.Id}:context:workload_name"] = ProfileSanitizer.Redact(session.SystemWide ? "System-wide diagnostic; not a game benchmark" : session.ProcessName);
            facts[$"measurement:{session.Id}:context:duration_ms"] = report.Quality.DurationMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            facts[$"measurement:{session.Id}:context:frame_metrics_available"] = (report.FrameTimes is not null).ToString();
            facts[$"measurement:{session.Id}:quality:valid"] = report.Quality.IsValid.ToString();
            facts[$"measurement:{session.Id}:quality:system_wide_diagnostic"] = session.SystemWide.ToString();
            facts[$"measurement:{session.Id}:quality:events_lost"] = report.Quality.EventsLost.ToString();
            facts[$"measurement:{session.Id}:quality:target_presence_percent"] = report.Quality.TargetPresencePercent.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var item in report.Interrupts)
                facts[$"measurement:{session.Id}:interrupt:{TraceAnalyzer.EvidencePart(item.Kind)}:{TraceAnalyzer.EvidencePart(item.Module)}:lp{item.LogicalProcessor}:p99_us"] = item.Distribution.P99Microseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var item in report.Processors)
            {
                facts[$"measurement:{session.Id}:cpu:{item.LogicalProcessor}:interrupt_share_percent"] = item.InterruptSharePercent.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                facts[$"measurement:{session.Id}:cpu:{item.LogicalProcessor}:target_running_ms"] = item.TargetRunningMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                facts[$"measurement:{session.Id}:cpu:{item.LogicalProcessor}:ready_overlap_us"] = item.ReadyOverlapMicroseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                if (item.ScheduledBusyMilliseconds is { } busy)
                    facts[$"measurement:{session.Id}:cpu:{item.LogicalProcessor}:scheduled_busy_ms"] = busy.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                if (item.Dpc is { } dpc)
                    facts[$"measurement:{session.Id}:cpu:{item.LogicalProcessor}:dpc_total_us"] = dpc.TotalMicroseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                if (item.Isr is { } isr)
                    facts[$"measurement:{session.Id}:cpu:{item.LogicalProcessor}:isr_total_us"] = isr.TotalMicroseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            }
            if (session.HardFaultsEnabled)
                foreach (var item in report.HardFaults)
                {
                    facts[$"measurement:{session.Id}:fault:{item.ProcessKey}:count"] = item.Resolution.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    facts[$"measurement:{session.Id}:fault:{item.ProcessKey}:max_us"] = item.Resolution.MaxMicroseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                }
            foreach (var item in report.Threads)
                facts[$"measurement:{session.Id}:thread:{item.ThreadKey}:ready_p99_us"] = item.ReadyTime.P99Microseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            if (report.FrameTimes is { } frames)
            {
                facts[$"measurement:{session.Id}:frames:average_fps"] = frames.AverageFps.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                facts[$"measurement:{session.Id}:frames:one_percent_low_fps"] = frames.OnePercentLowFps.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                facts[$"measurement:{session.Id}:frames:p99_ms"] = frames.P99Milliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                facts[$"measurement:{session.Id}:frames:stutter_count"] = frames.StutterCount.ToString();
            }
        }
        return facts;
    }

    internal static bool CapturedDurationsMatch(IEnumerable<MeasurementSession> sessions)
    {
        var durations = sessions.Select(item => item.Report?.Quality.DurationMilliseconds ?? 0).ToList();
        if (durations.Count == 0 || durations.Any(value => !double.IsFinite(value) || value <= 0)) return false;
        var median = Median(durations);
        return durations.All(value => Math.Abs(value - median) / median <= .10);
    }

    internal static Dictionary<string, double> SessionMetrics(MeasurementSession session)
    {
        var report = session.Report!;
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var group in report.Interrupts.GroupBy(item => (item.Kind, item.Module)))
            result[$"interrupt:{TraceAnalyzer.EvidencePart(group.Key.Kind)}:{TraceAnalyzer.EvidencePart(group.Key.Module)}:worst_core_p99_us"] = group.Max(item => item.Distribution.P99Microseconds);
        result["interrupt:system:worst_module_p99_us"] = report.Interrupts.Select(item => item.Distribution.P99Microseconds).DefaultIfEmpty(0).Max();
        if (session.HardFaultsEnabled)
        {
            result["fault:system:count"] = report.HardFaults.Sum(item => item.Resolution.Count);
            result["fault:system:worst_process_p99_us"] = report.HardFaults.Select(item => item.Resolution.P99Microseconds).DefaultIfEmpty(0).Max();
        }
        // Redistribution is not an improvement: shares sum to 100% even when total latency grows.
        result["target:worst_thread_ready_p99_us"] = report.Threads.Count == 0 ? 0 : report.Threads.Max(item => item.ReadyTime.P99Microseconds);
        result["target:migrations"] = report.Threads.Sum(item => item.Migrations);
        if (report.FrameTimes is { } frames)
        {
            result["frames:average_fps"] = frames.AverageFps;
            result["frames:one_percent_low_fps"] = frames.OnePercentLowFps;
            result["frames:p99_ms"] = frames.P99Milliseconds;
            result["frames:stutter_count"] = frames.StutterCount;
        }
        return result;
    }

    private MeasurementSession Load(Guid id) => TryLoad(id) ?? throw new InvalidOperationException("The measurement session was not found.");
    private static MeasurementSession? TryLoad(Guid id)
    {
        var path = SessionPath(id);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<MeasurementSession>(File.ReadAllText(path), _json); }
        catch (JsonException) { return null; }
    }

    private void Save(MeasurementSession session)
    {
        Directory.CreateDirectory(SessionDirectory(session.Id));
        var destination = SessionPath(session.Id);
        var temporary = destination + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(session, _json));
        if (File.Exists(destination)) File.Replace(temporary, destination, null);
        else File.Move(temporary, destination);
    }

    private void RecoverAndPurge()
    {
        Directory.CreateDirectory(MeasurementsDirectory);
        foreach (var session in ListWithoutRecovery())
        {
            if (session.State == MeasurementSessionState.Recording && session.RecordingStartedAtUtc is { } started &&
                started.AddSeconds(session.DurationSeconds) <= DateTimeOffset.UtcNow)
            {
                try { Stop(session.Id); }
                catch { /* Stop persists the failure for recovery diagnostics. */ }
                continue;
            }
            if (session.State == MeasurementSessionState.Analyzing && File.Exists(TracePath(session.Id)))
            {
                Transition(session, MeasurementSessionState.Captured);
                session.Error = null;
                Save(session);
            }
            if (session.State == MeasurementSessionState.Failed && session.CapturedAtUtc is { } captured && captured < DateTimeOffset.UtcNow.AddHours(-24))
                File.Delete(TracePath(session.Id));
        }
    }

    public static bool HasActiveRecording() => ListWithoutRecovery().Any(session => session.State == MeasurementSessionState.Recording);

    private static IEnumerable<MeasurementSession> ListWithoutRecovery() => !Directory.Exists(MeasurementsDirectory) ? [] :
        Directory.EnumerateDirectories(MeasurementsDirectory).Select(Path.GetFileName).Select(name => Guid.TryParse(name, out var id) ? TryLoad(id) : null).Where(item => item is not null).Cast<MeasurementSession>();

    private static void RunWpr(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(WindowsCommand.PathFor("wpr.exe"));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        _ = WindowsCommand.RunAsync(start, TimeSpan.FromMinutes(3)).GetAwaiter().GetResult();
    }

    internal static void WithCaptureMutex(Action action)
    {
        using var mutex = new Mutex(false, @"Global\NeuroTune.MeasurementCapture");
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(15))) throw new InvalidOperationException("Another NeuroTune measurement operation is active.");
        }
        catch (AbandonedMutexException) { }
        try { action(); } finally { mutex.ReleaseMutex(); }
    }

    private static string FirstLine(params string[] values) => values.SelectMany(value => value.Split('\r', '\n')).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? "unknown error";
    private static void Transition(MeasurementSession session, MeasurementSessionState state)
    {
        if (!MeasurementStateMachine.CanTransition(session.State, state))
            throw new InvalidOperationException($"Invalid measurement transition: {session.State} → {state}.");
        session.State = state;
    }
    internal static string Fingerprint(params string[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values)))).ToLowerInvariant();
    internal static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return 0;
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
    internal static ComparisonOutcome RepeatedOutcome(double baselineMedian, IReadOnlyList<double> candidate)
        => RepeatedOutcome(baselineMedian, candidate, false);
    internal static ComparisonOutcome RepeatedOutcome(double baselineMedian, IReadOnlyList<double> candidate, bool higherIsBetter)
    {
        if (candidate.Count < 3) return ComparisonOutcome.Inconclusive;
        var required = (int)Math.Ceiling(candidate.Count * 2d / 3d);
        if (candidate.Count(value => higherIsBetter ? value > baselineMedian : value < baselineMedian) >= required) return ComparisonOutcome.Improvement;
        if (candidate.Count(value => higherIsBetter ? value < baselineMedian : value > baselineMedian) >= required) return ComparisonOutcome.Regression;
        return ComparisonOutcome.Inconclusive;
    }
    internal static (ComparisonDecision Decision, string Reason) Recommend(ComparisonLevel level, IReadOnlyList<ComparisonMetric> metrics)
    {
        if (level != ComparisonLevel.Repeated)
            return (ComparisonDecision.InsufficientEvidence, "Record at least three matching Baselines and three Candidates before choosing Keep or Rollback from measurements.");
        var meaningful = metrics.Where(metric => Math.Abs(metric.DeltaPercent) >= 3 && metric.Outcome != ComparisonOutcome.Inconclusive).ToList();
        var improvements = meaningful.Count(metric => metric.Outcome == ComparisonOutcome.Improvement);
        var regressions = meaningful.Count(metric => metric.Outcome == ComparisonOutcome.Regression);
        if (improvements == 0 && regressions == 0)
            return (ComparisonDecision.InsufficientEvidence, "No repeatable improvement beyond the noise band; do not automatically keep the change.");
        return regressions > 0
            ? (ComparisonDecision.Rollback, $"Rollback is safer: {regressions} reproducible metric regression(s) versus {improvements} improvement(s) beyond the 3% noise band.")
            : (ComparisonDecision.Keep, $"Keep is reasonable: {improvements} reproducible improvement(s) versus {regressions} regression(s) beyond the 3% noise band. This is not a performance guarantee.");
    }

    private static MeasurementEnvironment CaptureEnvironment()
    {
        var cpu = SystemProfiler.Query("SELECT Name, ProcessorId, NumberOfLogicalProcessors FROM Win32_Processor",
            row => $"{row["Name"]}|{row["ProcessorId"]}|{row["NumberOfLogicalProcessors"]}");
        var memory = SystemProfiler.Query("SELECT Capacity FROM Win32_PhysicalMemory", row => row["Capacity"]?.ToString() ?? "");
        var gpuHardware = SystemProfiler.Query("SELECT PNPDeviceID FROM Win32_VideoController", row => row["PNPDeviceID"]?.ToString() ?? "");
        var gpuConfiguration = SystemProfiler.Query("SELECT PNPDeviceID, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController",
            row => $"{row["PNPDeviceID"]}|{row["DriverVersion"]}|{row["CurrentHorizontalResolution"]}x{row["CurrentVerticalResolution"]}@{row["CurrentRefreshRate"]}");
        var temperatures = SystemProfiler.Query(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature",
            row => (Convert.ToDouble(row["CurrentTemperature"] ?? 0) / 10d) - 273.15d).Where(value => value is >= -20 and <= 150).ToList();
        var performance = SystemProfiler.Query("SELECT PercentofMaximumFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'",
            row => Convert.ToDouble(row["PercentofMaximumFrequency"] ?? 0)).Where(value => value is > 0 and <= 500).ToList();
        return new(
            Fingerprint(cpu.Concat(memory).Concat(gpuHardware).Order(StringComparer.OrdinalIgnoreCase).ToArray()),
            Fingerprint([Environment.OSVersion.VersionString, .. gpuConfiguration.Order(StringComparer.OrdinalIgnoreCase)]),
            temperatures.Count == 0 ? null : temperatures.Max(),
            performance.Count == 0 ? null : performance.Average());
    }

    private sealed record MeasurementEnvironment(string HardwareFingerprint, string ConfigurationFingerprint,
        double? ThermalCelsius, double? CpuPerformancePercent);
    private static MeasurementComparison NewComparison(MeasurementCompareRequest request, ComparisonLevel level, IReadOnlyList<ComparisonMetric> metrics, IReadOnlyList<string> reasons) => new()
    { Id = Guid.NewGuid(), Level = level, BaselineSessionIds = request.BaselineSessionIds, CandidateSessionIds = request.CandidateSessionIds, Metrics = metrics, RejectionReasons = reasons };
    private static string SessionDirectory(Guid id) => Path.Combine(MeasurementsDirectory, id.ToString("D"));
    private static string SessionPath(Guid id) => Path.Combine(SessionDirectory(id), SessionFileName);
    private static string TracePath(Guid id) => Path.Combine(SessionDirectory(id), TraceFileName);
    private static void DeleteDirectory(Guid id)
    {
        var root = Path.GetFullPath(MeasurementsDirectory) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(SessionDirectory(id));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid measurement path.");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
}
