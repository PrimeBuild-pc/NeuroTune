using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;

namespace NeuroTune;

public sealed class PerformanceSnapshotService
{
    public async Task<IReadOnlyList<ProcessorTimeSample>> CollectProcessorsAsync()
    {
        var before = ReadProcessorCounters();
        await Task.Delay(1100);
        var after = ReadProcessorCounters();
        var result = after.Where(item => before.ContainsKey(item.Key))
            .Select(item => ProcessorDelta(item.Key, before[item.Key], item.Value)).OfType<ProcessorTimeSample>()
            .OrderBy(item => item.ProcessorGroup).ThenBy(item => item.LogicalProcessor).ToList();
        if (result.Count == 0) throw new InvalidOperationException("Per-processor counters are unavailable or did not advance.");
        return result;
    }

    internal sealed record ProcessorCounters(ulong Timestamp, ulong Idle, ulong Dpc, ulong Isr);

    private static Dictionary<string, ProcessorCounters> ReadProcessorCounters() => SystemProfiler.Query(
        "SELECT Name, Timestamp_Sys100NS, PercentIdleTime, PercentDPCTime, PercentInterruptTime FROM Win32_PerfRawData_Counters_ProcessorInformation",
        row => (Name: row["Name"]?.ToString() ?? "", Counters: new ProcessorCounters(
            Convert.ToUInt64(row["Timestamp_Sys100NS"]), Convert.ToUInt64(row["PercentIdleTime"]),
            Convert.ToUInt64(row["PercentDPCTime"]), Convert.ToUInt64(row["PercentInterruptTime"]))))
        .Where(item => !item.Name.Contains("_Total", StringComparison.OrdinalIgnoreCase)).ToDictionary(item => item.Name, item => item.Counters);

    internal static ProcessorTimeSample? ProcessorDelta(string name, ProcessorCounters before, ProcessorCounters after)
    {
        var coordinates = name.Split(',');
        if (coordinates.Length != 2 || !ushort.TryParse(coordinates[0], out var group) || !byte.TryParse(coordinates[1], out var cpu) ||
            after.Timestamp <= before.Timestamp || after.Idle < before.Idle || after.Dpc < before.Dpc || after.Isr < before.Isr) return null;
        var elapsed = (after.Timestamp - before.Timestamp) / 10_000d;
        var idle = Math.Min(elapsed, (after.Idle - before.Idle) / 10_000d);
        return new(group, cpu, elapsed, elapsed - idle, idle,
            (after.Dpc - before.Dpc) / 10_000d, (after.Isr - before.Isr) / 10_000d);
    }

    public PerformanceSnapshot Collect()
    {
        var snapshot = new PerformanceSnapshot
        {
            CpuLoadPercent = ReadCpuLoad(),
            ProcessCount = CountProcesses(),
            LatencyMs = ReadLatency(),
            ActivePowerPlan = SystemProfiler.Run("powercfg.exe", "/getactivescheme")
        };
        (snapshot.UsedMemoryGb, snapshot.TotalMemoryGb) = ReadMemory();
        return snapshot;
    }

    private static int? ReadCpuLoad()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
            var values = searcher.Get().Cast<ManagementBaseObject>()
                .Select(x => Convert.ToInt32(x["LoadPercentage"] ?? 0)).ToList();
            return values.Count == 0 ? null : (int)Math.Round(values.Average());
        }
        catch { return null; }
    }

    private static (double? Used, double? Total) ReadMemory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            using var os = searcher.Get().Cast<ManagementBaseObject>().FirstOrDefault();
            if (os is null) return (null, null);
            var total = Convert.ToUInt64(os["TotalVisibleMemorySize"] ?? 0) / 1024d / 1024d;
            var free = Convert.ToUInt64(os["FreePhysicalMemory"] ?? 0) / 1024d / 1024d;
            return (Math.Max(0, total - free), total);
        }
        catch { return (null, null); }
    }

    private static int CountProcesses()
    {
        try
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes) process.Dispose();
            return processes.Length;
        }
        catch { return 0; }
    }

    private static long? ReadLatency()
    {
        try
        {
            using var ping = new Ping();
            var samples = Enumerable.Range(0, 3)
                .Select(_ => ping.Send("1.1.1.1", 1000))
                .Where(x => x.Status == IPStatus.Success)
                .Select(x => x.RoundtripTime).ToList();
            return samples.Count == 0 ? null : (long)Math.Round(samples.Average());
        }
        catch { return null; }
    }
}

public sealed record ProcessorTimeSample(ushort ProcessorGroup, byte LogicalProcessor, double WindowMilliseconds,
    double BusyMilliseconds, double IdleMilliseconds, double DpcMilliseconds, double IsrMilliseconds);
