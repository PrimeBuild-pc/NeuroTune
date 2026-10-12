using Microsoft.Win32;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NeuroTune;

public sealed record ParametricActionRequest(string Kind, JsonElement Parameters);

// Typed write primitives with free parameters. The ID is kind + parameter hash; the journal keeps Kind + Parameters so
// recovery rebuilds the exact action from the record alone. Forbidden targets are refused here, before any capture.
internal static class ParametricActions
{
    internal static readonly string[] Kinds =
    [
        "registry.set", "registry.delete", "service.start-type", "netadapter.property", "qos.policy", "powercfg.setting",
        "bcd.option", "startup.entry", "scheduled-task.state", "appcompat.layer", "ifeo.perf", "file.json", "file.ini",
        "device.disable", "defender.exclusion"
    ];

    private static readonly JsonSerializerOptions Strict = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static OptimizationAction Create(string kind, string parameters)
    {
        if (parameters is null || parameters.Length > 16_384) throw new InvalidOperationException("Action parameters are missing or too large.");
        try
        {
            return kind switch
            {
                "registry.set" => RegistryValue(kind, Read<RegistryParameters>(parameters), set: true),
                "registry.delete" => RegistryValue(kind, Read<RegistryParameters>(parameters), set: false),
                "service.start-type" => ServiceStartType(Read<ServiceParameters>(parameters)),
                "netadapter.property" => NetAdapterProperty(Read<NetAdapterParameters>(parameters)),
                "qos.policy" => QosPolicy(Read<QosParameters>(parameters)),
                "powercfg.setting" => PowerSetting(Read<PowerSettingParameters>(parameters)),
                "bcd.option" => BcdOption(Read<BcdParameters>(parameters)),
                "startup.entry" => StartupEntry(Read<StartupParameters>(parameters)),
                "scheduled-task.state" => ScheduledTask(Read<TaskParameters>(parameters)),
                "appcompat.layer" => AppCompatLayer(Read<AppCompatParameters>(parameters)),
                "ifeo.perf" => IfeoPerf(Read<IfeoParameters>(parameters)),
                "file.json" => JsonFile(Read<JsonFileParameters>(parameters)),
                "file.ini" => IniFile(Read<IniFileParameters>(parameters)),
                "device.disable" => DeviceDisable(Read<DeviceParameters>(parameters)),
                "defender.exclusion" => DefenderExclusion(Read<DefenderParameters>(parameters)),
                _ => throw new InvalidOperationException($"Unknown action kind: {kind}.")
            };
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or OverflowException)
        { throw new InvalidOperationException($"Invalid {kind} parameters: {exception.Message}", exception); }
    }

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Strict) ?? throw new JsonException("The parameters were empty.");

    private static OptimizationAction Build<T>(string kind, T parameters, string name, string category, RiskLevel risk,
        bool restart, string target, string sideEffect, Func<ActionAvailability> inspect, Func<string> capture,
        Action apply, Action<string> restore, Func<bool> verify, Action<string> validateSnapshot,
        Func<string, bool>? verifyRestored = null)
    {
        var canonical = JsonSerializer.Serialize(parameters);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}\n{canonical}")))[..16].ToLowerInvariant();
        return new($"{kind}.{hash}", name, $"{kind} on {target}", category, risk, restart, null,
            () => { try { return inspect(); } catch (Exception exception) { return ActionAvailability.Unavailable(exception.Message); } },
            capture, apply, restore, verify,
            supportedHardware: ["The target named in the action parameters"],
            evidenceRequirements: [$"Exact local state of {target} captured before the write"],
            sources: ["Model-proposed parameters validated by a typed NeuroTune primitive and its forbidden-target list"],
            sideEffects: [sideEffect], validateSnapshot: validateSnapshot, conflictTarget: target, verifyRestored: verifyRestored)
        { Kind = kind, Parameters = canonical };
    }

    private static string Short(string value) => value.Length <= 120 ? value : value[..117] + "...";

    private static JsonElement ExactObject(string state, params string[] keys)
    {
        using var json = JsonDocument.Parse(state);
        var names = json.RootElement.EnumerateObject().Select(item => item.Name).ToList();
        if (names.Count != keys.Length || !names.ToHashSet(StringComparer.Ordinal).SetEquals(keys))
            throw new InvalidOperationException("The snapshot does not contain exactly the expected fields.");
        return json.RootElement.Clone();
    }

    // ---------------------------------------------------------------- Registry

    internal sealed record RegistryParameters(string Hive, string Path, string Name, string? Type = null, string? Value = null);
    internal sealed record RegistryState(OptimizationCatalog.RegistryValueSnapshot Value, int MissingKeys);

    private static readonly string[] AutostartKeys =
        ["Run", "RunOnce", "RunOnceEx", "RunServices", "RunServicesOnce", @"Policies\Explorer\Run"];

    private static readonly string[] ForbiddenRegistry =
    [
        @"HKLM\SAM", @"HKLM\SECURITY", @"HKLM\BCD00000000",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa", @"HKLM\SYSTEM\CurrentControlSet\Control\CI",
        @"HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders",
        @"HKLM\SYSTEM\CurrentControlSet\Control\SafeBoot", @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\AppCertDlls",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs", @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment",
        @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess",
        @"HKLM\SOFTWARE\Microsoft\Windows Defender", @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Advanced Threat Protection", @"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SilentProcessExit",
        @"HKLM\SOFTWARE\Classes", @"HKCU\Software\Classes", @"HKCU\Environment",
        @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon", @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\Windows",
        .. AutostartKeys.Select(key => $@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\{key}"),
        .. AutostartKeys.Select(key => $@"HKCU\Software\Microsoft\Windows\CurrentVersion\{key}"),
        .. CriticalServices().Select(service => $@"HKLM\SYSTEM\CurrentControlSet\Services\{service}")
    ];

    internal static void RequireAllowedRegistry(string hive, string path, string name)
    {
        // Aliases must not bypass the list: ControlSet00N and the WOW64 view reach the same protected data.
        var full = Regex.Replace($@"{hive}\{path}", @"^HKLM\\SYSTEM\\ControlSet\d{3}(?=\\|$)", @"HKLM\SYSTEM\CurrentControlSet", RegexOptions.IgnoreCase);
        full = Regex.Replace(full, @"\\WOW6432Node(?=\\|$)", "", RegexOptions.IgnoreCase);
        bool Under(string prefix) => full.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(prefix + @"\", StringComparison.OrdinalIgnoreCase);
        var execValue = Under(@"HKLM\SYSTEM\CurrentControlSet\Services") &&
            name is "ImagePath" or "ServiceDll" or "FailureCommand" or "ObjectName" or "RequiredPrivileges" ||
            full.Equals(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager", StringComparison.OrdinalIgnoreCase) &&
            name is "BootExecute" or "SetupExecute" or "Execute" or "S0InitialCommand" or "PendingFileRenameOperations";
        if (execValue || ForbiddenRegistry.Any(Under))
            throw new InvalidOperationException($@"{hive}\{path}\{name} is a protected security, boot or autostart target.");
    }

    private static (string Hive, string Path, string Name) RegistryTarget(string? hive, string? path, string? name)
    {
        var normalizedHive = hive?.Trim().ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => "HKCU",
            "HKLM" or "HKEY_LOCAL_MACHINE" => "HKLM",
            _ => throw new InvalidOperationException("Only HKCU and HKLM can be written.")
        };
        var normalizedPath = path?.Trim().Trim('\\') ?? "";
        if (normalizedPath.Length is 0 or > 512 || normalizedPath.Split('\\').Any(part => part.Length is 0 or > 255) ||
            normalizedPath.Any(char.IsControl))
            throw new InvalidOperationException("The Registry path is invalid.");
        if (name is null || name.Length > 255 || name.Any(char.IsControl)) throw new InvalidOperationException("The Registry value name is invalid.");
        return (normalizedHive, normalizedPath, name);
    }

    private static OptimizationAction RegistryValue(string kind, RegistryParameters parameters, bool set)
    {
        var (hive, path, name) = RegistryTarget(parameters.Hive, parameters.Path, parameters.Name);
        RequireAllowedRegistry(hive, path, name);
        (object Value, RegistryValueKind Kind)? desired = null;
        if (set)
        {
            if (!Enum.TryParse<RegistryValueKind>(parameters.Type, true, out var type) || !Enum.IsDefined(type) || type is not (RegistryValueKind.String or
                RegistryValueKind.ExpandString or RegistryValueKind.DWord or RegistryValueKind.QWord or RegistryValueKind.MultiString or RegistryValueKind.Binary) ||
                int.TryParse(parameters.Type, out _))
                throw new InvalidOperationException("Type must be String, ExpandString, DWord, QWord, MultiString or Binary.");
            desired = (ParseRegistryValue(type, parameters.Value ?? throw new InvalidOperationException("A value is required.")), type);
            parameters = new(hive, path, name, type.ToString(), FormatRegistryValue(type, desired.Value.Value));
        }
        else
        {
            if (parameters.Type is not null || parameters.Value is not null) throw new InvalidOperationException("registry.delete takes no type or value.");
            parameters = new(hive, path, name);
        }
        var display = $@"{hive}\{path}\{(name.Length == 0 ? "(Default)" : name)}";
        return Registry(kind, parameters, hive, path, name, () => desired,
            set ? $"Set {Short(display)} = {Short(parameters.Value!)}" : $"Delete {Short(display)}", "Registry",
            hive == "HKLM" ? RiskLevel.High : RiskLevel.Medium,
            hive == "HKLM" && path.StartsWith(@"SYSTEM\", StringComparison.OrdinalIgnoreCase),
            "Effect depends on the component that reads this value; some values apply only after a restart or sign-out");
    }

    internal static object ParseRegistryValue(RegistryValueKind kind, string value)
    {
        if (value.Length > 16_384) throw new InvalidOperationException("The Registry value is too large.");
        static ulong Number(string text, ulong maximum, long minimum)
        {
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ulong.Parse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) is var hex && hex <= maximum
                    ? hex : throw new InvalidOperationException("The number is out of range.");
            if (text.StartsWith('-'))
                return long.Parse(text, CultureInfo.InvariantCulture) is var negative && negative >= minimum
                    ? unchecked((ulong)negative) : throw new InvalidOperationException("The number is out of range.");
            return ulong.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) is var parsed && parsed <= maximum
                ? parsed : throw new InvalidOperationException("The number is out of range.");
        }
        return kind switch
        {
            RegistryValueKind.DWord => unchecked((int)(uint)Number(value, uint.MaxValue, int.MinValue)),
            RegistryValueKind.QWord => unchecked((long)Number(value, ulong.MaxValue, long.MinValue)),
            RegistryValueKind.Binary => Convert.FromHexString(Regex.Replace(value, @"[\s,\-]", "")),
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(value) is { } strings && strings.All(item => item is not null && !item.Contains('\0'))
                ? strings : throw new InvalidOperationException("MultiString values must be a JSON array of strings."),
            RegistryValueKind.String or RegistryValueKind.ExpandString => !value.Contains('\0') ? value
                : throw new InvalidOperationException("String values cannot contain NUL."),
            _ => throw new InvalidOperationException($"Unsupported Registry type: {kind}.")
        };
    }

    internal static string? FormatRegistryValue(RegistryValueKind kind, object? value) => (kind, value) switch
    {
        (RegistryValueKind.DWord, int number) => unchecked((uint)number).ToString(CultureInfo.InvariantCulture),
        (RegistryValueKind.QWord, long number) => unchecked((ulong)number).ToString(CultureInfo.InvariantCulture),
        (RegistryValueKind.Binary, byte[] bytes) => Convert.ToHexString(bytes),
        (RegistryValueKind.MultiString, string[] strings) => JsonSerializer.Serialize(strings),
        (RegistryValueKind.String or RegistryValueKind.ExpandString, string text) => text,
        _ => null
    };

    private static RegistryKey Base(string hive) => RegistryKey.OpenBaseKey(
        hive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);

    // Shared by every Registry-backed kind: desired() == null deletes the value.
    private static OptimizationAction Registry<T>(string kind, T parameters, string hive, string path, string name,
        Func<(object Value, RegistryValueKind Kind)?> desired, string title, string category, RiskLevel risk, bool restart,
        string sideEffect, Func<object?, RegistryValueKind?, bool>? matches = null, Func<string?>? unavailable = null)
    {
        matches ??= (value, valueKind) => desired() is { } wanted
            ? valueKind == wanted.Kind && FormatRegistryValue(valueKind.Value, value) == FormatRegistryValue(wanted.Kind, wanted.Value)
            : value is null;
        (object? Value, RegistryValueKind? Kind) Current()
        {
            using var root = Base(hive);
            using var key = root.OpenSubKey(path);
            var value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return (value, value is null ? null : key!.GetValueKind(name));
        }
        ActionAvailability Inspect()
        {
            var (value, valueKind) = Current();
            var label = value is null ? "Not set" : $"{valueKind}: {Short(FormatRegistryValue(valueKind!.Value, value) ?? "unsupported type")}";
            if (unavailable?.Invoke() is { } reason) return ActionAvailability.Unavailable(reason, label);
            return matches(value, valueKind) ? ActionAvailability.Applied(label) : ActionAvailability.Ready(label);
        }
        void Apply()
        {
            using var root = Base(hive);
            if (desired() is { } wanted)
            {
                using var key = root.CreateSubKey(path, true) ?? throw new InvalidOperationException($@"Cannot open {hive}\{path}.");
                key.SetValue(name, wanted.Value, wanted.Kind);
            }
            else
            {
                using var key = root.OpenSubKey(path, true);
                key?.DeleteValue(name, false);
            }
        }
        return Build(kind, parameters, title, category, risk, restart, $@"{hive}\{path}\{name}", sideEffect, Inspect,
            () => CaptureRegistry(hive, path, name), Apply, state => RestoreRegistry(hive, path, name, state),
            () => { var (value, valueKind) = Current(); return matches(value, valueKind); },
            state => _ = ReadRegistryState(state));
    }

    internal static string CaptureRegistry(string hive, string path, string name)
    {
        using var root = Base(hive);
        var missing = 0;
        for (var current = path; ; missing++)
        {
            using (var key = root.OpenSubKey(current)) if (key is not null) break;
            var cut = current.LastIndexOf('\\');
            if (cut < 0) { missing++; break; }
            current = current[..cut];
        }
        var value = OptimizationCatalog.CaptureRegistryValue(hive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, path, name);
        return JsonSerializer.Serialize(new RegistryState(value, missing));
    }

    internal static RegistryState ReadRegistryState(string state)
    {
        var root = ExactObject(state, "Value", "MissingKeys");
        var saved = new RegistryState(OptimizationCatalog.DeserializeRegistrySnapshot(root.GetProperty("Value").GetRawText()),
            root.GetProperty("MissingKeys").GetInt32());
        if (saved.MissingKeys is < 0 or > 64 || saved.MissingKeys > 0 && saved.Value.Exists)
            throw new InvalidOperationException("The Registry snapshot is inconsistent.");
        return saved;
    }

    private static void RestoreRegistry(string hive, string path, string name, string state)
    {
        var saved = ReadRegistryState(state);
        if (saved.MissingKeys == 0)
        {
            OptimizationCatalog.RestoreRegistryValue(hive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, path, name, saved.Value);
            return;
        }
        using var root = Base(hive);
        using (var key = root.OpenSubKey(path, true)) key?.DeleteValue(name, false);
        var current = path;
        for (var level = 0; level < saved.MissingKeys; level++)
        {
            using (var key = root.OpenSubKey(current))
            {
                // Something else wrote here meanwhile: keep it; VerifyRestored reports the difference.
                if (key is not null && (key.ValueCount > 0 || key.SubKeyCount > 0)) return;
            }
            root.DeleteSubKey(current, false);
            var cut = current.LastIndexOf('\\');
            if (cut < 0) return;
            current = current[..cut];
        }
    }

    internal sealed record StartupParameters(string Scope, string Location, string Name, bool? Enabled);

    private static OptimizationAction StartupEntry(StartupParameters parameters)
    {
        var (hive, _, name) = RegistryTarget(parameters.Scope, "Software", parameters.Name);
        if (name.Length == 0 || name.Contains('\\')) throw new InvalidOperationException("The startup entry name is invalid.");
        var location = parameters.Location switch
        {
            "Run" or "Run32" or "StartupFolder" => parameters.Location,
            _ => throw new InvalidOperationException("Location must be Run, Run32 or StartupFolder.")
        };
        var enabled = parameters.Enabled ?? throw new InvalidOperationException("Enabled is required.");
        parameters = parameters with { Scope = hive, Location = location, Name = name };
        bool Exists()
        {
            if (location == "StartupFolder")
                return File.Exists(Path.Combine(Environment.GetFolderPath(hive == "HKLM"
                    ? Environment.SpecialFolder.CommonStartup : Environment.SpecialFolder.Startup), name));
            using var root = Base(hive);
            using var key = root.OpenSubKey(location == "Run32" ? @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
                : @"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue(name) is not null;
        }
        // StartupApproved: first byte 2 = enabled, 3 = disabled, followed by the FILETIME of the change.
        (object, RegistryValueKind)? Desired()
        {
            var value = new byte[12];
            value[0] = (byte)(enabled ? 2 : 3);
            if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(value, 4);
            return (value, RegistryValueKind.Binary);
        }
        return Registry("startup.entry", parameters, hive,
            $@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{location}", name, Desired,
            $"{(enabled ? "Enable" : "Disable")} startup entry {Short(name)}", "Apps",
            hive == "HKLM" ? RiskLevel.Medium : RiskLevel.Low, false,
            "The application no longer starts (or starts again) at sign-in; it remains installed",
            (value, _) => value is byte[] { Length: > 0 } bytes ? (bytes[0] & 1) == (enabled ? 0 : 1) : enabled,
            () => Exists() ? null : "The startup entry was not found");
    }

    internal sealed record AppCompatParameters(string Exe, string[] Flags);

    private static readonly string[] CompatFlags = ["DISABLEDXMAXIMIZEDWINDOWEDMODE", "HIGHDPIAWARE", "DPIUNAWARE", "GDIDPISCALING"];

    private static OptimizationAction AppCompatLayer(AppCompatParameters parameters)
    {
        var exe = ExecutablePath(parameters.Exe);
        var flags = (parameters.Flags ?? []).Select(flag => flag?.Trim().ToUpperInvariant() ?? "").Distinct().Order(StringComparer.Ordinal).ToArray();
        if (flags.Any(flag => !CompatFlags.Contains(flag))) throw new InvalidOperationException($"Flags must be among: {string.Join(", ", CompatFlags)}.");
        parameters = new(exe, flags);
        (object, RegistryValueKind)? Desired() => flags.Length == 0 ? null : ("~ " + string.Join(' ', flags), RegistryValueKind.String);
        return Registry("appcompat.layer", parameters, "HKCU", @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
            exe, Desired, flags.Length == 0 ? $"Clear compatibility flags for {Path.GetFileName(exe)}" : $"Compatibility flags {string.Join(' ', flags)} for {Path.GetFileName(exe)}",
            "Gaming", RiskLevel.Low, false, "Applies the next time the executable starts; replaces any other compatibility flags on it",
            unavailable: () => File.Exists(exe) ? null : "The executable no longer exists");
    }

    internal static string ExecutablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) || !path.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) ||
            path.IndexOf(':', 2) >= 0)
            throw new InvalidOperationException("The executable must be a normalized local .exe path.");
        return path;
    }

    internal sealed record IfeoParameters(string Exe, string Name, int? Value);

    private static OptimizationAction IfeoPerf(IfeoParameters parameters)
    {
        var exe = parameters.Exe?.Trim() ?? "";
        if (!Regex.IsMatch(exe, @"^[A-Za-z0-9 _.()\-]{1,100}\.exe$", RegexOptions.IgnoreCase)) throw new InvalidOperationException("Exe must be a file name such as game.exe.");
        int[] allowed = parameters.Name switch
        {
            "CpuPriorityClass" => [1, 2, 3, 5, 6],
            "IoPriority" => [0, 1, 2],
            "PagePriority" => [1, 2, 3, 4, 5],
            _ => throw new InvalidOperationException("Name must be CpuPriorityClass, IoPriority or PagePriority.")
        };
        if (parameters.Value is { } value && !allowed.Contains(value)) throw new InvalidOperationException($"{parameters.Name} must be one of {string.Join(", ", allowed)}.");
        parameters = parameters with { Exe = exe.ToLowerInvariant() };
        // Only PerfOptions is reachable here: the IFEO Debugger/GlobalFlag values stay forbidden for registry.set.
        return Registry("ifeo.perf", parameters, "HKLM",
            $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{parameters.Exe}\PerfOptions", parameters.Name,
            () => parameters.Value is { } wanted ? (wanted, RegistryValueKind.DWord) : null,
            parameters.Value is null ? $"Remove {parameters.Name} for {exe}" : $"{parameters.Name} = {parameters.Value} for {exe}",
            "Gaming", RiskLevel.Medium, false, "Applies the next time the executable starts");
    }

    // ---------------------------------------------------------------- Services

    internal sealed record ServiceParameters(string Name, string StartType);
    internal sealed record ServiceState(int Start, bool Delayed, bool Running);

    // Only services Windows cannot boot or sign in without, Winmgmt (NeuroTune reads and verifies through WMI) and
    // Defender/firewall (F7 owns Defender). Every other service, including automatic ones, can be changed.
    private static IEnumerable<string> CriticalServices() =>
    [
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "BrokerInfrastructure", "CoreMessagingRegistrar", "SystemEventsBroker",
        "StateRepository", "PlugPlay", "Power", "ProfSvc", "SamSs", "UserManager", "gpsvc", "Winmgmt", "BFE", "mpssvc",
        "WinDefend", "WdNisSvc", "WdNisDrv", "WdFilter", "WdBoot", "Sense", "SecurityHealthService", "wscsvc", "SgrmBroker"
    ];

    private static OptimizationAction ServiceStartType(ServiceParameters parameters)
    {
        var name = parameters.Name?.Trim() ?? "";
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_.\-]{1,80}$")) throw new InvalidOperationException("The service name is invalid.");
        if (CriticalServices().Contains(name, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException($"{name} is a protected Windows service.");
        var (start, delayed) = parameters.StartType switch
        {
            "Automatic" => (2, false),
            "AutomaticDelayed" => (2, true),
            "Manual" => (3, false),
            "Disabled" => (4, false),
            _ => throw new InvalidOperationException("StartType must be Automatic, AutomaticDelayed, Manual or Disabled.")
        };
        parameters = new(name, parameters.StartType);
        ServiceState Current()
        {
            using var root = Base("HKLM");
            using var key = root.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}") ?? throw new InvalidOperationException($"Service {name} does not exist.");
            if (((key.GetValue("Type") as int? ?? 0) & 0x30) == 0) throw new InvalidOperationException($"{name} is a driver, not a Win32 service.");
            var states = SystemProfiler.Query($"SELECT State FROM Win32_Service WHERE Name='{name}'", row => row["State"]?.ToString());
            if (states.Count != 1) throw new InvalidOperationException($"The state of service {name} could not be read.");
            var startValue = key.GetValue("Start") as int? ?? -1;
            return new(startValue, startValue == 2 && key.GetValue("DelayedAutostart") as int? == 1, states[0] == "Running");
        }
        void Set(int value, bool delay) => WindowsCommand.Run("sc.exe", "config", name, "start=", value switch
        {
            2 => delay ? "delayed-auto" : "auto",
            3 => "demand",
            4 => "disabled",
            _ => throw new InvalidOperationException("Unsupported service start type.")
        });
        void Wait(bool running)
        {
            for (var attempt = 0; attempt < 60 && Current().Running != running; attempt++) Thread.Sleep(500);
        }
        ActionAvailability Inspect()
        {
            var state = Current();
            var label = $"Start={state.Start}{(state.Delayed ? " (delayed)" : "")}, {(state.Running ? "running" : "stopped")}";
            if (start == 4)
            {
                var dependents = SystemProfiler.Query($"ASSOCIATORS OF {{Win32_Service.Name='{name}'}} WHERE AssocClass=Win32_DependentService Role=Antecedent",
                    row => (Name: row["Name"]?.ToString() ?? "", State: row["State"]?.ToString(), Mode: row["StartMode"]?.ToString()))
                    .Where(item => item.State == "Running" || item.Mode == "Auto").Select(item => item.Name).ToList();
                if (dependents.Count > 0) return ActionAvailability.Unavailable($"Active services depend on it: {string.Join(", ", dependents)}", label);
            }
            return state.Start == start && state.Delayed == delayed && !(start == 4 && state.Running)
                ? ActionAvailability.Applied(label) : ActionAvailability.Ready(label);
        }
        static ServiceState ReadState(string state)
        {
            ExactObject(state, "Start", "Delayed", "Running");
            var saved = JsonSerializer.Deserialize<ServiceState>(state)!;
            if (saved.Start is < 2 or > 4 || saved.Delayed && saved.Start != 2) throw new InvalidOperationException("The saved service state is invalid.");
            return saved;
        }
        return Build("service.start-type", parameters, $"Set service {name} to {parameters.StartType}", "Services",
            start == 4 ? RiskLevel.High : RiskLevel.Medium, false, $"service:{name}",
            start == 4 ? "Stops the service now and keeps it from starting; features that need it stop working" : "Changes when Windows starts the service",
            Inspect, () => JsonSerializer.Serialize(Current()),
            () =>
            {
                Set(start, delayed);
                if (start == 4 && Current().Running) { WindowsCommand.Run("sc.exe", "stop", name); Wait(false); }
            },
            state =>
            {
                var saved = ReadState(state);
                Set(saved.Start, saved.Delayed);
                if (saved.Running && !Current().Running) { WindowsCommand.Run("sc.exe", "start", name); Wait(true); }
            },
            () => Current() is var state && state.Start == start && state.Delayed == delayed && !(start == 4 && state.Running),
            state => _ = ReadState(state),
            state => { var saved = ReadState(state); var now = Current(); return now.Start == saved.Start && now.Delayed == saved.Delayed && (!saved.Running || now.Running); });
    }

    // ---------------------------------------------------------------- PowerShell-backed kinds

    private static string PowerShell(string script) => WindowsCommand.Run("powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive",
        "-Command", "$ErrorActionPreference = 'Stop'; " + script).Trim();

    private static string Quote(string value)
    {
        // PowerShell also closes single-quoted strings on typographic quotes.
        if (value.Any(character => char.IsControl(character) || character is '‘' or '’' or '‚' or '‛'))
            throw new InvalidOperationException("The value contains unsupported characters.");
        return "'" + value.Replace("'", "''") + "'";
    }

    internal sealed record NetAdapterParameters(string Adapter, string Keyword, string Value);

    private static OptimizationAction NetAdapterProperty(NetAdapterParameters parameters)
    {
        var adapter = Guid.Parse(parameters.Adapter ?? "").ToString("B").ToUpperInvariant();
        if (!Regex.IsMatch(parameters.Keyword ?? "", @"^\*?[A-Za-z0-9_]{1,64}$") || !Regex.IsMatch(parameters.Value ?? "", @"^[A-Za-z0-9_.\-]{1,64}$"))
            throw new InvalidOperationException("The keyword or value is invalid.");
        parameters = new(adapter, parameters.Keyword!, parameters.Value!);
        var prefix = $"$a = @(Get-NetAdapter -IncludeHidden | Where-Object InterfaceGuid -eq {Quote(adapter)}); " +
            "if ($a.Count -ne 1) { throw 'The network adapter was not found.' }; " +
            $"$p = Get-NetAdapterAdvancedProperty -Name $a[0].Name -RegistryKeyword {Quote(parameters.Keyword)}; ";
        string[] Current() => JsonSerializer.Deserialize<string[]>(PowerShell(prefix + "[Console]::Write((ConvertTo-Json -Compress -InputObject @($p.RegistryValue)))")) ?? [];
        void Set(string[] values) => PowerShell(prefix + $"Set-NetAdapterAdvancedProperty -Name $a[0].Name -RegistryKeyword {Quote(parameters.Keyword)} " +
            $"-RegistryValue @({string.Join(',', values.Select(Quote))})");
        static string[] ReadState(string state)
        {
            var values = JsonSerializer.Deserialize<string[]>(state) ?? [];
            if (values.Length is 0 or > 16 || values.Any(value => value is null || value.Length > 256)) throw new InvalidOperationException("The saved adapter property is invalid.");
            return values;
        }
        return Build("netadapter.property", parameters, $"Adapter {adapter[1..9]} {parameters.Keyword} = {parameters.Value}", "Network",
            RiskLevel.Medium, false, $"netadapter:{adapter}:{parameters.Keyword}", "The adapter restarts and the connection drops for a few seconds",
            () => Current() is var current && current.SequenceEqual([parameters.Value]) ? ActionAvailability.Applied(string.Join(",", current))
                : ActionAvailability.Ready(string.Join(",", Current())),
            () => Current() is { Length: > 0 } current ? JsonSerializer.Serialize(current)
                : throw new InvalidOperationException("The property has no explicit value to restore."),
            () => PowerShell(prefix + $"if ($p.ValidRegistryValues -and ($p.ValidRegistryValues -notcontains {Quote(parameters.Value)})) {{ throw 'The driver does not accept this value.' }}; " +
                $"Set-NetAdapterAdvancedProperty -Name $a[0].Name -RegistryKeyword {Quote(parameters.Keyword)} -RegistryValue {Quote(parameters.Value)}"),
            state => Set(ReadState(state)),
            () => Current().SequenceEqual([parameters.Value]), state => _ = ReadState(state),
            state => Current().SequenceEqual(ReadState(state)));
    }

    internal sealed record QosParameters(string Exe, int? Dscp);

    private static OptimizationAction QosPolicy(QosParameters parameters)
    {
        var exe = parameters.Exe?.Trim().ToLowerInvariant() ?? "";
        if (!Regex.IsMatch(exe, @"^[a-z0-9 _.()\-]{1,100}\.exe$") || parameters.Dscp is not (>= 0 and <= 63))
            throw new InvalidOperationException("Exe must be a file name such as game.exe and Dscp 0-63.");
        var target = parameters.Dscp!.Value;
        parameters = new(exe, target);
        var policy = Quote($"NeuroTune {exe}");
        int Current() => int.Parse(PowerShell($"$p = Get-NetQosPolicy -PolicyStore localhost -Name {policy} -ErrorAction SilentlyContinue; " +
            "if ($p) { [Console]::Write($p.DSCPAction) } else { [Console]::Write(-1) }"), CultureInfo.InvariantCulture);
        void Set(int dscp) => PowerShell(dscp < 0
            ? $"Remove-NetQosPolicy -PolicyStore localhost -Name {policy} -Confirm:$false"
            : $"if (Get-NetQosPolicy -PolicyStore localhost -Name {policy} -ErrorAction SilentlyContinue) " +
              $"{{ Set-NetQosPolicy -PolicyStore localhost -Name {policy} -DSCPAction {dscp} -Confirm:$false }} " +
              $"else {{ New-NetQosPolicy -PolicyStore localhost -Name {policy} -AppPathNameMatchCondition {Quote(exe)} -DSCPAction {dscp} -NetworkProfile All | Out-Null }}");
        static int ReadState(string state) => int.Parse(state, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) is var dscp && dscp is >= -1 and <= 63
            ? dscp : throw new InvalidOperationException("The saved QoS state is invalid.");
        return Build("qos.policy", parameters, $"Mark {exe} traffic with DSCP {target}", "Network", RiskLevel.Medium, false,
            $"qos:{exe}", "Tags outgoing packets of the executable; only networks that honor DSCP change their priority",
            () => Current() is var dscp && dscp == target ? ActionAvailability.Applied($"DSCP {dscp}")
                : ActionAvailability.Ready(dscp < 0 ? "No NeuroTune policy" : $"DSCP {dscp}"),
            () => Current().ToString(CultureInfo.InvariantCulture), () => Set(target),
            state => { var saved = ReadState(state); if (saved >= 0 || Current() >= 0) Set(saved); },
            () => Current() == target, state => _ = ReadState(state));
    }

    internal sealed record TaskParameters(string Path, bool? Enabled);

    private static OptimizationAction ScheduledTask(TaskParameters parameters)
    {
        var path = parameters.Path?.Trim() ?? "";
        if (!Regex.IsMatch(path, @"^\\([^\\*?\[\]\x00-\x1f]+\\)*[^\\*?\[\]\x00-\x1f]+$") || path.Length > 260 || path.Split('\\').Any(part => part is "." or ".."))
            throw new InvalidOperationException("The task path is invalid.");
        if (path.StartsWith(@"\Microsoft\Windows\Windows Defender\", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(@"\Microsoft\Windows\ExploitGuard\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Security scheduled tasks are protected.");
        var enabled = parameters.Enabled ?? throw new InvalidOperationException("Enabled is required.");
        parameters = new(path, enabled);
        var cut = path.LastIndexOf('\\');
        var target = $"-TaskPath {Quote(path[..(cut + 1)])} -TaskName {Quote(path[(cut + 1)..])}";
        bool Enabled() => PowerShell($"[Console]::Write((Get-ScheduledTask {target}).State)") != "Disabled";
        void Set(bool enabled) => PowerShell($"{(enabled ? "Enable" : "Disable")}-ScheduledTask {target} | Out-Null");
        static bool ReadState(string state) => JsonSerializer.Deserialize<bool>(state);
        return Build("scheduled-task.state", parameters, $"{(enabled ? "Enable" : "Disable")} scheduled task {Short(path)}", "Services",
            RiskLevel.Medium, false, $"task:{path}", "The task runs (or no longer runs) on its triggers",
            () => Enabled() == enabled ? ActionAvailability.Applied(enabled ? "Enabled" : "Disabled")
                : ActionAvailability.Ready(enabled ? "Disabled" : "Enabled"),
            () => JsonSerializer.Serialize(Enabled()), () => Set(enabled), state => Set(ReadState(state)),
            () => Enabled() == enabled, state => _ = ReadState(state));
    }

    internal sealed record DefenderParameters(string Type, string Value);

    private static OptimizationAction DefenderExclusion(DefenderParameters parameters)
    {
        var value = parameters.Value?.Trim() ?? "";
        if (parameters.Type == "Path")
        {
            if (value.Length > 1024 || !Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal) ||
                !value.Equals(Path.GetFullPath(value).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The exclusion must be a normalized local path.");
            string[] broad =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetPathRoot(value)!.TrimEnd('\\')
            ];
            var users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!;
            if (broad.Any(root => value.Equals(root, StringComparison.OrdinalIgnoreCase)) || IsUnder(value, broad[0]) ||
                value.Equals(users, StringComparison.OrdinalIgnoreCase) || IsUnder(value, users) && !Path.GetRelativePath(users, value).Contains('\\'))
                throw new InvalidOperationException("The exclusion is too broad or covers Windows itself.");
        }
        else if (parameters.Type == "Process")
        {
            if (!Regex.IsMatch(value, @"^[A-Za-z0-9 _.()\-]{1,100}\.exe$", RegexOptions.IgnoreCase)) value = ExecutablePath(value);
        }
        else throw new InvalidOperationException("Type must be Path or Process.");
        parameters = new(parameters.Type, value);
        var list = $"Exclusion{parameters.Type}";
        bool Present() => PowerShell($"[Console]::Write(@((Get-MpPreference).{list}) -contains {Quote(value)})") == "True";
        static bool ReadState(string state) => JsonSerializer.Deserialize<bool>(state);
        return Build("defender.exclusion", parameters, $"Exclude {parameters.Type.ToLowerInvariant()} {Short(value)} from Microsoft Defender", "Security",
            RiskLevel.High, false, $"defender:{parameters.Type}:{value.ToLowerInvariant()}",
            "Microsoft Defender no longer scans this path or process until the exclusion is removed",
            () => Present() ? ActionAvailability.Applied("Excluded") : ActionAvailability.Ready("Scanned"),
            () => JsonSerializer.Serialize(Present()), () => PowerShell($"Add-MpPreference -{list} {Quote(value)}"),
            state => { if (!ReadState(state) && Present()) PowerShell($"Remove-MpPreference -{list} {Quote(value)}"); },
            Present, state => _ = ReadState(state));
    }

    // ---------------------------------------------------------------- Power and boot

    internal sealed record PowerSettingParameters(Guid Subgroup, Guid Setting, uint? Ac, uint? Dc, Guid? Scheme = null);
    internal sealed record PowerSettingState(Guid SchemeId, uint Ac, uint Dc);

    private static OptimizationAction PowerSetting(PowerSettingParameters parameters)
    {
        if (parameters.Subgroup == Guid.Empty || parameters.Setting == Guid.Empty || parameters.Scheme == Guid.Empty ||
            parameters.Ac is null && parameters.Dc is null)
            throw new InvalidOperationException("Subgroup, Setting and at least one of Ac/Dc are required.");
        var captured = parameters.Scheme;
        bool Matches(Guid scheme)
        {
            var value = OptimizationCatalog.ReadPowerSetting(parameters.Subgroup, parameters.Setting, scheme);
            return (parameters.Ac is null || value.Ac == parameters.Ac) && (parameters.Dc is null || value.Dc == parameters.Dc);
        }
        void Write(Guid scheme, uint? ac, uint? dc)
        {
            if (ac is { } acValue) WindowsCommand.Run("powercfg.exe", "/setacvalueindex", scheme.ToString("D"), parameters.Subgroup.ToString("D"), parameters.Setting.ToString("D"), acValue.ToString(CultureInfo.InvariantCulture));
            if (dc is { } dcValue) WindowsCommand.Run("powercfg.exe", "/setdcvalueindex", scheme.ToString("D"), parameters.Subgroup.ToString("D"), parameters.Setting.ToString("D"), dcValue.ToString(CultureInfo.InvariantCulture));
            if (OptimizationCatalog.ActivePowerScheme() == scheme) WindowsCommand.Run("powercfg.exe", "/setactive", scheme.ToString("D"));
        }
        static PowerSettingState ReadState(string state)
        {
            ExactObject(state, "SchemeId", "Ac", "Dc");
            var saved = JsonSerializer.Deserialize<PowerSettingState>(state)!;
            return saved.SchemeId != Guid.Empty ? saved : throw new InvalidOperationException("The saved power scheme is invalid.");
        }
        return Build("powercfg.setting", parameters, $"Power setting {parameters.Setting.ToString()[..8]} AC={parameters.Ac?.ToString() ?? "-"} DC={parameters.Dc?.ToString() ?? "-"}",
            "Power", RiskLevel.Medium, false, $"power:{parameters.Scheme?.ToString() ?? "active"}:{parameters.Setting}",
            "Changes the power scheme value; may affect heat, noise and energy use",
            () =>
            {
                var scheme = parameters.Scheme ?? OptimizationCatalog.ActivePowerScheme();
                var value = OptimizationCatalog.ReadPowerSetting(parameters.Subgroup, parameters.Setting, scheme);
                var label = $"AC={value.Ac} DC={value.Dc}";
                return Matches(scheme) ? ActionAvailability.Applied(label) : ActionAvailability.Ready(label);
            },
            () =>
            {
                captured = parameters.Scheme ?? OptimizationCatalog.ActivePowerScheme();
                var value = OptimizationCatalog.ReadPowerSetting(parameters.Subgroup, parameters.Setting, captured);
                return JsonSerializer.Serialize(new PowerSettingState(captured.Value, value.Ac, value.Dc));
            },
            () => Write(captured ?? throw new InvalidOperationException("Capture the exact scheme before applying."), parameters.Ac, parameters.Dc),
            state => { var saved = ReadState(state); Write(saved.SchemeId, saved.Ac, saved.Dc); },
            () => captured is { } scheme && Matches(scheme), state => _ = ReadState(state),
            state =>
            {
                var saved = ReadState(state);
                var value = OptimizationCatalog.ReadPowerSetting(parameters.Subgroup, parameters.Setting, saved.SchemeId);
                return value.Ac == saved.Ac && value.Dc == saved.Dc;
            });
    }

    internal sealed record BcdParameters(string Element, string? Value);

    private static readonly Dictionary<string, string[]> BcdElements = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disabledynamictick"] = ["Yes", "No"],
        ["tscsyncpolicy"] = ["Default", "Legacy", "Enhanced"],
        ["x2apicpolicy"] = ["Enable", "Disable"],
        ["quietboot"] = ["Yes", "No"],
        ["bootux"] = ["Disabled", "Basic", "Standard"],
        ["bootmenupolicy"] = ["Legacy", "Standard"],
        ["hypervisorlaunchtype"] = ["Off", "Auto"]
    };

    internal static OptimizationAction BcdOption(BcdParameters parameters, string? store = null, string entry = "{current}")
    {
        if (parameters.Element is null || !BcdElements.TryGetValue(parameters.Element, out var allowed))
            throw new InvalidOperationException($"Element must be one of: {string.Join(", ", BcdElements.Keys)}.");
        var element = BcdElements.Keys.Single(key => key.Equals(parameters.Element, StringComparison.OrdinalIgnoreCase));
        string? Canonical(string? value) => value is null ? null
            : allowed.FirstOrDefault(item => item.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"{element} must be one of: {string.Join(", ", allowed)}.");
        parameters = new(element, Canonical(parameters.Value));
        string Bcd(params string[] arguments) => WindowsCommand.Run("bcdedit.exe", store is null ? arguments : ["/store", store, .. arguments]);
        string? Current() => Bcd("/enum", entry).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[0].Equals(element, StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim()).FirstOrDefault();
        void Set(string? value)
        {
            if (value is not null) Bcd("/set", entry, element, value);
            else if (Current() is not null) Bcd("/deletevalue", entry, element);
        }
        string? ReadState(string state) => Canonical(JsonSerializer.Deserialize<string?>(state));
        return Build("bcd.option", parameters, parameters.Value is null ? $"Remove boot option {element}" : $"Boot option {element} = {parameters.Value}",
            "Boot", RiskLevel.High, true, $"bcd:{element}", element == "hypervisorlaunchtype"
                ? "Off stops the Windows hypervisor after restart: VBS, Memory Integrity, Credential Guard, WSL2, Hyper-V and Windows Sandbox stop working, and anti-cheats that require them refuse to start"
                : "Changes the boot configuration after restart",
            () => Current() is var current && string.Equals(current, parameters.Value, StringComparison.OrdinalIgnoreCase)
                ? ActionAvailability.Applied(current ?? "Not set") : ActionAvailability.Ready(current ?? "Not set"),
            () => JsonSerializer.Serialize(Canonical(Current())), () => Set(parameters.Value), state => Set(ReadState(state)),
            () => string.Equals(Current(), parameters.Value, StringComparison.OrdinalIgnoreCase), state => _ = ReadState(state),
            state => string.Equals(Current(), ReadState(state), StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- Configuration files

    internal sealed record JsonFileParameters(string Path, string Key, JsonNode? Value);
    internal sealed record IniFileParameters(string Path, string Section, string Key, string Value);

    private static readonly JsonSerializerOptions ConfigJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static string ConfigPath(string? value, string extension)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || !Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal) ||
            !value.Equals(Path.GetFullPath(value), StringComparison.OrdinalIgnoreCase) || value.IndexOf(':', 2) >= 0 ||
            !Path.GetExtension(value).Equals(extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The configuration file must be a normalized local {extension} path.");
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using var steam = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var roots = new[] { profile, (steam?.GetValue("SteamPath") as string)?.Replace('/', '\\') }
            .Where(root => !string.IsNullOrWhiteSpace(root) && Path.IsPathFullyQualified(root)).Select(root => Path.GetFullPath(root!));
        if (!roots.Any(root => IsUnder(value, root)) || IsUnder(value, SettingsService.DataDirectory) ||
            IsUnder(value, Path.Combine(profile, @"AppData\Roaming\Microsoft\Windows\Start Menu")))
            throw new InvalidOperationException("Configuration files must be under the user profile or the Steam folder, outside NeuroTune's data.");
        return value;
    }

    private static bool IsUnder(string path, string root) =>
        path.StartsWith(root.TrimEnd('\\') + @"\", StringComparison.OrdinalIgnoreCase);

    private static void RequireNoLinks(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.Exists && info.LinkTarget is not null) throw new InvalidOperationException("Linked configuration paths are refused.");
        }
    }

    private static (string Text, Encoding Encoding) ReadText(string path)
    {
        RequireNoLinks(path);
        var bytes = File.ReadAllBytes(path);
        foreach (var encoding in new Encoding[] { new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true) })
        {
            var preamble = encoding.GetPreamble();
            if (bytes.AsSpan().StartsWith(preamble)) return (encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length), encoding);
        }
        return (new UTF8Encoding(false, true).GetString(bytes), new UTF8Encoding(false, true));
    }

    private static void WriteText(string path, string text, Encoding encoding)
    {
        RequireNoLinks(path);
        File.WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);
    }

    // Shared by file.json and file.ini: the snapshot is the whole file, restored byte for byte.
    private static OptimizationAction ConfigFile<T>(string kind, T parameters, string path, string key, string title,
        Func<string, bool> matches, Func<string, string> edit)
    {
        string Capture()
        {
            RequireNoLinks(path);
            var bytes = File.Exists(path) ? File.ReadAllBytes(path) : throw new InvalidOperationException("Only existing configuration files can be edited.");
            return bytes.Length <= 131_072 ? JsonSerializer.Serialize(new { Content = Convert.ToBase64String(bytes) })
                : throw new InvalidOperationException("The configuration file is larger than 128 KiB.");
        }
        static byte[] ReadState(string state) => Convert.FromBase64String(ExactObject(state, "Content").GetProperty("Content").GetString()!);
        return Build(kind, parameters, title, "Apps", RiskLevel.Low, false, $"file:{path.ToLowerInvariant()}:{key.ToLowerInvariant()}",
            "Applies the next time the application reads its configuration; the application may overwrite it while running",
            () => File.Exists(path) ? matches(ReadText(path).Text) ? ActionAvailability.Applied("Configured") : ActionAvailability.Ready("Different value")
                : ActionAvailability.Unavailable("The configuration file was not found"),
            Capture,
            () => { var (text, encoding) = ReadText(path); WriteText(path, edit(text), encoding); },
            state => { var bytes = ReadState(state); RequireNoLinks(path); File.WriteAllBytes(path, bytes); },
            () => matches(ReadText(path).Text), state => _ = ReadState(state));
    }

    private static OptimizationAction JsonFile(JsonFileParameters parameters)
    {
        var path = ConfigPath(parameters.Path, ".json");
        var keys = parameters.Key?.Split('.') ?? [];
        if (keys.Length is 0 or > 8 || keys.Any(part => part.Length is 0 or > 100 || part.Any(char.IsControl)))
            throw new InvalidOperationException("Key must be a dotted property path of up to 8 segments.");
        parameters = new(path, parameters.Key!, parameters.Value);
        var documentOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        JsonObject Root(string text) => JsonNode.Parse(text, documentOptions: documentOptions) as JsonObject
            ?? throw new InvalidOperationException("The configuration root is not a JSON object.");
        bool Matches(string text)
        {
            JsonNode? node = Root(text);
            foreach (var part in keys) { if (node is not JsonObject json || !json.TryGetPropertyValue(part, out node)) return false; }
            return JsonNode.DeepEquals(node, parameters.Value);
        }
        string Edit(string text)
        {
            var root = Root(text);
            var node = root;
            foreach (var part in keys[..^1])
            {
                if (node[part] is JsonObject child) { node = child; continue; }
                if (node[part] is not null) throw new InvalidOperationException($"{part} is not a JSON object.");
                var created = new JsonObject();
                node[part] = created;
                node = created;
            }
            node[keys[^1]] = parameters.Value?.DeepClone();
            return root.ToJsonString(ConfigJson);
        }
        return ConfigFile("file.json", parameters, path, parameters.Key, $"{Path.GetFileName(path)}: {Short(parameters.Key)} = {Short(parameters.Value?.ToJsonString() ?? "null")}",
            Matches, Edit);
    }

    private static OptimizationAction IniFile(IniFileParameters parameters)
    {
        var path = ConfigPath(parameters.Path, ".ini");
        var section = parameters.Section?.Trim() ?? "";
        var key = parameters.Key?.Trim() ?? "";
        var value = parameters.Value?.Trim() ?? throw new InvalidOperationException("A value is required.");
        if (!Regex.IsMatch(section, @"^[^\[\]\r\n]{1,100}$") || !Regex.IsMatch(key, @"^[^=;\[\]\r\n]{1,100}$") || value.Length > 1_000 || value.Any(char.IsControl))
            throw new InvalidOperationException("The INI section, key or value is invalid.");
        parameters = new(path, section, key, value);
        return ConfigFile("file.ini", parameters, path, $"{section}/{key}", $"{Path.GetFileName(path)}: [{Short(section)}] {Short(key)} = {Short(value)}",
            text => GetIniValue(text, section, key) == value, text => SetIniValue(text, section, key, value));
    }

    private static bool IsIniKey(string line, string key)
    {
        var equals = line.IndexOf('=');
        return equals > 0 && !line.TrimStart().StartsWith(';') && line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
    }

    private static (List<string> Lines, int Start, int End) IniSection(string text, string section)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var start = lines.FindIndex(line => line.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        var end = start + 1;
        while (start >= 0 && end < lines.Count && !lines[end].TrimStart().StartsWith('[')) end++;
        return (lines, start, end);
    }

    internal static string? GetIniValue(string text, string section, string key)
    {
        var (lines, start, end) = IniSection(text, section);
        if (start < 0) return null;
        var line = lines.Skip(start + 1).Take(end - start - 1).FirstOrDefault(line => IsIniKey(line, key));
        return line?[(line.IndexOf('=') + 1)..].Trim();
    }

    internal static string SetIniValue(string text, string section, string key, string value)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var (lines, start, end) = IniSection(text, section);
        if (start < 0)
        {
            if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.AddRange([$"[{section}]", $"{key}={value}", ""]);
            return string.Join(newline, lines);
        }
        var found = lines.FindIndex(start + 1, end - start - 1, line => IsIniKey(line, key));
        if (found >= 0) lines[found] = $"{lines[found][..lines[found].IndexOf('=')].Trim()}={value}";
        else
        {
            var insert = end;
            while (insert > start + 1 && lines[insert - 1].Trim().Length == 0) insert--;
            lines.Insert(insert, $"{key}={value}");
        }
        return string.Join(newline, lines);
    }

    // ---------------------------------------------------------------- Devices

    internal sealed record DeviceParameters(string Slug);

    // device.disable keeps its existing allowlisted IDs and guards; the kind only maps the slug parameter onto them.
    private static OptimizationAction DeviceDisable(DeviceParameters parameters) =>
        DeviceControl.Actions(DeviceControl.Machine).SingleOrDefault(action => action.Id == $"device.disable.{parameters.Slug}")
            ?? throw new InvalidOperationException($"Unknown device slug: {parameters.Slug}.");
}
