using Microsoft.Win32;
using System.Text;
using System.Text.Json;

namespace NeuroTune.Tests;

[TestClass]
public sealed class ParametricActionTests
{
    private static OptimizationAction Create(string kind, object parameters) =>
        ParametricActions.Create(kind, JsonSerializer.Serialize(parameters));

    // Apply with the live action, then restore with one rebuilt from Kind + Parameters only, as journal recovery does.
    private static void RoundTrip(OptimizationAction action, Func<OptimizationAction>? rebuild = null)
    {
        var state = action.Capture();
        action.ValidateSnapshot(state);
        action.Apply();
        Assert.IsTrue(action.Verify(), "Verify after Apply");
        Assert.IsTrue(action.Inspect().AlreadyApplied, "Inspect after Apply");
        var recovered = rebuild?.Invoke() ?? ParametricActions.Create(action.Kind!, action.Parameters!);
        Assert.AreEqual(action.Id, recovered.Id);
        recovered.Restore(state);
        Assert.IsTrue(recovered.VerifyRestored(state), "VerifyRestored");
    }

    private static void RequireAdministrator()
    {
        if (!LogService.IsAdministrator()) Assert.Inconclusive("Needs an elevated test run; uses isolated test targets only.");
    }

    private static string Ps(string script) => WindowsCommand.Run("powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference = 'Stop'; " + script);

    [TestMethod]
    public void Registry_set_on_a_missing_key_restores_by_deleting_the_created_keys()
    {
        var root = $@"Software\NeuroTuneTest-{Guid.NewGuid():N}";
        try
        {
            var action = Create("registry.set", new { Hive = "HKCU", Path = root + @"\Nested", Name = "Value", Type = "DWord", Value = "1" });
            StringAssert.StartsWith(action.Id, "registry.set.");
            RoundTrip(action);
            using var key = Registry.CurrentUser.OpenSubKey(root);
            Assert.IsNull(key, "Both keys created by Apply are removed again");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(root, false); }
    }

    [TestMethod]
    public void Registry_set_restores_the_original_type_and_value_or_absence()
    {
        var root = $@"Software\NeuroTuneTest-{Guid.NewGuid():N}";
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(root)) key.SetValue("Existing", "original", RegistryValueKind.String);
            var overwrite = Create("registry.set", new { Hive = "HKEY_CURRENT_USER", Path = root, Name = "Existing", Type = "dword", Value = "4294967295" });
            var state = overwrite.Capture();
            overwrite.Apply();
            using (var key = Registry.CurrentUser.OpenSubKey(root)!)
            {
                Assert.AreEqual(RegistryValueKind.DWord, key.GetValueKind("Existing"));
                Assert.AreEqual(-1, key.GetValue("Existing"));
            }
            ParametricActions.Create(overwrite.Kind!, overwrite.Parameters!).Restore(state);
            using (var key = Registry.CurrentUser.OpenSubKey(root)!)
            {
                Assert.AreEqual(RegistryValueKind.String, key.GetValueKind("Existing"));
                Assert.AreEqual("original", key.GetValue("Existing"));
            }

