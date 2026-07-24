using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NeuroTune;

public sealed record OptimizationAction(
    string Id,
    string Name,
    string Description,
    string Category,
    RiskLevel Risk,
    bool RequiresRestart,
    string? RegistryExportPath,
    Func<ActionAvailability> Inspect,
    Func<string> Capture,
    Action Apply,
    Action<string> Restore,
    Func<bool> Verify);

public sealed class OptimizationCatalog
{
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private readonly Dictionary<string, OptimizationAction> _actions;

    public OptimizationCatalog()
    {
        var actions = new[]
        {
            PowerPlan(),
            RegistryDword("gaming.game-mode", "Enable Game Mode",
                "Lets Windows prioritize gaming workloads while a game is running.", "Gaming", RiskLevel.Low, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1,
                stateLabel: OnOffState),
            RegistryDword("gaming.hags", "Enable hardware GPU scheduling",
                "Moves supported GPU scheduling work to dedicated hardware.", "Gaming", RiskLevel.Medium, true,
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2,
                () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) ? null : "Requires Windows 10 version 2004 or newer",
                stateLabel: value => value switch { 2 => "Enabled", 1 => "Disabled", _ => "Not configured" }),
            RegistryDword("gaming.game-dvr-off", "Disable background Game DVR",
                "Stops Xbox Game Bar from recording gameplay in the background.", "Gaming", RiskLevel.Medium, false,
                RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0,
                stateLabel: OnOffState),
            RegistryDword("system.visual-effects", "Prefer performance visual effects",
                "Reduces Windows animations and visual effects.", "System", RiskLevel.Low, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2,
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
                @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", ["PowerThrottlingOff"])
        };
        _actions = actions.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<OptimizationAction> All => _actions.Values;

    public OptimizationAction Get(string id) => _actions.TryGetValue(id, out var action)
        ? action
        : throw new InvalidOperationException($"Action is not allowlisted: {id}");

    public bool Contains(string id) => _actions.ContainsKey(id);

    private static OptimizationAction PowerPlan()
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
                if (current.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase))
                    return ActionAvailability.Applied("High performance");
                var plans = RunPowerCfg("/list");
                return plans.Contains(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase)
                    ? ActionAvailability.Ready(current)
                    : ActionAvailability.Unavailable("High Performance power plan is not available", current);
            }
            catch (Exception exception) { return ActionAvailability.Unavailable(exception.Message); }
        }

        void Apply() => RunPowerCfg("/setactive", "SCHEME_MIN");
        void Restore(string guid) => RunPowerCfg("/setactive", guid);
        bool Verify() => Capture().Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase);

        return new("system.high-performance", "Use the High Performance power plan",
            "Reduces power-saving delays at the cost of higher energy use.", "System", RiskLevel.Medium, false,
            null, Inspect, Capture, Apply, Restore, Verify);
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

        string Capture()
        {
            var current = ReadCurrent();
            return JsonSerializer.Serialize(new RegistrySnapshot(current is not null, current));
        }

        void Apply()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).CreateSubKey(path, true)
                ?? throw new InvalidOperationException($"Cannot open {exportPath}.");
            key.SetValue(valueName, desiredValue, RegistryValueKind.DWord);
        }

        void Restore(string state)
        {
            var snapshot = JsonSerializer.Deserialize<RegistrySnapshot>(state)
                ?? throw new InvalidOperationException("The Registry snapshot is invalid.");
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).CreateSubKey(path, true)
                ?? throw new InvalidOperationException($"Cannot open {exportPath}.");
            if (snapshot.Exists) key.SetValue(valueName, snapshot.Value ?? 0, RegistryValueKind.DWord);
            else key.DeleteValue(valueName, false);
        }

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

        string Capture() => JsonSerializer.Serialize(ReadCurrent());

        void Apply()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path, true)
                ?? throw new InvalidOperationException($"Cannot open {exportPath}.");
            foreach (var valueName in valueNames) key.DeleteValue(valueName, false);
        }

        void Restore(string state)
        {
            var snapshot = JsonSerializer.Deserialize<Dictionary<string, int?>>(state)
                ?? throw new InvalidOperationException("The Registry snapshot is invalid.");
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).CreateSubKey(path, true)
                ?? throw new InvalidOperationException($"Cannot open {exportPath}.");
            foreach (var (valueName, value) in snapshot)
                if (value is not null) key.SetValue(valueName, value.Value, RegistryValueKind.DWord);
                else key.DeleteValue(valueName, false);
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

        string Capture()
        {
            var current = ReadCurrent();
            return JsonSerializer.Serialize(new RegistrySnapshot(current is not null, current));
        }

        void Apply()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path, true)
                ?? throw new InvalidOperationException($"Cannot open {exportPath}.");
            key.DeleteValue(valueName, false);
        }

        void Restore(string state)
        {
            var snapshot = JsonSerializer.Deserialize<RegistrySnapshot>(state)
                ?? throw new InvalidOperationException("The Registry snapshot is invalid.");
            if (!snapshot.Exists) return;
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).CreateSubKey(path, true)
                ?? throw new InvalidOperationException($"Cannot open {exportPath}.");
            key.SetValue(valueName, snapshot.Value ?? 0, RegistryValueKind.DWord);
        }

        return new(id, name, description, category, risk, restart, exportPath,
            Inspect, Capture, Apply, Restore, () => ReadCurrent() is null);
    }

    private static string OnOffState(int? value) => value switch
    {
        1 => "Enabled",
        0 => "Disabled",
        _ => "Not configured"
    };

    private static string RunPowerCfg(params string[] arguments)
    {
        var start = new ProcessStartInfo("powercfg.exe")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start powercfg.exe.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "powercfg.exe failed." : error.Trim());
        return output;
    }

    private sealed record RegistrySnapshot(bool Exists, int? Value);
}
