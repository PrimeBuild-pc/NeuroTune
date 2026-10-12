namespace NeuroTune.Tests;

[TestClass]
public sealed class DeviceControlTests
{
    private sealed class FakeMachine
    {
        public Dictionary<string, (string Name, string Class, int Problem)> Devices = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string[]> Children = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Commands = [];
        public int ExitCode;
        public bool Admin = true;
        public string? GuardReason;
        public DeviceHost Host => new(
            () => Devices.Select(item => new PnpDevice(item.Value.Name, item.Key, item.Value.Class, item.Value.Problem)).ToList(),
            (verb, id) =>
            {
                Commands.Add($"{verb} {id}");
                if (ExitCode is 0 or 3010 && Devices.TryGetValue(id, out var device))
                    Devices[id] = device with { Problem = verb == "/disable-device" && ExitCode == 0 ? 22 : verb == "/enable-device" ? 0 : device.Problem };
                return ExitCode;
            },
            () => Admin, (_, _) => GuardReason, id => Children.TryGetValue(id, out var below) ? below : []);
    }

    private static OptimizationAction ActionFor(string slug, DeviceHost host) =>
        DeviceControl.Actions(host).Single(action => action.Id == "device.disable." + slug);

    [TestMethod]
    public void Candidate_table_is_unique_valid_and_inside_the_execution_policy()
    {
        var host = new FakeMachine().Host;
        var actions = DeviceControl.Actions(host).ToList();
        Assert.HasCount(DeviceControl.Candidates.Count, actions);
        Assert.AreEqual(actions.Count, actions.Select(action => action.Id).Distinct().Count());
        foreach (var action in actions)
        {
            action.Definition.Validate();
            Assert.AreNotEqual(PolicyDisposition.Blocked, ActionPolicy.Evaluate(action.Definition, RiskProfile.Aggressive).Disposition, action.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(action.Description));
        }
        Assert.IsTrue(new OptimizationCatalog(includeDynamic: false).Contains("device.disable.umbus-root-bus"));
    }

    [TestMethod]
    public void Device_toggles_are_never_preselected()
    {
        foreach (var action in DeviceControl.Actions(new FakeMachine().Host))
            foreach (var profile in Enum.GetValues<RiskProfile>())
                Assert.IsFalse(ActionPolicy.Evaluate(action.Definition, profile).Preselected, action.Id);
    }

    [TestMethod]
    [DataRow("composite-bus", "Composite Bus Enumerator", "System", @"ROOT\COMPOSITEBUS\0000", true)]
    [DataRow("composite-bus", "Composite Bus Enumerator", "HIDClass", @"ROOT\COMPOSITEBUS\0000", false)]
    [DataRow("wan-miniports", "WAN Miniport (IKEv2)", "Net", @"SWD\MSRRAS\MS_AGILEVPNMINIPORT", true)]
    [DataRow("wan-miniports", "WAN Miniport (IP) extra", "Net", @"SWD\MSRRAS\X", false)]
    [DataRow("bluetooth-radio", "Intel(R) Wireless Bluetooth(R)", "Bluetooth", @"USB\VID_8087&PID_0AA7\1", true)]
    [DataRow("bluetooth-radio", "Bluetooth Device (RFCOMM Protocol TDI)", "Bluetooth", @"BTH\MS_RFCOMM\1", false)]
    [DataRow("wifi-adapter", "Intel(R) Dual Band Wireless-AC 3168", "Net", @"PCI\VEN_8086&DEV_24FB\1", true)]
    [DataRow("wifi-adapter", "Microsoft Wi-Fi Direct Virtual Adapter", "Net", @"SWD\WFDMS\1", false)]
    [DataRow("gpu-hdmi-dp-audio", "AMD High Definition Audio Device", "MEDIA", @"HDAUDIO\FUNC_01&VEN_1002\1", true)]
    public void Candidates_match_only_the_exact_device_class_and_name(string slug, string name, string deviceClass, string id, bool expected)
    {
        var candidate = DeviceControl.Candidates.Single(item => item.Slug == slug);
        Assert.AreEqual(expected, candidate.Match([new(name, id, deviceClass, 0)]).Count == 1);
    }

    [TestMethod]
    public void Disable_and_restore_round_trip_changes_only_enabled_matches()
    {
        var machine = new FakeMachine();
        machine.Devices[@"ROOT\KDNIC\0000"] = ("Microsoft Kernel Debug Network Adapter", "Net", 0);
        machine.Devices[@"ROOT\OTHER\0000"] = ("Intel(R) Ethernet", "Net", 0);
        var action = ActionFor("kernel-debug-network-adapter", machine.Host);
        Assert.IsTrue(action.Inspect().CanApply);
        var snapshot = action.Capture();
        action.ValidateSnapshot(snapshot);
        action.Apply();
        Assert.IsTrue(action.Verify());
        CollectionAssert.AreEqual(new[] { @"/disable-device ROOT\KDNIC\0000" }, machine.Commands);
        Assert.AreEqual(22, machine.Devices[@"ROOT\KDNIC\0000"].Problem);
        Assert.AreEqual(0, machine.Devices[@"ROOT\OTHER\0000"].Problem);
        Assert.IsTrue(action.Inspect().AlreadyApplied);
        action.Restore(snapshot);
        Assert.IsTrue(action.VerifyRestored(snapshot));
        Assert.AreEqual(0, machine.Devices[@"ROOT\KDNIC\0000"].Problem);
    }

