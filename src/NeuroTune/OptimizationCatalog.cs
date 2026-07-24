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
            RegistryDword("gaming.game-mode", "Attiva Game Mode",
                "Dà priorità al gioco quando Windows rileva una sessione gaming.", "Gaming", RiskLevel.Low, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
            RegistryDword("gaming.hags", "Attiva HAGS",
                "Abilita la pianificazione GPU con accelerazione hardware, se supportata.", "Gaming", RiskLevel.Medium, true,
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2),
            RegistryDword("gaming.game-dvr-off", "Disattiva Game DVR",
                "Disattiva la registrazione in background di Xbox Game Bar.", "Gaming", RiskLevel.Medium, false,
                RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0),
            RegistryDword("system.visual-effects", "Effetti visivi per prestazioni",
                "Riduce animazioni ed effetti visivi di Windows.", "Sistema", RiskLevel.Low, false,
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2)
        };
        _actions = actions.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<OptimizationAction> All => _actions.Values;

    public OptimizationAction Get(string id) => _actions.TryGetValue(id, out var action)
        ? action
        : throw new InvalidOperationException($"Azione non consentita: {id}");

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
                : throw new InvalidOperationException("Piano energetico attivo non rilevato.");
        }

        void Apply() => RunPowerCfg("/setactive", "SCHEME_MIN");
        void Restore(string guid) => RunPowerCfg("/setactive", guid);
        bool Verify() => Capture().Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase);

        return new("system.high-performance", "Piano Prestazioni elevate",
            "Attiva il piano energetico Prestazioni elevate.", "Sistema", RiskLevel.Medium, false,
            null, Capture, Apply, Restore, Verify);
    }

    private static OptimizationAction RegistryDword(string id, string name, string description, string category,
        RiskLevel risk, bool restart, RegistryHive hive, string path, string valueName, int desiredValue)
    {
        var exportPath = $"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{path}";
        string Capture()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
            var current = key?.GetValue(valueName);
            return JsonSerializer.Serialize(new RegistrySnapshot(current is not null, current is null ? null : Convert.ToInt32(current)));
        }

        void Apply()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).CreateSubKey(path, true)
                ?? throw new InvalidOperationException($"Impossibile aprire {exportPath}.");
            key.SetValue(valueName, desiredValue, RegistryValueKind.DWord);
        }

        void Restore(string state)
        {
            var snapshot = JsonSerializer.Deserialize<RegistrySnapshot>(state)
                ?? throw new InvalidOperationException("Snapshot del registro non valido.");
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).CreateSubKey(path, true)
                ?? throw new InvalidOperationException($"Impossibile aprire {exportPath}.");
            if (snapshot.Exists) key.SetValue(valueName, snapshot.Value ?? 0, RegistryValueKind.DWord);
            else key.DeleteValue(valueName, false);
        }

        bool Verify()
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
            return key?.GetValue(valueName) is int value && value == desiredValue;
        }

        return new(id, name, description, category, risk, restart, exportPath, Capture, Apply, Restore, Verify);
    }

    private static string RunPowerCfg(params string[] arguments)
    {
        var start = new ProcessStartInfo("powercfg.exe") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Impossibile avviare powercfg.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "powercfg non riuscito." : error.Trim());
        return output;
    }

    private sealed record RegistrySnapshot(bool Exists, int? Value);
}
