using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NeuroTune;

public sealed class OptimizationAction : IReversibleAction
{
    private readonly Func<ActionAvailability> _inspect;
    private readonly Func<string> _capture;
    private readonly Action _apply;
    private readonly Action<string> _restore;
    private readonly Func<bool> _verify;

    public OptimizationAction(string id, string name, string description, string category, RiskLevel risk,
        bool requiresRestart, string? registryExportPath, Func<ActionAvailability> inspect, Func<string> capture,
        Action apply, Action<string> restore, Func<bool> verify, IReadOnlyList<string>? supportedWindowsBuilds = null,
        IReadOnlyList<string>? supportedHardware = null, IReadOnlyList<string>? evidenceRequirements = null,
        IReadOnlyList<string>? sources = null, IReadOnlyList<string>? sideEffects = null)
    {
        Definition = new(id, name, description, category, risk, requiresRestart, registryExportPath,
            supportedWindowsBuilds ?? ["Windows 11"],
            supportedHardware ?? ["Any hardware supported by the installed Windows build"],
            evidenceRequirements ?? [registryExportPath is null ? "Exact active power-scheme identifier" : $"Exact local state at {registryExportPath}"],
            sources ?? ["Microsoft Windows platform behavior plus exact local state inspection"],
            sideEffects ?? [requiresRestart ? "Takes effect after a Windows restart" : "May change the current Windows preference"]);
        Definition.Validate();
        _inspect = inspect;
        _capture = capture;
        _apply = apply;
        _restore = restore;
        _verify = verify;
    }

    public ActionDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Category => Definition.Category;
    public RiskLevel Risk => Definition.Risk;
    public bool RequiresRestart => Definition.RequiresRestart;
    public string? RegistryExportPath => Definition.RegistryExportPath;
    public ActionAvailability Inspect() => _inspect();
    public string Capture() => _capture();
    public void Apply() => _apply();
    public void Restore(string capturedState) => _restore(capturedState);
    public bool Verify() => _verify();
}

public sealed class OptimizationCatalog
{
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private readonly Dictionary<string, OptimizationAction> _actions;

