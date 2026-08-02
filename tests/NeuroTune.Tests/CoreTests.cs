using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace NeuroTune.Tests;

[TestClass]
public sealed class CoreTests
{
    [TestMethod]
    public void Diagnosis_accepts_only_catalog_actions()
    {
        var catalog = new OptimizationCatalog();
        var json = """
            {"summary":"Sistema valido","findings":[],"recommendations":[{"id":"game-mode","kind":"executableAction","title":"Game Mode","actionId":"gaming.game-mode","evidenceIds":[],"reason":"Riduce attività concorrenti"}],"consentQuestion":"Apply selected actions?"}
            """;

        var result = LlmClient.ParseDiagnosis(json, catalog);

        Assert.AreEqual("gaming.game-mode", result.Recommendations.Single().ActionId);
    }

    [TestMethod]
    public void Diagnosis_rejects_unknown_actions()
    {
        var json = """
            {"summary":"Sistema valido","findings":[],"recommendations":[{"id":"bad","kind":"executableAction","title":"Bad","actionId":"run.powershell","evidenceIds":[],"reason":"Esegui script"}],"consentQuestion":"Apply selected actions?"}
            """;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LlmClient.ParseDiagnosis(json, new OptimizationCatalog()));
    }

    [TestMethod]
    public void Diagnosis_rejects_fabricated_evidence()
    {
        var facts = new Dictionary<string, string> { ["gaming:Game Mode"] = "1" };
        var valid = """
            {"summary":"Checked","findings":[{"title":"Game Mode","evidenceId":"gaming:Game Mode","currentValue":"1","assessment":"Enabled"}],"recommendations":[{"id":"game-mode","kind":"executableAction","title":"Game Mode","actionId":"gaming.game-mode","evidenceIds":["gaming:Game Mode"],"reason":"Matches the goal"}],"consentQuestion":"Apply selected actions?"}
            """;
        var fabricated = valid.Replace("\"currentValue\":\"1\"", "\"currentValue\":\"0\"");
        var unsupportedRecommendation = valid.Replace("\"evidenceIds\":[\"gaming:Game Mode\"]", "\"evidenceIds\":[\"missing\"]");

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
    public void Dynamic_plan_accepts_all_review_kinds_but_only_registered_execution()
    {
        var facts = new Dictionary<string, string> { ["system:cpu"] = "CPU" };
        var json = """
            {"summary":"Checked","findings":[],"recommendations":[
              {"id":"action","kind":"executableAction","title":"Game Mode","evidenceIds":["system:cpu"],"reason":"Relevant","actionId":"gaming.game-mode"},
              {"id":"manual","kind":"manualGuidance","title":"Check settings","evidenceIds":["system:cpu"],"reason":"User review"},
              {"id":"script","kind":"scriptArtifact","title":"Review script","evidenceIds":["system:cpu"],"reason":"Optional manual aid","scriptLanguage":"powershell","script":"Get-Process"},
              {"id":"resource","kind":"externalResource","title":"Known config","evidenceIds":["system:cpu"],"reason":"Verified artifact","resourceId":"cfg.known"},
              {"id":"update","kind":"updateNotice","title":"Driver update","evidenceIds":["system:cpu"],"reason":"Official notice","updateId":"driver.known"}
            ],"consentQuestion":"Apply selected registered actions?"}
            """;

        var resource = new ExternalArtifactDefinition("cfg.known", ExternalArtifactKind.Cfg, RiskLevel.Medium, false,
            "https://example.com/game.cfg", new string('a', 64), "text/plain", 4, ["Game"], ["1.0"],
            ArtifactDestinationMode.AppResolved, ".cfg", "Copy current file", "Compare SHA-256");
        var update = new UpdateNoticeDefinition("driver.known", UpdateComponentKind.GpuDriver, "NVIDIA", "GPU",
            "1.0", "2.0", "https://www.nvidia.com/en-us/drivers/", UpdateComparisonStatus.UpdateAvailable, "Newer");
        var result = LlmClient.ParseDiagnosis(json, new OptimizationCatalog(), facts,
            new Dictionary<string, ExternalArtifactDefinition> { [resource.Id] = resource },
            new Dictionary<string, UpdateNoticeDefinition> { [update.Id] = update });

        Assert.HasCount(5, result.Recommendations);
        Assert.AreEqual(PlanRecommendationKind.ScriptArtifact, result.Recommendations[2].Kind);
        StringAssert.Contains(result.Recommendations[2].ReviewWarnings[0], "cannot execute");
        Assert.AreEqual(resource.SourceUrl, result.Recommendations[3].SourceReferences.Single().Url);
        Assert.AreEqual(update.OfficialUrl, result.Recommendations[4].SourceReferences.Single().Url);
    }

    [TestMethod]
    public void Script_review_flags_sensitive_commands_without_executing_them()
    {
        var warnings = ScriptReviewService.Analyze("powershell", "Set-MpPreference -DisableRealtimeMonitoring $true");

        Assert.IsGreaterThanOrEqualTo(2, warnings.Count);
        Assert.IsTrue(warnings.Any(warning => warning.Contains("Defender", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Risk_profiles_preselect_registered_actions_deterministically()
    {
        var catalog = new OptimizationCatalog();
        var low = new PlanRecommendation { Kind = PlanRecommendationKind.ExecutableAction, ActionId = "gaming.game-mode" };
        var high = new PlanRecommendation { Kind = PlanRecommendationKind.ExecutableAction, ActionId = "graphics.tdr-default" };
        var script = new PlanRecommendation { Kind = PlanRecommendationKind.ScriptArtifact };

        Assert.IsTrue(PlanSelectionPolicy.Evaluate(low, RiskProfile.Safe, catalog).Preselected);
        Assert.IsFalse(PlanSelectionPolicy.Evaluate(high, RiskProfile.Balanced, catalog).Preselected);
        Assert.IsTrue(PlanSelectionPolicy.Evaluate(high, RiskProfile.Aggressive, catalog).RequiresSeparateConfirmation);
        Assert.AreEqual(PolicyDisposition.ManualOnly,
            PlanSelectionPolicy.Evaluate(script, RiskProfile.Aggressive, catalog).Disposition);
    }

    [TestMethod]
    public void User_measurements_are_bounded_and_always_labelled_user_provided()
    {
        var goals = new TuningGoals
        {
            GameContext = new GameContext { Game = "Fortnite", Width = 2560, Height = 1440, RefreshRateHz = 360 },
            PerformanceInput = new UserPerformanceInput { UserProvided = false, AverageFps = 240, PacketLossPercent = 0.5 }
        };

        goals.Validate();

        Assert.IsTrue(goals.PerformanceInput.UserProvided);
        goals.PerformanceInput.PacketLossPercent = 101;
        Assert.ThrowsExactly<InvalidOperationException>(goals.Validate);
    }

    [TestMethod]
    public void Capability_registry_separates_complete_metadata_from_reversible_execution()
    {
        var catalog = new OptimizationCatalog();

        Assert.HasCount(25, catalog.All);
        Assert.HasCount(25, catalog.Definitions.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(catalog.All.All(action => action is IReversibleAction));
        Assert.IsTrue(catalog.Definitions.All(definition =>
        {
            definition.Validate();
            return definition.SchemaVersion == 1 && definition.SupportedWindowsBuilds.Count > 0 &&
                definition.SupportedHardware.Count > 0 && definition.EvidenceRequirements.Count > 0 &&
                definition.Sources.Count > 0 && definition.SideEffects.Count > 0;
        }));
    }

    [TestMethod]
    public void Capability_policy_blocks_forbidden_targets_and_confirms_high_risk_actions()
    {
        var catalog = new OptimizationCatalog();
        var high = ActionPolicy.Evaluate(catalog.Get("graphics.tdr-default").Definition, RiskProfile.Aggressive);
        var forbidden = catalog.Get("gaming.game-mode").Definition with { Id = "gaming.hpet-force" };

        Assert.AreEqual(PolicyDisposition.ConfirmationRequired, high.Disposition);
        Assert.IsTrue(high.Preselected);
        Assert.IsTrue(high.RequiresSeparateConfirmation);
        Assert.AreEqual(PolicyDisposition.Blocked, ActionPolicy.Evaluate(forbidden, RiskProfile.Aggressive).Disposition);
    }

    [TestMethod]
    public void Capability_state_families_are_registered_without_forbidden_performance_targets()
    {
        var ids = new OptimizationCatalog().Definitions.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var id in new[]
        {
            "system.high-performance", "system.balanced",
            "gaming.game-mode", "gaming.game-mode-off", "gaming.game-mode-default",
            "gaming.hags", "gaming.hags-off", "gaming.hags-default",
            "gaming.game-dvr-on", "gaming.game-dvr-off", "gaming.game-dvr-default",
            "gaming.app-capture-on", "gaming.app-capture-off", "gaming.app-capture-default",
            "system.visual-effects", "system.visual-effects-default", "system.visual-effects-appearance",
            "system.bcd-timer-default", "system.bcd-resource-default"
        }) Assert.Contains(id, ids);
        Assert.IsFalse(ids.Any(id => new[] { "defender", "firewall", "uac", "hpet" }
            .Any(forbidden => id.Contains(forbidden, StringComparison.OrdinalIgnoreCase))));
    }

    [TestMethod]
    public void External_artifacts_require_exact_source_hash_type_and_bounded_destination()
    {
        var payload = Encoding.UTF8.GetBytes("safe=true\n");
        var source = new Uri("https://downloads.example.org/game.cfg");
        var definition = new ExternalArtifactDefinition("cfg.verified", ExternalArtifactKind.Cfg,
            RiskLevel.Medium, false, source.AbsoluteUri, Convert.ToHexString(SHA256.HashData(payload)),
            "text/plain", payload.Length, ["Example Game"], ["1.2"], ArtifactDestinationMode.AppResolved,
            ".cfg", "Copy the previous file", "Compare the installed SHA-256");
        var root = Path.Combine(Path.GetTempPath(), "neurotune-artifact-root");
        var destination = Path.Combine(root, "game.cfg");

        Assert.AreEqual(Path.GetFullPath(destination), ExternalArtifactValidator.Validate(
            definition, source, "text/plain; charset=utf-8", payload, root, destination));
        Assert.ThrowsExactly<InvalidOperationException>(() => ExternalArtifactValidator.Validate(
            definition, new Uri("https://mirror.example.org/game.cfg"), "text/plain", payload, root, destination));
        Assert.ThrowsExactly<InvalidOperationException>(() => ExternalArtifactValidator.Validate(
            definition, source, "text/plain", Encoding.UTF8.GetBytes("changed"), root, destination));
        Assert.ThrowsExactly<InvalidOperationException>(() => ExternalArtifactValidator.Validate(
            definition, source, "application/x-msdownload", payload, root, destination));
        Assert.ThrowsExactly<InvalidOperationException>(() => ExternalArtifactValidator.Validate(
            definition, source, "text/plain", payload, root, Path.Combine(root, "..", "escape.cfg")));
    }

    [TestMethod]
    public void External_catalogs_are_empty_until_primebuild_approves_each_entry()
    {
        Assert.IsEmpty(new ExternalArtifactCatalog().All);
        Assert.IsEmpty(new ExternalApplicationCatalog().All);
    }

    [TestMethod]
    public void Verified_text_artifact_applies_and_rolls_back_the_exact_previous_file()
    {
        var root = Path.Combine(Path.GetTempPath(), $"neurotune-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var destination = Path.Combine(root, "game.cfg");
            File.WriteAllText(destination, "old=true\n");
            var payload = Encoding.UTF8.GetBytes("new=true\n");
            var source = new Uri("https://downloads.example.org/game.cfg");
            var definition = new ExternalArtifactDefinition("cfg.transaction", ExternalArtifactKind.Cfg,
                RiskLevel.Medium, false, source.AbsoluteUri, Convert.ToHexString(SHA256.HashData(payload)),
                "text/plain", payload.Length, ["Example Game"], ["1.2"], ArtifactDestinationMode.AppResolved,
                ".cfg", "Capture exact existing bytes", "Compare installed SHA-256");
            var action = new VerifiedArtifactAction(definition, source, "text/plain", payload, root, destination);

            var captured = action.Capture();
            action.Apply();
            Assert.IsTrue(action.Verify());
            Assert.AreEqual("new=true\n", File.ReadAllText(destination));
            action.Restore(captured);
            Assert.AreEqual("old=true\n", File.ReadAllText(destination));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Update_advisor_claims_updates_only_for_exact_official_numeric_records()
    {
        var advisor = new OfficialUpdateAdvisor([new OfficialUpdateRecord(
            UpdateComponentKind.GpuDriver, "NVIDIA", "Example GPU", "2.1.0",
            "https://www.nvidia.com/en-us/drivers/")]);

        var exact = advisor.AnalyzeComponents([
            new(UpdateComponentKind.GpuDriver, "NVIDIA", "Example GPU", "2.0.0")]).Single();
        var unknown = advisor.AnalyzeComponents([
            new(UpdateComponentKind.GpuDriver, "NVIDIA", "Different GPU", "1.0")]).Single();
        var bios = advisor.AnalyzeComponents([
            new(UpdateComponentKind.Bios, "MSI", "Board", "E7C37AMS.1Q0")]).Single();

        Assert.AreEqual(UpdateComparisonStatus.UpdateAvailable, exact.Status);
        Assert.AreEqual("2.1.0", exact.LatestVersion);
        Assert.AreEqual(UpdateComparisonStatus.ComparisonUnavailable, unknown.Status);
        Assert.AreEqual("", unknown.LatestVersion);
        Assert.AreEqual(UpdateComparisonStatus.ComparisonUnavailable, bios.Status);
        Assert.IsTrue(new Uri(exact.OfficialUrl).Host.EndsWith("nvidia.com", StringComparison.OrdinalIgnoreCase));
        Assert.ThrowsExactly<InvalidOperationException>(() => new OfficialUpdateAdvisor([new OfficialUpdateRecord(
            UpdateComponentKind.GpuDriver, "NVIDIA", "Example GPU", "3.0", "https://evil.example/driver")]));
    }

    [TestMethod]
    public void Update_version_comparison_is_deterministic_and_rejects_ambiguous_vendor_formats()
    {
        Assert.IsTrue(OfficialUpdateAdvisor.TryCompareVersions("31.0.15.5200", "31.0.15.6000", out var older));
        Assert.IsLessThan(0, older);
        Assert.IsTrue(OfficialUpdateAdvisor.TryCompareVersions("2.1", "2.1.0", out var equal));
        Assert.AreEqual(0, equal);
        Assert.IsFalse(OfficialUpdateAdvisor.TryCompareVersions("E7C37AMS.1Q0", "E7C37AMS.1R0", out _));
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
