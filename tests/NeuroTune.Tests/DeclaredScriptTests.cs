using System.Text.Json;
using Microsoft.Win32;

namespace NeuroTune.Tests;

[TestClass]
public sealed class DeclaredScriptTests
{
    private const string Declared = @"HKCU\Software\NeuroTuneTest-declared";

    [TestMethod]
    [DataRow(@"Remove-Item C:\neurotune-sentinel")]
    [DataRow(@"iex 'calc'")]
    [DataRow(@"Invoke-WebRequest https://example.test -OutFile x.exe")]
    [DataRow(@"Start-Process notepad")]
    [DataRow(@"net user neurotune P4ss /add")]
    [DataRow(@"bcdedit /set '{current}' quietboot Yes")]
    [DataRow(@"Set-MpPreference -DisableRealtimeMonitoring $true")]
    [DataRow(@"$c = New-Object Net.WebClient")]
    [DataRow(@"$w.DownloadString('https://example.test')")]
    [DataRow(@". C:\neurotune-sentinel.ps1")]
    [DataRow(@"& $command")]
    [DataRow(@"powershell -EncodedCommand AAAA")]
    [DataRow(@"Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa' -Name RunAsPPL -Value 0")]
    [DataRow(@"Remove-ItemProperty 'HKCU:\Software\Other' -Name V")]
    [DataRow(@"netsh advfirewall set allprofiles state off")]
    [DataRow(@"Register-ScheduledTask -TaskName x -Action $a")]
    [DataRow(@"Set-ItemProperty 'HKCU:\Software\X' -Name V -Value (")]
    public void Dangerous_scripts_are_refused_before_running(string script) =>
        Assert.IsNotNull(DeclaredScripts.Check(script, [Declared]), script);

    [TestMethod]
    [DataRow(@"Set-ItemProperty 'HKCU:\Software\NeuroTuneTest-declared' -Name V -Value 1")]
    [DataRow(@"Remove-ItemProperty -Path 'HKCU:\Software\NeuroTuneTest-declared' -Name V")]
    [DataRow(@"powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2; powercfg /setactive SCHEME_CURRENT")]
    [DataRow(@"$p = Get-ItemProperty 'HKCU:\Software\NeuroTuneTest-declared'; if ($p.V -ne 1) { New-ItemProperty 'HKCU:\Software\NeuroTuneTest-declared' -Name V -Value 1 -PropertyType DWord -Force | Out-Null }")]
    public void Ordinary_scripts_pass_the_policy(string script) => Assert.IsNull(DeclaredScripts.Check(script, [Declared]), script);

    [TestMethod]
    public void Script_round_trip_tests_the_model_rollback_reports_undeclared_writes_and_restores_from_the_snapshot()
    {
        if (!LogService.IsAdministrator()) Assert.Inconclusive("ETW kernel sessions need an elevated run.");
        var key = $@"Software\NeuroTuneTest-{Guid.NewGuid():N}";
        var extra = key + "-undeclared";
        try
        {
            var target = new { kind = "registry.set", parameters = new { Hive = "HKCU", Path = key, Name = "V", Type = "DWord", Value = "1" } };
            var action = ParametricActions.Create("script.declared", JsonSerializer.Serialize(new
            {
                Script = $"New-Item -Path 'HKCU:\\{key}' -Force | Out-Null; Set-ItemProperty -Path 'HKCU:\\{key}' -Name V -Value 1 -Type DWord; New-Item -Path 'HKCU:\\{extra}' -Force | Out-Null",
                RollbackScript = "Write-Output 'forgot to undo'", // A wrong model rollback: NeuroTune must still restore the target.
                Targets = new[] { target }
            }));
            Assert.AreEqual(RiskLevel.High, action.Risk);
            Assert.AreEqual(action.Id, ParametricActions.Create(action.Kind!, action.Parameters!).Id);
            var state = action.Capture();
            action.Apply();
            Assert.IsTrue(action.Verify());
            var notes = action.NotesForJournal;
            Assert.IsTrue(notes.Any(note => note.StartsWith("Rollback not guaranteed", StringComparison.Ordinal) && note.Contains("-undeclared", StringComparison.Ordinal)), string.Join(" | ", notes));
            Assert.IsTrue(notes.Any(note => note.Contains("missed a target", StringComparison.Ordinal)), string.Join(" | ", notes));

            action.Restore(state);
            Assert.IsNull(Registry.CurrentUser.OpenSubKey(key), "The created key is removed again by the snapshot restore.");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(key, false);
            Registry.CurrentUser.DeleteSubKeyTree(extra, false);
        }
    }

    [TestMethod]
    public void Tracer_reports_registry_writes_of_child_processes()
    {
        if (!LogService.IsAdministrator()) Assert.Inconclusive("ETW kernel sessions need an elevated run.");
        var key = $@"Software\NeuroTuneTest-{Guid.NewGuid():N}";
        try
        {
            using var tracer = new RegistryWriteTracer();
            WindowsCommand.Run("powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                $"New-Item -Path 'HKCU:\\{key}' -Force | Out-Null; Set-ItemProperty -Path 'HKCU:\\{key}' -Name Probe -Value 1");
            var writes = tracer.Stop();
            Assert.IsTrue(writes.Any(write => write.Contains(key.Split('\\')[1], StringComparison.OrdinalIgnoreCase) && write.EndsWith(@"\Probe", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(writes.Any(write => write.Contains("SystemCertificates", StringComparison.OrdinalIgnoreCase)), "Opening existing keys is not a write.");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }
}