    public OptimizationCatalog()
    {
        var actions = new List<OptimizationAction>
        {
            PowerPlan("system.high-performance", "Use the High Performance power plan",
                "Reduces power-saving delays at the cost of higher energy use.", HighPerformanceGuid, "SCHEME_MIN", "High performance"),
            PowerPlan("system.balanced", "Use the Balanced power plan",
                "Returns the active scheme to the standard Windows balance of performance and energy use.", BalancedGuid, "SCHEME_BALANCED", "Balanced"),
            RegistryDword("gaming.game-mode", "Enable Game Mode",
                "Lets Windows prioritize gaming workloads while a game is running.", "Gaming", RiskLevel.Low, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1,
                stateLabel: OnOffState),
            RegistryDword("gaming.game-mode-off", "Disable Game Mode",
                "Turns off the user Game Mode preference for workloads where it is counterproductive.", "Gaming", RiskLevel.Low, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 0,
                stateLabel: OnOffState),
            RegistryDeleteDword("gaming.game-mode-default", "Restore the default Game Mode preference",
                "Removes the explicit per-user Game Mode override and lets Windows manage its default.", "Gaming", RiskLevel.Low, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"),
            RegistryDword("gaming.hags", "Enable hardware GPU scheduling",
                "Moves supported GPU scheduling work to dedicated hardware.", "Gaming", RiskLevel.Medium, true,
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2,
                () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? null : "Requires Windows 11",
                stateLabel: value => value switch { 2 => "Enabled", 1 => "Disabled", _ => "Not configured" }),
            RegistryDword("gaming.hags-off", "Disable hardware GPU scheduling",
                "Explicitly disables hardware GPU scheduling for compatibility diagnosis.", "Gaming", RiskLevel.Medium, true,
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1,
                () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? null : "Requires Windows 11",
                stateLabel: value => value switch { 2 => "Enabled", 1 => "Disabled", _ => "Not configured" }),
            RegistryDeleteDword("gaming.hags-default", "Restore the default GPU scheduling policy",
                "Removes the explicit HAGS override and returns scheduling policy to Windows and the graphics driver.", "Gaming", RiskLevel.Medium, true,
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"),
            RegistryDword("gaming.game-dvr-off", "Disable background Game DVR",
                "Stops Xbox Game Bar from recording gameplay in the background.", "Gaming", RiskLevel.Medium, false,
                RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0,
                stateLabel: OnOffState),
            RegistryDword("gaming.game-dvr-on", "Enable background Game DVR",
                "Restores the user Game DVR recording preference when capture must be preserved.", "Gaming", RiskLevel.Medium, false,
                RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1,
                stateLabel: OnOffState),
            RegistryDeleteDword("gaming.game-dvr-default", "Restore the default Game DVR preference",
                "Removes the explicit Game DVR preference and lets Windows manage its default.", "Gaming", RiskLevel.Medium, false,
                RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"),
            RegistryDword("system.visual-effects", "Prefer performance visual effects",
                "Reduces Windows animations and visual effects.", "System", RiskLevel.Low, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2,
                stateLabel: value => value switch { 1 => "Best appearance", 2 => "Best performance", 3 => "Custom", _ => "Automatic" }),
            RegistryDword("system.visual-effects-default", "Let Windows choose visual effects",
                "Returns the global visual-effects preference to Windows automatic selection.", "System", RiskLevel.Low, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 0,
                stateLabel: value => value switch { 1 => "Best appearance", 2 => "Best performance", 3 => "Custom", _ => "Automatic" }),
            RegistryDword("system.visual-effects-appearance", "Prefer appearance visual effects",
                "Preserves Windows animations and visual effects when image quality is a stated constraint.", "System", RiskLevel.Low, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 1,
                stateLabel: value => value switch { 1 => "Best appearance", 2 => "Best performance", 3 => "Custom", _ => "Automatic" }),
            RegistryDword("system.large-cache-default", "Restore the Windows client cache policy",
                "Disables the server-oriented LargeSystemCache override.", "System", RiskLevel.Medium, true,
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "LargeSystemCache", 0,
                stateLabel: OnOffState),
            RegistryDword("system.paging-executive-default", "Restore default kernel paging",
                "Lets Windows page kernel components instead of forcing a manual memory override.", "System", RiskLevel.Medium, true,
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", 0,
                stateLabel: value => value == 1 ? "Forced in memory" : "Windows default"),
            RegistryDeleteDword("graphics.mpo-default", "Remove the manual MPO override",
                "Returns Desktop Window Manager overlay selection to the graphics stack.", "Graphics", RiskLevel.Medium, true,
                RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode"),
            RegistryDword("gaming.app-capture-off", "Disable Windows app capture",
                "Disables the second Windows game-capture preference used by Game Bar.", "Gaming", RiskLevel.Medium, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0,
                stateLabel: OnOffState),
            RegistryDword("gaming.app-capture-on", "Enable Windows app capture",
                "Restores the user app-capture preference when recording must be preserved.", "Gaming", RiskLevel.Medium, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1,
                stateLabel: OnOffState),
            RegistryDeleteDword("gaming.app-capture-default", "Restore the default app-capture preference",
                "Removes the explicit app-capture preference and lets Windows manage its default.", "Gaming", RiskLevel.Medium, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"),
            RegistryDeleteDwords("graphics.tdr-default", "Restore default GPU timeout recovery",
                "Removes manual TDR delay and debug overrides so Windows can recover a stalled graphics driver normally.",
                "Graphics", RiskLevel.High, true, RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", ["TdrDelay", "TdrDdiDelay", "TdrLevel", "TdrDebugMode"]),
            RegistryDeleteDwords("network.tcp-default", "Restore Windows TCP auto-tuning defaults",
                "Removes legacy global TCP window, timeout, TTL, and offload overrides; adapter-specific settings remain unchanged.",
                "Network", RiskLevel.High, true, RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                ["TcpTimedWaitDelay", "MaxUserPort", "DefaultTTL", "Tcp1323Opts", "EnablePMTUDiscovery", "DisableTaskOffload", "EnableTCPChimney", "EnableRSS", "EnableDCA", "SackOpts", "GlobalMaxTcpWindowSize", "TcpWindowSize", "KeepAliveTime"]),
            RegistryDeleteDwords("system.power-throttling-default", "Restore Windows power throttling policy",
                "Removes the system-wide PowerThrottlingOff override and returns scheduling decisions to Windows.",
                "Power", RiskLevel.Medium, true, RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", ["PowerThrottlingOff"]),
            BcdDeleteValues("system.bcd-timer-default", "Remove manual boot timer overrides",
                "Removes forced platform clock/tick and TSC synchronization choices so Windows can select its timer policy.",
                ["useplatformclock", "useplatformtick", "disabledynamictick", "tscsyncpolicy"]),
            BcdDeleteValues("system.bcd-resource-default", "Remove manual boot resource limits",
                "Removes CPU-count and memory-limit overrides from the active Windows boot entry.",
                ["numproc", "truncatememory", "removememory"])
        };
        actions.Add(PageFileManagedSizes());
        actions.Add(CoreParkingOff());
        foreach (var target in GameGpuTargetStore.Load()) actions.AddRange(PerAppGpuPreferences(target));
        foreach (var plan in new PowerPlanStore().ListStaged()) actions.Add(CustomPowerPlan(plan));
        if (actions.Select(action => action.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != actions.Count)
            throw new InvalidOperationException("The capability registry contains duplicate action IDs.");
        foreach (var action in actions) action.Definition.Validate();
        _actions = actions.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<OptimizationAction> All => _actions.Values;
    public IReadOnlyCollection<ActionDefinition> Definitions => _actions.Values.Select(action => action.Definition).ToList();

    public OptimizationAction Get(string id) => _actions.TryGetValue(id, out var action)
        ? action
        : throw new InvalidOperationException($"Action is not allowlisted: {id}");

    public bool Contains(string id) => _actions.ContainsKey(id);

    private static OptimizationAction PageFileManagedSizes()
    {
        const string path = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
        const string valueName = "PagingFiles";
        string[] Current()
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            var value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value as string[] ?? throw new InvalidOperationException("PagingFiles is missing or is not REG_MULTI_SZ.");
        }
        string[] Desired() => Current().Select(entry =>
        {
            var parsed = ParsePageFileEntry(entry);
            return parsed.InitialSize is null ? entry : $"{parsed.Path} 0 0";
        }).ToArray();
        bool IsManaged() => Current().All(entry =>
        {
            var parsed = ParsePageFileEntry(entry);
            return parsed.InitialSize is null || parsed is { InitialSize: 0, MaximumSize: 0 };
        });
        ActionAvailability Inspect()
        {
            try
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return ActionAvailability.Unavailable("Requires Windows 11");
                var current = Current();
                if (current.Length == 0) return ActionAvailability.Unavailable("No configured page file was found");
                return IsManaged() ? ActionAvailability.Applied("All configured page files use Windows-managed sizes")
                    : ActionAvailability.Ready("One or more page files use fixed sizes");
            }
            catch (Exception exception) { return ActionAvailability.Unavailable(exception.Message); }
        }
        void Apply()
        {
            var desired = Desired();
            using var key = Registry.LocalMachine.CreateSubKey(path, true)
                ?? throw new InvalidOperationException("The page-file policy key is unavailable.");
            key.SetValue(valueName, desired, RegistryValueKind.MultiString);
        }
        return new("system.pagefile-managed-sizes", "Use Windows-managed page-file sizes",
            "Preserves every configured page-file volume and lets Windows size each one instead of retaining fixed limits.",
            "System", RiskLevel.Medium, true, @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
            Inspect, () => JsonSerializer.Serialize(CaptureRegistryValue(RegistryHive.LocalMachine, path, valueName)), Apply,
            state => RestoreRegistryValue(RegistryHive.LocalMachine, path, valueName, DeserializeRegistrySnapshot(state)), IsManaged,
            evidenceRequirements: ["Exact local PagingFiles REG_MULTI_SZ value; at least one existing page-file entry"],
            sources: ["Microsoft Win32_PageFileSetting mapping and documented zero/zero per-volume system-managed sizing"],
            sideEffects: ["Takes effect after restart; page-file volumes remain unchanged but Windows may grow or shrink their files"]);
    }

    internal static PageFileEntry ParsePageFileEntry(string value)
    {
        var match = Regex.Match(value.Trim(), @"^(?<path>.+?)\s+(?<initial>\d+)\s+(?<maximum>\d+)$");
        return match.Success && uint.TryParse(match.Groups["initial"].Value, CultureInfo.InvariantCulture, out var initial) &&
            uint.TryParse(match.Groups["maximum"].Value, CultureInfo.InvariantCulture, out var maximum)
            ? new(match.Groups["path"].Value, initial, maximum) : new(value.Trim(), null, null);
    }

    private static OptimizationAction CoreParkingOff()
    {
        const string subgroup = "54533251-82be-4824-96c1-47b60b740d00";
        const string setting = "0cc5b647-c1df-4637-891a-dec35c318583";
        int Current() => checked((int)ReadPowerSetting(Guid.Parse(subgroup), Guid.Parse(setting)).Ac);
        ActionAvailability Inspect()
        {
            try
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return ActionAvailability.Unavailable("Requires Windows 11");
                if (SystemProfiler.Query("SELECT Name FROM Win32_Battery", row => row["Name"]?.ToString() ?? "").Count > 0)
                    return ActionAvailability.Unavailable("Core-parking override is limited to AC-only desktop validation");
                var current = Current();
                return current == 100 ? ActionAvailability.Applied("AC minimum unparked cores: 100%")
                    : ActionAvailability.Ready($"AC minimum unparked cores: {current}%");
            }
            catch (Exception exception) { return ActionAvailability.Unavailable(exception.Message); }
        }
        void Set(int value)
        {
            RunPowerCfg("/setacvalueindex", "SCHEME_CURRENT", subgroup, setting,
                value.ToString(CultureInfo.InvariantCulture));
            RunPowerCfg("/setactive", "SCHEME_CURRENT");
        }
        return new("system.core-parking-off", "Disable core parking on AC power",
            "Keeps every logical core available in the active desktop power scheme; compare repeated measurements because this can increase power and heat.",
            "Power", RiskLevel.Medium, false, null, Inspect,
            () => Current().ToString(CultureInfo.InvariantCulture), () => Set(100),
            state => Set(int.Parse(state, CultureInfo.InvariantCulture)), () => Current() == 100,
            supportedHardware: ["AC-powered desktop with a power scheme exposing the Windows core-parking minimum-cores setting"],
            evidenceRequirements: ["Exact active-scheme AC CPMINCORES value and no detected battery"],
            sources: ["Microsoft powercfg command contract and exact local power-setting GUID inspection"],
            sideEffects: ["May increase idle power, temperature, and fan noise; does not change the DC value"]);
    }

    internal static IEnumerable<OptimizationAction> PerAppGpuPreferences(GameGpuTarget target)
    {
        yield return PerAppGpuPreference(target, "high", "High performance", "GpuPreference=2;");
        yield return PerAppGpuPreference(target, "saving", "Power saving", "GpuPreference=1;");
        yield return PerAppGpuPreference(target, "default", "Windows default", null);
    }

    private static OptimizationAction PerAppGpuPreference(GameGpuTarget target, string suffix, string label, string? desired)
    {
        const string path = @"Software\Microsoft\DirectX\UserGpuPreferences";
        string? Current()
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            var value = key?.GetValue(target.ExecutablePath, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value is null ? null : value as string
                ?? throw new InvalidOperationException("The existing per-app GPU preference has an unsupported Registry type.");
        }
        ActionAvailability Inspect()
        {
            try
            {
                if (!File.Exists(target.ExecutablePath)) return ActionAvailability.Unavailable("The detected executable no longer exists");
                var current = Current();
                var currentLabel = current is null ? "Windows default" : current.Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase)
                    ? "High performance" : current.Contains("GpuPreference=1", StringComparison.OrdinalIgnoreCase) ? "Power saving" : "Custom/unknown";
                return current == desired ? ActionAvailability.Applied(currentLabel) : ActionAvailability.Ready(currentLabel);
            }
            catch (Exception exception) { return ActionAvailability.Unavailable(exception.Message); }
        }
        void Apply()
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, true)
                ?? throw new InvalidOperationException("The per-app GPU preference key is unavailable.");
            if (desired is null) key.DeleteValue(target.ExecutablePath, false);
            else key.SetValue(target.ExecutablePath, desired, RegistryValueKind.String);
        }
        var display = $"{target.ExecutableName} [{target.Id[..6]}]";
        return new($"gaming.gpu-{target.Id}.{suffix}", $"{label} GPU for {display}",
            $"Sets the detected executable to {label.ToLowerInvariant()} in the Windows per-app graphics preference.",
            "Gaming", RiskLevel.Low, false, @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", Inspect,
            () => JsonSerializer.Serialize(CaptureRegistryValue(RegistryHive.CurrentUser, path, target.ExecutablePath)), Apply,
            state => RestoreRegistryValue(RegistryHive.CurrentUser, path, target.ExecutablePath, DeserializeRegistrySnapshot(state)),
            () => Current() == desired,
            supportedHardware: ["Detected local executable and a Windows 11 graphics stack exposing per-app GPU preferences"],
            evidenceRequirements: [$"Durable detected-target ID {target.Id}; exact Registry value kind and content"],
            sources: ["Windows 11 per-app Graphics settings plus exact local UserGpuPreferences inspection"],
            sideEffects: ["Changes only the selected executable preference; Windows and the graphics driver choose the physical adapter"]);
    }

    private static OptimizationAction PowerPlan(string id, string name, string description, string targetGuid,
        string targetAlias, string targetLabel)
    {
        string Capture()
        {
            var output = RunPowerCfg("/getactivescheme");
            return Regex.Match(output, "[0-9a-fA-F-]{36}").Value is { Length: 36 } guid
                ? guid
                : throw new InvalidOperationException("The active power plan could not be detected.");
        }

        ActionAvailability Inspect()
        {
            try
            {
                var current = Capture();
                if (current.Equals(targetGuid, StringComparison.OrdinalIgnoreCase))
                    return ActionAvailability.Applied(targetLabel);
                var plans = RunPowerCfg("/list");
                return plans.Contains(targetGuid, StringComparison.OrdinalIgnoreCase)
                    ? ActionAvailability.Ready(current)
                    : ActionAvailability.Unavailable($"{targetLabel} power plan is not available", current);
            }
            catch (Exception exception) { return ActionAvailability.Unavailable(exception.Message); }
        }

        void Apply() => RunPowerCfg("/setactive", targetAlias);
        void Restore(string guid) => RunPowerCfg("/setactive", guid);
        bool Verify() => Capture().Equals(targetGuid, StringComparison.OrdinalIgnoreCase);

        return new(id, name, description, "System", RiskLevel.Medium, false,
            null, Inspect, Capture, Apply, Restore, Verify,
            evidenceRequirements: ["Exact active and available power-scheme identifiers"],
            sources: ["Microsoft powercfg command contract and exact local scheme inventory"],
            sideEffects: ["Changes the active system power policy and may affect energy use"]);
    }

    internal static OptimizationAction CustomPowerPlan(CustomPowerPlanFile plan)
    {
        var target = new Guid(Convert.FromHexString(plan.Sha256)[..16]).ToString("D");
        bool Exists() => RunPowerCfg("/list").Contains(target, StringComparison.OrdinalIgnoreCase);
        string Active() => Regex.Match(RunPowerCfg("/getactivescheme"), "[0-9a-fA-F-]{36}").Value is { Length: 36 } guid
            ? guid : throw new InvalidOperationException("The active power plan could not be detected.");
        string Capture() => JsonSerializer.Serialize(new CustomPowerPlanState(Active(), Exists()));
        void Apply()
        {
            if (!PowerPlanStore.Matches(plan)) throw new InvalidOperationException("The staged .pow file no longer matches its SHA-256.");
            if (!Exists()) RunPowerCfg("/import", plan.Path, target);
            RunPowerCfg("/setactive", target);
        }
        void Restore(string state)
        {
            var previous = JsonSerializer.Deserialize<CustomPowerPlanState>(state)
                ?? throw new InvalidOperationException("The custom power-plan snapshot was invalid.");
            RunPowerCfg("/setactive", previous.ActiveGuid);
            if (!previous.TargetExisted && Exists()) RunPowerCfg("/delete", target);
        }
        ActionAvailability Inspect()
        {
            try
            {
                if (!PowerPlanStore.Matches(plan)) return ActionAvailability.Unavailable("The staged .pow file is missing or no longer matches its SHA-256");
                return Active().Equals(target, StringComparison.OrdinalIgnoreCase)
                    ? ActionAvailability.Applied($"Active custom plan · SHA-256 {plan.Sha256[..12]}")
                    : ActionAvailability.Ready($"Opaque .pow · SHA-256 {plan.Sha256[..12]} · {(Exists() ? "installed" : "not installed")}");
            }
            catch (Exception exception) { return ActionAvailability.Unavailable(exception.Message); }
        }
        return new($"power.custom.{plan.Sha256.ToLowerInvariant()}", $"Import and use {plan.Name}",
            "Imports the selected opaque .pow file under a deterministic GUID and activates it for measured comparison.",
            "Power", RiskLevel.High, false, null, Inspect, Capture, Apply, Restore,
            () => Active().Equals(target, StringComparison.OrdinalIgnoreCase),
            supportedHardware: ["Windows 11 PC; the third-party plan may still contain hardware-specific values"],
            evidenceRequirements: [$"User-staged .pow file with SHA-256 {plan.Sha256}; plan contents are opaque"],
            sources: ["User-provided powercfg export; Microsoft powercfg import contract"],
            sideEffects: ["Changes the active power policy; may increase temperature, energy use, instability, or latency; requires Baseline and separate high-risk confirmation"]);
    }

    private static OptimizationAction RegistryDword(string id, string name, string description, string category,
        RiskLevel risk, bool restart, RegistryHive hive, string path, string valueName, int desiredValue,
        Func<string?>? compatibilityIssue = null, Func<int?, string>? stateLabel = null)
    {
        var exportPath = $"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{path}";

        int? ReadCurrent()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
            var current = key?.GetValue(valueName);
            return current is null ? null : Convert.ToInt32(current);
        }

        ActionAvailability Inspect()
        {
            try
            {
                var current = ReadCurrent();
                var currentLabel = stateLabel?.Invoke(current) ?? current?.ToString() ?? "Not configured";
                var issue = compatibilityIssue?.Invoke();
                if (issue is not null) return ActionAvailability.Unavailable(issue, currentLabel);
                return current == desiredValue
                    ? ActionAvailability.Applied(currentLabel)
                    : ActionAvailability.Ready(currentLabel);
            }
            catch (Exception exception) { return ActionAvailability.Unavailable(exception.Message); }
        }

        string Capture() => JsonSerializer.Serialize(CaptureRegistryValue(hive, path, valueName));

        void Apply()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).CreateSubKey(path, true)
                ?? throw new InvalidOperationException($"Cannot open {exportPath}.");
            key.SetValue(valueName, desiredValue, RegistryValueKind.DWord);
        }

        void Restore(string state) => RestoreRegistryValue(hive, path, valueName, DeserializeRegistrySnapshot(state));

        bool Verify() => ReadCurrent() == desiredValue;

        return new(id, name, description, category, risk, restart, exportPath, Inspect, Capture, Apply, Restore, Verify);
    }

