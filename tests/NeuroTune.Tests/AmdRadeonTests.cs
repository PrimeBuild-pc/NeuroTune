using System.Text.Json;

namespace NeuroTune.Tests;

[TestClass]
public sealed class AmdRadeonTests
{
    private static void RequireAmd()
    {
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "amdadlx64.dll"))) Assert.Inconclusive("Needs an AMD graphics driver (amdadlx64.dll).");
        if (!AmdRadeon.Included) Assert.Inconclusive("Needs the private AMD component (src/NeuroTune.AmdAdlx).");
    }

    [TestMethod]
    public void Reader_lists_every_setting_with_support_and_value()
    {
        RequireAmd();
        var read = JsonSerializer.Serialize(AmdRadeon.Read());
        foreach (var name in AmdRadeon.Names) StringAssert.Contains(read, $"\"{name}\"");
        StringAssert.Contains(read, "\"supported\":true");
    }

    [TestMethod]
    [DataRow("morphologicalAntiAliasing")]
    [DataRow("waitForVerticalRefresh")]
    public void Setting_round_trips_when_opted_in(string name)
    {
        RequireAmd();
        if (Environment.GetEnvironmentVariable("NEUROTUNE_TEST_AMD") != "1") Assert.Inconclusive("Writes a global Radeon setting; set NEUROTUNE_TEST_AMD=1.");
        var before = AmdRadeon.Get(name);
        var wanted = name == "waitForVerticalRefresh" ? before == 1 ? 0 : 1 : 1 - before;
        var action = ParametricActions.Create("amd.setting", JsonSerializer.Serialize(new { setting = name, value = wanted }));
        var state = action.Capture();
        action.Apply();
        Assert.IsTrue(action.Verify());
        Assert.AreEqual(wanted, AmdRadeon.Get(name));
        ParametricActions.Create(action.Kind!, action.Parameters!).Restore(state);
        Assert.AreEqual(before, AmdRadeon.Get(name));
    }

    [TestMethod]
    public void Unknown_settings_and_out_of_range_values_are_refused_before_any_write()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => ParametricActions.Create("amd.setting", """{"setting":"powerLimit","value":1}"""));
        Assert.ThrowsExactly<InvalidOperationException>(() => ParametricActions.Create("amd.setting", """{"setting":"antiLag","value":2}"""));
        Assert.ThrowsExactly<InvalidOperationException>(() => ParametricActions.Create("amd.setting", """{"setting":"waitForVerticalRefresh","value":4}"""));
        var action = ParametricActions.Create("amd.setting", """{"setting":"antiLag","value":1}""");
        StringAssert.StartsWith(action.Id, "amd.setting.");
    }

    [TestMethod]
    public void Builds_without_the_AMD_component_refuse_clearly()
    {
        if (AmdRadeon.Included) Assert.Inconclusive("This build includes the AMD component.");
        StringAssert.Contains(Assert.ThrowsExactly<InvalidOperationException>(() => AmdRadeon.Read()).Message, "AMD component");
    }

    [TestMethod]
    public void Public_setting_names_match_the_AMD_component()
    {
        if (!AmdRadeon.Included) Assert.Inconclusive("Needs the private AMD component (src/NeuroTune.AmdAdlx).");
        var component = Type.GetType("NeuroTune.AmdAdlxNative, NeuroTune.AmdAdlx", throwOnError: true)!;
        CollectionAssert.AreEquivalent(AmdRadeon.Names, ((IEnumerable<string>)component.GetProperty("Names")!.GetValue(null)!).ToArray());
    }
}
