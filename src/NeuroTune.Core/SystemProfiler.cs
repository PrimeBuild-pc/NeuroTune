using Microsoft.Win32;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NeuroTune;

public sealed class SystemProfiler
{
    public SystemProfile Collect(Action<string>? progress = null, bool optionalTelemetryConsent = false)
    {
        var profile = new SystemProfile();
        RunPhase("Hardware and firmware", () =>
        {
            profile.OperatingSystem = ReadOperatingSystem();
            profile.Cpu = ReadCpu();
            profile.Gpus = Query("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController",
                row => $"{row["Name"]} (driver {row["DriverVersion"]}, date {FormatWmiDate(row["DriverDate"])})");
            profile.Memory = ReadMemory();
            profile.Disks = ReadDisks();
            profile.HardwareCapabilities = ReadHardwareCapabilities();
            profile.FirmwareAndMemory = ReadFirmwareAndMemory();
            profile.ComponentIdentities = ReadComponentIdentities();
            profile.FactoryBaselines = ComponentBaselineCatalog.Compare(profile.ComponentIdentities);
            profile.TelemetryCapabilities = TelemetryProcessClient.QueryCapabilities(optionalTelemetryConsent);
        });
        RunPhase("Windows, boot, and Registry", () =>
        {
            profile.ActivePowerPlan = Run("powercfg.exe", "/getactivescheme");
            profile.WindowsSettings = ReadWindowsSettings();
            profile.GamingSettings = ReadGamingSettings();
            profile.PerformanceRegistry = ReadPerformanceRegistry();
            profile.BootConfiguration = ReadBootConfiguration();
        });
        RunPhase("Network and device stack", () =>
        {
            profile.NetworkAdapters = ReadNetworkAdapters();
            profile.NetworkSettings = ReadNetworkSettings();
            profile.RelevantDrivers = ReadRelevantDrivers();
            profile.DeviceIssues = ReadDeviceIssues();
        });
        RunPhase("Installed and active software", () =>
        {
            profile.InstalledSoftware = ReadInstalledSoftware();
            profile.TopProcesses = ReadTopProcesses();
            profile.StartupItems = ReadStartupItems();
            profile.SoftwareSignals = ReadSoftwareSignals(profile);
        });
        RunPhase("Services and local conflict checks", () =>
        {
            profile.AutomaticServices = Query("SELECT Name FROM Win32_Service WHERE StartMode='Auto' AND State='Running'",
                row => row["Name"]?.ToString() ?? "").Where(x => x.Length > 0).Take(100).ToList();
            profile.PolicyConflicts = FindPolicyConflicts(profile);
        });
        return profile;

        void RunPhase(string name, Action collect)
        {
            progress?.Invoke(name);
            var before = CountFacts(profile);
            var timer = Stopwatch.StartNew();
            collect();
            timer.Stop();
            profile.ScanPhases.Add(new(name, timer.ElapsedMilliseconds, CountFacts(profile) - before));
        }
    }

    private static int CountFacts(SystemProfile profile) =>
        profile.Gpus.Count + profile.Disks.Count + profile.WindowsSettings.Count + profile.GamingSettings.Count +
        profile.NetworkAdapters.Count + profile.NetworkSettings.Count + profile.HardwareCapabilities.Count +
        profile.FirmwareAndMemory.Count + profile.ComponentIdentities.Count + profile.FactoryBaselines.Count +
        profile.TelemetryCapabilities.Count + profile.BootConfiguration.Count + profile.PerformanceRegistry.Count +
        profile.PolicyConflicts.Count + profile.InstalledSoftware.Count + profile.RelevantDrivers.Count +
        profile.DeviceIssues.Count + profile.SoftwareSignals.Count + profile.TopProcesses.Count +
        profile.StartupItems.Count + profile.AutomaticServices.Count;

    private static string FormatWmiDate(object? value)
    {
        try { return $"{ManagementDateTimeConverter.ToDateTime(value?.ToString() ?? ""):yyyy-MM-dd}"; }
        catch { return "Unavailable"; }
    }

    private static string ReadWmiText(object? value) => value is ushort[] characters
        ? new string(characters.TakeWhile(character => character != 0).Select(character => (char)character).ToArray())
        : value?.ToString() ?? "";

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