    [TestMethod]
    public void A_device_that_was_already_disabled_is_not_enabled_by_rollback()
    {
        var machine = new FakeMachine();
        machine.Devices[@"ROOT\KDNIC\0000"] = ("Microsoft Kernel Debug Network Adapter", "Net", 22);
        var action = ActionFor("kernel-debug-network-adapter", machine.Host);
        Assert.IsFalse(action.Inspect().CanApply);
        var snapshot = action.Capture();
        action.Restore(snapshot);
        Assert.IsEmpty(machine.Commands);
    }

    [TestMethod]
    public void Restart_required_exit_code_is_a_pending_success_and_other_failures_throw()
    {
        var machine = new FakeMachine { ExitCode = 3010 };
        machine.Devices[@"ACPI\PNP0000\0"] = ("Programmable interrupt controller", "System", 0);
        var action = ActionFor("programmable-interrupt-controller", machine.Host);
        action.Apply();
        Assert.IsTrue(action.Verify());
        machine.ExitCode = 5;
        machine.Devices[@"ACPI\PNP0200\0"] = ("Direct memory access controller", "System", 0);
        Assert.ThrowsExactly<InvalidOperationException>(() => ActionFor("direct-memory-access-controller", machine.Host).Apply());
    }

    [TestMethod]
    public void Rollback_from_a_new_agent_process_re_enables_a_restart_pending_device()
    {
        var machine = new FakeMachine { ExitCode = 3010 };
        machine.Devices[@"ACPI\PNP0000\0"] = ("Programmable interrupt controller", "System", 0);
        var snapshot = ActionFor("programmable-interrupt-controller", machine.Host).Capture();
        ActionFor("programmable-interrupt-controller", machine.Host).Apply();
        Assert.AreEqual(0, machine.Devices[@"ACPI\PNP0000\0"].Problem, "A 3010 disable stays invisible until reboot.");
        machine.ExitCode = 0;
        ActionFor("programmable-interrupt-controller", machine.Host).Restore(snapshot);
        Assert.AreEqual(@"/enable-device ACPI\PNP0000\0", machine.Commands.Last());
    }

    [TestMethod]
    public void Inspect_refuses_missing_devices_non_admin_guards_and_critical_children()
    {
        var machine = new FakeMachine();
        var bus = ActionFor("umbus-root-bus", machine.Host);
        Assert.IsFalse(bus.Inspect().CanApply);
        Assert.Contains("not present", bus.Inspect().Status);

        machine.Devices[@"ROOT\UMBUS\0000"] = ("UMBus Root Bus Enumerator", "System", 0);
        machine.Devices[@"HID\VID_1\0"] = ("USB Input Device", "HIDClass", 0);
        machine.Children[@"ROOT\UMBUS\0000"] = [@"HID\VID_1\0"];
        Assert.Contains("HIDClass", bus.Inspect().Status);
        machine.Children.Clear();
        Assert.IsTrue(bus.Inspect().CanApply);
        machine.Admin = false;
        Assert.IsFalse(bus.Inspect().CanApply);
        machine.Admin = true;
        machine.GuardReason = "A physical battery is present";
        Assert.IsFalse(bus.Inspect().CanApply);
    }

    [TestMethod]
    public void Snapshots_reject_unsafe_instance_IDs_and_restore_ignores_devices_outside_the_candidate()
    {
        var machine = new FakeMachine();
        machine.Devices[@"ROOT\KDNIC\0000"] = ("Microsoft Kernel Debug Network Adapter", "Net", 0);
        machine.Devices[@"PCI\VEN_10DE\1"] = ("NVIDIA GeForce", "Display", 22);
        var action = ActionFor("kernel-debug-network-adapter", machine.Host);
        Assert.ThrowsExactly<InvalidOperationException>(() => action.ValidateSnapshot("""{"V":1,"Devices":[{"Id":"ROOT\\X\"; calc","Enabled":true}]}"""));
        Assert.ThrowsExactly<InvalidOperationException>(() => action.ValidateSnapshot("""{"V":2,"Devices":[]}"""));
        action.Restore("""{"V":1,"Devices":[{"Id":"PCI\\VEN_10DE\\1","Enabled":true}]}""");
        Assert.IsEmpty(machine.Commands);
    }

    [TestMethod]
    public void Evidence_describes_present_candidates_with_children_state_and_actionability()
    {
        var machine = new FakeMachine();
        machine.Devices[@"ROOT\UMBUS\0000"] = ("UMBus Root Bus Enumerator", "System", 22);
        machine.Devices[@"SWD\X\1"] = ("Some Software Device", "SoftwareDevice", 0);
        machine.Children[@"ROOT\UMBUS\0000"] = [@"SWD\X\1"];
        var lines = DeviceControl.Describe(machine.Host);
        var line = lines.Single();
        Assert.StartsWith("device.disable.umbus-root-bus:", line);
        Assert.Contains("Some Software Device", line);
        Assert.Contains("1 of 1 disabled", line);
        Assert.Contains("already applied", line);
    }

    [TestMethod]
    [TestCategory("WindowsIntegration")]
    public void Real_machine_enumeration_and_inspection_are_read_only()
    {
        var lines = DeviceControl.Describe(DeviceControl.Machine);
        foreach (var action in DeviceControl.Actions(DeviceControl.Machine)) _ = action.Inspect();
        Assert.IsNotNull(lines);
    }
}
