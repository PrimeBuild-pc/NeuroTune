using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
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
            supportedWindowsBuilds ?? ["Windows 10 22H2", "Windows 11"],
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
        var actions = new[]
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
                () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) ? null : "Requires Windows 10 version 2004 or newer",
                stateLabel: value => value switch { 2 => "Enabled", 1 => "Disabled", _ => "Not configured" }),
            RegistryDword("gaming.hags-off", "Disable hardware GPU scheduling",
                "Explicitly disables hardware GPU scheduling for compatibility diagnosis.", "Gaming", RiskLevel.Medium, true,
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1,
                () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) ? null : "Requires Windows 10 version 2004 or newer",
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
        if (actions.Select(action => action.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != actions.Length)
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
}