            RoundTrip(Create("registry.set", new { Hive = "HKCU", Path = root, Name = "Absent", Type = "MultiString", Value = "[\"a\",\"b\"]" }));
            using (var key = Registry.CurrentUser.OpenSubKey(root)!)
            {
                Assert.IsNull(key.GetValue("Absent"));
                Assert.AreEqual("original", key.GetValue("Existing"), "The existing key and its other values stay");
            }
            RoundTrip(Create("registry.set", new { Hive = "HKCU", Path = root, Name = "Bytes", Type = "Binary", Value = "01 ff 10" }));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(root, false); }
    }

    [TestMethod]
    public void Registry_delete_restores_the_deleted_value()
    {
        var root = $@"Software\NeuroTuneTest-{Guid.NewGuid():N}";
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(root)) key.SetValue("Paths", new[] { "x", "y" }, RegistryValueKind.MultiString);
            RoundTrip(Create("registry.delete", new { Hive = "HKCU", Path = root, Name = "Paths" }));
            using var restored = Registry.CurrentUser.OpenSubKey(root)!;
            CollectionAssert.AreEqual(new[] { "x", "y" }, (string[])restored.GetValue("Paths")!);
            Assert.ThrowsExactly<InvalidOperationException>(() => Create("registry.delete", new { Hive = "HKCU", Path = root, Name = "Paths", Value = "1" }));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(root, false); }
    }

    [TestMethod]
    [DataRow("HKLM", @"SAM\SAM\Domains", "F")]
    [DataRow("HKLM", @"SYSTEM\ControlSet001\Control\Lsa", "RunAsPPL")]
    [DataRow("HKLM", @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios", "Enabled")]
    [DataRow("HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "x")]
    [DataRow("HKCU", @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "x")]
    [DataRow("HKCU", @"Software\Classes\CLSID\{00000000-0000-0000-0000-000000000000}\InprocServer32", "")]
    [DataRow("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\game.exe", "Debugger")]
    [DataRow("HKLM", @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware")]
    [DataRow("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA")]
    [DataRow("HKLM", @"SYSTEM\CurrentControlSet\Services\RpcSs", "Start")]
    [DataRow("HKLM", @"SYSTEM\CurrentControlSet\Services\SomeService", "ImagePath")]
    [DataRow("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager", "BootExecute")]
    public void Registry_writes_to_protected_targets_are_refused_before_capture(string hive, string path, string name)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => Create("registry.set", new { Hive = hive, Path = path, Name = name, Type = "DWord", Value = "1" }));
        Assert.ThrowsExactly<InvalidOperationException>(() => Create("registry.delete", new { Hive = hive, Path = path, Name = name }));
    }

    [TestMethod]
    public void Parameters_are_strict_and_the_id_depends_only_on_their_normalized_values()
    {
        var first = ParametricActions.Create("registry.set", """{"Hive":"HKCU","Path":"Software\\NeuroTuneTest","Name":"A","Type":"DWord","Value":"0x10"}""");
        var second = ParametricActions.Create("registry.set", """{"value":"16","type":"DWORD","name":"A","path":"\\Software\\NeuroTuneTest\\","hive":"hkey_current_user"}""");
        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(first.Parameters, second.Parameters);
        Assert.AreNotEqual(first.Id, Create("registry.set", new { Hive = "HKCU", Path = @"Software\NeuroTuneTest", Name = "A", Type = "DWord", Value = "17" }).Id);
        Assert.AreEqual(RiskLevel.Medium, first.Risk);
        Assert.AreEqual(RiskLevel.High, Create("registry.set", new { Hive = "HKLM", Path = @"SOFTWARE\NeuroTuneTest", Name = "A", Type = "DWord", Value = "1" }).Risk);

        foreach (var (kind, json) in new[]
        {
            ("registry.set", """{"Hive":"HKCU","Path":"Software\\X","Name":"A","Type":"DWord","Value":"1","Extra":1}"""),
            ("registry.set", """{"Hive":"HKCU","Path":"Software\\X","Name":"A","Type":"4","Value":"1"}"""),
            ("registry.set", """{"Hive":"HKCU","Path":"Software\\X","Name":"A","Type":"DWord","Value":"4294967296"}"""),
            ("registry.set", """{"Hive":"HKU","Path":"Software\\X","Name":"A","Type":"DWord","Value":"1"}"""),
            ("registry.set", """{"Hive":"HKCU","Path":"Software\\\\X","Name":"A","Type":"DWord","Value":"1"}"""),
            ("unknown.kind", "{}"),
            ("service.start-type", """{"Name":"RpcSs","StartType":"Disabled"}"""),
            ("service.start-type", """{"Name":"Spooler","StartType":"Off"}"""),
            ("startup.entry", """{"Scope":"HKCU","Location":"Run","Name":"App"}"""),
            ("scheduled-task.state", """{"Path":"\\Microsoft\\Windows\\Windows Defender\\Scan","Enabled":false}"""),
            ("ifeo.perf", """{"Exe":"game.exe","Name":"CpuPriorityClass","Value":4}"""),
            ("ifeo.perf", """{"Exe":"C:\\game.exe","Name":"CpuPriorityClass","Value":3}"""),
            ("bcd.option", """{"Element":"useplatformclock","Value":"Yes"}"""),
            ("bcd.option", """{"Element":"hypervisorlaunchtype","Value":"Off"}"""),
            ("qos.policy", """{"Exe":"game.exe","Dscp":64}"""),
            ("file.ini", """{"Path":"C:\\Windows\\win.ini","Section":"a","Key":"b","Value":"c"}"""),
            ("file.json", """{"Path":"C:\\Users\\..\\Windows\\x.json","Key":"a","Value":1}"""),
            ("defender.exclusion", """{"Type":"Path","Value":"C:\\"}"""),
            ("defender.exclusion", """{"Type":"Path","Value":"C:\\Windows\\Temp"}"""),
            ("device.disable", """{"Slug":"not-a-device"}""")
        })
            Assert.ThrowsExactly<InvalidOperationException>(() => ParametricActions.Create(kind, json), $"{kind} {json}");
    }

    [TestMethod]
    public void Recovery_rebuilds_parametric_actions_and_rejects_tampered_records()
    {
        var catalog = new OptimizationCatalog(includeDynamic: false);
        var action = Create("registry.set", new { Hive = "HKCU", Path = @"Software\NeuroTuneTest", Name = "A", Type = "String", Value = "x" });
        Assert.ThrowsExactly<InvalidOperationException>(() => catalog.Get(action.Id));
        var record = new ActionRecord
        {
            ActionId = action.Id, Kind = action.Kind, Parameters = action.Parameters, Attempted = true,
            OriginalState = """{"Value":{"Exists":false,"Kind":0,"Value":null},"MissingKeys":1}"""
        };
        Assert.AreEqual(action.Id, catalog.ResolveRollback(record).Id);

        record.Parameters = action.Parameters!.Replace("\"x\"", "\"y\"");
        Assert.ThrowsExactly<InvalidOperationException>(() => catalog.ResolveRollback(record));
        record.Parameters = action.Parameters;
        record.OriginalState = """{"Value":{"Exists":true,"Kind":1,"Value":"x"},"MissingKeys":1}""";
        Assert.ThrowsExactly<InvalidOperationException>(() => catalog.ResolveRollback(record));
        Assert.ThrowsExactly<InvalidOperationException>(() => catalog.ResolveRollback(new ActionRecord
        {
            ActionId = "gaming.game-mode", Parameters = "{}", Attempted = true, OriginalState = """{"Exists":false,"Kind":0,"Value":null}"""
        }));

        Assert.AreSame(catalog.AddParametric(action.Kind!, action.Parameters!), catalog.Get(action.Id));
        Assert.AreSame(catalog.Get("device.disable." + DeviceControl.Candidates[0].Slug),
            catalog.AddParametric("device.disable", JsonSerializer.Serialize(new { DeviceControl.Candidates[0].Slug })));
    }

    [TestMethod]
    public void Journal_v4_persists_parametric_records_and_v3_journals_stay_readable()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"parametric-operations-{Guid.NewGuid():N}");
        try
        {
            var service = new BackupService(root);
            var action = Create("registry.set", new { Hive = "HKCU", Path = @"Software\NeuroTuneTest", Name = "A", Type = "DWord", Value = "1" });
            var manifest = new OperationManifest
            {
                Status = "Applying",
                Actions = [new() { ActionId = action.Id, Kind = action.Kind, Parameters = action.Parameters, Attempted = true, OriginalState = action.Capture() }]
            };
            manifest.DirectoryPath = Path.Combine(root, $"{manifest.CreatedAt:yyyyMMdd-HHmmss}-{manifest.Id:N}");
            Assert.AreEqual(4, manifest.SchemaVersion);
            service.Save(manifest);
            var loaded = service.Load(manifest.Id)!;
            Assert.AreEqual(action.Parameters, loaded.Actions[0].Parameters);

            var path = Path.Combine(manifest.DirectoryPath, "manifest.json");
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            json["SchemaVersion"] = 3;
            File.WriteAllText(path, json.ToJsonString());
            Assert.ThrowsExactly<InvalidOperationException>(() => service.Load(manifest.Id), "v3 journals cannot carry parametric records");

            json["Actions"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"ActionId":"gaming.game-mode","OriginalState":"{\"Exists\":false,\"Kind\":0,\"Value\":null}","Attempted":true}]""");
            File.WriteAllText(path, json.ToJsonString());
            Assert.AreEqual(3, service.Load(manifest.Id)!.SchemaVersion);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Selection_rejects_a_parametric_and_a_static_action_on_the_same_registry_value()
    {
        var catalog = new OptimizationCatalog(includeDynamic: false);
        var parametric = catalog.AddParametric("registry.set", JsonSerializer.Serialize(new
        {
            Hive = "HKCU", Path = @"Software\Microsoft\GameBar", Name = "AutoGameModeEnabled", Type = "DWord", Value = "1"
        }));
        Assert.ThrowsExactly<InvalidOperationException>(() => OptimizationCatalog.ValidateSelection([parametric, catalog.Get("gaming.game-mode")]));
    }

    [TestMethod]
    public void Policy_allows_only_the_Defender_exclusion_primitive_and_always_requires_confirmation()
    {
        var exclusion = Create("defender.exclusion", new { Type = "Process", Value = "game.exe" });
        var decision = ActionPolicy.Evaluate(exclusion.Definition, RiskProfile.Aggressive);
        Assert.AreEqual(PolicyDisposition.ConfirmationRequired, decision.Disposition);
        Assert.AreEqual(PolicyDisposition.Blocked, ActionPolicy.Evaluate(exclusion.Definition with { Id = "defender.realtime-off" }, RiskProfile.Aggressive).Disposition);
    }

    [TestMethod]
    public void Json_and_ini_files_are_edited_in_place_and_restored_byte_for_byte()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"NeuroTuneTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var json = Path.Combine(directory, "settings.json");
            var original = "{\n  // comment\n  \"keep\": \"è\",\n  \"hardware\": { \"acceleration\": true },\n}";
            File.WriteAllBytes(json, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(original)]);
            var bytes = File.ReadAllBytes(json);
            RoundTrip(Create("file.json", new { Path = json, Key = "hardware.acceleration", Value = false }));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(json));
            var nested = Create("file.json", new { Path = json, Key = "overlay.enabled.value", Value = new { on = false } });
            nested.Apply();
            Assert.IsTrue(nested.Verify());
            StringAssert.Contains(File.ReadAllText(json), "\"keep\": \"è\"");

            var ini = Path.Combine(directory, "GameUserSettings.ini");
            File.WriteAllText(ini, "[/Script/Engine.GameUserSettings]\r\nbUseVSync=True\r\nFrameRateLimit=60.000000\r\n\r\n[Other]\r\nx=1\r\n", Encoding.Unicode);
            bytes = File.ReadAllBytes(ini);
            RoundTrip(Create("file.ini", new { Path = ini, Section = "/Script/Engine.GameUserSettings", Key = "FrameRateLimit", Value = "0.000000" }));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(ini));
            Assert.ThrowsExactly<InvalidOperationException>(() => Create("file.ini", new { Path = Path.Combine(directory, "missing.ini"), Section = "a", Key = "b", Value = "c" }).Capture());
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Ini_editing_replaces_inserts_and_appends_without_touching_other_lines()
    {
        const string text = "; header\r\n[A]\r\nx = 1\r\n\r\n[B]\r\ny=2\r\n";
        Assert.AreEqual("; header\r\n[A]\r\nx=5\r\n\r\n[B]\r\ny=2\r\n", ParametricActions.SetIniValue(text, "a", "X", "5"));
        Assert.AreEqual("; header\r\n[A]\r\nx = 1\r\nz=3\r\n\r\n[B]\r\ny=2\r\n", ParametricActions.SetIniValue(text, "A", "z", "3"));
        Assert.AreEqual("; header\r\n[A]\r\nx = 1\r\n\r\n[B]\r\ny=2\r\n[C]\r\nk=v\r\n", ParametricActions.SetIniValue(text, "C", "k", "v"));
        Assert.AreEqual("1", ParametricActions.GetIniValue(text, "A", "x"));
        Assert.IsNull(ParametricActions.GetIniValue(text, "B", "x"));
    }

    [TestMethod]
    public void Power_setting_round_trips_on_a_temporary_scheme()
    {
        var scheme = Guid.NewGuid();
        WindowsCommand.Run("powercfg.exe", "/duplicatescheme", OptimizationCatalog.ActivePowerScheme().ToString("D"), scheme.ToString("D"));
        try
        {
            // "Turn off hard disk after" (seconds): full range even in tweaker plans that pin processor settings.
            RoundTrip(Create("powercfg.setting", new
            {
                Subgroup = "0012ee47-9041-4b5d-9b77-535fba8b1442", Setting = "6738e2c4-e8a5-4a42-b16a-e040e769756e", Ac = 1234u, Dc = 4321u, Scheme = scheme
            }));
        }
        finally { WindowsCommand.Run("powercfg.exe", "/delete", scheme.ToString("D")); }
    }

    [TestMethod]
    public void Startup_entry_and_compat_layer_round_trip_in_the_current_user_hive()
    {
        var name = $"NeuroTuneTest-{Guid.NewGuid():N}";
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        using (var key = Registry.CurrentUser.CreateSubKey(run)) key.SetValue(name, @"C:\NeuroTuneTest\missing.exe");
        try
        {
            RoundTrip(Create("startup.entry", new { Scope = "HKCU", Location = "Run", Name = name, Enabled = false }));
            using (var key = Registry.CurrentUser.OpenSubKey(approved)) Assert.IsNull(key?.GetValue(name));
            RoundTrip(Create("appcompat.layer", new { Exe = Environment.ProcessPath!, Flags = new[] { "highdpiaware", "DISABLEDXMAXIMIZEDWINDOWEDMODE" } }));
        }
        finally
        {
            using var key = Registry.CurrentUser.OpenSubKey(run, true);
            key?.DeleteValue(name, false);
        }
    }

    [TestMethod]
    public void Scheduled_task_state_round_trips_on_a_test_task()
    {
        var name = $"NeuroTuneTest-{Guid.NewGuid():N}";
        Ps($"Register-ScheduledTask -TaskName '{name}' -Action (New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c exit') " +
            "-Trigger (New-ScheduledTaskTrigger -Once -At '2000-01-01T00:00:00') | Out-Null");
        try { RoundTrip(Create("scheduled-task.state", new { Path = @"\" + name, Enabled = false })); }
        finally { Ps($"Unregister-ScheduledTask -TaskName '{name}' -Confirm:$false"); }
    }

    [TestMethod]
    public void Service_start_type_round_trips_on_a_test_service()
    {
        RequireAdministrator();
        var name = $"NeuroTuneTest{Guid.NewGuid():N}"[..30];
        WindowsCommand.Run("sc.exe", "create", name, "binPath=", @"C:\Windows\System32\svchost.exe -k NeuroTuneTest", "start=", "demand");
        try
        {
            RoundTrip(Create("service.start-type", new { Name = name, StartType = "Disabled" }));
            RoundTrip(Create("service.start-type", new { Name = name, StartType = "AutomaticDelayed" }));
            RoundTrip(Create("service.start-type", new { Name = name, StartType = "Automatic" }));
        }
        finally { WindowsCommand.Run("sc.exe", "delete", name); }
    }

    [TestMethod]
    public void Machine_registry_qos_and_ifeo_round_trip_on_test_targets()
    {
        RequireAdministrator();
        var exe = $"neurotunetest-{Guid.NewGuid():N}.exe";
        try
        {
            RoundTrip(Create("ifeo.perf", new { Exe = exe, Name = "CpuPriorityClass", Value = 3 }));
            using (var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{exe}"))
                Assert.IsNull(key, "The IFEO key created for the test executable is removed again");
            RoundTrip(Create("qos.policy", new { Exe = exe, Dscp = 46 }));
            Assert.AreEqual("-1", Ps($"$p = Get-NetQosPolicy -PolicyStore localhost -Name 'NeuroTune {exe}' -ErrorAction SilentlyContinue; if ($p) {{ 'present' }} else {{ '-1' }}").Trim());
        }
        finally
        {
            Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{exe}", false);
            try { Ps($"Remove-NetQosPolicy -PolicyStore localhost -Name 'NeuroTune {exe}' -Confirm:$false"); } catch (InvalidOperationException) { /* already removed */ }
        }
    }

    [TestMethod]
    public void Bcd_option_round_trips_on_a_private_test_store()
    {
        RequireAdministrator();
        var directory = Path.Combine(Path.GetTempPath(), $"NeuroTuneTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var store = Path.Combine(directory, "test.bcd");
        try
        {
            WindowsCommand.Run("bcdedit.exe", "/createstore", store);
            var entry = System.Text.RegularExpressions.Regex.Match(WindowsCommand.Run("bcdedit.exe", "/store", store, "/create", "/d", "NeuroTuneTest", "/application", "osloader"),
                @"\{[0-9a-fA-F-]{36}\}").Value;
            foreach (var parameters in new[] { new ParametricActions.BcdParameters("disabledynamictick", "yes"), new("tscsyncpolicy", "Enhanced") })
            {
                var action = ParametricActions.BcdOption(parameters, store, entry);
                RoundTrip(action, () => ParametricActions.BcdOption(parameters, store, entry));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    // Real NIC and Defender writes affect the machine: run them only in a VM, opted in by environment variable.
    [TestMethod]
    public void Network_adapter_property_round_trips_when_a_test_adapter_is_provided()
    {
        var target = Environment.GetEnvironmentVariable("NEUROTUNE_TEST_NETADAPTER");
        if (target is null) Assert.Inconclusive("Set NEUROTUNE_TEST_NETADAPTER=<interface GUID>|<RegistryKeyword>|<value> in a VM.");
        RequireAdministrator();
        var parts = target.Split('|');
        RoundTrip(Create("netadapter.property", new { Adapter = parts[0], Keyword = parts[1], Value = parts[2] }));
    }

    [TestMethod]
    public void Defender_exclusion_round_trips_when_opted_in()
    {
        if (Environment.GetEnvironmentVariable("NEUROTUNE_TEST_DEFENDER") != "1") Assert.Inconclusive("Set NEUROTUNE_TEST_DEFENDER=1 in a VM.");
        RequireAdministrator();
        RoundTrip(Create("defender.exclusion", new { Type = "Process", Value = $"neurotunetest-{Guid.NewGuid():N}.exe" }));
    }
}
