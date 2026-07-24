using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;

namespace NeuroTune;

public sealed class PerformanceSnapshotService
{
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
