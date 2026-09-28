using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace NeuroTune;

public sealed class TraceAnalyzer
{
    private sealed record RawInterrupt(string Kind, double StartMs, double DurationMs, int Cpu, ulong Routine);
    internal sealed record ImageRange(ulong Start, ulong End, string Module, bool ChangedDuringTrace = false);
    private sealed record RunningInterval(int ThreadId, TimeInterval Interval);
    private sealed record ReadyInterval(int ThreadId, TimeInterval Interval);
    private sealed record RawHardFault(int ProcessId, double StartMs, double DurationMs);

    public TraceReport Analyze(string etlPath, MeasurementSession session, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(etlPath)) throw new FileNotFoundException("The captured ETL was not found.", etlPath);
        if (new FileInfo(etlPath).Length > 512L * 1024 * 1024)
            throw new InvalidOperationException("Trace exceeds the 512 MiB analysis limit; record a shorter interval.");

        var interrupts = new List<RawInterrupt>();
        var images = new List<ImageRange>();
        var running = new List<RunningInterval>();
        var ready = new List<ReadyInterval>();
        var targetThreads = new HashSet<int>();
        var readyAt = new Dictionary<int, double>();
        var activeByCpu = new Dictionary<int, (int ThreadId, double StartMs)>();
        var scheduledBusy = new Dictionary<int, double>();
        var scheduledIdle = new Dictionary<int, double>();
        var hardFaults = new List<RawHardFault>();
        var processNames = new Dictionary<int, HashSet<string>>();
        var threadOwners = new Dictionary<int, HashSet<int>>();
        var threadLifetimes = new Dictionary<int, List<(TraceEventOpcode Opcode, double Timestamp)>>();
        var processLifetimes = new Dictionary<int, List<(TraceEventOpcode Opcode, double Timestamp)>>();
        var streamCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["ISR/DPC"] = 0,
            ["CSwitch"] = 0,
            ["ReadyThread"] = 0,
            ["ProcessThread"] = 0
        };
        double firstTargetMs = double.MaxValue, lastTargetMs = 0;
        double firstSchedulingMs = double.MaxValue, lastSchedulingMs = 0;

        // ponytail: a discovery pass avoids retaining every system scheduling event; revisit only if ETL parse time becomes material.
        using (var identitySource = new ETWTraceEventSource(etlPath))
        {
            void RegisterIdentity(ThreadTraceData data)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!session.SystemWide && data.ProcessID == session.ProcessId) targetThreads.Add(data.ThreadID);
                if (!threadOwners.TryGetValue(data.ThreadID, out var owners)) threadOwners[data.ThreadID] = owners = [];
                owners.Add(data.ProcessID);
                if (!threadLifetimes.TryGetValue(data.ThreadID, out var events)) threadLifetimes[data.ThreadID] = events = [];
                events.Add((data.Opcode, data.TimeStampRelativeMSec));
            }
            void RegisterProcess(ProcessTraceData data)
            {
                if (!processNames.TryGetValue(data.ProcessID, out var names)) processNames[data.ProcessID] = names = [];
                names.Add(Path.GetFileName(data.ImageFileName));
                if (!processLifetimes.TryGetValue(data.ProcessID, out var events)) processLifetimes[data.ProcessID] = events = [];
                events.Add((data.Opcode, data.TimeStampRelativeMSec));
            }
            identitySource.Kernel.ThreadStartGroup += RegisterIdentity;
            identitySource.Kernel.ThreadEndGroup += RegisterIdentity;
            identitySource.Kernel.ProcessStartGroup += RegisterProcess;
            identitySource.Kernel.ProcessEndGroup += RegisterProcess;
            identitySource.Process();
        }
        // Reused IDs must not make another process look like the selected target.
        var ambiguousTarget = targetThreads.Any(id => threadOwners[id].Count != 1 || IdentityWasReused(threadLifetimes[id]));
        targetThreads.RemoveWhere(id => threadOwners[id].Count != 1 || IdentityWasReused(threadLifetimes[id]));
        var reusedProcesses = processLifetimes.Where(item => IdentityWasReused(item.Value)).Select(item => item.Key).ToHashSet();
        if (reusedProcesses.Contains(session.ProcessId)) targetThreads.Clear();

        using var source = new ETWTraceEventSource(etlPath);
        source.Kernel.PerfInfoDPC += data => AddInterrupt("dpc", data.TimeStampRelativeMSec, data.ElapsedTimeMSec, data.ProcessorNumber, data.Routine);
        source.Kernel.PerfInfoThreadedDPC += data => AddInterrupt("dpc", data.TimeStampRelativeMSec, data.ElapsedTimeMSec, data.ProcessorNumber, data.Routine);
        source.Kernel.PerfInfoTimerDPC += data => AddInterrupt("dpc", data.TimeStampRelativeMSec, data.ElapsedTimeMSec, data.ProcessorNumber, data.Routine);
        source.Kernel.PerfInfoISR += data => AddInterrupt("isr", data.TimeStampRelativeMSec, data.ElapsedTimeMSec, data.ProcessorNumber, data.Routine);
        source.Kernel.ThreadStartGroup += RegisterThread;
        source.Kernel.ThreadEndGroup += RegisterThread;
        source.Kernel.ThreadCSwitch += data =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            streamCounts["CSwitch"]++;
            var timestamp = data.TimeStampRelativeMSec;
            firstSchedulingMs = Math.Min(firstSchedulingMs, timestamp);
            lastSchedulingMs = Math.Max(lastSchedulingMs, timestamp);
            var newIsTarget = targetThreads.Contains(data.NewThreadID);
            var oldIsTarget = targetThreads.Contains(data.OldThreadID);
            if (activeByCpu.TryGetValue(data.ProcessorNumber, out var active) && timestamp >= active.StartMs)
            {
                AddScheduled(active.ThreadId, data.ProcessorNumber, timestamp - active.StartMs);
                if (oldIsTarget && active.ThreadId == data.OldThreadID)
                    running.Add(new(active.ThreadId, new(active.StartMs, timestamp, data.ProcessorNumber)));
            }
            activeByCpu[data.ProcessorNumber] = (data.NewThreadID, timestamp);
            if (newIsTarget && readyAt.Remove(data.NewThreadID, out var readyTimestamp) && timestamp >= readyTimestamp)
                ready.Add(new(data.NewThreadID, new(readyTimestamp, timestamp, data.ProcessorNumber)));
            if (newIsTarget || oldIsTarget)
            {
                firstTargetMs = Math.Min(firstTargetMs, timestamp);
                lastTargetMs = Math.Max(lastTargetMs, timestamp);
            }
        };
        source.Kernel.DispatcherReadyThread += data =>
        {
            streamCounts["ReadyThread"]++;
            if (targetThreads.Contains(data.AwakenedThreadID))
            {
                readyAt.TryAdd(data.AwakenedThreadID, data.TimeStampRelativeMSec);
            }
        };
        source.Kernel.ImageLoad += RegisterImage;
        source.Kernel.ImageUnload += RegisterImage;
        source.Kernel.ImageDCStart += RegisterImage;
        source.Kernel.ImageDCStop += RegisterImage;
        source.Kernel.MemoryHardFault += data =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hardFaults.Count >= 1_000_000) throw new InvalidOperationException("Too many hard faults to analyze; use a shorter capture.");
            if (double.IsFinite(data.ElapsedTimeMSec) && data.ElapsedTimeMSec >= 0)
                hardFaults.Add(new(data.ProcessID, Math.Max(0, data.TimeStampRelativeMSec - data.ElapsedTimeMSec), data.ElapsedTimeMSec));
        };
        source.Process();

        var durationMs = Math.Max(0, source.SessionDuration.TotalMilliseconds);
        foreach (var (cpu, active) in activeByCpu)
        {
            AddScheduled(active.ThreadId, cpu, Math.Max(0, durationMs - active.StartMs));
            if (targetThreads.Contains(active.ThreadId))
                running.Add(new(active.ThreadId, new(active.StartMs, durationMs, cpu)));
        }
        var targetRunning = running.Where(item => targetThreads.Contains(item.ThreadId)).ToList();
        var targetReady = ready.Where(item => targetThreads.Contains(item.ThreadId)).ToList();
        var targetPresence = firstTargetMs == double.MaxValue || durationMs <= 0
            ? 0
            : Math.Clamp((lastTargetMs - firstTargetMs) / durationMs * 100, 0, 100);
        // No interrupts in an idle interval is different from a missing scheduler stream.
        var missing = streamCounts.Where(item => item.Key != "ISR/DPC" && item.Value == 0).Select(item => item.Key).Order().ToList();
        var schedulingCoverage = durationMs > 0 ? Math.Max(0, lastSchedulingMs - firstSchedulingMs) / durationMs : 0;
        if (schedulingCoverage < .8) missing.Add("SchedulingWindowCoverageBelow80Percent");
        if (!session.SystemWide && targetThreads.Count == 0) missing.Add("TargetProcess");
        if (!session.SystemWide && ambiguousTarget) missing.Add("ReusedTargetThreadId");
        if (!session.SystemWide && reusedProcesses.Contains(session.ProcessId)) missing.Add("ReusedTargetProcessId");
        var quality = new TraceQuality(durationMs, new FileInfo(etlPath).Length, source.EventsLost, missing,
            Math.Round(targetPresence, 2), durationMs > 0 && source.EventsLost == 0 && missing.Count == 0 && (session.SystemWide || targetPresence >= 50));

        images.Sort((left, right) => left.Start.CompareTo(right.Start));
        var interruptMetrics = interrupts
            .GroupBy(item => (item.Kind, Module: ResolveModule(images, item.Routine), item.Cpu))
            .Select(group => new InterruptMetrics(group.Key.Kind, group.Key.Module, group.Key.Cpu,
                Describe(group.Select(item => item.DurationMs * 1000), durationMs)))
            .OrderByDescending(item => item.Distribution.TotalMicroseconds)
            .ToList();

        var interruptIntervals = interrupts.Select(item => new TimeInterval(item.StartMs, item.StartMs + item.DurationMs, item.Cpu)).ToList();
        var processors = Enumerable.Range(0, source.NumberOfProcessors).Concat(interrupts.Select(item => item.Cpu)).Distinct()
            .Select(cpu =>
            {
                var totalInterruptUs = interrupts.Where(item => item.Cpu == cpu).Sum(item => item.DurationMs * 1000);
                var allInterruptUs = interrupts.Sum(item => item.DurationMs * 1000);
                return new ProcessorMetrics(cpu, allInterruptUs <= 0 ? 0 : totalInterruptUs / allInterruptUs * 100,
                    targetRunning.Where(item => item.Interval.LogicalProcessor == cpu).Sum(item => item.Interval.EndMilliseconds - item.Interval.StartMilliseconds),
                    OverlapMicroseconds(targetReady.Select(item => item.Interval), interruptIntervals, cpu))
                {
                    ScheduledBusyMilliseconds = activeByCpu.ContainsKey(cpu) ? scheduledBusy.GetValueOrDefault(cpu) : null,
                    ScheduledIdleMilliseconds = activeByCpu.ContainsKey(cpu) ? scheduledIdle.GetValueOrDefault(cpu) : null,
                    UnobservedMilliseconds = Math.Max(0, durationMs - scheduledBusy.GetValueOrDefault(cpu) - scheduledIdle.GetValueOrDefault(cpu)),
                    Dpc = Describe(interrupts.Where(item => item.Cpu == cpu && item.Kind == "dpc").Select(item => item.DurationMs * 1000), durationMs),
                    Isr = Describe(interrupts.Where(item => item.Cpu == cpu && item.Kind == "isr").Select(item => item.DurationMs * 1000), durationMs)
                };
            })
            .OrderByDescending(item => item.InterruptSharePercent)
            .ToList();

        var threadIds = targetThreads.Order().ToList();
        var threadKeys = threadIds.Select((id, index) => (id, key: $"thread-{index + 1}")).ToDictionary(item => item.id, item => item.key);
        var threads = threadIds.Select(id =>
        {
            var runs = targetRunning.Where(item => item.ThreadId == id).Select(item => item.Interval).OrderBy(item => item.StartMilliseconds).ToList();
            var waits = targetReady.Where(item => item.ThreadId == id).Select(item => item.Interval.EndMilliseconds - item.Interval.StartMilliseconds);
            var migrations = runs.Zip(runs.Skip(1)).Count(pair => pair.First.LogicalProcessor != pair.Second.LogicalProcessor);
            var residency = runs.GroupBy(item => item.LogicalProcessor).ToDictionary(group => group.Key,
                group => Math.Round(group.Sum(item => item.EndMilliseconds - item.StartMilliseconds), 3));
            return new ThreadSchedulingMetrics(threadKeys[id], Math.Round(runs.Sum(item => item.EndMilliseconds - item.StartMilliseconds), 3),
                Describe(waits.Select(value => value * 1000), durationMs), migrations, residency);
        }).OrderByDescending(item => item.ReadyTime.TotalMicroseconds).ToList();

        var report = new TraceReport
        {
            SchemaVersion = 3,
            SessionId = session.Id,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            TargetExecutable = session.ProcessName,
            Quality = quality,
            Interrupts = interruptMetrics,
            Processors = processors,
            Threads = threads,
            HardFaults = hardFaults.GroupBy(item => item.ProcessId).OrderBy(group => group.Key).Select((group, index) =>
                new HardFaultMetrics($"process-{index + 1}",
                    !reusedProcesses.Contains(group.Key) && processNames.TryGetValue(group.Key, out var names) && names.Count == 1 ? names.Single() : "Unknown / reused process ID",
                    Describe(group.Select(item => item.DurationMs * 1000), durationMs))).OrderByDescending(item => item.Resolution.MaxMicroseconds).ToList(),
            LongestSpikes = interrupts.Select(item => new LatencySpike(item.Kind, ResolveModule(images, item.Routine), item.Cpu, item.StartMs, item.DurationMs * 1000))
                .OrderByDescending(item => item.DurationMicroseconds).Take(50).OrderBy(item => item.StartMilliseconds).ToList(),
            Limitations = [
                "Interrupt-to-process latency and firmware/SMI stalls are not measured. Ready Time is scheduler waiting, not a substitute.",
                "Scheduled busy/idle time includes time interrupted by ISR/DPC. Separate interrupt totals can overlap; do not add them to busy time.",
                "Time before each processor's first context switch is unobserved; a processor without switches has no inferred busy/idle value. Scheduling must span at least 80% of the trace window.",
                "The timeline retains the 50 longest interrupt events; driver/core distributions include all captured events.",
                "Module attribution is not device/service causality. Framework drivers may represent multiple devices; no service is blamed from its host alone.",
                "Kernel address ranges loaded/unloaded during capture or mapped to conflicting modules remain Unknown. Reused target PID/TID lifetimes invalidate workload comparisons instead of merging identities.",
                "Ready Time includes observed wake-to-run intervals; preemption-only ready intervals are not yet reconstructed. Comparisons use all target threads, including those outside the UI's top ten.",
                session.HardFaultsEnabled ? "Hard-fault tracing enabled; zero observed faults is valid. Process names are local and reused/ambiguous IDs remain Unknown." : "Hard-fault tracing was not enabled for this legacy session; an empty list does not prove zero faults."
            ]
        };
        report = report.WithObservations(BuildObservations(report));
        return report;

        void AddInterrupt(string kind, double timestamp, double elapsed, int cpu, ulong routine)
        {
            cancellationToken.ThrowIfCancellationRequested();
            streamCounts["ISR/DPC"]++;
            if (interrupts.Count >= 2_000_000) throw new InvalidOperationException("Too many interrupts to analyze; use a shorter capture.");
            if (double.IsFinite(elapsed) && elapsed >= 0)
                interrupts.Add(new(kind, InterruptStartMilliseconds(timestamp, elapsed), elapsed, cpu, routine));
        }
        void AddScheduled(int thread, int cpu, double elapsed)
        {
            var totals = thread == 0 ? scheduledIdle : scheduledBusy;
            totals[cpu] = totals.GetValueOrDefault(cpu) + Math.Max(0, elapsed);
        }
        void RegisterThread(ThreadTraceData data)
        {
            streamCounts["ProcessThread"]++;
            // Identities were discovered before scheduling analysis; do not re-add reused IDs.
        }
        void RegisterImage(ImageLoadTraceData data)
        {
            if (data.ProcessID is not (0 or 4) || data.ImageBase == 0 || data.ImageSize <= 0) return;
            var name = Path.GetFileName(data.FileName);
            if (ulong.MaxValue - data.ImageBase < (ulong)data.ImageSize) return;
            images.Add(new(data.ImageBase, data.ImageBase + (ulong)data.ImageSize, string.IsNullOrWhiteSpace(name) ? "Unknown" : name,
                data.Opcode != TraceEventOpcode.DataCollectionStart && data.Opcode != TraceEventOpcode.DataCollectionStop));
        }
    }

    // ETW logs ISR/DPC completion; its timestamp is the end of the routine.
    internal static double InterruptStartMilliseconds(double endMilliseconds, double durationMilliseconds) =>
        Math.Max(0, endMilliseconds - durationMilliseconds);

    internal static DistributionMetrics Describe(IEnumerable<double> values, double durationMilliseconds)
    {
        var sorted = values.Where(double.IsFinite).Where(value => value >= 0).Order().ToArray();
        if (sorted.Length == 0) return new(0, 0, 0, 0, 0, 0, 0);
        return new(sorted.Length, durationMilliseconds <= 0 ? 0 : sorted.Length / (durationMilliseconds / 1000),
            sorted.Sum(), PercentileNearestRank(sorted, .50), PercentileNearestRank(sorted, .95),
            PercentileNearestRank(sorted, .99), sorted[^1]);
    }

    internal static double PercentileNearestRank(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        var rank = Math.Clamp((int)Math.Ceiling(percentile * sortedValues.Count), 1, sortedValues.Count);
        return sortedValues[rank - 1];
    }

    internal static double OverlapMicroseconds(IEnumerable<TimeInterval> left, IEnumerable<TimeInterval> right, int cpu)
    {
        var a = MergeIntervals(left, cpu);
        var b = MergeIntervals(right, cpu);
        var i = 0; var j = 0; var overlap = 0d;
        while (i < a.Length && j < b.Length)
        {
            overlap += Math.Max(0, Math.Min(a[i].EndMilliseconds, b[j].EndMilliseconds) - Math.Max(a[i].StartMilliseconds, b[j].StartMilliseconds));
            if (a[i].EndMilliseconds <= b[j].EndMilliseconds) i++; else j++;
        }
        return overlap * 1000;
    }

    private static TimeInterval[] MergeIntervals(IEnumerable<TimeInterval> intervals, int cpu)
    {
        var merged = new List<TimeInterval>();
        foreach (var item in intervals.Where(item => item.LogicalProcessor == cpu && item.EndMilliseconds > item.StartMilliseconds).OrderBy(item => item.StartMilliseconds))
        {
            if (merged.Count > 0 && item.StartMilliseconds <= merged[^1].EndMilliseconds)
                merged[^1] = merged[^1] with { EndMilliseconds = Math.Max(merged[^1].EndMilliseconds, item.EndMilliseconds) };
            else merged.Add(item);
        }
        return merged.ToArray();
    }

    internal static bool IdentityWasReused(IEnumerable<(TraceEventOpcode Opcode, double Timestamp)> events)
    {
        var ordered = events.OrderBy(item => item.Timestamp).ToList();
        var started = false;
        var stopped = false;
        foreach (var item in ordered)
        {
            if (item.Opcode == TraceEventOpcode.Start)
            {
                if (started || stopped) return true;
                started = true;
            }
            else if (item.Opcode == TraceEventOpcode.DataCollectionStart) started = true;
            else if (item.Opcode == TraceEventOpcode.Stop) stopped = true;
        }
        return false;
    }

    internal static string ResolveModule(IReadOnlyList<ImageRange> images, ulong address)
    {
        if (address == 0) return "Unknown";
        // ponytail: changed ranges stay unknown until time-qualified image lifetimes are validated with unload/reload ETL fixtures.
        string? module = null;
        foreach (var match in images.Where(image => image.Start <= address && address < image.End))
        {
            if (match.ChangedDuringTrace || (module is not null && !string.Equals(module, match.Module, StringComparison.OrdinalIgnoreCase))) return "Unknown";
            module = match.Module;
        }
        return module ?? "Unknown";
    }

    private static IReadOnlyList<DiagnosticObservation> BuildObservations(TraceReport report)
    {
        var observations = new List<DiagnosticObservation>();
        var interrupt = report.Interrupts.OrderByDescending(item => item.Distribution.P99Microseconds).FirstOrDefault();
        if (interrupt is not null)
        {
            var id = $"measurement:{report.SessionId}:interrupt:{EvidencePart(interrupt.Kind)}:{EvidencePart(interrupt.Module)}:lp{interrupt.LogicalProcessor}:p99_us";
            observations.Add(new("Highest observed interrupt tail", "Interrupts", [id],
                $"{interrupt.Distribution.P99Microseconds:F2} µs P99 for {interrupt.Kind.ToUpperInvariant()} in {interrupt.Module}",
                "This identifies concentration in the captured interval; it does not establish causality.",
                "Repeat the same workload and check whether the same module remains concentrated.", report.Quality.IsValid ? "medium" : "low"));
        }
        var thread = report.Threads.OrderByDescending(item => item.ReadyTime.P99Microseconds).FirstOrDefault();
        if (thread is not null)
        {
            var id = $"measurement:{report.SessionId}:thread:{thread.ThreadKey}:ready_p99_us";
            observations.Add(new("Longest target ready-time tail", "Scheduling", [id],
                $"{thread.ReadyTime.P99Microseconds:F2} µs P99 on {thread.ThreadKey}",
                "Ready time is observed scheduler waiting, not proof of a particular device or driver cause.",
                "Repeat the workload and compare this thread-level tail with interrupt overlap.", report.Quality.IsValid ? "medium" : "low"));
        }
        var fault = report.HardFaults.FirstOrDefault();
        if (fault is not null)
        {
            observations.Add(new("Longest observed hard pagefault", "Memory",
                [$"measurement:{report.SessionId}:fault:{fault.ProcessKey}:max_us"],
                $"{fault.Resolution.MaxMicroseconds:F2} µs maximum resolution time in {fault.ProcessName}",
                "Hard faults require storage-backed memory resolution. Their occurrence alone does not prove stutter or a need to disable the page file.",
                "Repeat the affected workload and check whether faults coincide with its stalls before changing memory or storage settings.", report.Quality.IsValid ? "medium" : "low"));
        }
        return observations;
    }

    internal static string EvidencePart(string value) => string.Concat(value.ToLowerInvariant().Select(character =>
        char.IsLetterOrDigit(character) || character is '.' or '-' ? character : '_'));
}

