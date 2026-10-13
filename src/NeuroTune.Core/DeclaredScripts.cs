using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NeuroTune;

// F6: a model-written PowerShell script with declared targets (typed primitives) and its own rollback script.
// NeuroTune blocks dangerous constructs before running anything, snapshots every target with the primitives' Capture,
// tests the model's rollback where no restart is needed, and on Restore falls back to the primitives for any target the
// model rollback did not bring back. Registry writes outside the targets are traced and reported, not restorable.
internal static class DeclaredScripts
{
    internal sealed record Target(string Kind, JsonElement Parameters);
    internal sealed record Parameters(string Script, string RollbackScript, Target[] Targets);

    private const int MaximumScript = 20_000;
    private static readonly ConcurrentDictionary<string, string?> Checked = new(StringComparer.Ordinal);

    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        // Code from elsewhere or from strings.
        "Invoke-Expression", "iex", "Invoke-WebRequest", "iwr", "Invoke-RestMethod", "irm", "curl", "wget", "Start-BitsTransfer", "bitsadmin",
        "certutil", "Add-Type", "Import-Module", "ipmo", "Start-Process", "saps", "start", "Invoke-Command", "icm", "Start-Job", "sajb",
        "powershell", "pwsh", "cmd", "wscript", "cscript", "mshta", "rundll32", "regsvr32", "msiexec", "wmic",
        // Deleting files and disks.
        "Remove-Item", "rm", "del", "erase", "rd", "rmdir", "ri", "Clear-Content", "clc", "Clear-Item", "cli", "Format-Volume", "Clear-Disk",
        "Initialize-Disk", "Remove-Partition", "diskpart", "format", "cipher", "vssadmin", "wbadmin", "takeown", "icacls", "cacls",
        // Accounts and credentials.
        "New-LocalUser", "Set-LocalUser", "Remove-LocalUser", "Enable-LocalUser", "Disable-LocalUser", "Rename-LocalUser", "Add-LocalGroupMember",
        "Remove-LocalGroupMember", "net", "net1", "cmdkey", "vaultcmd", "Set-ExecutionPolicy",
        // Security, boot and persistence go through typed primitives or stay forbidden.
        "Set-MpPreference", "Add-MpPreference", "Remove-MpPreference", "New-NetFirewallRule", "Set-NetFirewallProfile", "Set-NetFirewallRule",
        "Remove-NetFirewallRule", "Disable-NetFirewallRule", "bcdedit", "schtasks", "Register-ScheduledTask", "New-Service", "sc", "New-PSDrive"
    };

    private static readonly Regex DangerousMember = new(@"download(string|file|data)|upload(string|file|data)|openread|invokescript|newscriptblock|\[scriptblock\]::create|process\]::start|\.invoke\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RegistryPath = new(@"^(?:registry::)?(HKLM|HKCU|HKEY_LOCAL_MACHINE|HKEY_CURRENT_USER):?\\(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> RegistryWriters = new(StringComparer.OrdinalIgnoreCase)
        { "Set-ItemProperty", "sp", "New-ItemProperty", "New-Item", "ni", "Set-Item", "si", "Remove-ItemProperty", "rp", "Rename-ItemProperty", "rnp", "Copy-ItemProperty", "cpp", "reg" };

    internal static OptimizationAction Create(Parameters parameters)
    {
        if (parameters.Script is not { Length: > 0 and <= MaximumScript } || parameters.RollbackScript is not { Length: > 0 and <= MaximumScript })
            throw new InvalidOperationException($"Script and rollbackScript must be 1-{MaximumScript} characters.");
        if (parameters.Targets is not { Length: > 0 and <= 20 }) throw new InvalidOperationException("Declare 1-20 targets the script writes.");
        var targets = parameters.Targets.Select(target => target?.Kind is null or "script.declared"
            ? throw new InvalidOperationException("Targets must be typed primitives other than script.declared.")
            : ParametricActions.Create(target.Kind, target.Parameters.ValueKind == JsonValueKind.String ? target.Parameters.GetString()! : target.Parameters.GetRawText())).ToList();
        if (targets.Select(target => target.Id).Distinct(StringComparer.Ordinal).Count() != targets.Count) throw new InvalidOperationException("A target is declared twice.");
        var declared = targets.Where(target => target.Kind is "registry.set" or "registry.delete")
            .Select(target => JsonDocument.Parse(target.Parameters!).RootElement).Select(item => $"{item.GetProperty("Hive").GetString()}\\{item.GetProperty("Path").GetString()}").ToList();
        foreach (var script in new[] { parameters.Script, parameters.RollbackScript })
            if (Check(script, declared) is { } rejection) throw new InvalidOperationException($"The script was refused before running: {rejection}");
        var canonical = new Parameters(parameters.Script, parameters.RollbackScript,
            targets.Select(target => new Target(target.Kind!, JsonDocument.Parse(target.Parameters!).RootElement.Clone())).ToArray());
        return Build(canonical, targets);
    }

    internal static string? Check(string script, IReadOnlyList<string> declaredRegistryKeys)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script + "\n" + string.Join('\n', declaredRegistryKeys))));
        return Checked.GetOrAdd(hash, _ => Check(PowerShellReader.ParseAsync(script, CancellationToken.None, MaximumScript).GetAwaiter().GetResult(), declaredRegistryKeys));
    }

    internal static string? Check(PowerShellReader.Shape shape, IReadOnlyList<string> declaredRegistryKeys)
    {
        if (shape.Errors > 0) return "it does not parse";
        if (shape.Members?.FirstOrDefault(DangerousMember.IsMatch) is { } member) return $"{member[..Math.Min(80, member.Length)]} can download or run code";
        foreach (var command in shape.Commands)
        {
            if (command.Name is null || command.Operator == "Dot") return "commands must have a static name and cannot be dot-sourced";
            var name = Path.GetFileNameWithoutExtension(command.Name.Split('\\', '/').Last());
            if (Blocked.Contains(name)) return $"{command.Name} is not allowed in a declared script (downloads, nested shells, deletions, accounts, security, boot and persistence)";
            if (name.Equals("New-Object", StringComparison.OrdinalIgnoreCase) &&
                command.Arguments.Any(argument => Regex.IsMatch(argument.Value, "webclient|httpclient|comobject|wscript|shell\\.application", RegexOptions.IgnoreCase)))
                return "New-Object for web or COM clients is not allowed";
            if (name.Equals("netsh", StringComparison.OrdinalIgnoreCase) && command.Arguments.Any(argument => argument.Value.Contains("firewall", StringComparison.OrdinalIgnoreCase)))
                return "firewall changes are not allowed";
            if (!RegistryWriters.Contains(name)) continue;
            var paths = command.Arguments.Where(argument => argument.Static).Select(argument => RegistryPath.Match(argument.Value.Trim('\'', '"'))).Where(match => match.Success).ToList();
            foreach (var path in paths)
            {
                var hive = path.Groups[1].Value.ToUpperInvariant() is "HKLM" or "HKEY_LOCAL_MACHINE" ? "HKLM" : "HKCU";
                try { ParametricActions.RequireAllowedRegistry(hive, path.Groups[2].Value.TrimEnd('\\'), ""); }
                catch (InvalidOperationException exception) { return exception.Message; }
            }
            var deletes = name is "Remove-ItemProperty" or "rp" || name.Equals("reg", StringComparison.OrdinalIgnoreCase) &&
                command.Arguments.FirstOrDefault()?.Value.Equals("delete", StringComparison.OrdinalIgnoreCase) == true;
            if (deletes && !paths.Any(path => declaredRegistryKeys.Contains(
                    $"{(path.Groups[1].Value.ToUpperInvariant() is "HKLM" or "HKEY_LOCAL_MACHINE" ? "HKLM" : "HKCU")}\\{path.Groups[2].Value.TrimEnd('\\')}", StringComparer.OrdinalIgnoreCase)))
                return "Registry deletions must name a declared registry.set/registry.delete key literally";
        }
        return null;
    }

    private static OptimizationAction Build(Parameters parameters, List<OptimizationAction> targets)
    {
        var notes = new List<string>();
        string? captured = null; // The engine captures right before Apply; the rollback test compares against that snapshot.
        var restart = targets.Any(target => target.RequiresRestart);
        var declared = targets.Where(target => target.Kind is "registry.set" or "registry.delete")
            .Select(target => JsonDocument.Parse(target.Parameters!).RootElement).Select(item => item.GetProperty("Path").GetString()!).ToList();
        string[] ReadStates(string state) => JsonSerializer.Deserialize<string[]>(state) is { } states && states.Length == targets.Count
            ? states : throw new InvalidOperationException("The script snapshot does not match its targets.");
        bool Restored(string state) => ReadStates(state).Zip(targets).All(pair => pair.Second.VerifyRestored(pair.First));
        void PrimitiveRestore(string state, string reason)
        {
            foreach (var (target, saved) in targets.Zip(ReadStates(state)))
                if (!target.VerifyRestored(saved)) { target.Restore(saved); notes.Add($"{reason}: NeuroTune restored {target.Name} from its snapshot."); }
        }
        void RunTraced(string script, string label)
        {
            using var tracer = new RegistryWriteTracer();
            try { Run(script); }
            finally
            {
                var undeclared = tracer.Stop().Where(write => !declared.Any(path => write.Contains(path, StringComparison.OrdinalIgnoreCase))).Take(50).ToList();
                if (undeclared.Count > 0)
                    notes.Add($"Rollback not guaranteed: the {label} wrote Registry keys outside its declared targets: {string.Join("; ", undeclared)}");
            }
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parameters.Script)))[..12].ToLowerInvariant();
        var canonical = JsonSerializer.Serialize(parameters);
        var id = "script.declared." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"script.declared\n{canonical}")))[..16].ToLowerInvariant();
        return new OptimizationAction(id, $"Declared script ({targets.Count} targets): {string.Join(", ", targets.Select(target => target.Name)).Truncate(200)}",
            "Model-written PowerShell, run elevated after a blocking check; targets are snapshotted and restored by NeuroTune if the model rollback fails",
            "Script", RiskLevel.High, restart, null,
            () =>
            {
                var states = targets.Select(target => target.Inspect()).ToList();
                if (states.FirstOrDefault(state => !state.CanApply && !state.AlreadyApplied) is { } blocked) return ActionAvailability.Unavailable(blocked.Status);
                return states.All(state => state.AlreadyApplied) ? ActionAvailability.Applied("All targets already set") : ActionAvailability.Ready("Script ready");
            },
            () => captured = JsonSerializer.Serialize(targets.Select(target => target.Capture()).ToArray()),
            () => Apply(captured ?? throw new InvalidOperationException("The targets were not captured before the script.")),
            state =>
            {
                try { RunTraced(parameters.RollbackScript, "rollback script"); }
                catch (Exception exception) when (exception is InvalidOperationException or TimeoutException) { notes.Add($"The model rollback failed: {exception.Message.Truncate(300)}"); }
                PrimitiveRestore(state, "The model rollback missed a target");
            },
            () => targets.All(target => target.Verify()),
            supportedHardware: ["Declared targets"], evidenceRequirements: ["Exact state of every declared target captured before the script"],
            sources: ["Model-written script checked by NeuroTune's blocking AST policy"],
            sideEffects: ["Anything the script does outside its declared targets is reported, not restored"],
            validateSnapshot: state => _ = ReadStates(state), conflictTarget: $"script:{hash}", verifyRestored: Restored)
        { Kind = "script.declared", Parameters = canonical, Notes = notes };

        void Apply(string state)
        {
            RunTraced(parameters.Script, "script");
            if (!targets.All(target => target.Verify())) throw new InvalidOperationException("The script finished but its declared targets do not have the declared values.");
            if (restart) { notes.Add("The model rollback was not tested because a target needs a restart; NeuroTune's snapshot restore remains available."); return; }
            // Test the model rollback now, while it is cheap to fix: compare with the snapshot, then apply the script again.
            try { RunTraced(parameters.RollbackScript, "rollback script"); }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException) { notes.Add($"The model rollback failed its test: {exception.Message.Truncate(300)}"); }
            if (Restored(state)) notes.Add("The model rollback was tested and restored every declared target.");
            else PrimitiveRestore(state, "The model rollback test missed a target");
            RunTraced(parameters.Script, "script");
            if (!targets.All(target => target.Verify())) throw new InvalidOperationException("The script did not reach its declared state again after the rollback test.");
        }
    }

    private static void Run(string script)
    {
        var start = new ProcessStartInfo(WindowsCommand.PathFor("powershell.exe"));
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
            "$ErrorActionPreference = 'Stop'; & ([scriptblock]::Create($env:NEUROTUNE_SCRIPT))" })
            start.ArgumentList.Add(argument);
        start.Environment["NEUROTUNE_SCRIPT"] = script;
        WindowsCommand.RunAsync(start, TimeSpan.FromMinutes(2)).GetAwaiter().GetResult();
    }

    private static string Truncate(this string value, int length) => value.Length <= length ? value : value[..(length - 3)] + "...";
}
