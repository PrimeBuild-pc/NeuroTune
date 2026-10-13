using System.Text.Json;

namespace NeuroTune.Tests;

[TestClass]
public sealed class PowerShellReaderTests
{
    private static string? Check(string command) => PowerShellReader.Check(PowerShellReader.ParseAsync(command, CancellationToken.None).GetAwaiter().GetResult());

    [TestMethod]
    [DataRow(@"Remove-Item C:\neurotune-sentinel")]
    [DataRow(@"Set-ItemProperty HKCU:\Software\NeuroTuneTest -Name X -Value 1")]
    [DataRow(@"[IO.File]::WriteAllText('C:\neurotune-sentinel', 'x')")]
    [DataRow(@"Get-ChildItem C:\neurotune-sentinel | ForEach-Object Delete")]
    [DataRow(@"Get-ChildItem C:\neurotune-sentinel | % -MemberName Delete")]
    [DataRow(@"Get-ChildItem C:\neurotune-sentinel | ForEach-Object { $_.Delete() }")]
    [DataRow(@"iex 'Remove-Item C:\neurotune-sentinel'")]
    [DataRow(@"& 'reg' add HKCU\Software\NeuroTuneTest")]
    [DataRow(@"reg add HKCU\Software\NeuroTuneTest /f")]
    [DataRow(@"Get-Process > C:\neurotune-sentinel.txt")]
    [DataRow(@"$x = Get-Process")]
    [DataRow(@"""$(Remove-Item C:\neurotune-sentinel)""")]
    [DataRow(@"Get-CimInstance Win32_OperatingSystem -Comp server")]
    [DataRow(@"netsh interface set interface Ethernet disable")]
    [DataRow(@"netsh advfirewall show allprofiles; netsh advfirewall reset")]
    [DataRow(@"bcdedit /set '{current}' quietboot Yes")]
    [DataRow(@"powercfg /setactive SCHEME_MIN")]
    [DataRow(@"nvidia-smi -pl 100")]
    [DataRow(@"Microsoft.PowerShell.Management\Remove-Item C:\neurotune-sentinel")]
    [DataRow(@"Get-Content \\server\share\file.txt")]
    [DataRow(@"Get-Process | Stop-Process")]
    [DataRow(@"function f { Remove-Item C:\neurotune-sentinel }; f")]
    [DataRow(@". C:\neurotune-sentinel.ps1")]
    [DataRow(@"reg query $env:x")]
    [DataRow(@"Get-Service | Where-Object {")]
    public void Write_or_escape_attempts_are_refused_before_execution(string command) =>
        Assert.IsNotNull(Check(command), command);

    [TestMethod]
    [DataRow(@"Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' | Select-Object CurrentBuild")]
    [DataRow(@"Get-Service | Where-Object Status -eq Running | Select-Object -First 3 Name")]
    [DataRow(@"gci HKLM:\SYSTEM\CurrentControlSet\Services | % { $_.PSChildName } | sort")]
    [DataRow(@"Get-NetAdapterAdvancedProperty -Name * | Select-Object Name, RegistryKeyword, RegistryValue | Format-Table -AutoSize")]
    [DataRow(@"reg query HKCU\Software\Microsoft /s")]
    [DataRow(@"powercfg /q")]
    [DataRow(@"netsh int tcp show global")]
    [DataRow(@"bcdedit /enum '{current}'")]
    [DataRow(@"nvidia-smi '--query-gpu=name,driver_version' --format=csv")]
    [DataRow(@"Get-WinEvent -LogName System -MaxEvents 5 | Select-Object @{n='Id';e={$_.Id}}, TimeCreated")]
    public void Read_commands_are_allowed(string command) => Assert.IsNull(Check(command), command);

    [TestMethod]
    public void Every_command_suggested_in_the_targets_preset_is_allowed()
    {
        var preset = LlmClient.Preset("targets");
        var commands = System.Text.RegularExpressions.Regex.Matches(preset, @"`((?:Get-|gci )[^`]+)`").Select(match => match.Groups[1].Value).ToList();
        Assert.IsGreaterThanOrEqualTo(3, commands.Count);
        foreach (var command in commands) Assert.IsNull(Check(command), command);
    }

    [TestMethod]
    public void Only_powershell_read_accepts_a_command()
    {
        var turn = PlannerProtocol.Parse("""{"kind":"requestInvestigation","toolId":"powershell-read","question":"NIC","command":"Get-NetAdapter"}""");
        Assert.AreEqual("Get-NetAdapter", turn.Investigation!.Command);
        Assert.ThrowsExactly<InvalidOperationException>(() => PlannerProtocol.Parse("""{"kind":"requestInvestigation","toolId":"powershell-read","question":"NIC"}"""));
        Assert.ThrowsExactly<InvalidOperationException>(() => PlannerProtocol.Parse("""{"kind":"requestInvestigation","toolId":"service-state","question":"x","command":"Get-Service"}"""));
    }

    [TestMethod]
    public async Task An_allowed_read_runs_non_admin_and_a_refused_one_reports_its_reason()
    {
        var refused = JsonSerializer.Serialize(await PowerShellReader.ReadAsync(@"Remove-Item C:\neurotune-sentinel", CancellationToken.None));
        StringAssert.Contains(refused, "\"status\":\"rejected\"");

        var read = JsonSerializer.Serialize(await PowerShellReader.ReadAsync(
            @"Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' | Select-Object -ExpandProperty CurrentBuild", CancellationToken.None));
        if (read.Contains("\"status\":\"failed\"", StringComparison.Ordinal) && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            Assert.Inconclusive("The GitHub runner desktop refuses the non-admin token; run this test locally.");
        StringAssert.Contains(read, "\"status\":\"ok\"");
        StringAssert.Contains(read, Environment.OSVersion.Version.Build.ToString());
    }
}
