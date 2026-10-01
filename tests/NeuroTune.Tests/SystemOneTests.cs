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
    public async Task Native_worker_roundtrips_unicode_without_admin_or_parent_secret_environment()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var script = "[Console]::InputEncoding=[Text.Encoding]::UTF8; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $v=[Console]::In.ReadToEnd(); [Console]::Write($v); $p=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()); [Console]::Write('|admin='+$p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator).ToString().ToLower()+'|secret='+$env:NEUROTUNE_TEST_SECRET)";
        Environment.SetEnvironmentVariable("NEUROTUNE_TEST_SECRET", "not-for-worker");
        try
        {
            var result = await OptionalWorkerProcess.RunAsync(Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], Path.GetTempPath(), "hello àè世界",
                new Dictionary<string, string> { ["SystemRoot"] = windows, ["WINDIR"] = windows }, TimeSpan.FromSeconds(20), null, CancellationToken.None);
            Assert.AreEqual("hello àè世界|admin=false|secret=", result);
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
    public async Task Optional_worker_deadline_stops_its_process_tree()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var script = "Start-Sleep -Seconds 30";
        var started = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() => OptionalWorkerProcess.RunAsync(Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], Path.GetTempPath(), "",
            new Dictionary<string, string> { ["SystemRoot"] = windows }, TimeSpan.FromMilliseconds(700), null, CancellationToken.None));
        Assert.IsTrue(started.Elapsed < TimeSpan.FromSeconds(10));
    }
}
