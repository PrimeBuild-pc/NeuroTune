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
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public MeasurementService() => RecoverAndPurge();

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
        var workload = Workloads().SingleOrDefault(item => item.ProcessId == request.ProcessId &&
            Math.Abs((item.StartTimeUtc - request.ProcessStartTimeUtc).TotalSeconds) < 1)
            ?? throw new InvalidOperationException("The selected process ended or its identity changed. Refresh the process list.");
        var id = Guid.NewGuid();
        var environment = CaptureEnvironment();
        var session = new MeasurementSession
        {
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
            Transition(session, MeasurementSessionState.Recording);
            session.RecordingStartedAtUtc = DateTimeOffset.UtcNow;
            Save(session);
            WithCaptureMutex(() =>
            {
                if (ListWithoutRecovery().Any(item => item.Id != id && item.State == MeasurementSessionState.Recording))
                    throw new InvalidOperationException("Another NeuroTune measurement is already recording.");
                RunWpr(["-start", $"{Path.GetFullPath(wprProfilePath)}!NeuroTuneLatency", "-instancename", session.InstanceName]);
            });
            Save(session);
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
        if (session.State != MeasurementSessionState.Recording) throw new InvalidOperationException("Only a recording session can be stopped.");
        try
        {
            WithCaptureMutex(() => RunWpr(["-stop", TracePath(id), "-instancename", session.InstanceName]));
            Transition(session, MeasurementSessionState.Captured);
            session.CapturedAtUtc = DateTimeOffset.UtcNow;
            session.Error = null;
            Save(session);
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

    public MeasurementSession Cancel(Guid id)
    {
        var session = Load(id);
        if (session.State == MeasurementSessionState.Recording)
            WithCaptureMutex(() => RunWpr(["-cancel", "-instancename", session.InstanceName]));
        Transition(session, MeasurementSessionState.Cancelled);
        session.Error = null;
        DeleteDirectory(id);
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
        var baseline = request.BaselineSessionIds.Distinct().Select(Load).ToList();
        var candidate = request.CandidateSessionIds.Distinct().Select(Load).ToList();
        if (baseline.Count == 0 || candidate.Count == 0) throw new InvalidOperationException("Select at least one baseline and one candidate session.");
        var all = baseline.Concat(candidate).ToList();
        var reasons = new List<string>();
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
        var durationMedian = Median(all.Select(item => (double)item.DurationSeconds));
        if (all.Any(item => Math.Abs(item.DurationSeconds - durationMedian) / durationMedian > .10)) reasons.Add("Session durations differ by more than 10%.");
        if (reasons.Count > 0) return NewComparison(request, ComparisonLevel.Exploratory, [], reasons);

        var level = baseline.Count >= 3 && candidate.Count >= 3 ? ComparisonLevel.Repeated : ComparisonLevel.Exploratory;
        var baselineFacts = baseline.Select(SessionMetrics).ToList();
        var candidateFacts = candidate.Select(SessionMetrics).ToList();
        var keys = baselineFacts.SelectMany(item => item.Keys).Intersect(candidateFacts.SelectMany(item => item.Keys), StringComparer.Ordinal).Distinct().Order().ToList();
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
        return new MeasurementComparison
        {
            Id = comparisonId,
            Level = level,
            BaselineSessionIds = request.BaselineSessionIds,
            CandidateSessionIds = request.CandidateSessionIds,
            Metrics = metrics,
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
            facts[$"measurement:{session.Id}:quality:valid"] = report.Quality.IsValid.ToString();
            facts[$"measurement:{session.Id}:quality:events_lost"] = report.Quality.EventsLost.ToString();
            facts[$"measurement:{session.Id}:quality:target_presence_percent"] = report.Quality.TargetPresencePercent.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var item in report.Interrupts)
                facts[$"measurement:{session.Id}:interrupt:{TraceAnalyzer.EvidencePart(item.Kind)}:{TraceAnalyzer.EvidencePart(item.Module)}:lp{item.LogicalProcessor}:p99_us"] = item.Distribution.P99Microseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var item in report.Processors)
            {
                facts[$"measurement:{session.Id}:cpu:{item.LogicalProcessor}:interrupt_share_percent"] = item.InterruptSharePercent.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                facts[$"measurement:{session.Id}:cpu:{item.LogicalProcessor}:target_running_ms"] = item.TargetRunningMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                facts[$"measurement:{session.Id}:cpu:{item.LogicalProcessor}:ready_overlap_us"] = item.ReadyOverlapMicroseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
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

    private Dictionary<string, double> SessionMetrics(MeasurementSession session)
    {
        var report = session.Report!;
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var group in report.Interrupts.GroupBy(item => (item.Kind, item.Module)))
            result[$"interrupt:{TraceAnalyzer.EvidencePart(group.Key.Kind)}:{TraceAnalyzer.EvidencePart(group.Key.Module)}:p99_us"] = group.Max(item => item.Distribution.P99Microseconds);
        foreach (var cpu in report.Processors)
            result[$"cpu:{cpu.LogicalProcessor}:interrupt_share_percent"] = cpu.InterruptSharePercent;
        result["target:ready_p99_us"] = report.Threads.Count == 0 ? 0 : report.Threads.Max(item => item.ReadyTime.P99Microseconds);
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
    private MeasurementSession? TryLoad(Guid id)
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

    private IEnumerable<MeasurementSession> ListWithoutRecovery() => !Directory.Exists(MeasurementsDirectory) ? [] :
        Directory.EnumerateDirectories(MeasurementsDirectory).Select(Path.GetFileName).Select(name => Guid.TryParse(name, out var id) ? TryLoad(id) : null).Where(item => item is not null).Cast<MeasurementSession>();

    private static void RunWpr(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "wpr.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows Performance Recorder could not be started.");
        var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"WPR failed ({process.ExitCode}): {FirstLine(error, output)}");
    }

    private static void WithCaptureMutex(Action action)
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
        return regressions > improvements
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
