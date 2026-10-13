using System.Diagnostics;
using System.Text.Json;

namespace NeuroTune;

// Free read for the agent: PowerShell's own parser describes the command, NeuroTune checks every AST node and command
// against allowlists, and only then runs it in Constrained Language mode, non-admin unless a listed command needs admin.
internal static class PowerShellReader
{
    internal const string ToolId = "powershell-read";

    private static readonly HashSet<string> Cmdlets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get-ChildItem", "Get-Item", "Get-ItemProperty", "Get-ItemPropertyValue", "Get-Content", "Get-Service", "Get-Process",
        "Get-CimInstance", "Get-WmiObject", "Get-WinEvent", "Get-Counter", "Get-HotFix", "Get-ComputerInfo", "Get-MMAgent", "Get-ProcessMitigation",
        "Get-NetAdapter", "Get-NetAdapterAdvancedProperty", "Get-NetAdapterRss", "Get-NetAdapterRsc", "Get-NetAdapterLso",
        "Get-NetAdapterChecksumOffload", "Get-NetAdapterPowerManagement", "Get-NetAdapterBinding", "Get-NetAdapterHardwareInfo",
        "Get-NetIPInterface", "Get-NetTCPSetting", "Get-NetOffloadGlobalSetting", "Get-NetQosPolicy", "Get-DnsClientServerAddress",
        "Get-NetConnectionProfile", "Get-PnpDevice", "Get-PnpDeviceProperty", "Get-ScheduledTask", "Get-ScheduledTaskInfo",
        "Get-MpComputerStatus", "Get-MpPreference", "Get-PhysicalDisk", "Get-Disk", "Get-Volume", "Get-StorageReliabilityCounter", "Get-AppxPackage",
        "Select-Object", "Where-Object", "ForEach-Object", "Sort-Object", "Group-Object", "Measure-Object", "Select-String",
        "Format-List", "Format-Table", "Out-String", "ConvertTo-Json", "ConvertTo-Csv"
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["select"] = "Select-Object",
        ["where"] = "Where-Object",
        ["?"] = "Where-Object",
        ["%"] = "ForEach-Object",
        ["foreach"] = "ForEach-Object",
        ["sort"] = "Sort-Object",
        ["group"] = "Group-Object",
        ["measure"] = "Measure-Object",
        ["fl"] = "Format-List",
        ["ft"] = "Format-Table",
        ["gci"] = "Get-ChildItem",
        ["ls"] = "Get-ChildItem",
        ["dir"] = "Get-ChildItem",
        ["gi"] = "Get-Item",
        ["gp"] = "Get-ItemProperty",
        ["gc"] = "Get-Content",
        ["cat"] = "Get-Content",
        ["gsv"] = "Get-Service",
        ["gps"] = "Get-Process",
        ["gcim"] = "Get-CimInstance",
        ["gwmi"] = "Get-WmiObject"
    };

    private static readonly string[] Natives = ["reg", "powercfg", "pnputil", "netsh", "bcdedit", "driverquery", "wevtutil", "nvidia-smi"];
    private static readonly HashSet<string> NeedsAdmin = new(StringComparer.OrdinalIgnoreCase) { "bcdedit", "Get-MpPreference", "Get-StorageReliabilityCounter" };
    private static readonly string[] RemoteParameters = ["ComputerName", "CimSession", "Credential", "Session"];

    // Everything else (method calls, assignments, redirection to files, $( ), functions, loops, & and . invocation) is refused.
    private static readonly HashSet<string> Nodes = new(StringComparer.Ordinal)
    {
        "ScriptBlockAst", "NamedBlockAst", "StatementBlockAst", "PipelineAst", "CommandAst", "CommandExpressionAst", "CommandParameterAst",
        "StringConstantExpressionAst", "ConstantExpressionAst", "ExpandableStringExpressionAst", "VariableExpressionAst", "MemberExpressionAst",
        "TypeExpressionAst", "ScriptBlockExpressionAst", "ArrayLiteralAst", "ArrayExpressionAst", "BinaryExpressionAst", "UnaryExpressionAst",
        "ParenExpressionAst", "HashtableAst", "IndexExpressionAst", "MergingRedirectionAst"
    };

    internal static string Description =>
        "Read-only PowerShell 5.1 command in the command field (max 2000 characters), parsed and checked before it runs. Allowed: " +
        string.Join(", ", Cmdlets.Order(StringComparer.OrdinalIgnoreCase)) + "; native " + string.Join(", ", Natives) +
        " in read forms only (reg query, powercfg /q|/l|/a|/getactivescheme, pnputil /enum-*, netsh ... show ..., bcdedit /enum, " +
        "wevtutil qe|gl|el|gli, nvidia-smi -q|-L|--query-gpu=...). Pipelines, property access, comparisons and script blocks are fine; " +
        "method calls, assignments, file redirection, $( ), & or . invocation, remote parameters and UNC paths are refused. Quote native arguments that contain commas or braces, such as '{current}'.";

    internal sealed record Argument(bool Static, string Value, string Type, bool Parameter);
    internal sealed record Command(string? Name, string Operator, List<Argument> Arguments);
    internal sealed record Shape(int Errors, List<Command> Commands, List<string> NodeTypes, List<string>? Members = null);

    private const string ParseScript = """
        $ErrorActionPreference = 'Stop'; $t = $null; $e = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseInput($env:NEUROTUNE_READ_COMMAND, [ref]$t, [ref]$e)
        $all = @($ast.FindAll({ $true }, $true))
        $commands = @($all | Where-Object { $_ -is [System.Management.Automation.Language.CommandAst] } | ForEach-Object {
            [ordered]@{ name = $_.GetCommandName(); operator = $_.InvocationOperator.ToString(); arguments = @($_.CommandElements | Select-Object -Skip 1 | ForEach-Object {
                if ($_ -is [System.Management.Automation.Language.CommandParameterAst]) {
                    [ordered]@{ static = ($null -eq $_.Argument -or $_.Argument -is [System.Management.Automation.Language.ConstantExpressionAst]); value = '-' + $_.ParameterName; type = $_.GetType().Name; parameter = $true }
                } elseif ($_ -is [System.Management.Automation.Language.ConstantExpressionAst]) {
                    [ordered]@{ static = $true; value = [string]$_.Value; type = $_.GetType().Name; parameter = $false }
                } else { [ordered]@{ static = $false; value = $_.Extent.Text; type = $_.GetType().Name; parameter = $false } } }) } })
        $types = @($all | ForEach-Object { $_.GetType().Name } | Sort-Object -Unique)
        $members = @($all | Where-Object { $_ -is [System.Management.Automation.Language.InvokeMemberExpressionAst] } | ForEach-Object { $_.Extent.Text })
        [Console]::Write((ConvertTo-Json -Compress -Depth 6 -InputObject ([ordered]@{ errors = @($e).Count; commands = $commands; nodeTypes = $types; members = $members })))
        """;

    private const string RunScript =
        "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $c = $env:NEUROTUNE_READ_COMMAND; " +
        "$ExecutionContext.SessionState.LanguageMode = 'ConstrainedLanguage'; Invoke-Expression $c 2>&1 | Out-String -Width 250";

    internal static void ValidateText(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 2000 || command.Contains('\0'))
            throw new ArgumentException("A powershell-read command must be 1-2000 characters.", nameof(command));
    }

    internal static async Task<object> ReadAsync(string command, CancellationToken cancellationToken)
    {
        var shape = await ParseAsync(command, cancellationToken);
        if (Check(shape) is { } rejection)
            return new { status = "rejected", command, reason = rejection, note = "Nothing was executed. Rewrite the command within the allowed read-only forms." };
        var admin = shape.Commands.Any(item => NeedsAdmin.Contains(Canonical(item.Name!)));
        string output;
        try { output = await RunPowerShellAsync(RunScript, command, admin, cancellationToken); }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        { return new { status = "failed", command, elevated = admin, error = error.Message[..Math.Min(1000, error.Message.Length)] }; }
        return new { status = "ok", command, elevated = admin, truncated = output.Length > 15_000, output = output[..Math.Min(15_000, output.Length)] };
    }

    // Parsing never executes the command, so it runs in the agent's own process context.
    internal static async Task<Shape> ParseAsync(string command, CancellationToken cancellationToken, int maximumLength = 2000)
    {
        if (maximumLength == 2000) ValidateText(command);
        else if (string.IsNullOrWhiteSpace(command) || command.Length > maximumLength || command.Contains('\0')) throw new ArgumentException("The script is empty or too long.");
        return JsonSerializer.Deserialize<Shape>(await RunPowerShellAsync(ParseScript, command, admin: true, cancellationToken),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("The parser returned nothing.");
    }

    internal static string Canonical(string name)
    {
        if (Aliases.TryGetValue(name, out var cmdlet)) return cmdlet;
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4].ToLowerInvariant() : name;
    }

    // Returns the refusal reason, or null when the parsed command is an allowed read.
    internal static string? Check(Shape shape)
    {
        if (shape.Errors > 0) return "The command does not parse.";
        if (shape.NodeTypes.FirstOrDefault(type => !Nodes.Contains(type)) is { } node)
            return $"{node} is not allowed (no method calls, assignments, file redirection, subexpressions, functions or loops).";
        if (shape.Commands.Count == 0) return "No command was found.";
        foreach (var command in shape.Commands)
        {
            if (command.Name is null || command.Operator != "Unknown" || command.Name.IndexOfAny(['\\', '/', ':']) >= 0)
                return "Commands must be called by a plain static name, without & or . and without a path or module prefix.";
            if (command.Arguments.Any(argument => argument.Static && argument.Value.StartsWith(@"\\", StringComparison.Ordinal)))
                return "UNC and device paths are not allowed.";
            var name = Canonical(command.Name);
            if (Natives.Contains(name)) { if (CheckNative(name, command.Arguments) is { } native) return native; continue; }
            if (!Cmdlets.Contains(name)) return $"{command.Name} is not an allowed read command.";
            if (command.Arguments.Any(argument => argument.Parameter && argument.Value.Length > 1 &&
                RemoteParameters.Any(remote => remote.StartsWith(argument.Value[1..], StringComparison.OrdinalIgnoreCase))))
                return "Remote, session and credential parameters are not allowed.";
            // ForEach-Object -MemberName (or a bare name) calls methods such as Delete(); only script blocks are allowed.
            if (name == "ForEach-Object" && command.Arguments.Any(argument => argument.Parameter
                    ? !new[] { "Process", "Begin", "End" }.Any(block => block.StartsWith(argument.Value[1..], StringComparison.OrdinalIgnoreCase)) || argument.Value.Length < 2
                    : argument.Type != "ScriptBlockExpressionAst"))
                return "ForEach-Object accepts only script blocks.";
        }
        return null;
    }

    private static string? CheckNative(string name, List<Argument> arguments)
    {
        if (arguments.Any(argument => !argument.Static)) return $"{name} arguments must be literal values.";
        var values = arguments.Select(argument => argument.Value.Trim()).ToList();
        var first = values.FirstOrDefault()?.ToLowerInvariant() ?? "";
        string[] netshWrites = ["set", "add", "delete", "del", "reset", "dump", "exec", "install", "uninstall", "import", "export", "start", "stop", "flush", "connect", "disconnect", "trace", "-f", "-r"];
        var allowed = name switch
        {
            "reg" => first == "query",
            "powercfg" => first.TrimStart('/', '-') is "q" or "query" or "l" or "list" or "a" or "availablesleepstates" or "getactivescheme",
            "pnputil" => first.StartsWith("/enum-", StringComparison.Ordinal),
            "netsh" => values.Contains("show", StringComparer.OrdinalIgnoreCase) && !values.Any(value => netshWrites.Contains(value, StringComparer.OrdinalIgnoreCase)),
            "bcdedit" => first is "/enum" or "/v" && values.All(value => System.Text.RegularExpressions.Regex.IsMatch(value,
                @"^(/enum|/v|\{[a-z0-9-]+\}|all|active|firmware|bootapp|bootmgr|osloader|resume|inherit)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
            "driverquery" => values.All(value => value.ToLowerInvariant() is "/v" or "/si" or "/nh" or "/fo" or "csv" or "table" or "list"),
            "wevtutil" => first is "qe" or "query-events" or "gl" or "get-log" or "el" or "enum-logs" or "gli" or "get-loginfo" &&
                !values.Any(value => value.StartsWith("/r", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/u", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/p", StringComparison.OrdinalIgnoreCase)),
            "nvidia-smi" => values.Count > 0 && NvidiaSmiRead(values),
            _ => false
        };
        return allowed ? null : $"Only read forms of {name} are allowed.";
    }

    private static bool NvidiaSmiRead(List<string> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value is "-q" or "--query" or "-L" or "--list-gpus" or "-x" or "--xml-format") continue;
            if (value is "-d" or "-i" && index + 1 < values.Count && !values[index + 1].StartsWith('-')) { index++; continue; }
            if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^--(query-gpu|query-compute-apps|format|id|display)=[A-Za-z0-9_.,:\-]+$")) continue;
            return false;
        }
        return true;
    }

    private static async Task<string> RunPowerShellAsync(string script, string command, bool admin, CancellationToken cancellationToken)
    {
        var executable = WindowsCommand.PathFor("powershell.exe");
        var system = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(executable)))!;
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NEUROTUNE_READ_COMMAND"] = command,
            ["PATH"] = string.Join(';', system, Path.Combine(system, "Wbem"), Path.GetDirectoryName(executable)),
            ["PSModulePath"] = Path.Combine(Path.GetDirectoryName(executable)!, "Modules")
        };
        foreach (var name in new[] { "SystemRoot", "WINDIR", "SystemDrive", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "ProgramData", "ProgramFiles",
            "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles", "COMPUTERNAME", "USERNAME", "USERDOMAIN", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE" })
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        string[] arguments = ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script];
        if (!admin)
            return await OptionalWorkerProcess.RunAsync(executable, arguments, system, "", environment, TimeSpan.FromSeconds(30), null, cancellationToken);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = system };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        foreach (var item in environment) start.Environment[item.Key] = item.Value;
        return await WindowsCommand.RunAsync(start, TimeSpan.FromSeconds(30));
    }
}