internal static class TraceReportExtensions
{
    public static TraceReport WithFrameTimes(this TraceReport report, FrameTimeMetrics frameTimes) => new()
    {
        SchemaVersion = report.SchemaVersion,
        SessionId = report.SessionId,
        GeneratedAtUtc = report.GeneratedAtUtc,
        TargetExecutable = report.TargetExecutable,
        Quality = report.Quality,
        Interrupts = report.Interrupts,
        Processors = report.Processors,
        Threads = report.Threads,
        HardFaults = report.HardFaults,
        LongestSpikes = report.LongestSpikes,
        Limitations = report.Limitations,
        FrameTimes = frameTimes,
        Observations = report.Observations
    };

    public static TraceReport WithObservations(this TraceReport report, IReadOnlyList<DiagnosticObservation> observations) => new()
    {
        SchemaVersion = report.SchemaVersion,
        SessionId = report.SessionId,
        GeneratedAtUtc = report.GeneratedAtUtc,
        TargetExecutable = report.TargetExecutable,
        Quality = report.Quality,
        Interrupts = report.Interrupts,
        Processors = report.Processors,
        Threads = report.Threads,
        HardFaults = report.HardFaults,
        LongestSpikes = report.LongestSpikes,
        Limitations = report.Limitations,
        FrameTimes = report.FrameTimes,
        Observations = observations
    };
}
