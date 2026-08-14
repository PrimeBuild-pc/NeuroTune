using System.Management;
using System.Runtime.InteropServices;

namespace NeuroTune;

public sealed class HardwareTopologyService
{
    private const int ErrorInsufficientBuffer = 122;
    private const int CpuSetInformationType = 0;
    private const string AffinitySuffix = @"\Device Parameters\Interrupt Management\Affinity Policy";

    public MachineTopology Collect()
    {
        var host = SystemProfiler.Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem",
            row => $"{row["Manufacturer"]} {row["Model"]}").FirstOrDefault() ?? "";
        var physicalHost = !new[] { "virtual", "vmware", "virtualbox", "kvm", "qemu", "xen", "hyper-v" }
            .Any(marker => host.Contains(marker, StringComparison.OrdinalIgnoreCase));
        var drivers = SystemProfiler.Query("SELECT DeviceID, DriverVersion FROM Win32_PnPSignedDriver WHERE DeviceClass='DISPLAY'",
            row => (Id: Text(row, "DeviceID"), Version: Text(row, "DriverVersion")))
            .Where(item => item.Id.Length > 0).GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Version, StringComparer.OrdinalIgnoreCase);
        var gpus = SystemProfiler.Query("SELECT Name, PNPDeviceID, DriverVersion FROM Win32_VideoController",
            row => (Name: Text(row, "Name"), Id: Text(row, "PNPDeviceID"), Version: Text(row, "DriverVersion")))
            .Where(item => item.Id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
            .Select(item =>
            {
                var vendor = item.Id.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ? "NVIDIA" :
                    item.Id.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ? "AMD" : "Unsupported";
                var version = drivers.GetValueOrDefault(item.Id, item.Version);
                return new GpuDeviceTopology(MeasurementService.Fingerprint(item.Id), item.Name, vendor, version, item.Id,
                    $@"SYSTEM\CurrentControlSet\Enum\{item.Id}{AffinitySuffix}", physicalHost);
            })
            .Where(item => item.Vendor != "Unsupported")
            .ToList();
        return new(ReadProcessors(), gpus);
    }

    public GpuCandidateSet Generate(GpuCandidateRequest request, IReadOnlyList<MeasurementSession> sessions, MachineTopology? topology = null)
    {
        if (sessions.Count < 3) throw new InvalidOperationException("At least three valid baseline sessions are required.");
        if (sessions.Any(item => item.Label != MeasurementLabel.Baseline || item.State != MeasurementSessionState.Completed || item.Report?.Quality.IsValid != true))
            throw new InvalidOperationException("Every selected session must be a completed, quality-valid baseline.");
        if (sessions.Select(item => item.HardwareFingerprint).Distinct(StringComparer.Ordinal).Count() != 1 ||
            sessions.Select(item => item.ConfigurationFingerprint).Distinct(StringComparer.Ordinal).Count() != 1 ||
            sessions.Select(item => item.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            throw new InvalidOperationException("Baseline executable, hardware, and configuration must match.");
        var duration = MeasurementService.Median(sessions.Select(item => (double)item.DurationSeconds));
        if (sessions.Any(item => Math.Abs(item.DurationSeconds - duration) / duration > .10))
            throw new InvalidOperationException("Baseline durations differ by more than 10%.");

        topology ??= Collect();
        var gpu = topology.Gpus.SingleOrDefault(item => item.DeviceKey == request.DeviceKey)
            ?? throw new InvalidOperationException("The selected GPU is unavailable or unsupported.");
        if (!gpu.PhysicalHost) throw new InvalidOperationException("GPU IRQ candidates are disabled on virtualized hardware.");
        if (string.IsNullOrWhiteSpace(gpu.DriverVersion)) throw new InvalidOperationException("The GPU driver identity is unavailable.");

        var fingerprint = sessions[0].HardwareFingerprint;
        var ranked = RankProcessors(topology.Processors, sessions).Take(3).ToList();
        var candidates = ranked.Select(item => new CandidateAction(
            MeasurementService.Fingerprint(fingerprint, gpu.DeviceKey, item.Cpu.ProcessorGroup.ToString(), item.Cpu.LogicalProcessor.ToString())[..24],
            "gpuIrqAffinitySingleCore", gpu.DeviceKey, gpu.Name, item.Cpu.ProcessorGroup, item.Cpu.LogicalProcessor,
            item.Cpu.PhysicalCore, item.Cpu.SmtIndex, item.Cpu.EfficiencyClass, item.Cpu.CacheCluster,
            $"0x{(1UL << item.Cpu.LogicalProcessor):X16}", 4,
            item.InterruptSharePercent, item.TargetRunningMilliseconds, item.ReadyOverlapMicroseconds,
            sessions.SelectMany(session => new[]
            {
                $"measurement:{session.Id}:cpu:{item.Cpu.LogicalProcessor}:interrupt_share_percent",
                $"measurement:{session.Id}:cpu:{item.Cpu.LogicalProcessor}:target_running_ms",
                $"measurement:{session.Id}:cpu:{item.Cpu.LogicalProcessor}:ready_overlap_us"
            }).ToList(), false,
            "Preview only: physical AMD/NVIDIA driver validation and the rollback matrix are incomplete."))
            .ToList();
        return new(fingerprint, request.BaselineSessionIds, candidates);
    }

    internal static IReadOnlyList<RankedProcessor> RankProcessors(IReadOnlyList<CpuTopologyEntry> topology, IReadOnlyList<MeasurementSession> sessions) =>
        // ponytail: AssignmentSetOverride preview is group 0 only; add group-aware policy after >64-LP hardware validation.
        topology.Where(cpu => cpu.ProcessorGroup == 0 && cpu.LogicalProcessor < 64)
            .Select(cpu =>
            {
                var samples = sessions.Select(session => session.Report!.Processors.SingleOrDefault(item => item.LogicalProcessor == cpu.LogicalProcessor)).ToList();
                return samples.Any(item => item is null) ? null : new RankedProcessor(cpu,
                    MeasurementService.Median(samples.Select(item => item!.InterruptSharePercent)),
                    MeasurementService.Median(samples.Select(item => item!.TargetRunningMilliseconds)),
                    MeasurementService.Median(samples.Select(item => item!.ReadyOverlapMicroseconds)));
            })
            .Where(item => item is not null).Cast<RankedProcessor>()
            .GroupBy(item => (item.Cpu.ProcessorGroup, item.Cpu.PhysicalCore))
            .Select(group => group.OrderBy(item => item.InterruptSharePercent)
                .ThenBy(item => item.TargetRunningMilliseconds).ThenBy(item => item.ReadyOverlapMicroseconds).First())
            .OrderBy(item => item.InterruptSharePercent).ThenBy(item => item.TargetRunningMilliseconds)
            .ThenBy(item => item.ReadyOverlapMicroseconds).ToList();

    private static IReadOnlyList<CpuTopologyEntry> ReadProcessors()
    {
        _ = GetSystemCpuSetInformation(IntPtr.Zero, 0, out var length, IntPtr.Zero, 0);
        if (length == 0 || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            throw new InvalidOperationException("Windows CPU-set topology is unavailable.");
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetSystemCpuSetInformation(buffer, length, out length, IntPtr.Zero, 0))
                throw new InvalidOperationException("Windows CPU-set topology could not be read.");
            var raw = new List<(ushort Group, byte Logical, byte Core, byte Efficiency, byte Cache)>();
            for (var offset = 0; offset < length;)
            {
                var current = IntPtr.Add(buffer, offset);
                var size = Marshal.ReadInt32(current);
                if (size < 24 || offset + size > length) throw new InvalidOperationException("Windows returned invalid CPU-set topology data.");
                if (Marshal.ReadInt32(current, 4) == CpuSetInformationType)
                    raw.Add(((ushort)Marshal.ReadInt16(current, 12), Marshal.ReadByte(current, 14), Marshal.ReadByte(current, 15),
                        Marshal.ReadByte(current, 18), Marshal.ReadByte(current, 16)));
                offset += size;
            }
            return raw.GroupBy(item => (item.Group, item.Core)).SelectMany(group => group.OrderBy(item => item.Logical)
                .Select((item, smt) => new CpuTopologyEntry(item.Group, item.Logical, item.Core, (byte)smt, item.Efficiency, item.Cache)))
                .OrderBy(item => item.ProcessorGroup).ThenBy(item => item.LogicalProcessor).ToList();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string Text(ManagementBaseObject row, string name) => row[name]?.ToString()?.Trim() ?? "";

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemCpuSetInformation(IntPtr information, uint bufferLength, out uint returnedLength, IntPtr process, uint flags);
}

internal sealed record RankedProcessor(CpuTopologyEntry Cpu, double InterruptSharePercent, double TargetRunningMilliseconds, double ReadyOverlapMicroseconds);
