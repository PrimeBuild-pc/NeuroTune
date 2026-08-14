using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace NeuroTune;

public sealed class TraceAnalyzer
{
    private sealed record RawInterrupt(string Kind, double StartMs, double DurationMs, int Cpu, ulong Routine);
    private sealed record ImageRange(ulong Start, ulong End, string Module);
    private sealed record RunningInterval(int ThreadId, TimeInterval Interval);
    private sealed record ReadyInterval(int ThreadId, TimeInterval Interval);

    public TraceReport Analyze(string etlPath, MeasurementSession session, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(etlPath)) throw new FileNotFoundException("The captured ETL was not found.", etlPath);

        var interrupts = new List<RawInterrupt>();
        var images = new List<ImageRange>();
        var running = new List<RunningInterval>();
        var ready = new List<ReadyInterval>();
        var targetThreads = new HashSet<int>();
        var readyAt = new Dictionary<int, double>();
        var activeByCpu = new Dictionary<int, (int ThreadId, double StartMs)>();
        var streamCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["ISR/DPC"] = 0,
            ["CSwitch"] = 0,
            ["ReadyThread"] = 0,
            ["ProcessThread"] = 0
        };
        double firstTargetMs = double.MaxValue, lastTargetMs = 0;

        // ponytail: a discovery pass avoids retaining every system scheduling event; revisit only if ETL parse time becomes material.
        using (var identitySource = new ETWTraceEventSource(etlPath))
        {
            void RegisterIdentity(ThreadTraceData data)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (data.ProcessID == session.ProcessId) targetThreads.Add(data.ThreadID);
            }
            identitySource.Kernel.ThreadStartGroup += RegisterIdentity;
            identitySource.Kernel.ThreadEndGroup += RegisterIdentity;
            identitySource.Process();
        }

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
            var newIsTarget = targetThreads.Contains(data.NewThreadID);
            var oldIsTarget = targetThreads.Contains(data.OldThreadID);
            if (oldIsTarget && activeByCpu.TryGetValue(data.ProcessorNumber, out var active) && timestamp >= active.StartMs)
                running.Add(new(active.ThreadId, new(active.StartMs, timestamp, data.ProcessorNumber)));
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
                readyAt[data.AwakenedThreadID] = data.TimeStampRelativeMSec;
            }
        };
        source.Kernel.ImageLoad += RegisterImage;
        source.Kernel.ImageDCStart += RegisterImage;
        source.Process();

        var durationMs = Math.Max(0, source.SessionDuration.TotalMilliseconds);
        var targetRunning = running.Where(item => targetThreads.Contains(item.ThreadId)).ToList();
        var targetReady = ready.Where(item => targetThreads.Contains(item.ThreadId)).ToList();
        var targetPresence = firstTargetMs == double.MaxValue || durationMs <= 0
            ? 0
            : Math.Clamp((lastTargetMs - firstTargetMs) / durationMs * 100, 0, 100);
        var missing = streamCounts.Where(item => item.Value == 0).Select(item => item.Key).Order().ToList();
        if (targetThreads.Count == 0) missing.Add("TargetProcess");
        var quality = new TraceQuality(durationMs, new FileInfo(etlPath).Length, source.EventsLost, missing,
            Math.Round(targetPresence, 2), source.EventsLost == 0 && missing.Count == 0 && targetPresence >= 50);

        images.Sort((left, right) => left.Start.CompareTo(right.Start));
        var interruptMetrics = interrupts
            .GroupBy(item => (item.Kind, Module: ResolveModule(images, item.Routine), item.Cpu))
            .Select(group => new InterruptMetrics(group.Key.Kind, group.Key.Module, group.Key.Cpu,
                Describe(group.Select(item => item.DurationMs * 1000), durationMs)))
            .OrderByDescending(item => item.Distribution.TotalMicroseconds)
            .Take(30)
            .ToList();

        var interruptIntervals = interrupts.Select(item => new TimeInterval(item.StartMs, item.StartMs + item.DurationMs, item.Cpu)).ToList();
        var processors = interrupts.Select(item => item.Cpu).Concat(targetRunning.Select(item => item.Interval.LogicalProcessor)).Distinct()
            .Select(cpu =>
            {
                var totalInterruptUs = interrupts.Where(item => item.Cpu == cpu).Sum(item => item.DurationMs * 1000);
                var allInterruptUs = interrupts.Sum(item => item.DurationMs * 1000);
                return new ProcessorMetrics(cpu, allInterruptUs <= 0 ? 0 : totalInterruptUs / allInterruptUs * 100,
                    targetRunning.Where(item => item.Interval.LogicalProcessor == cpu).Sum(item => item.Interval.EndMilliseconds - item.Interval.StartMilliseconds),
                    OverlapMicroseconds(targetReady.Select(item => item.Interval), interruptIntervals, cpu));
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
        }).OrderByDescending(item => item.ReadyTime.TotalMicroseconds).Take(10).ToList();

        var report = new TraceReport
        {
            SessionId = session.Id,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            TargetExecutable = session.ProcessName,
            Quality = quality,
            Interrupts = interruptMetrics,
            Processors = processors,
            Threads = threads
        };
        report = report.WithObservations(BuildObservations(report));
        return report;

        void AddInterrupt(string kind, double timestamp, double elapsed, int cpu, ulong routine)
        {
            cancellationToken.ThrowIfCancellationRequested();
            streamCounts["ISR/DPC"]++;
            if (elapsed >= 0) interrupts.Add(new(kind, timestamp, elapsed, cpu, routine));
        }
        void RegisterThread(ThreadTraceData data)
        {
            streamCounts["ProcessThread"]++;
            if (data.ProcessID == session.ProcessId) targetThreads.Add(data.ThreadID);
        }
        void RegisterImage(ImageLoadTraceData data)
        {
            if (data.ImageBase == 0 || data.ImageSize <= 0) return;
            var name = Path.GetFileName(data.FileName);
            images.Add(new(data.ImageBase, data.ImageBase + (ulong)data.ImageSize, string.IsNullOrWhiteSpace(name) ? "Unknown" : name));
        }
    }

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
        var a = left.Where(item => item.LogicalProcessor == cpu).OrderBy(item => item.StartMilliseconds).ToArray();
        var b = right.Where(item => item.LogicalProcessor == cpu).OrderBy(item => item.StartMilliseconds).ToArray();
        var i = 0; var j = 0; var overlap = 0d;
        while (i < a.Length && j < b.Length)
        {
            overlap += Math.Max(0, Math.Min(a[i].EndMilliseconds, b[j].EndMilliseconds) - Math.Max(a[i].StartMilliseconds, b[j].StartMilliseconds));
            if (a[i].EndMilliseconds <= b[j].EndMilliseconds) i++; else j++;
        }
        return overlap * 1000;
    }

    private static string ResolveModule(IReadOnlyList<ImageRange> images, ulong address)
    {
        if (address == 0) return "Unknown";
        var match = images.LastOrDefault(image => image.Start <= address && address < image.End);
        return match?.Module ?? "Unknown";
    }

    private static IReadOnlyList<DiagnosticObservation> BuildObservations(TraceReport report)
    {
        var observations = new List<DiagnosticObservation>();
        var interrupt = report.Interrupts.FirstOrDefault();
        if (interrupt is not null)
        {
            var id = $"measurement:{report.SessionId}:interrupt:{EvidencePart(interrupt.Kind)}:{EvidencePart(interrupt.Module)}:p99_us";
            observations.Add(new("Highest observed interrupt tail", "Interrupts", [id],
                $"{interrupt.Distribution.P99Microseconds:F2} µs P99 for {interrupt.Kind.ToUpperInvariant()} in {interrupt.Module}",
                "This identifies concentration in the captured interval; it does not establish causality.",
                "Repeat the same workload and check whether the same module remains concentrated.", report.Quality.IsValid ? "medium" : "low"));
        }
        var thread = report.Threads.FirstOrDefault();
        if (thread is not null)
        {
            var id = $"measurement:{report.SessionId}:thread:{thread.ThreadKey}:ready_p99_us";
            observations.Add(new("Longest target ready-time tail", "Scheduling", [id],
                $"{thread.ReadyTime.P99Microseconds:F2} µs P99 on {thread.ThreadKey}",
                "Ready time is observed scheduler waiting, not proof of a particular device or driver cause.",
                "Repeat the workload and compare this thread-level tail with interrupt overlap.", report.Quality.IsValid ? "medium" : "low"));
        }
        return observations;
    }

    internal static string EvidencePart(string value) => string.Concat(value.ToLowerInvariant().Select(character =>
        char.IsLetterOrDigit(character) || character is '.' or '-' ? character : '_'));
}

internal static class TraceReportExtensions
{
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
        Observations = observations
    };
}
