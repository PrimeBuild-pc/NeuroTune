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
    public void Diagnosis_rejects_fabricated_evidence()
    {
        var facts = new Dictionary<string, string> { ["gaming:Game Mode"] = "1" };
        var valid = """
            {"summary":"Checked","findings":[{"title":"Game Mode","evidenceId":"gaming:Game Mode","currentValue":"1","assessment":"Enabled"}],"recommendations":[{"actionId":"gaming.game-mode","evidenceId":"gaming:Game Mode","reason":"Matches the goal"}]}
            """;
        var fabricated = valid.Replace("\"currentValue\":\"1\"", "\"currentValue\":\"0\"");
        var unsupportedRecommendation = valid.Replace("\"evidenceId\":\"gaming:Game Mode\",\"reason\"", "\"evidenceId\":\"missing\",\"reason\"");

        Assert.AreEqual("Game Mode", LlmClient.ParseDiagnosis(valid, new OptimizationCatalog(), facts).Findings.Single().Title);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LlmClient.ParseDiagnosis(fabricated, new OptimizationCatalog(), facts));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LlmClient.ParseDiagnosis(unsupportedRecommendation, new OptimizationCatalog(), facts));
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
        var facts = LlmClient.BuildEvidenceFacts(profile);
        Assert.DoesNotContain(Environment.UserName, facts["system:cpu"], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, facts["system:cpu"], StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Provider_model_list_is_parsed_and_sorted()
    {
        var models = LlmClient.ParseModels("""
            {"data":[{"id":"z-model"},{"id":"a-model"},{"id":"a-model"}]}
            """);

        CollectionAssert.AreEqual(new[] { "a-model", "z-model" }, models.ToArray());
    }

    [TestMethod]
    public void Custom_provider_requires_https_unless_it_is_local()
    {
        var remote = LlmClient.Defaults(LlmProvider.Custom);
        remote.BaseUrl = "http://example.com/v1";
        Assert.ThrowsExactly<InvalidOperationException>(() => LlmClient.ValidateBaseUrl(remote));

        var local = LlmClient.Defaults(LlmProvider.Local);
        local.BaseUrl = "http://127.0.0.1:11434/v1";
        Assert.AreEqual("127.0.0.1", LlmClient.ValidateBaseUrl(local).Host);
    }

    [TestMethod]
    public void DeepSeek_has_a_native_provider_preset()
    {
        var settings = LlmClient.Defaults(LlmProvider.DeepSeek);

        Assert.AreEqual("https://api.deepseek.com/v1", settings.BaseUrl);
        Assert.AreEqual("deepseek-chat", settings.Model);
        Assert.AreEqual(ApiProtocol.OpenAiCompatible, settings.Protocol);
    }

    [TestMethod]
    public void Tuning_goals_are_trimmed_deduplicated_and_bounded()
    {
        var goals = new TuningGoals { Games = [" Valorant ", "valorant", ""] };

        goals.Validate();

        CollectionAssert.AreEqual(new[] { "Valorant" }, goals.Games);
        goals.Notes = new string('x', 1_001);
        Assert.ThrowsExactly<InvalidOperationException>(goals.Validate);
    }

    [TestMethod]
    public void Empty_consent_question_is_rejected()
    {
        var json = """
            {"summary":"Evidence checked","findings":[],"recommendations":[],"consentQuestion":""}
            """;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LlmClient.ParseDiagnosis(json, new OptimizationCatalog()));
    }

    [TestMethod]
    public void Incomplete_operation_is_flagged_for_recovery()
    {
        var manifest = new OperationManifest
        {
            Status = "Applying",
            Actions = [new ActionRecord { Attempted = true }]
        };

        Assert.IsTrue(manifest.HasPendingRollback);
        manifest.Status = "Completed";
        Assert.IsFalse(manifest.HasPendingRollback);
    }

    [TestMethod]
    [TestCategory("WindowsIntegration")]
    public void Profiler_collects_a_windows_profile()
    {
        var profile = new SystemProfiler().Collect();

        Assert.IsFalse(string.IsNullOrWhiteSpace(profile.OperatingSystem));
        Assert.IsNotNull(profile.GamingSettings);
        Assert.IsNotNull(profile.NetworkSettings);
        Assert.IsGreaterThan(10, profile.PerformanceRegistry.Count);
        Assert.IsNotNull(profile.HardwareCapabilities);
        Assert.IsNotNull(profile.PolicyConflicts);
    }
}