    private static OptimizationAction RegistryDeleteDwords(string id, string name, string description, string category,
        RiskLevel risk, bool restart, RegistryHive hive, string path, string[] valueNames)
    {
        var exportPath = $"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{path}";

        Dictionary<string, int?> ReadCurrent()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
            return valueNames.ToDictionary(valueName => valueName, valueName =>
            {
                var value = key?.GetValue(valueName);
                return value is null ? (int?)null : Convert.ToInt32(value);
            }, StringComparer.OrdinalIgnoreCase);
        }

        ActionAvailability Inspect()
        {
            try
            {
                var configured = ReadCurrent().Where(item => item.Value is not null).ToList();
                return configured.Count == 0
                    ? ActionAvailability.Applied("No manual overrides")
                    : ActionAvailability.Ready(string.Join(", ", configured.Select(item => $"{item.Key}={item.Value}")));
            }
            catch (Exception exception) { return ActionAvailability.Unavailable(exception.Message); }
        }

        string Capture() => JsonSerializer.Serialize(valueNames.ToDictionary(
            valueName => valueName,
            valueName => CaptureRegistryValue(hive, path, valueName),
            StringComparer.OrdinalIgnoreCase));

        void Apply()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path, true);
            if (key is null) return;
            foreach (var valueName in valueNames) key.DeleteValue(valueName, false);
        }

        void Restore(string state)
        {
            var snapshot = DeserializeRegistrySnapshots(state);
            foreach (var (valueName, value) in snapshot)
                RestoreRegistryValue(hive, path, valueName, value);
        }

        return new(id, name, description, category, risk, restart, exportPath,
            Inspect, Capture, Apply, Restore, () => ReadCurrent().Values.All(value => value is null));
    }

    private static OptimizationAction RegistryDeleteDword(string id, string name, string description, string category,
        RiskLevel risk, bool restart, RegistryHive hive, string path, string valueName)
    {
        var exportPath = $"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{path}";

        int? ReadCurrent()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
            var current = key?.GetValue(valueName);
            return current is null ? null : Convert.ToInt32(current);
        }

        ActionAvailability Inspect()
        {
            try
            {
                var current = ReadCurrent();
                return current is null
                    ? ActionAvailability.Applied("Not configured (Windows default)")
                    : ActionAvailability.Ready(current.Value.ToString());
            }
            catch (Exception exception) { return ActionAvailability.Unavailable(exception.Message); }
        }

        string Capture() => JsonSerializer.Serialize(CaptureRegistryValue(hive, path, valueName));

        void Apply()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path, true);
            if (key is null) return;
            key.DeleteValue(valueName, false);
        }

        void Restore(string state) => RestoreRegistryValue(hive, path, valueName, DeserializeRegistrySnapshot(state));

        return new(id, name, description, category, risk, restart, exportPath,
            Inspect, Capture, Apply, Restore, () => ReadCurrent() is null);
    }

    private static OptimizationAction BcdDeleteValues(string id, string name, string description, string[] valueNames)
    {
        Dictionary<string, string?> ReadCurrent()
        {
            var output = RunExecutable("bcdedit.exe", "/enum", "{current}");
            return valueNames.ToDictionary(valueName => valueName, valueName => output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(valueName, StringComparison.OrdinalIgnoreCase))
                .Select(line => line[valueName.Length..].Trim())
                .FirstOrDefault(), StringComparer.OrdinalIgnoreCase);
        }

        ActionAvailability Inspect()
        {
            try
            {
                var configured = ReadCurrent().Where(item => item.Value is not null).ToList();
                return configured.Count == 0
                    ? ActionAvailability.Applied("No manual overrides")
                    : ActionAvailability.Ready(string.Join(", ", configured.Select(item => $"{item.Key}={item.Value}")));
            }
            catch (Exception exception) { return ActionAvailability.Unavailable(exception.Message); }
        }

        string Capture() => JsonSerializer.Serialize(ReadCurrent());

        void Apply()
        {
            foreach (var valueName in ReadCurrent().Where(item => item.Value is not null).Select(item => item.Key))
                RunExecutable("bcdedit.exe", "/deletevalue", "{current}", valueName);
        }

        void Restore(string state)
        {
            var snapshot = JsonSerializer.Deserialize<Dictionary<string, string?>>(state)
                ?? throw new InvalidOperationException("The BCD snapshot is invalid.");
            foreach (var (valueName, value) in snapshot)
            {
                var current = ReadCurrent()[valueName];
                if (value is null)
                {
                    if (current is not null) RunExecutable("bcdedit.exe", "/deletevalue", "{current}", valueName);
                }
                else RunExecutable("bcdedit.exe", "/set", "{current}", valueName, value);
            }
        }

        bool Verify() => ReadCurrent().Values.All(value => value is null);

        return new(id, name, description, "Boot", RiskLevel.High, true, null,
            Inspect, Capture, Apply, Restore, Verify,
            evidenceRequirements: ["Exact values from the active BCD entry"],
            sources: ["Microsoft BCDEdit command contract and exact local active-entry inspection"],
            sideEffects: ["Changes boot policy after restart; malformed overrides are removed, never forced"]);
    }

    private static RegistryValueSnapshot CaptureRegistryValue(RegistryHive hive, string path, string valueName)
    {
        using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
        var value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) return new(false, RegistryValueKind.Unknown, null);
        var kind = key!.GetValueKind(valueName);
        return new(true, kind, SerializeRegistryValue(kind, value));
    }

    private static void RestoreRegistryValue(RegistryHive hive, string path, string valueName, RegistryValueSnapshot snapshot)
    {
        using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).CreateSubKey(path, true)
            ?? throw new InvalidOperationException($"Cannot open {(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{path}.");
        if (!snapshot.Exists)
        {
            key.DeleteValue(valueName, false);
            return;
        }
        key.SetValue(valueName, DeserializeRegistryValue(snapshot.Kind, snapshot.Value), snapshot.Kind);
    }

    internal static RegistryValueSnapshot DeserializeRegistrySnapshot(string state)
    {
        using var json = JsonDocument.Parse(state);
        if (json.RootElement.TryGetProperty("Kind", out _))
            return JsonSerializer.Deserialize<RegistryValueSnapshot>(state)
                ?? throw new InvalidOperationException("The Registry snapshot is invalid.");
        var exists = json.RootElement.GetProperty("Exists").GetBoolean();
        var value = json.RootElement.GetProperty("Value");
        return new(exists, RegistryValueKind.DWord, value.ValueKind == JsonValueKind.Null ? null : value.GetRawText());
    }

    private static Dictionary<string, RegistryValueSnapshot> DeserializeRegistrySnapshots(string state)
    {
        using var json = JsonDocument.Parse(state);
        return json.RootElement.EnumerateObject().ToDictionary(
            item => item.Name,
            item => item.Value.ValueKind == JsonValueKind.Object
                ? DeserializeRegistrySnapshot(item.Value.GetRawText())
                : new RegistryValueSnapshot(item.Value.ValueKind != JsonValueKind.Null, RegistryValueKind.DWord,
                    item.Value.ValueKind == JsonValueKind.Null ? null : item.Value.GetRawText()),
            StringComparer.OrdinalIgnoreCase);
    }

    internal static string? SerializeRegistryValue(RegistryValueKind kind, object value) => kind switch
    {
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
        RegistryValueKind.MultiString => JsonSerializer.Serialize((string[])value),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    internal static object DeserializeRegistryValue(RegistryValueKind kind, string? value) => kind switch
    {
        RegistryValueKind.DWord => int.Parse(value ?? "0", CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(value ?? "0", CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.FromBase64String(value ?? ""),
        RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(value ?? "[]") ?? [],
        RegistryValueKind.String or RegistryValueKind.ExpandString => value ?? "",
        _ => throw new InvalidOperationException($"Unsupported saved Registry kind: {kind}.")
    };

    private static string OnOffState(int? value) => value switch
    {
        1 => "Enabled",
        0 => "Disabled",
        _ => "Not configured"
    };

    private static string RunPowerCfg(params string[] arguments)
        => RunExecutable("powercfg.exe", arguments);

    internal static PowerSettingValue ReadPowerSetting(Guid subgroup, Guid setting)
    {
        var status = NativePower.PowerGetActiveScheme(IntPtr.Zero, out var schemePointer);
        if (status != 0 || schemePointer == IntPtr.Zero)
            throw new InvalidOperationException($"The active power scheme could not be read (Win32 {status}).");
        try
        {
            var scheme = Marshal.PtrToStructure<Guid>(schemePointer);
            status = NativePower.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var value);
            if (status != 0)
                throw new InvalidOperationException($"The active AC power setting could not be read (Win32 {status}).");
            var dcStatus = NativePower.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var dc);
            if (dcStatus != 0)
                throw new InvalidOperationException($"The DC power setting could not be read (Win32 {dcStatus}).");
            return new(value, dc);
        }
        finally { NativePower.LocalFree(schemePointer); }
    }

    private static string RunExecutable(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {executable}.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"{executable} failed." : error.Trim());
        return output;
    }

    internal sealed record RegistryValueSnapshot(bool Exists, RegistryValueKind Kind, string? Value);
    internal sealed record PageFileEntry(string Path, uint? InitialSize, uint? MaximumSize);
    internal sealed record CustomPowerPlanState(string ActiveGuid, bool TargetExisted);
    internal readonly record struct PowerSettingValue(uint Ac, uint Dc);

    private static class NativePower
    {
        [DllImport("powrprof.dll")]
        internal static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        internal static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out uint acValueIndex);

        [DllImport("powrprof.dll")]
        internal static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out uint dcValueIndex);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}
