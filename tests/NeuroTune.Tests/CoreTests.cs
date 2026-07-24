namespace NeuroTune.Tests;

[TestClass]
public sealed class CoreTests
{
    [TestMethod]
    public void Diagnosis_accepts_only_catalog_actions()
    {
        var catalog = new OptimizationCatalog();
        var json = """
            {"summary":"Sistema valido","findings":[],"recommendations":[{"actionId":"gaming.game-mode","reason":"Riduce attività concorrenti"}]}
            """;

        var result = LlmClient.ParseDiagnosis(json, catalog);

        Assert.AreEqual("gaming.game-mode", result.Recommendations.Single().ActionId);
    }

    [TestMethod]
    public void Diagnosis_rejects_unknown_actions()
    {
        var json = """
            {"summary":"Sistema valido","findings":[],"recommendations":[{"actionId":"run.powershell","reason":"Esegui script"}]}
            """;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LlmClient.ParseDiagnosis(json, new OptimizationCatalog()));
    }

    [TestMethod]
    public void Sanitizer_removes_windows_identity()
    {
        var profile = new SystemProfile
        {
            Cpu = $"CPU di {Environment.UserName} su {Environment.MachineName}"
        };

        var json = ProfileSanitizer.Serialize(profile);

        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Balanced_preset_selects_only_recommended_low_risk_actions()
    {
        var catalog = new OptimizationCatalog();
        var low = catalog.Get("gaming.game-mode");
        var medium = catalog.Get("gaming.hags");

        Assert.IsTrue(OptimizationCatalog.SelectForPreset(low, true, OptimizationPreset.Balanced));
        Assert.IsFalse(OptimizationCatalog.SelectForPreset(medium, true, OptimizationPreset.Balanced));
        Assert.IsFalse(OptimizationCatalog.SelectForPreset(low, false, OptimizationPreset.Balanced));
    }

    [TestMethod]
    [TestCategory("WindowsIntegration")]
    public void Profiler_collects_a_windows_profile()
    {
        var profile = new SystemProfiler().Collect();

        Assert.IsFalse(string.IsNullOrWhiteSpace(profile.OperatingSystem));
        Assert.IsNotNull(profile.GamingSettings);
        Assert.IsNotNull(profile.NetworkSettings);
    }
}