    private static Dictionary<string, string> ReadPerformanceRegistry() => ProbeCatalog.Registry.ToDictionary(
        probe => probe.ProfileKey,
        probe => ReadRegistry(
            probe.Hive == RegistryProbeHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine,
            probe.Path,
            probe.ValueName),
        StringComparer.Ordinal);

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
        var cpuClock = Query("SELECT Name, ProcessorFrequency, PercentofMaximumFrequency, PercentProcessorPerformance FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'",
            row => $"sampled={row["ProcessorFrequency"]} MHz; maximum-frequency={row["PercentofMaximumFrequency"]}%; performance={row["PercentProcessorPerformance"]}%").FirstOrDefault() ?? "Unavailable";
        var thermalZones = Query(@"root\WMI", "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", row =>
        {
            var celsius = (Convert.ToDouble(row["CurrentTemperature"] ?? 0) / 10d) - 273.15d;
            return $"{row["InstanceName"]}: {celsius:0.#} °C";
        });
        var monitors = Query(@"root\WMI", "SELECT ManufacturerName, UserFriendlyName FROM WmiMonitorID", row =>
            $"{ReadWmiText(row["ManufacturerName"])} {ReadWmiText(row["UserFriendlyName"])}".Trim());
        var storageHealth = Query(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, Temperature, Wear, ReadErrorsTotal, WriteErrorsTotal FROM MSFT_StorageReliabilityCounter",
            row => $"device={row["DeviceId"]}; temperature={row["Temperature"]} °C; wear={row["Wear"]}%; read-errors={row["ReadErrorsTotal"]}; write-errors={row["WriteErrorsTotal"]}");
        return new()
        {
            ["Battery"] = battery.Count == 0 ? "Not detected" : string.Join("; ", battery),
            ["Displays"] = displays.Count == 0 ? "Unavailable" : string.Join("; ", displays),
            ["Monitor identity"] = monitors.Count == 0 ? "Unavailable" : string.Join("; ", monitors),
            ["Page file and system type"] = pageFile,
            ["Virtualization-based security status"] = deviceGuard,
            ["CPU clock sample"] = cpuClock,
            ["Performance counter"] = $"high-resolution={Stopwatch.IsHighResolution}; frequency={Stopwatch.Frequency} Hz",
            ["ACPI thermal zones"] = thermalZones.Count == 0 ? "Unavailable" : string.Join("; ", thermalZones),
            ["Storage reliability"] = storageHealth.Count == 0 ? "Unavailable" : string.Join("; ", storageHealth)
        };
    }

    private static Dictionary<string, string> ReadFirmwareAndMemory()
    {
        var facts = new Dictionary<string, string>
        {
            ["Motherboard"] = Query("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard",
                row => $"{row["Manufacturer"]} {row["Product"]} {row["Version"]}").FirstOrDefault() ?? "Unavailable",
            ["BIOS"] = Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS",
                row => $"{row["Manufacturer"]} {row["SMBIOSBIOSVersion"]} ({FormatWmiDate(row["ReleaseDate"])})").FirstOrDefault() ?? "Unavailable",
            ["Firmware mode"] = ReadFirmwareType(),
            ["Secure Boot"] = ReadRegistry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled") switch
            {
                "1" => "Enabled",
                "0" => "Disabled",
                var value => value
            },
            ["CPU identity"] = Query("SELECT Name, Manufacturer, MaxClockSpeed, CurrentClockSpeed, Revision FROM Win32_Processor",
                row => $"{row["Name"]}; vendor={row["Manufacturer"]}; max={row["MaxClockSpeed"]} MHz; sampled={row["CurrentClockSpeed"]} MHz; revision={row["Revision"]}").FirstOrDefault() ?? "Unavailable",
            ["CPUID vendor"] = ReadCpuIdVendor(),
            ["Low-level telemetry provider"] = DetectPawnIo()
        };
        var modules = Query("SELECT BankLabel, Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, ConfiguredVoltage, MinVoltage, MaxVoltage, SMBIOSMemoryType FROM Win32_PhysicalMemory", row =>
        {
            var size = Convert.ToUInt64(row["Capacity"] ?? 0) / 1024d / 1024d / 1024d;
            return $"{row["BankLabel"]}: {row["Manufacturer"]} {row["PartNumber"]}; {size:0.#} GB; SMBIOS={row["Speed"]} MT/s; configured={row["ConfiguredClockSpeed"]} MT/s; voltage={row["ConfiguredVoltage"]} mV; min/max={row["MinVoltage"]}/{row["MaxVoltage"]} mV; type={row["SMBIOSMemoryType"]}";
        });
        for (var index = 0; index < modules.Count; index++) facts[$"DIMM {index + 1}"] = modules[index];
        facts["Memory profile assessment"] = AssessMemoryProfile();
        facts["PBO/CPU overclock assessment"] = "Not exposed reliably by Windows; low-level telemetry required for a supported conclusion";
        return facts;

        string AssessMemoryProfile()
        {
            var clocks = Query("SELECT Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory", row =>
                (Speed: Convert.ToUInt32(row["Speed"] ?? 0), Configured: Convert.ToUInt32(row["ConfiguredClockSpeed"] ?? 0), Type: Convert.ToUInt32(row["SMBIOSMemoryType"] ?? 0)));
            if (clocks.Count == 0) return "Unavailable";
            if (clocks.Any(x => x.Type == 26 && x.Configured > 3_200))
                return "Possible XMP/DOCP or manual DDR4 profile: configured speed exceeds 3200 MT/s; heuristic only, timings and voltage are not confirmed";
            if (clocks.Any(x => x.Speed > 0 && x.Configured > x.Speed))
                return "Possible XMP/DOCP/EXPO or manual profile: configured speed exceeds the SMBIOS speed; heuristic only";
            if (clocks.Select(x => x.Configured).Where(x => x > 0).Distinct().Count() > 1)
                return "Configured DIMM speeds differ; verify channel and firmware training";
            return "No profile can be confirmed from SMBIOS/WMI alone";
        }
    }

    private static Dictionary<string, string> ReadComponentIdentities()
    {
        var identities = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CPU specification ID"] = ReadCpuSpecificationId(),
            ["CPU model"] = Query("SELECT Name FROM Win32_Processor", row => row["Name"]?.ToString()?.Trim() ?? "").FirstOrDefault(value => value.Length > 0) ?? "Unavailable",
            ["Motherboard"] = Query("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard", row => $"{row["Manufacturer"]}|{row["Product"]}|{row["Version"]}").FirstOrDefault() ?? "Unavailable",
            ["BIOS"] = Query("SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS", row => $"{row["Manufacturer"]}|{row["SMBIOSBIOSVersion"]}").FirstOrDefault() ?? "Unavailable"
        };
        var gpus = Query("SELECT Name, PNPDeviceID FROM Win32_VideoController", row =>
        {
            var parts = row["PNPDeviceID"]?.ToString()?.Split('\\') ?? [];
            var pciId = parts.Length > 1 ? parts[1] : "Unavailable";
            return $"{pciId}|{row["Name"]?.ToString()?.Trim()}";
        });
        for (var index = 0; index < gpus.Count; index++) identities[$"GPU {index + 1} specification ID"] = gpus[index];

        var modules = Query("SELECT Manufacturer, PartNumber, Capacity, ConfiguredClockSpeed FROM Win32_PhysicalMemory", row =>
        {
            var manufacturer = row["Manufacturer"]?.ToString()?.Trim() ?? "";
            var partNumber = row["PartNumber"]?.ToString()?.Trim() ?? "";
            return (Manufacturer: manufacturer, PartNumber: partNumber,
                Capacity: Convert.ToUInt64(row["Capacity"] ?? 0), Configured: Convert.ToUInt32(row["ConfiguredClockSpeed"] ?? 0));
        });
        for (var index = 0; index < modules.Count; index++)
        {
            var module = modules[index];
            if (module.Manufacturer.Length == 0 || module.PartNumber.Length == 0) continue;
            identities[$"DIMM {index + 1} specification ID"] = $"{module.Manufacturer}|{module.PartNumber}|{module.Capacity}";
            identities[$"DIMM {index + 1} configured rate"] = $"{module.Configured} MT/s";
        }
        return identities;
    }

    private static string ReadCpuSpecificationId()
    {
        if (!System.Runtime.Intrinsics.X86.X86Base.IsSupported) return "Unavailable";
        var vendor = ReadCpuIdVendor();
        var eax = System.Runtime.Intrinsics.X86.X86Base.CpuId(1, 0).Eax;
        var stepping = eax & 0xf;
        var baseModel = (eax >> 4) & 0xf;
        var baseFamily = (eax >> 8) & 0xf;
        var family = baseFamily == 0xf ? baseFamily + ((eax >> 20) & 0xff) : baseFamily;
        var model = baseFamily is 0x6 or 0xf ? baseModel + (((eax >> 16) & 0xf) << 4) : baseModel;
        return $"{vendor}-{family}-{model:X}-{stepping}";
    }

    private static string ReadFirmwareType() => GetFirmwareType(out var type)
        ? type switch { FirmwareType.Bios => "Legacy BIOS", FirmwareType.Uefi => "UEFI", _ => "Unknown" }
        : "Unavailable";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFirmwareType(out FirmwareType firmwareType);

    private enum FirmwareType { Unknown, Bios, Uefi, Max }

    private static string DetectPawnIo()
    {
        try
        {
            using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return service is null
                ? "Not installed; no kernel driver was downloaded or installed"
                : "PawnIO service detected; low-level access remains disabled until a reviewed integration is explicitly approved";
        }
        catch { return "Unavailable"; }
    }

    private static string ReadCpuIdVendor()
    {
        if (!System.Runtime.Intrinsics.X86.X86Base.IsSupported) return "Unavailable";
        var registers = System.Runtime.Intrinsics.X86.X86Base.CpuId(0, 0);
        Span<byte> vendor = stackalloc byte[12];
        BitConverter.TryWriteBytes(vendor[..4], registers.Ebx);
        BitConverter.TryWriteBytes(vendor[4..8], registers.Edx);
        BitConverter.TryWriteBytes(vendor[8..], registers.Ecx);
        return System.Text.Encoding.ASCII.GetString(vendor);
    }

    private static Dictionary<string, string> ReadBootConfiguration()
    {
        var bcd = Run("bcdedit.exe", "/enum", "{current}");
        var facts = new Dictionary<string, string>
        {
            ["Available sleep states"] = Run("powercfg.exe", "/a"),
            ["TRIM state"] = Run("fsutil.exe", "behavior", "query", "DisableDeleteNotify"),
            ["File-system filters"] = Run("fltmc.exe", "filters")
        };
        foreach (var setting in new[] { "useplatformclock", "disabledynamictick", "tscsyncpolicy", "useplatformtick", "x2apicpolicy", "hypervisorlaunchtype", "numproc", "truncatememory", "removememory" })
            facts[$"BCD {setting}"] = ReadCommandSetting(bcd, setting);
        return facts;
    }

    private static string ReadCommandSetting(string output, string setting)
    {
        if (output == "Unavailable") return output;
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(x => x.TrimStart().StartsWith(setting, StringComparison.OrdinalIgnoreCase));
        return line is null ? "Not configured" : line.Trim()[setting.Length..].Trim();
    }

    private static List<string> ReadInstalledSoftware()
    {
        var software = new List<string>();
        foreach (var (hive, path) in new[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall")
        })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
                if (root is null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    using var app = root.OpenSubKey(name);
                    var displayName = app?.GetValue("DisplayName")?.ToString();
                    if (string.IsNullOrWhiteSpace(displayName)) continue;
                    software.Add($"{displayName} | {app?.GetValue("DisplayVersion")} | {app?.GetValue("Publisher")}");
                }
            }
            catch { }
        }
        return software.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(500).ToList();
    }

    private static List<string> ReadRelevantDrivers() => Query(
        "SELECT DeviceName, DeviceClass, Manufacturer, DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceClass='DISPLAY' OR DeviceClass='NET' OR DeviceClass='MEDIA' OR DeviceClass='HDC' OR DeviceClass='SCSIADAPTER' OR DeviceClass='SYSTEM'",
        row => $"{row["DeviceClass"]}: {row["DeviceName"]} | {row["Manufacturer"]} | {row["DriverVersion"]} | {FormatWmiDate(row["DriverDate"])}")
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(160).ToList();

    private static List<string> ReadDeviceIssues() => Query(
        "SELECT Name, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0",
        row => $"{row["PNPClass"]}: {row["Name"]} (device error {row["ConfigManagerErrorCode"]})")
        .Where(x => !string.IsNullOrWhiteSpace(x)).Take(100).ToList();

    private static List<string> ReadSoftwareSignals(SystemProfile profile)
    {
        var haystack = profile.InstalledSoftware.Concat(profile.TopProcesses).Concat(profile.StartupItems).ToList();
        var patterns = new[]
        {
            "Afterburner", "RTSS", "RivaTuner", "Ryzen Master", "Intel Extreme Tuning", "ThrottleStop",
            "Process Lasso", "ParkControl", "Armoury Crate", "AI Suite", "Gigabyte Control Center", "MSI Center",
            "NVIDIA App", "GeForce Experience", "AMD Software", "OBS", "Discord", "Overwolf", "ExitLag",
            "NordVPN", "ExpressVPN", "WireGuard", "OpenVPN", "Hyper-V", "VMware", "VirtualBox",
            "Vanguard", "Easy Anti-Cheat", "BattlEye", "Libre Hardware Monitor", "HWiNFO", "CPU-Z", "PawnIO"
        };
        return patterns.Where(pattern => haystack.Any(item => item.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            .Select(pattern => $"Detected software family: {pattern}").ToList();
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

        var advanced = Query(@"root\StandardCimv2", "SELECT InterfaceDescription, DisplayName, DisplayValue FROM MSFT_NetAdapterAdvancedPropertySettingData",
            row => $"{row["InterfaceDescription"]}: {row["DisplayName"]}={row["DisplayValue"]}")
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(160);
        return new()
        {
            ["Latency to 1.1.1.1"] = latency,
            ["Nagle overrides"] = $"{nagleOverrides} interfaces",
            ["Global TCP settings"] = Run("netsh.exe", "interface", "tcp", "show", "global"),
            ["Adapter advanced properties"] = string.Join("\n", advanced),
            ["Installed network components"] = Run("netcfg.exe", "-s", "n"),
            ["WinHTTP proxy"] = Run("netsh.exe", "winhttp", "show", "proxy")
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
