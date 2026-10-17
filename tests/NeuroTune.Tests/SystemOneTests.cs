using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NeuroTune.Tests;

[TestClass]
public sealed class SystemOneTests
{
    [TestMethod]
    public void Typed_local_advice_cannot_invent_evidence_or_an_executable_choice()
    {
        const string output = """{"answers":{"domain":{"type":"choice","status":"ok","choice":"memory","probabilities":{"memory":0.8,"unknown":0.2}}}}""";
        var result = SystemOneService.ParseAdvisory(output, "analysis", ["measurement:hard_faults", "network:rss", "hardware:Memory pressure sample"], 1.2);
        Assert.AreEqual("memory", result.Domain);
        CollectionAssert.AreEqual(new[] { "measurement:hard_faults", "hardware:Memory pressure sample" }, result.EvidenceIds.ToArray());
        StringAssert.Contains(result.Detail, "not causal proof");
        Assert.ThrowsExactly<InvalidOperationException>(() => SystemOneService.ParseAdvisory(output.Replace("\"choice\":\"memory\"", "\"choice\":\"run_scewin\""), "analysis", [], 0));
        Assert.ThrowsExactly<InvalidOperationException>(() => SystemOneService.ParseAdvisory(output.Replace("0.8", "1.8"), "analysis", [], 0));
        Assert.ThrowsExactly<ArgumentException>(() => SystemOneService.ValidateOptions(new(true, "remote-model", "auto")));
        Assert.ThrowsExactly<ArgumentException>(() => SystemOneService.ValidateOptions(new(true, "4b", "shell")));
    }
    [TestMethod]
    public async Task Cloud_classifier_is_explicit_bounded_and_never_invents_calibrated_scores()
    {
        using var request = LlmClient.CreateTopicClassificationRequest("vendor/small-model", "separate-key", "observed data");
        Assert.AreEqual("https://openrouter.ai/api/v1/chat/completions", request.RequestUri!.AbsoluteUri);
        Assert.AreEqual("separate-key", request.Headers.Authorization!.Parameter);
        using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        Assert.AreEqual(256, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.IsFalse(body.RootElement.GetProperty("provider").GetProperty("allow_fallbacks").GetBoolean());
        var advisory = SystemOneService.ParseCloudAdvisory("""{"domain":"memory","score":1,"actionIds":["run"]}""", "analysis", ["memory:pressure", "network:rss"], 0.5, "vendor/small-model");
        Assert.IsNull(advisory.Score);
        var redacted = SystemOneService.CloudContext($"File C:\\Users\\{Environment.UserName}\\private\\capture.etl on {Environment.MachineName}");
        Assert.IsFalse(redacted.Contains("capture.etl", StringComparison.Ordinal));
        StringAssert.Contains(redacted, "[local-path]");
        CollectionAssert.AreEqual(new[] { "memory:pressure" }, advisory.EvidenceIds.ToArray());
        StringAssert.Contains(advisory.Detail, "OpenRouter API");
        Assert.ThrowsExactly<InvalidOperationException>(() => SystemOneService.ParseCloudAdvisory("""{"domain":"execute"}""", "analysis", [], 0, "test"));
        Assert.AreEqual("uncertain", SystemOneService.ParseCloudAdvisory("""{"domain":"unknown"}""", "analysis", [], 0, "test").Status);
        Assert.ThrowsExactly<ArgumentException>(() => SystemOneService.ValidateOptions(new(false, Source: "https://fake.example")));
    }
    [TestMethod]
    public async Task Free_cloud_mode_locks_the_router_and_caps_all_provider_prices_at_zero()
    {
        var options = new SystemOneOptions(true, Source: "openRouterFree", Model: SystemOneService.FreeRouterModel, CloudConsent: true);
        SystemOneService.ValidateOptions(options);
        Assert.IsFalse(new SystemOneOptions(false).Enabled);
        Assert.IsFalse(new SystemOneOptions(false).CloudConsent);
        foreach (var model in new[] { "", "openrouter/auto", "openai/gpt-4o-mini", "vendor/model:free", "openrouter/free " })
        {
            Assert.ThrowsExactly<ArgumentException>(() => SystemOneService.ValidateOptions(options with { Model = model }));
            Assert.ThrowsExactly<ArgumentException>(() => LlmClient.CreateTopicClassificationRequest(model, "separate-key", "sample", freeOnly: true));
        }
        using var request = LlmClient.CreateTopicClassificationRequest(options.Model, "separate-key", "sample", freeOnly: true);
        Assert.AreEqual("https://openrouter.ai/api/v1/chat/completions", request.RequestUri!.AbsoluteUri);
        Assert.AreEqual("separate-key", request.Headers.Authorization!.Parameter);
        using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        Assert.AreEqual("openrouter/free", body.RootElement.GetProperty("model").GetString());
        Assert.IsFalse(body.RootElement.TryGetProperty("models", out _));
        Assert.IsFalse(body.RootElement.TryGetProperty("route", out _));
        var provider = body.RootElement.GetProperty("provider");
        Assert.IsFalse(provider.GetProperty("allow_fallbacks").GetBoolean());
        Assert.IsTrue(provider.GetProperty("require_parameters").GetBoolean());
        foreach (var price in new[] { "prompt", "completion", "request", "image" })
            Assert.AreEqual(0, provider.GetProperty("max_price").GetProperty(price).GetInt32());
        Assert.AreEqual(256, body.RootElement.GetProperty("max_tokens").GetInt32());
        var advisory = SystemOneService.ParseCloudAdvisory("""{"domain":"memory"}""", "analysis", ["memory:pressure"], 0.5, options.Model);
        Assert.IsNull(advisory.Score);
        StringAssert.Contains(advisory.Detail, "zero-price free router");
    }
    [TestMethod]
    public void Stored_cloud_settings_without_separate_consent_stay_disabled_and_preserve_the_draft()
    {
        var legacy = SystemOneService.ParseOptions("""{"Enabled":true,"Source":"openRouter","Model":"vendor/paid-model"}""");
        Assert.IsFalse(legacy.Enabled);
        Assert.IsFalse(legacy.CloudConsent);
        Assert.AreEqual("openRouter", legacy.Source);
        Assert.AreEqual("vendor/paid-model", legacy.Model);
        Assert.IsTrue(SystemOneService.ParseOptions("""{"Enabled":true,"Source":"local"}""").Enabled);
        var free = SystemOneService.ParseOptions("""{"Enabled":true,"Source":"openRouterFree","Model":"openrouter/free","CloudConsent":true}""");
        Assert.IsTrue(free.Enabled);
        Assert.ThrowsExactly<ArgumentException>(() => SystemOneService.ParseOptions("""{"Enabled":false,"Source":"openRouterFree","Model":"vendor/paid-model"}"""));
        Assert.IsFalse(SystemOneService.ParseOptions("{}").Enabled);
    }
    [TestMethod]
    public async Task Both_cloud_sources_require_separate_consent_and_cannot_install_a_local_runtime()
    {
        foreach (var source in new[] { "openRouter", "openRouterFree" })
        {
            var options = new SystemOneOptions(true, Source: source, Model: source == "openRouterFree" ? SystemOneService.FreeRouterModel : "vendor/model");
            Assert.ThrowsExactly<ArgumentException>(() => SystemOneService.ValidateOptions(options));
            // Validation must reject before saving credentials/configuration or making a network request.
            Assert.ThrowsExactly<ArgumentException>(() => SystemOneService.Configure(new SystemOneConfigureRequest(options, "not-a-real-key")));
            SystemOneService.ValidateOptions(options with { Enabled = false });
            SystemOneService.ValidateOptions(options with { CloudConsent = true });
            await Assert.ThrowsAsync<InvalidOperationException>(() => SystemOneService.InstallAsync(options with { CloudConsent = true }));
        }
        SystemOneService.ValidateOptions(new(false, Source: "openRouter", Model: "vendor/paid-model"));
        using var paid = LlmClient.CreateTopicClassificationRequest("vendor/paid-model", "separate-key", "sample");
        using var body = System.Text.Json.JsonDocument.Parse(await paid.Content!.ReadAsStringAsync());
        Assert.IsFalse(body.RootElement.GetProperty("provider").TryGetProperty("max_price", out _));
    }
    [TestMethod]
    public void Auxiliary_credentials_are_encrypted_and_have_a_distinct_key_identity()
    {
        Assert.AreNotEqual(SystemOneService.OpenRouterCredentialId, new UserSettings { Provider = LlmProvider.OpenRouter }.CredentialId);
        var name = "test-auxiliary-" + Guid.NewGuid().ToString("N"); var settings = new SettingsService();
        try
        {
            settings.SaveSecret(name, "test-secret-not-a-real-key");
            Assert.AreEqual("test-secret-not-a-real-key", settings.LoadSecret(name));
            Assert.IsFalse(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(SettingsService.DataDirectory, name + ".key"))).Contains("test-secret-not-a-real-key", StringComparison.Ordinal));
            settings.DeleteSecret(name); Assert.IsNull(settings.LoadSecret(name));
        }
        finally { settings.DeleteSecret(name); }
    }
    [TestMethod]
    public void Optional_archives_cannot_escape_the_managed_directory()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "neurotune-worker-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var zip = Path.Combine(temporary, "bad.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) using (var writer = new StreamWriter(archive.CreateEntry("../escape.py").Open())) writer.Write("not executed");
            Assert.ThrowsExactly<IOException>(() => SystemOneService.ExtractZip(zip, Path.Combine(temporary, "managed")));
            Assert.IsFalse(File.Exists(Path.Combine(temporary, "escape.py")));
        }
        finally { Directory.Delete(temporary, true); }
    }
    [TestMethod]
    public async Task Native_worker_roundtrips_unicode_without_admin_or_parent_secret_environment_or_refuses_unavailable_token()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var script = "[Console]::InputEncoding=[Text.Encoding]::UTF8; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $v=[Console]::In.ReadToEnd(); [Console]::Write($v); $p=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()); [Console]::Write('|admin='+$p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator).ToString().ToLower()+'|secret='+$env:NEUROTUNE_TEST_SECRET)";
        Environment.SetEnvironmentVariable("NEUROTUNE_TEST_SECRET", "not-for-worker");
        try
        {
            Console.WriteLine($"Interactive desktop={Environment.UserInteractive}; administrator={LogService.IsAdministrator()}");
            var execution = OptionalWorkerProcess.RunAsync(Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], Path.GetTempPath(), "hello àè世界",
                new Dictionary<string, string> { ["SystemRoot"] = windows, ["WINDIR"] = windows }, TimeSpan.FromSeconds(20), null, CancellationToken.None);
            if (WorkerTokenUnavailable())
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
                Assert.AreEqual(OptionalWorkerProcess.TokenUnavailable, error.Message);
            }
            else Assert.AreEqual("hello àè世界|admin=false|secret=", await execution);
        }
        finally { Environment.SetEnvironmentVariable("NEUROTUNE_TEST_SECRET", null); }
        Assert.AreEqual("\"C:\\space folder\\\\\"", OptionalWorkerProcess.Quote("C:\\space folder\\"));
    }
    [TestMethod]
    public void Main_comparison_explanation_is_narrative_only_never_a_new_decision()
    {
        Assert.AreEqual("The result is inconclusive.", LlmClient.ParseComparisonExplanation("""{"summary":"The result is inconclusive.","recommendation":"keep","actionIds":["execute"]}"""));
        Assert.ThrowsExactly<InvalidOperationException>(() => LlmClient.ParseComparisonExplanation("""{"actionIds":["execute"]}"""));
        Assert.ThrowsExactly<InvalidOperationException>(() => LlmClient.ParseComparisonExplanation("""{"summary":""}"""));
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Optional_worker_deadline_stops_its_process_tree_or_refuses_unavailable_token(bool inheritHostToken)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var script = "Start-Sleep -Seconds 30";
        var started = System.Diagnostics.Stopwatch.StartNew();
        var execution = OptionalWorkerProcess.RunAsync(Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], Path.GetTempPath(), "",
            new Dictionary<string, string> { ["SystemRoot"] = windows }, TimeSpan.FromMilliseconds(700), null, CancellationToken.None, inheritHostToken: inheritHostToken);
        if (!inheritHostToken && WorkerTokenUnavailable())
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
            Assert.AreEqual(OptionalWorkerProcess.TokenUnavailable, error.Message);
        }
        else await Assert.ThrowsAsync<OperationCanceledException>(() => execution);
        Assert.IsTrue(started.Elapsed < TimeSpan.FromSeconds(10));
    }
    private static bool WorkerTokenUnavailable()
    {
        if (!PiAgentBridgeTests.CanStartHelpers) return true;
        try
        {
            using var token = OptionalWorkerProcess.NonAdminToken();
            if (token is not null)
            {
                using var identity = new System.Security.Principal.WindowsIdentity(token.DangerousGetHandle());
                Assert.IsFalse(new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
            }
            return false;
        }
        catch (InvalidOperationException error) when (error.Message == OptionalWorkerProcess.TokenUnavailable)
        {
            Console.WriteLine("No verified UAC token: checking refusal before process creation, not desktop inference.");
            return true;
        }
    }
}
