using System.Text.Json;
using NeuroTune;

namespace NeuroTune.Tests;

[TestClass]
public sealed class DeviceTopologyTests
{
    [TestMethod]
    public void AttachSeparatesCpuLanesFromTheChipset()
    {
        // X570: NVMe on a CPU root port, NVMe behind the chipset upstream switch, GPU behind its own AMD switch.
        Assert.AreEqual("cpu", DeviceTopology.Attach([new("144D", 2, 1, 0), new("1022", 8, 0, 1)]));
        Assert.AreEqual("chipset", DeviceTopology.Attach([new("144D", 2, 34, 0), new("1022", 10, 33, 0), new("1022", 9, 32, 0), new("1022", 8, 0, 1)]));
        Assert.AreEqual("cpu", DeviceTopology.Attach([new("1002", 2, 48, 0), new("1002", 10, 47, 0), new("1002", 9, 46, 0), new("1022", 8, 0, 3)]));
        // Intel: PCH root ports and integrated xHCI sit at bus-0 device 0x12 or higher.
        Assert.AreEqual("chipset", DeviceTopology.Attach([new("15B7", 2, 4, 0), new("8086", 8, 0, 0x1D)]));
        Assert.AreEqual("chipset", DeviceTopology.Attach([new("8086", 2, 0, 0x14)]));
        Assert.AreEqual("cpu", DeviceTopology.Attach([new("10DE", 2, 1, 0), new("8086", 8, 0, 1)]));
        Assert.AreEqual("unknown", DeviceTopology.Attach([new("1414", 2, 0, 0)]));
    }

    [TestMethod]
    public void PanelDecodesFirmwareLocationAndRejectsMissingData()
    {
        byte[] Pld(byte flags) { var value = new byte[16]; value[0] = 0x82; value[8] = flags; return value; }
        Assert.AreEqual("back", DeviceTopology.Panel(Pld(0x69)));
        Assert.AreEqual("front", DeviceTopology.Panel(Pld(0x21)));
        Assert.AreEqual("internal", DeviceTopology.Panel(Pld(0x28)));
        Assert.IsNull(DeviceTopology.Panel(Pld(0x31)));
        Assert.IsNull(DeviceTopology.Panel(null));
        Assert.IsNull(DeviceTopology.Panel(new byte[8]));
    }

    [TestMethod]
    public void LinkRejectsUnreportedValues()
    {
        Assert.AreEqual("Gen4 x16", DeviceTopology.Link(4, 16));
        Assert.IsNull(DeviceTopology.Link(0, 16));
        Assert.IsNull(DeviceTopology.Link(4, null));
    }

    [TestMethod]
    public void LiveReadIsBoundedAndCarriesNoInstanceIds()
    {
        var json = JsonSerializer.Serialize(DeviceTopology.Read());
        Console.WriteLine(json);
        Assert.IsLessThan(20_000, json.Length);
        StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex(@"VID_|VEN_|PCI\\\\|USB\\\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }
}
