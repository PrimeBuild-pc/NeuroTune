using System.Text.Json;

namespace NeuroTune.Tests;

[TestClass]
public sealed class NvidiaProfileTests
{
    private const string Export = """
        <?xml version="1.0" encoding="utf-16"?>
        <ArrayOfProfile>
          <Profile>
            <ProfileName>Fortnite</ProfileName>
            <Executeables><string>fortniteclient-win64-shipping.exe</string><string>fortnitelauncher.exe</string></Executeables>
            <Settings>
              <ProfileSetting><SettingNameInfo>Power Management - Mode</SettingNameInfo><SettingID>274197361</SettingID><SettingValue>0</SettingValue><ValueType>Dword</ValueType></ProfileSetting>
              <ProfileSetting><SettingNameInfo>Threaded Optimization</SettingNameInfo><SettingID>549528094</SettingID><SettingValue>1</SettingValue><ValueType>Dword</ValueType></ProfileSetting>
            </Settings>
          </Profile>
        </ArrayOfProfile>
        """;

    [TestMethod]
    public void Import_keeps_the_exported_applications_and_settings_and_overrides_only_requested_values()
    {
        var node = NvidiaProfileInspector.FindProfile(Export, "Fortnite");
        Assert.IsNull(NvidiaProfileInspector.FindProfile(Export, "fortnite"), "DRS profile names are exact.");
        var import = NvidiaProfileInspector.BuildImport(node, "Fortnite", new Dictionary<uint, uint> { [274197361] = 1, [277041154] = 237 });
        var imported = NvidiaProfileInspector.FindProfile(import, "Fortnite");
        StringAssert.Contains(import, "fortnitelauncher.exe");
        CollectionAssert.AreEquivalent(new Dictionary<uint, uint> { [274197361] = 1, [549528094] = 1, [277041154] = 237 }, NvidiaProfileInspector.Settings(imported));
        Assert.HasCount(2, NvidiaProfileInspector.Settings(node), "The captured node is not mutated.");

        var reset = NvidiaProfileInspector.BuildImport(null, NvidiaProfileInspector.BaseProfile, new Dictionary<uint, uint>());
        Assert.IsEmpty(NvidiaProfileInspector.Settings(NvidiaProfileInspector.FindProfile(reset, NvidiaProfileInspector.BaseProfile)));
    }

    [TestMethod]
    public void Primitive_validates_parameters_and_is_unavailable_without_its_tool()
    {
        var action = ParametricActions.Create("nvidia.profile", JsonSerializer.Serialize(new { Profile = "Base Profile", Settings = new[] { new { Id = 274197361u, Value = 1u } } }));
        StringAssert.StartsWith(action.Id, "nvidia.profile.");
        Assert.AreEqual(action.Id, ParametricActions.Create(action.Kind!, action.Parameters!).Id);
        if (NvidiaProfileInspector.Unavailable() is not null) Assert.IsFalse(action.Inspect().CanApply);
        foreach (var bad in new object[]
        {
            new { Profile = "", Settings = new[] { new { Id = 1u, Value = 1u } } },
            new { Profile = "Base Profile", Settings = Array.Empty<object>() },
            new { Profile = "Base Profile", Settings = new[] { new { Id = 1u, Value = 1u }, new { Id = 1u, Value = 2u } } }
        })
            Assert.ThrowsExactly<InvalidOperationException>(() => ParametricActions.Create("nvidia.profile", JsonSerializer.Serialize(bad)));
    }
}
