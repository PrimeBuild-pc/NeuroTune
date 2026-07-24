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
                stateLabel: value => value switch { 1 => "Best appearance", 2 => "Best performance", 3 => "Custom", _ => "Automatic" })
        };
        _actions = actions.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<OptimizationAction> All => _actions.Values;

    public OptimizationAction Get(string id) => _actions.TryGetValue(id, out var action)
        ? action
        : throw new InvalidOperationException($"Action is not allowlisted: {id}");

    public bool Contains(string id) => _actions.ContainsKey(id);

    public static bool SelectForPreset(OptimizationAction action, bool recommended, OptimizationPreset preset) =>
        recommended && preset switch
        {
            OptimizationPreset.Balanced => action.Risk == RiskLevel.Low,
            OptimizationPreset.Gaming => action.Category == "Gaming" || action.Risk == RiskLevel.Low,
            _ => false
        };

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
