using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NeuroTune;

internal sealed record PnpDevice(string Name, string Id, string Class, int Problem)
{
    public bool Disabled => Problem == 22; // CM_PROB_DISABLED
}

internal enum DeviceGuard { None, NoBattery, OtherAdapterConnected }

internal sealed record DeviceCandidate(string Slug, string Name, RiskLevel Risk, bool RequiresRestart, string[] Classes, string[] NamePatterns,
    string What, string Loses, string Latency, string[]? IdPrefixes = null, DeviceGuard Guard = DeviceGuard.None)
{
    public string ActionId => "device.disable." + Slug;

    internal List<PnpDevice> Match(IEnumerable<PnpDevice> devices) => devices.Where(device =>
        Classes.Contains(device.Class, StringComparer.OrdinalIgnoreCase) &&
        NamePatterns.Any(pattern => Regex.IsMatch(device.Name, "^(?:" + pattern + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) &&
        (IdPrefixes is null || IdPrefixes.Any(prefix => device.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))).ToList();
}

// The machine behind the device actions; replaced by a fake in tests.
internal sealed record DeviceHost(Func<IReadOnlyList<PnpDevice>> Enumerate, Func<string, string, int> Pnputil, Func<bool> IsAdmin,
    Func<DeviceGuard, IReadOnlyList<PnpDevice>, string?> Guard, Func<string, IReadOnlyList<string>> Descendants);

internal sealed record DeviceState(string Id, bool Enabled);
internal sealed record DeviceSnapshot(int V, List<DeviceState> Devices);

// Reversible, allowlisted Device Manager toggles. The model may only recommend these IDs; it never supplies an instance ID.
internal static partial class DeviceControl
{
    private const int MaxInstances = 12;
    private static readonly string[] CriticalClasses = ["HIDClass", "Keyboard", "Mouse", "DiskDrive", "SCSIAdapter", "HDC", "Display", "USB", "Processor"];
    private static readonly string[] Physical = ["USB\\", "PCI\\", "ACPI\\"];

    internal static IReadOnlyList<DeviceCandidate> Candidates { get; } =
    [
        new("umbus-root-bus", "UMBus Root Bus Enumerator", RiskLevel.High, true, ["System"], ["UMBus Root Bus Enumerator"],
            "Windows bus that enumerates user-mode (UMDF) software devices such as sensors, some audio/Bluetooth helpers and portable-device shims.",
            "Every device attached below it disappears until it is re-enabled.",
            "Removes one root enumerator from boot-time enumeration; any benefit is expected to be small and must be measured."),
        new("composite-bus", "Composite Bus Enumerator", RiskLevel.High, true, ["System"], ["Composite Bus Enumerator"],
            "Windows bus for composite software devices that expose several functions through one virtual device.",
            "Composite virtual devices below it (some virtual audio/driver front-ends) stop being enumerated.",
            "Removes one virtual enumerator; any benefit is expected to be small and must be measured."),
        new("programmable-interrupt-controller", "Programmable interrupt controller", RiskLevel.High, true, ["System"], ["Programmable interrupt controller"],
            "Legacy 8259 interrupt-controller stub that modern APIC-based PCs normally keep only for compatibility.",
            "On some firmware it can affect legacy interrupt routing; Windows may require a restart and an unexpected effect is possible.",
            "Latency guides cite it, but a measurable gain on APIC systems is unproven; verify with a before/after trace."),
        new("direct-memory-access-controller", "Direct memory access controller", RiskLevel.Medium, true, ["System"], ["Direct memory access controller"],
            "Legacy ISA DMA controller stub reported by ACPI; no modern PCIe device uses it.",
            "Old ISA-era or legacy-emulated devices that rely on it stop working.",
            "Removes a legacy resource claim; any benefit is expected to be negligible and must be measured."),
        new("numeric-data-processor", "Numeric data processor", RiskLevel.Low, true, ["System"], ["Numeric data processor"],
            "Legacy x87 math-coprocessor placeholder exposed for compatibility.",
            "Nothing observable on 64-bit Windows; very old 16-bit software is the only user.",
            "Negligible; listed for completeness."),
        new("remote-desktop-redirector-bus", "Remote Desktop Device Redirector Bus", RiskLevel.Low, false, ["System"], ["Remote Desktop Device Redirector Bus"],
            "Carries drive, printer, smart-card and audio redirection for Remote Desktop sessions.",
            "Redirected devices stop working in RDP sessions; local use is unaffected.",
            "Removes a driver and its enumeration work; benefit is small unless Remote Desktop is never used."),
        new("virtual-drive-enumerator", "Microsoft Virtual Drive Enumerator", RiskLevel.Low, false, ["System"], ["Microsoft Virtual Drive Enumerator"],
            "Enumerates virtual drives such as mounted ISO/VHD volumes.",
            "Mounting disk images may stop working until it is re-enabled.",
            "Small; relevant only if disk images are never mounted."),
        new("ndis-virtual-network-enumerator", "NDIS Virtual Network Adapter Enumerator", RiskLevel.Medium, true, ["System"], ["NDIS Virtual Network Adapter Enumerator"],
            "Hosts virtual network adapters created by VPN clients, Hyper-V, WSL and similar software.",
            "Virtual adapters below it (VPN, Hyper-V/WSL networking, containers) stop working.",
            "Fewer virtual adapters can reduce network stack work; benefit must be measured and VPN use rules it out."),
        new("kernel-debug-network-adapter", "Microsoft Kernel Debug Network Adapter", RiskLevel.Low, false, ["Net"], ["Microsoft Kernel Debug Network Adapter"],
            "Adapter used only for kernel debugging over the network.",
            "Network kernel debugging stops working; normal networking is unaffected.",
            "Negligible; removes an idle virtual adapter."),
        new("acpi-control-method-battery", "Microsoft ACPI-Compliant Control Method Battery", RiskLevel.Low, false, ["Battery"], ["Microsoft ACPI-Compliant Control Method Battery"],
            "Battery driver instance; on desktops it is usually an empty placeholder.",
            "Battery status disappears from Windows.",
            "Removes polling of a device that does not exist physically; offered only when no battery is present.", null, DeviceGuard.NoBattery),
        new("hyperv-virtual-ethernet", "Hyper-V virtual ethernet adapters", RiskLevel.Medium, true, ["Net"], ["Hyper-V Virtual Ethernet Adapter( #\\d+)?"],
            "Virtual switch adapters used by Hyper-V, WSL2, Windows Sandbox and containers.",
            "Virtual machines, WSL2 and container networking lose connectivity.",
            "Removes virtual interfaces from the network stack; only sensible if none of that software is used."),
        new("wan-miniports", "WAN Miniport adapters", RiskLevel.Medium, true, ["Net"], ["WAN Miniport \\((IP|IPv6|L2TP|Network Monitor|PPPOE|PPTP|SSTP|IKEv2)\\)"],
            "Software adapters Windows uses for VPN tunnels, PPPoE broadband and network capture.",
            "Built-in VPN, PPPoE internet connections and some packet-capture tools stop working.",
            "Fewer idle adapters; benefit is small and must be measured. Do not use if you dial PPPoE or use a VPN."),
        new("wifi-direct-virtual-adapters", "Microsoft Wi-Fi Direct Virtual Adapters", RiskLevel.Low, false, ["Net"], ["Microsoft Wi-Fi Direct Virtual Adapter( #\\d+)?"],
            "Virtual adapters for Mobile Hotspot, Miracast/wireless display and Nearby Sharing.",
            "Mobile Hotspot, casting to wireless displays and Nearby Sharing stop working.",
            "Removes idle virtual wireless interfaces; benefit is small."),
        new("bluetooth-radio", "Bluetooth radio", RiskLevel.Medium, false, ["Bluetooth"], [".*Bluetooth.*"],
            "The physical Bluetooth radio and its driver.",
            "All Bluetooth devices disconnect (headsets, controllers, keyboards, mice); wireless audio and Phone Link stop.",
            "Stops radio coexistence and driver activity, which can help on shared 2.4 GHz and USB-radio setups; measure it.", Physical),
        new("wifi-adapter", "Wi-Fi adapter", RiskLevel.Medium, false, ["Net"], [".*(Wireless|Wi-?Fi|WLAN|802\\.11).*"],
            "The physical wireless network adapter.",
            "No wireless connectivity until it is re-enabled; wired Ethernet is unaffected.",
            "Removes wireless scanning and power-save jitter when a wired link is in use; offered only while another physical adapter is connected.",
            Physical, DeviceGuard.OtherAdapterConnected),
        new("gpu-hdmi-dp-audio", "GPU HDMI/DisplayPort audio", RiskLevel.Medium, false, ["MEDIA"],
            ["(NVIDIA|AMD) High Definition Audio( Device)?", "Intel\\(R\\) Display Audio"],
            "Audio function of the graphics card that carries sound over HDMI/DisplayPort to a monitor or TV.",
            "Sound through the monitor/TV speakers or headphone jack stops; check the default playback device first.",
            "Removes an audio controller and its interrupts; can reduce DPC activity when the monitor audio is unused. Measure it."),
        new("virtual-audio-drivers", "Virtual audio drivers", RiskLevel.Low, false, ["MEDIA", "SoftwareDevice"],
            ["NVIDIA Virtual Audio Device.*", "Steam Streaming (Speakers|Microphone)", "Oculus Virtual Audio Device", "Virtual Desktop Audio"],
            "Virtual audio devices installed by NVIDIA, Steam Remote Play, Oculus and Virtual Desktop.",
            "Remote Play/streaming and VR audio routing through these devices stops.",
            "Fewer audio endpoints and drivers to service; benefit is small."),
        new("gs-wavetable-synth", "Microsoft GS Wavetable Synth", RiskLevel.Low, false, ["MEDIA", "SoftwareDevice"], ["Microsoft GS Wavetable Synth"],
            "Built-in software MIDI synthesizer Windows exposes as an audio device.",
            "MIDI playback without another synthesizer falls silent; normal audio is unaffected.",
            "Negligible; removes an unused software audio endpoint.")
    ];

    internal static DeviceHost Machine { get; } = new(Enumerate, RunPnputil, LogService.IsAdministrator, CheckGuard, Descendants);

    internal static IEnumerable<OptimizationAction> Actions(DeviceHost host) => Candidates.Select(candidate => Create(candidate, host));

    internal static OptimizationAction Create(DeviceCandidate candidate, DeviceHost host)
    {
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<PnpDevice> Matches() => candidate.Match(host.Enumerate());
        ActionAvailability Inspect()
        {
            try
            {
                if (!host.IsAdmin()) return ActionAvailability.Unavailable("Requires administrator rights");
                var found = Matches();
                if (found.Count == 0) return ActionAvailability.Unavailable("This device is not present on this PC");
                if (found.Count > MaxInstances) return ActionAvailability.Unavailable("Too many matching devices were found");
                if (host.Guard(candidate.Guard, found) is { } reason) return ActionAvailability.Unavailable(reason);
                if (BlockingDescendant(found, host) is { } blocking) return ActionAvailability.Unavailable($"It hosts {blocking}, which NeuroTune never disables together with its parent");
                return found.All(device => device.Disabled) ? ActionAvailability.Applied("Already disabled in Device Manager")
                    : ActionAvailability.Ready($"{found.Count(device => !device.Disabled)} of {found.Count} enabled");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return ActionAvailability.Unavailable(exception.Message); }
        }
        string Capture() => JsonSerializer.Serialize(new DeviceSnapshot(1, Matches().Select(device => new DeviceState(device.Id, !device.Disabled)).ToList()));
        void Apply()
        {
            pending.Clear();
            foreach (var device in Matches().Where(device => !device.Disabled))
            {
                var code = host.Pnputil("/disable-device", device.Id);
                if (code == 3010) pending.Add(device.Id);
                else if (code != 0) throw new InvalidOperationException($"Windows could not disable {device.Name} (pnputil exit {code}).");
            }
        }
        bool Verify() => Matches().All(device => device.Disabled || pending.Contains(device.Id));
        void Restore(string state)
        {
            var current = host.Enumerate();
            var known = candidate.Match(current).Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in ParseSnapshot(state).Devices.Where(item => item.Enabled && known.Contains(item.Id)))
            {
                var device = current.First(entry => entry.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
                if (!device.Disabled && !pending.Contains(device.Id)) continue;
                var code = host.Pnputil("/enable-device", item.Id);
                if (code is not (0 or 3010)) throw new InvalidOperationException($"Windows could not re-enable {device.Name} (pnputil exit {code}).");
            }
            pending.Clear();
        }
        bool VerifyRestored(string state)
        {
            var current = host.Enumerate();
            return ParseSnapshot(state).Devices.Where(item => item.Enabled).All(item =>
                current.FirstOrDefault(entry => entry.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)) is not { Disabled: true });
        }
        return new(candidate.ActionId, $"Disable {candidate.Name} in Device Manager",
            $"{candidate.What} Disabling it: {candidate.Loses} Latency: {candidate.Latency}",
            "Devices", candidate.Risk, candidate.RequiresRestart, null, Inspect, Capture, Apply, Restore, Verify,
            supportedHardware: [$"A PC that exposes \"{candidate.Name}\" in Windows Plug and Play"],
            evidenceRequirements: ["Exact device name, class and enabled state from Windows Plug and Play enumeration"],
            sources: ["Windows Plug and Play enumeration and pnputil /disable-device and /enable-device, plus exact local device state"],
            sideEffects: [candidate.Loses, "Reversible: NeuroTune rollback or pnputil /enable-device re-enables it" + (candidate.RequiresRestart ? "; a restart may be needed for the change to take full effect" : "")],
            validateSnapshot: state => _ = ParseSnapshot(state), verifyRestored: VerifyRestored);
    }

    // Evidence for the planner: which candidates exist here, their state, what sits below them and whether they are actionable.
    internal static List<string> Describe(DeviceHost host, IReadOnlyList<DeviceCandidate>? candidates = null)
    {
        var all = host.Enumerate();
        var lines = new List<string>();
        foreach (var candidate in candidates ?? Candidates)
        {
            var found = candidate.Match(all);
            if (found.Count == 0) continue;
            var below = found.SelectMany(device => host.Descendants(device.Id)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => all.FirstOrDefault(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name).Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct().Take(8).ToList();
            var availability = Create(candidate, host).Inspect();
            lines.Add($"{candidate.ActionId}: {candidate.Name} ({found[0].Class}); {found.Count(device => device.Disabled)} of {found.Count} disabled; " +
                $"hosts {(below.Count == 0 ? "no child devices" : string.Join(", ", below))}; risk {candidate.Risk}; " +
                $"does: {candidate.What} loses: {candidate.Loses} action: {(availability.CanApply ? "ready" : availability.AlreadyApplied ? "already applied" : "unavailable (" + availability.Status + ")")}.");
        }
        return lines;
    }

    private static string? BlockingDescendant(IReadOnlyList<PnpDevice> found, DeviceHost host)
    {
        if (found.All(device => !device.Class.Equals("System", StringComparison.OrdinalIgnoreCase))) return null;
        var all = host.Enumerate();
        foreach (var id in found.SelectMany(device => host.Descendants(device.Id)))
            if (all.FirstOrDefault(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } child &&
                CriticalClasses.Contains(child.Class, StringComparer.OrdinalIgnoreCase)) return $"a {child.Class} device";
        return null;
    }

    internal static DeviceSnapshot ParseSnapshot(string state)
    {
        var snapshot = JsonSerializer.Deserialize<DeviceSnapshot>(state) ?? throw new InvalidOperationException("The device snapshot is empty.");
        if (snapshot.V != 1 || snapshot.Devices is null || snapshot.Devices.Count > 16 ||
            snapshot.Devices.Any(device => device is null || !InstanceIdRegex().IsMatch(device.Id ?? "")))
            throw new InvalidOperationException("The device snapshot is invalid.");
        return snapshot;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_&\\.#{}\-]{3,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstanceIdRegex();

    // --- Real machine -----------------------------------------------------------------------------------------------

    private static (DateTime At, IReadOnlyList<PnpDevice> Devices)? _cache;

    private static IReadOnlyList<PnpDevice> Enumerate()
    {
        if (_cache is { } cached && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(3)) return cached.Devices;
        var devices = SystemProfiler.Query("SELECT Name, PNPDeviceID, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity", row => new PnpDevice(
            row["Name"]?.ToString() ?? "", row["PNPDeviceID"]?.ToString() ?? "", row["PNPClass"]?.ToString() ?? "",
            Convert.ToInt32(row["ConfigManagerErrorCode"] ?? 0, System.Globalization.CultureInfo.InvariantCulture)))
            .Where(device => device.Id.Length > 0).ToList();
        _cache = (DateTime.UtcNow, devices);
        return devices;
    }

    private static int RunPnputil(string verb, string instanceId)
    {
        if (verb is not ("/disable-device" or "/enable-device") || !InstanceIdRegex().IsMatch(instanceId)) throw new InvalidOperationException("Invalid device command.");
        var start = new ProcessStartInfo(WindowsCommand.PathFor("pnputil.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(verb);
        start.ArgumentList.Add(instanceId);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("pnputil could not start.");
            process.StandardOutput.ReadToEndAsync(); process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("pnputil exceeded its deadline."); }
            return process.ExitCode;
        }
        finally { _cache = null; }
    }

    private static string? CheckGuard(DeviceGuard guard, IReadOnlyList<PnpDevice> found)
    {
        switch (guard)
        {
            case DeviceGuard.NoBattery:
                return SystemProfiler.Query("SELECT DeviceID FROM Win32_Battery", row => 1).Count > 0 ? "A physical battery is present" : null;
            case DeviceGuard.OtherAdapterConnected:
                var ids = found.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var others = SystemProfiler.Query("SELECT PNPDeviceID, NetConnectionStatus FROM Win32_NetworkAdapter WHERE PhysicalAdapter = True",
                    row => (Id: row["PNPDeviceID"]?.ToString() ?? "", Status: Convert.ToInt32(row["NetConnectionStatus"] ?? -1, System.Globalization.CultureInfo.InvariantCulture)));
                return others.Any(adapter => adapter.Status == 2 && !ids.Contains(adapter.Id)) ? null : "No other physical network adapter is connected, so this would cut the PC off the network";
            default: return null;
        }
    }

    private static IReadOnlyList<string> Descendants(string instanceId)
    {
        var result = new List<string>();
        if (CM_Locate_DevNodeW(out var node, instanceId, 0) != 0) return result;
        var stack = new Stack<(uint Node, int Depth)>();
        stack.Push((node, 0));
        while (stack.Count > 0 && result.Count < 200)
        {
            var (current, depth) = stack.Pop();
            if (depth >= 4 || CM_Get_Child(out var child, current, 0) != 0) continue;
            for (var sibling = child; ;)
            {
                var id = new StringBuilder(260);
                if (CM_Get_Device_IDW(sibling, id, id.Capacity, 0) == 0) result.Add(id.ToString());
                stack.Push((sibling, depth + 1));
                if (CM_Get_Sibling(out var next, sibling, 0) != 0) break;
                sibling = next;
            }
        }
        return result;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Child(out uint child, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Sibling(out uint sibling, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int length, uint flags);
}
