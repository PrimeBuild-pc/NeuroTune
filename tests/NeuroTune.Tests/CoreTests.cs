using Microsoft.Win32;

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
    public void Probe_catalog_and_payload_report_are_typed_and_complete()
    {
        Assert.HasCount(83, ProbeCatalog.Registry);
        Assert.HasCount(83, ProbeCatalog.Registry.Select(probe => probe.StableEvidenceId).Distinct());
        Assert.IsTrue(ProbeCatalog.Registry.All(probe => probe.Source == ProbeSource.Registry &&
            probe.Privacy == EvidencePrivacy.SystemConfiguration && !string.IsNullOrWhiteSpace(probe.AbsenceSemantics)));

        var report = LlmClient.MeasureEvidence(new Dictionary<string, string>
        {
            ["system:cpu"] = "CPU",
            ["software:0"] = "Application"
        });
        Assert.AreEqual(2, report.FactCount);
        Assert.IsGreaterThan(0, report.Utf8Bytes);
        Assert.IsTrue(report.FitsSinglePass);
        Assert.IsFalse(LlmClient.MeasureEvidence(new Dictionary<string, string>
        {
            ["system:oversized"] = new string('x', LlmClient.MaxSinglePassEvidenceBytes)
        }).FitsSinglePass);
        Assert.AreEqual(1, report.PrivacyClasses[EvidencePrivacy.SystemConfiguration]);
        Assert.AreEqual(1, report.PrivacyClasses[EvidencePrivacy.SoftwareInventory]);
    }

    [TestMethod]
    public void Payload_metrics_store_only_bounded_metadata()
    {
        var path = Path.Combine(Path.GetTempPath(), $"neurotune-metrics-{Guid.NewGuid():N}.ndjson");
        try
        {
            new PayloadMetricsService(path).Record(
                new EvidencePayloadReport(678, 65_704, 256_000, true, []),
                LlmClient.Defaults(LlmProvider.Local),
                "local-scan-completed");
            var row = File.ReadAllText(path);
            StringAssert.Contains(row, "\"factCount\":678");
            StringAssert.Contains(row, "\"utf8Bytes\":65704");
            Assert.DoesNotContain("profile", row, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Factory_baselines_require_an_exact_local_match()
    {
        var exact = ComponentBaselineCatalog.Compare(new Dictionary<string, string>
        {
            ["CPU specification ID"] = "GenuineIntel-6-B7-1",
            ["CPU model"] = "13th Gen Intel(R) Core(TM) i9-13900K"
        });
        var missing = ComponentBaselineCatalog.Compare(new Dictionary<string, string>
        {
            ["CPU specification ID"] = "GenuineIntel-6-B7-1",
            ["CPU model"] = "Different CPU"
        });

        StringAssert.Contains(exact["CPU"], "Intel ARK 230496");
        StringAssert.StartsWith(missing["CPU"], "Baseline unavailable");

        var amd = ComponentBaselineCatalog.Compare(new Dictionary<string, string>
        {
            ["CPU specification ID"] = "AuthenticAMD-25-21-2",
            ["CPU model"] = "AMD Ryzen 7 5800X3D 8-Core Processor",
            ["GPU 1 specification ID"] = "VEN_1002&DEV_73A5&SUBSYS_05041043&REV_C0|AMD Radeon RX 6950 XT"
        });
        StringAssert.Contains(amd["CPU"], "100-100000651WOF");
        StringAssert.Contains(amd["GPU 1"], "PCI 1002:73A5");
    }

    [TestMethod]
    public void Conflict_graph_names_both_sides_and_their_values()
    {
        var profile = new SystemProfile();
        profile.PerformanceRegistry[@"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled"] = "1";
        profile.PerformanceRegistry[@"HKCU\Software\Microsoft\GameBar\AllowAutoGameMode"] = "0";

        var conflict = ConflictAnalyzer.Analyze(profile, new TuningGoals { Priority = OptimizationPriority.Fps })
            .Single(x => x.Id == "game-mode-policy");

        Assert.HasCount(2, conflict.Evidence);
        Assert.AreEqual("1", conflict.Evidence[@"registry:HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled"]);
        Assert.AreEqual("0", conflict.Evidence[@"registry:HKCU\Software\Microsoft\GameBar\AllowAutoGameMode"]);
        Assert.Contains(OptimizationPriority.Fps, conflict.Objectives);
    }

    [TestMethod]
    public void Conflict_graph_relates_vpn_filters_to_offload_overrides()
    {
        var profile = new SystemProfile
        {
            SoftwareSignals = ["Detected software family: WireGuard"],
            NetworkSettings = { ["Installed network components"] = "ms_tcpip" },
            PerformanceRegistry =
            {
                [@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\DisableTaskOffload"] = "1",
                [@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\EnableRSS"] = "Not configured"
            }
        };

        var conflict = ConflictAnalyzer.Analyze(profile, new TuningGoals { Priority = OptimizationPriority.NetworkLatency })
            .Single(item => item.Id == "vpn-offload-policy");

        Assert.HasCount(4, conflict.Evidence);
        CollectionAssert.Contains(conflict.SuggestedActionIds, "network.tcp-default");
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
    public void Registry_snapshots_preserve_value_kinds()
    {
        var binary = new byte[] { 0, 1, 255 };
        var multi = new[] { "one", "two" };

        CollectionAssert.AreEqual(binary, (byte[])OptimizationCatalog.DeserializeRegistryValue(
            RegistryValueKind.Binary, OptimizationCatalog.SerializeRegistryValue(RegistryValueKind.Binary, binary)));
        CollectionAssert.AreEqual(multi, (string[])OptimizationCatalog.DeserializeRegistryValue(
            RegistryValueKind.MultiString, OptimizationCatalog.SerializeRegistryValue(RegistryValueKind.MultiString, multi)));
        Assert.AreEqual(42L, OptimizationCatalog.DeserializeRegistryValue(
            RegistryValueKind.QWord, OptimizationCatalog.SerializeRegistryValue(RegistryValueKind.QWord, 42L)));
        var legacy = OptimizationCatalog.DeserializeRegistrySnapshot("{\"Exists\":true,\"Value\":7}");
        Assert.AreEqual(RegistryValueKind.DWord, legacy.Kind);
        Assert.AreEqual("7", legacy.Value);
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
        Assert.IsGreaterThan(75, profile.PerformanceRegistry.Count);
        Assert.IsNotNull(profile.HardwareCapabilities);
        Assert.IsNotNull(profile.FirmwareAndMemory);
        Assert.IsNotNull(profile.ComponentIdentities);
        Assert.IsNotNull(profile.FactoryBaselines);
        Assert.HasCount(4, profile.TelemetryCapabilities);
        Assert.IsFalse(profile.TelemetryCapabilities.Any(capability => capability.Status == TelemetryStatus.Supported));
        Assert.IsNotNull(profile.BootConfiguration);
        Assert.IsNotNull(profile.InstalledSoftware);
        Assert.IsNotNull(profile.RelevantDrivers);
        Assert.HasCount(5, profile.ScanPhases);
        Assert.IsNotNull(profile.PolicyConflicts);

        var facts = LlmClient.BuildEvidenceFacts(profile);
        var catalog = new OptimizationCatalog();
        var conflicts = ConflictAnalyzer.Analyze(profile, new TuningGoals { Priority = OptimizationPriority.Fps });
        Assert.IsTrue(conflicts.SelectMany(x => x.Evidence).All(item => facts[item.Key] == item.Value));
        Assert.IsTrue(conflicts.SelectMany(x => x.SuggestedActionIds).All(catalog.Contains));
    }
}
