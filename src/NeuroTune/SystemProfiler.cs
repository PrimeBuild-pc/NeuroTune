using Microsoft.Win32;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace NeuroTune;

public sealed class SystemProfiler
{
    public SystemProfile Collect()
    {
        var profile = new SystemProfile
        {
            OperatingSystem = ReadOperatingSystem(),
            Cpu = ReadCpu(),
            Gpus = Query("SELECT Name, DriverVersion FROM Win32_VideoController",
                row => $"{row["Name"]} (driver {row["DriverVersion"]})"),
            Memory = ReadMemory(),
            Disks = ReadDisks(),
            ActivePowerPlan = Run("powercfg.exe", "/getactivescheme"),
            WindowsSettings = ReadWindowsSettings(),
            GamingSettings = ReadGamingSettings(),
            NetworkAdapters = ReadNetworkAdapters(),
            NetworkSettings = ReadNetworkSettings(),
            TopProcesses = ReadTopProcesses(),
            StartupItems = ReadStartupItems(),
            AutomaticServices = Query("SELECT Name FROM Win32_Service WHERE StartMode='Auto' AND State='Running'",
                row => row["Name"]?.ToString() ?? "").Where(x => x.Length > 0).Take(60).ToList()
        };
        return profile;
    }

    private static string ReadOperatingSystem() => Query(
        "SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem",
        row => $"{row["Caption"]}, versione {row["Version"]}, build {row["BuildNumber"]}").FirstOrDefault()
        ?? Environment.OSVersion.VersionString;

    private static string ReadCpu() => Query(
        "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor",
        row => $"{row["Name"]} — {row["NumberOfCores"]} core / {row["NumberOfLogicalProcessors"]} thread")
        .FirstOrDefault() ?? "Non disponibile";

    private static string ReadMemory()
    {
        var modules = Query("SELECT Capacity, Speed FROM Win32_PhysicalMemory",
            row => (Capacity: Convert.ToUInt64(row["Capacity"] ?? 0), Speed: row["Speed"]?.ToString()));
        var totalGb = modules.Aggregate(0UL, (total, module) => total + module.Capacity) / 1024d / 1024d / 1024d;
        var speeds = string.Join(", ", modules.Select(x => x.Speed).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        return totalGb > 0 ? $"{totalGb:0.#} GB{(speeds.Length > 0 ? $" @ {speeds} MHz" : "")}" : "Non disponibile";
    }

    private static List<string> ReadDisks()
    {
        var disks = Query(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk", row =>
        {
            var size = Convert.ToUInt64(row["Size"] ?? 0) / 1024d / 1024d / 1024d;
            var media = Convert.ToUInt16(row["MediaType"] ?? 0) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Non specificato" };
            var bus = Convert.ToUInt16(row["BusType"] ?? 0) switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", _ => "Altro" };
            return $"{row["FriendlyName"]} — {media}/{bus} — {size:0} GB";
        });
        return disks.Count > 0 ? disks : Query("SELECT Model, MediaType, Size FROM Win32_DiskDrive", row =>
        {
            var size = Convert.ToUInt64(row["Size"] ?? 0) / 1024d / 1024d / 1024d;
            return $"{row["Model"]} — {row["MediaType"]} — {size:0} GB";
        });
    }

    private static Dictionary<string, string> ReadWindowsSettings() => new()
    {
        ["Telemetria policy"] = ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry")
    };

    private static Dictionary<string, string> ReadGamingSettings()
    {
        var globalGpu = ReadRegistry(Registry.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings");
        return new()
        {
            ["Game Mode"] = ReadRegistry(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"),
            ["HAGS"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"),
            ["Game DVR"] = ReadRegistry(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"),
            ["VRR"] = globalGpu.Contains("VRROptimizeEnable=1", StringComparison.OrdinalIgnoreCase) ? "Attivo" : "Non configurato/Disattivo"
        };
    }

    private static string ReadRegistry(RegistryKey hive, string path, string name)
    {
        try { return hive.OpenSubKey(path)?.GetValue(name)?.ToString() ?? "Non configurato"; }
        catch { return "Non disponibile"; }
    }

    private static List<string> ReadNetworkAdapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up &&
                            x.NetworkInterfaceType is not NetworkInterfaceType.Loopback)
                .Select(x => $"{x.Description} — {x.Speed / 1_000_000d:0} Mbps — {x.GetIPProperties().DnsAddresses.Count} DNS")
                .ToList();
        }
        catch { return []; }
    }

    private static Dictionary<string, string> ReadNetworkSettings()
    {
        string latency;
        try
        {
            using var ping = new Ping();
            var reply = ping.Send("1.1.1.1", 1200);
            latency = reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} ms" : reply.Status.ToString();
        }
        catch { latency = "Non disponibile"; }

        var nagleOverrides = 0;
        try
        {
            using var interfaces = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
            nagleOverrides = interfaces?.GetSubKeyNames().Count(name =>
            {
                using var adapter = interfaces.OpenSubKey(name);
                return adapter?.GetValue("TcpAckFrequency") is not null || adapter?.GetValue("TCPNoDelay") is not null;
            }) ?? 0;
        }
        catch { }

        return new()
        {
            ["Latenza verso 1.1.1.1"] = latency,
            ["Override Nagle"] = $"{nagleOverrides} interfacce",
            ["TCP globale"] = Run("netsh.exe", "interface", "tcp", "show", "global")
        };
    }

    private static List<string> ReadTopProcesses()
    {
        return Process.GetProcesses().Select(process =>
        {
            try { return (Name: process.ProcessName, Memory: process.WorkingSet64); }
            catch { return (Name: "", Memory: 0L); }
            finally { process.Dispose(); }
        }).Where(x => x.Name.Length > 0).OrderByDescending(x => x.Memory).Take(15)
          .Select(x => x.Name).ToList();
    }

    private static List<string> ReadStartupItems()
    {
        var items = new List<string>();
        ReadRunKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", items);
        ReadRunKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", items);
        return items.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
    }

    private static void ReadRunKey(RegistryKey hive, string path, List<string> output)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is not null) output.AddRange(key.GetValueNames());
        }
        catch { }
    }

    private static List<T> Query<T>(string query, Func<ManagementBaseObject, T> map) => Query(@"root\cimv2", query, map);

    private static List<T> Query<T>(string scope, string query, Func<ManagementBaseObject, T> map)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope($@"\\.\{scope}"), new ObjectQuery(query));
            return searcher.Get().Cast<ManagementBaseObject>().Select(map).ToList();
        }
        catch { return []; }
    }

    internal static string Run(string fileName, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException($"Impossibile avviare {fileName}.");
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : "Non disponibile";
        }
        catch { return "Non disponibile"; }
    }
}

public static class ProfileSanitizer
{
    public static string Serialize(SystemProfile profile)
    {
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        foreach (var value in new[] { Environment.UserName, Environment.MachineName })
            if (!string.IsNullOrWhiteSpace(value)) json = json.Replace(value, "[redatto]", StringComparison.OrdinalIgnoreCase);
        return json;
    }
}
