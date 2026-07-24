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
            HardwareCapabilities = ReadHardwareCapabilities(),
            PerformanceRegistry = ReadPerformanceRegistry(),
            TopProcesses = ReadTopProcesses(),
            StartupItems = ReadStartupItems(),
            AutomaticServices = Query("SELECT Name FROM Win32_Service WHERE StartMode='Auto' AND State='Running'",
                row => row["Name"]?.ToString() ?? "").Where(x => x.Length > 0).Take(60).ToList()
        };
        profile.PolicyConflicts = FindPolicyConflicts(profile);
        return profile;
    }

    private static string ReadOperatingSystem() => Query(
        "SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem",
        row => $"{row["Caption"]}, version {row["Version"]}, build {row["BuildNumber"]}").FirstOrDefault()
        ?? Environment.OSVersion.VersionString;

    private static string ReadCpu() => Query(
        "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor",
        row => $"{row["Name"]} — {row["NumberOfCores"]} cores / {row["NumberOfLogicalProcessors"]} threads")
        .FirstOrDefault() ?? "Unavailable";

    private static string ReadMemory()
    {
        var modules = Query("SELECT Capacity, Speed FROM Win32_PhysicalMemory",
            row => (Capacity: Convert.ToUInt64(row["Capacity"] ?? 0), Speed: row["Speed"]?.ToString()));
        var totalGb = modules.Aggregate(0UL, (total, module) => total + module.Capacity) / 1024d / 1024d / 1024d;
        var speeds = string.Join(", ", modules.Select(x => x.Speed).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        return totalGb > 0 ? $"{totalGb:0.#} GB{(speeds.Length > 0 ? $" @ {speeds} MHz" : "")}" : "Unavailable";
    }

    private static List<string> ReadDisks()
    {
        var disks = Query(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk", row =>
        {
            var size = Convert.ToUInt64(row["Size"] ?? 0) / 1024d / 1024d / 1024d;
            var media = Convert.ToUInt16(row["MediaType"] ?? 0) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Unspecified" };
            var bus = Convert.ToUInt16(row["BusType"] ?? 0) switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", _ => "Other" };
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
        ["Telemetry policy"] = ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry"),
        ["Power throttling"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff"),
        ["Visual effects"] = ReadRegistry(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting")
    };

    private static Dictionary<string, string> ReadGamingSettings()
    {
        var globalGpu = ReadRegistry(Registry.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings");
        return new()
        {
            ["Game Mode"] = ReadRegistry(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"),
            ["HAGS"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"),
            ["Game DVR"] = ReadRegistry(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"),
            ["VRR"] = globalGpu.Contains("VRROptimizeEnable=1", StringComparison.OrdinalIgnoreCase) ? "Enabled" : "Not configured/Disabled"
        };
    }

    private static Dictionary<string, string> ReadPerformanceRegistry() => new()
    {
        [@"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled"] = ReadRegistry(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"),
        [@"HKCU\Software\Microsoft\GameBar\AllowAutoGameMode"] = ReadRegistry(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode"),
        [@"HKCU\System\GameConfigStore\GameDVR_Enabled"] = ReadRegistry(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"),
        [@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled"] = ReadRegistry(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"),
        [@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR"] = ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR"),
        [@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"),
        [@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\VisualFXSetting"] = ReadRegistry(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting"),
        [@"HKCU\Control Panel\Mouse\MouseSpeed"] = ReadRegistry(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed"),
        [@"HKCU\Control Panel\Mouse\MouseThreshold1"] = ReadRegistry(Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1"),
        [@"HKCU\Control Panel\Mouse\MouseThreshold2"] = ReadRegistry(Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2"),
        [@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling\PowerThrottlingOff"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff"),
        [@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\NetworkThrottlingIndex"] = ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex"),
        [@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\SystemResponsiveness"] = ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness"),
        [@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\LargeSystemCache"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "LargeSystemCache"),
        [@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\DisablePagingExecutive"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive"),
        [@"HKLM\SOFTWARE\Microsoft\Windows\Dwm\OverlayTestMode"] = ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode"),
        [@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation"),
        [@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\GPU Priority"] = ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority"),
        [@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\Priority"] = ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority"),
        [@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\Scheduling Category"] = ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category"),
        [@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\SFIO Priority"] = ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "SFIO Priority"),
        [@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\TcpTimedWaitDelay"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "TcpTimedWaitDelay"),
        [@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\MaxUserPort"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "MaxUserPort"),
        [@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\DefaultTTL"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "DefaultTTL"),
        [@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\TdrDelay"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay"),
        [@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\TdrDdiDelay"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDdiDelay"),
        [@"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled"),
        [@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled"),
        [@"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\NtfsDisableLastAccessUpdate"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate"),
        [@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings"] = ReadRegistry(Registry.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings")
    };

    private static Dictionary<string, string> ReadHardwareCapabilities()
    {
        var battery = Query("SELECT Name, BatteryStatus FROM Win32_Battery", row => $"{row["Name"]} (status {row["BatteryStatus"]})");
        var displays = Query("SELECT VideoModeDescription, CurrentRefreshRate FROM Win32_VideoController",
            row => $"{row["VideoModeDescription"]} @ {row["CurrentRefreshRate"]} Hz").Where(x => !x.StartsWith(" @", StringComparison.Ordinal)).ToList();
        var pageFile = Query("SELECT AutomaticManagedPagefile, PCSystemType FROM Win32_ComputerSystem", row =>
        {
            var form = Convert.ToUInt16(row["PCSystemType"] ?? 0) switch { 1 => "desktop", 2 => "laptop", 3 => "workstation", _ => "unspecified" };
            return $"Windows-managed={row["AutomaticManagedPagefile"]}; form-factor={form}";
        }).FirstOrDefault() ?? "Unavailable";
        var deviceGuard = Query(@"root\Microsoft\Windows\DeviceGuard", "SELECT VirtualizationBasedSecurityStatus FROM Win32_DeviceGuard", row =>
            Convert.ToUInt32(row["VirtualizationBasedSecurityStatus"] ?? 0) switch
            {
                0 => "Disabled",
                1 => "Enabled but not running",
                2 => "Enabled and running",
                _ => "Unknown"
            }).FirstOrDefault() ?? "Unavailable";
        return new()
        {
            ["Battery"] = battery.Count == 0 ? "Not detected" : string.Join("; ", battery),
            ["Displays"] = displays.Count == 0 ? "Unavailable" : string.Join("; ", displays),
            ["Page file and system type"] = pageFile,
            ["Virtualization-based security status"] = deviceGuard
        };
    }

    private static List<string> FindPolicyConflicts(SystemProfile profile)
    {
        var values = profile.PerformanceRegistry;
        var findings = new List<string>();
        string Get(string suffix) => values.First(x => x.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).Value;
        if (Get(@"GameDVR\AllowGameDVR") == "0" && Get(@"GameDVR\AppCaptureEnabled") == "1")
            findings.Add("Game capture is enabled for the user but disabled by machine policy; the policy takes precedence.");
        if (Get(@"GameBar\AllowAutoGameMode") == "0" && Get(@"GameBar\AutoGameModeEnabled") == "1")
            findings.Add("Automatic Game Mode is enabled by the user but blocked by its user policy value.");
        if (Get(@"PowerThrottling\PowerThrottlingOff") == "1" && profile.HardwareCapabilities["Battery"] != "Not detected")
            findings.Add("System-wide power throttling is disabled on a battery-powered device.");
        if (Get(@"Memory Management\LargeSystemCache") == "1")
            findings.Add("The server-oriented LargeSystemCache override is enabled on this Windows client.");
        if (Get(@"Memory Management\DisablePagingExecutive") == "1")
            findings.Add("A manual DisablePagingExecutive memory-management override is enabled.");
        if (Get(@"Dwm\OverlayTestMode") != "Not configured")
            findings.Add("A manual Desktop Window Manager overlay override is present.");
        if (Get(@"GraphicsDrivers\TdrDelay") != "Not configured" || Get(@"GraphicsDrivers\TdrDdiDelay") != "Not configured")
            findings.Add("Manual GPU timeout detection and recovery delays are configured.");
        var tcpGlobals = new[] { @"Parameters\TcpTimedWaitDelay", @"Parameters\MaxUserPort", @"Parameters\DefaultTTL" }
            .Count(suffix => Get(suffix) != "Not configured");
        if (tcpGlobals > 0) findings.Add($"{tcpGlobals} manual global TCP Registry overrides are configured.");
        if (!profile.NetworkSettings["Nagle overrides"].StartsWith("0 ", StringComparison.Ordinal))
            findings.Add($"Manual TCP latency overrides were found on {profile.NetworkSettings["Nagle overrides"]}.");
        return findings;
    }

    private static string ReadRegistry(RegistryKey hive, string path, string name)
    {
        try { return hive.OpenSubKey(path)?.GetValue(name)?.ToString() ?? "Not configured"; }
        catch { return "Unavailable"; }
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
        catch { latency = "Unavailable"; }

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
            ["Latency to 1.1.1.1"] = latency,
            ["Nagle overrides"] = $"{nagleOverrides} interfaces",
            ["Global TCP settings"] = Run("netsh.exe", "interface", "tcp", "show", "global")
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
            var start = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {fileName}.");
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : "Unavailable";
        }
        catch { return "Unavailable"; }
    }
}

public static class ProfileSanitizer
{
    public static string Serialize(SystemProfile profile) =>
        Redact(JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));

    public static string Redact(string value)
    {
        foreach (var identity in new[] { Environment.UserName, Environment.MachineName })
            if (!string.IsNullOrWhiteSpace(identity)) value = value.Replace(identity, "[redacted]", StringComparison.OrdinalIgnoreCase);
        return value;
    }
}
