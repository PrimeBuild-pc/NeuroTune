using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace NeuroTune;

// Read-only PCIe/USB attachment map: CPU lanes versus chipset, negotiated versus maximum link and the firmware-declared
// USB panel. Instance IDs (which carry USB serial numbers) never leave this class.
public static partial class DeviceTopology
{
    internal readonly record struct PciHop(string Vendor, int Type, uint Bus, uint Device);

    private sealed class Node
    {
        public required uint Handle;
        public required string Id;
        public Node? Parent;
        public List<Node> Children { get; } = [];
        public string Class => Text(Handle, ClassKey) ?? "";
        public string Service => Text(Handle, ServiceKey) ?? "";
        public string Name => Text(Handle, BusReportedKey) is { Length: > 0 } reported && Id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)
            ? reported : Text(Handle, FriendlyNameKey) ?? Text(Handle, DescriptionKey) ?? "Unknown";
        public bool IsPci => Id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase);
        public int PciType => (int)(Number(Handle, PciTypeKey) ?? 0);
        public uint Address => Number(Handle, AddressKey) ?? 0;
        public string Location => $"{Number(Handle, BusKey) ?? 0:X2}:{Address >> 16:X2}.{Address & 0xFFFF}";
    }

    private static readonly HashSet<string> PciClasses = new(["Display", "SCSIAdapter", "HDC", "USB", "Net", "MEDIA"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> UsbKinds = new(["Keyboard", "Mouse", "HIDClass", "Media", "AudioEndpoint", "Camera", "Image", "Bluetooth", "DiskDrive", "Net", "Ports", "XboxComposite"], StringComparer.OrdinalIgnoreCase);

    public static object Read()
    {
        if (CM_Locate_DevNodeW(out var root, null, 0) != 0) throw new InvalidOperationException("Device tree unavailable.");
        var all = new List<Node>();
        var top = new Node { Handle = root, Id = "ROOT" };
        Walk(top, all, 0);
        var pciNodes = all.Where(node => node.IsPci && node.PciType is >= 0 and <= 5 && PciClasses.Contains(node.Class)).ToList();
        var pci = pciNodes.Take(40).Select(node => new
        {
            location = node.Location,
            name = node.Name,
            @class = node.Class,
            link = Link(Number(node.Handle, LinkSpeedKey), Number(node.Handle, LinkWidthKey)),
            maxLink = Link(Number(node.Handle, MaxSpeedKey), Number(node.Handle, MaxWidthKey)),
            attach = Attach(Hops(node)),
            path = Hops(node).Skip(1).Reverse().Select(hop => $"{hop.Vendor} {BridgeName(hop.Type)}").ToList(),
        }).ToList();
        var usbNodes = all.Where(node => node.Id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) && !IsHub(node) && node.Parent is { } parent && IsHub(parent)).ToList();
        var usb = usbNodes.Take(60).Select(node =>
        {
            var ports = new List<uint>();
            var current = node;
            for (; current.Parent is { } parent && !parent.Id.StartsWith(@"USB\ROOT_HUB", StringComparison.OrdinalIgnoreCase) && IsHub(parent); current = parent)
                ports.Insert(0, current.Address);
            ports.Insert(0, current.Address);
            var pld = Property(node.Handle, PldKey) ?? Property(current.Handle, PldKey);
            var controller = Ancestors(node).FirstOrDefault(item => item.IsPci);
            return new
            {
                name = node.Name,
                kinds = Descendants(node, 4).Select(item => item.Class).Where(UsbKinds.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                controller = controller?.Location ?? "unknown",
                controllerAttach = controller is null ? "unknown" : Attach(Hops(controller)),
                portPath = string.Join('.', ports),
                externalHubs = ports.Count - 1,
                firmwarePanel = Panel(pld),
            };
        }).ToList();
        return new
        {
            pci,
            pciTruncated = pciNodes.Count > 40,
            usb,
            usbTruncated = usbNodes.Count > 60,
            limitation = "attach is inferred from PCI topology (AMD: behind the chipset upstream switch; Intel: bus-0 device 0x12 or higher is the PCH), not a board-specific slot map. " +
                "Idle GPUs often negotiate a lower PCIe generation to save power; a narrower width than maxLink is the reliable lane-sharing signal. " +
                "firmwarePanel is the motherboard's ACPI _PLD declaration and is frequently missing or wrong; null means not declared, and no USB speed is read. " +
                "Which M.2/PCIe slots share lanes is only documented in the motherboard manual.",
        };
    }

    // hops: the device first, then each PCI ancestor up to the root bus.
    internal static string Attach(IReadOnlyList<PciHop> hops)
    {
        var top = hops[^1];
        if (top.Vendor == "1022") return hops.Skip(1).Any(hop => hop.Type == 9 && hop.Vendor is "1022" or "1B21") ? "chipset" : "cpu";
        if (top.Vendor == "8086" && top.Bus == 0) return top.Device >= 0x12 ? "chipset" : "cpu";
        return "unknown";
    }

    // ACPI _PLD revision 2: bit 64 user-visible, bits 67-69 panel.
    internal static string? Panel(byte[]? pld) => pld is not { Length: >= 16 } ? null : (pld[8] & 1) == 0 ? "internal" : ((pld[8] >> 3) & 7) switch
    {
        0 => "top",
        1 => "bottom",
        2 => "left",
        3 => "right",
        4 => "front",
        5 => "back",
        _ => null,
    };

    internal static string? Link(uint? speed, uint? width) => speed is > 0 and < 16 && width is > 0 and <= 32 ? $"Gen{speed} x{width}" : null;

    private static string BridgeName(int type) => type switch { 8 => "root port", 9 => "upstream switch", 10 => "downstream switch", _ => "bridge" };

    private static bool IsHub(Node node) => node.Service.StartsWith("usbhub", StringComparison.OrdinalIgnoreCase);

    private static List<PciHop> Hops(Node node) => [.. new[] { node }.Concat(Ancestors(node)).TakeWhile(item => item.IsPci)
        .Select(item => new PciHop(VendorPattern().Match(item.Id) is { Success: true } match ? match.Groups[1].Value.ToUpperInvariant() : "", item.PciType, Number(item.Handle, BusKey) ?? 0, item.Address >> 16))];

    private static IEnumerable<Node> Ancestors(Node node) { for (var current = node.Parent; current is not null; current = current.Parent) yield return current; }

    private static IEnumerable<Node> Descendants(Node node, int depth) => depth == 0 ? [node] : new[] { node }.Concat(node.Children.SelectMany(child => Descendants(child, depth - 1)));

    private static void Walk(Node parent, List<Node> all, int depth)
    {
        if (depth >= 24 || all.Count >= 4000 || CM_Get_Child(out var child, parent.Handle, 0) != 0) return;
        for (var sibling = child; ;)
        {
            var id = new StringBuilder(260);
            if (CM_Get_Device_IDW(sibling, id, id.Capacity, 0) == 0)
            {
                var node = new Node { Handle = sibling, Id = id.ToString(), Parent = parent };
                parent.Children.Add(node); all.Add(node);
                Walk(node, all, depth + 1);
            }
            if (CM_Get_Sibling(out var next, sibling, 0) != 0) break;
            sibling = next;
        }
    }

    private static byte[]? Property(uint node, DevPropKey key)
    {
        uint size = 0;
        CM_Get_DevNode_PropertyW(node, ref key, out _, null, ref size, 0);
        if (size is 0 or > 4096) return null;
        var buffer = new byte[size];
        return CM_Get_DevNode_PropertyW(node, ref key, out _, buffer, ref size, 0) == 0 ? buffer : null;
    }
    private static string? Text(uint node, DevPropKey key) => Property(node, key) is { } bytes ? Encoding.Unicode.GetString(bytes).TrimEnd('\0') : null;
    private static uint? Number(uint node, DevPropKey key) => Property(node, key) is { Length: 4 } bytes ? BitConverter.ToUInt32(bytes) : null;

    [GeneratedRegex(@"^PCI\\VEN_([0-9A-F]{4})", RegexOptions.IgnoreCase)] private static partial Regex VendorPattern();

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey(string fmtid, uint pid) { public Guid Fmtid = new(fmtid); public uint Pid = pid; }
    private const string DeviceProps = "a45c254e-df1c-4efd-8020-67d146a850e0", BusProps = "540b947e-8b40-45bc-a8a2-6a0b894cbda2", PciProps = "3ab22e31-8264-4b4e-9af5-a8d2d8e33e62";
    private static readonly DevPropKey DescriptionKey = new(DeviceProps, 2), ServiceKey = new(DeviceProps, 6), ClassKey = new(DeviceProps, 9), FriendlyNameKey = new(DeviceProps, 14),
        BusKey = new(DeviceProps, 23), AddressKey = new(DeviceProps, 30), BusReportedKey = new(BusProps, 4), PldKey = new(BusProps, 9),
        PciTypeKey = new(PciProps, 1), LinkSpeedKey = new(PciProps, 9), LinkWidthKey = new(PciProps, 10), MaxSpeedKey = new(PciProps, 11), MaxWidthKey = new(PciProps, 12);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Locate_DevNodeW(out uint devInst, string? deviceId, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Child(out uint child, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Sibling(out uint sibling, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int length, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DevPropKey key, out uint type, byte[]? buffer, ref uint size, uint flags);
}
