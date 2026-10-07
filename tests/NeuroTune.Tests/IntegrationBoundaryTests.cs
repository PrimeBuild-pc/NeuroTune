using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NeuroTune.Tests;

[TestClass]
public sealed class IntegrationBoundaryTests
{
    [TestMethod]
    public void Chatgpt_id_tokens_require_a_trusted_rsa_signature_identity_audience_expiry_and_nonce()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        using var jwks = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            keys = new[] { new {
            kid = "test", kty = "RSA", alg = "RS256", use = "sig", n = B64(parameters.Modulus!), e = B64(parameters.Exponent!)
        } }
        }));
        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["iss"] = "https://auth.openai.com",
            ["aud"] = "oaiapp_test",
            ["sub"] = "subject-1",
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            ["nonce"] = "nonce-1",
            ["email"] = "label@example.com"
        };
        string Sign(Dictionary<string, object> body, string algorithm = "RS256")
        {
            var data = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = algorithm, kid = "test" })) + "." + B64(JsonSerializer.SerializeToUtf8Bytes(body));
            return data + "." + B64(rsa.SignData(Encoding.ASCII.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        var valid = Sign(claims);
        Assert.AreEqual("subject-1", ChatGptOAuthService.ValidateIdToken(valid, jwks.RootElement, "oaiapp_test", "nonce-1", now).Subject);
        Assert.ThrowsExactly<InvalidOperationException>(() => ChatGptOAuthService.ValidateIdToken(valid, jwks.RootElement, "oaiapp_other", "nonce-1", now));
        Assert.ThrowsExactly<InvalidOperationException>(() => ChatGptOAuthService.ValidateIdToken(valid, jwks.RootElement, "oaiapp_test", "wrong", now));
        Assert.ThrowsExactly<InvalidOperationException>(() => ChatGptOAuthService.ValidateIdToken(Sign(claims, "none"), jwks.RootElement, "oaiapp_test", "nonce-1", now));
        foreach (var change in new[] { new KeyValuePair<string, object>("iss", "https://evil.example"), new("exp", now.AddSeconds(-1).ToUnixTimeSeconds()), new("sub", ""), new("azp", "oaiapp_other"), new("nbf", now.AddMinutes(10).ToUnixTimeSeconds()) })
        {
            var bad = new Dictionary<string, object>(claims) { [change.Key] = change.Value };
            Assert.ThrowsExactly<InvalidOperationException>(() => ChatGptOAuthService.ValidateIdToken(Sign(bad), jwks.RootElement, "oaiapp_test", "nonce-1", now));
        }
        using var untrusted = RSA.Create(2048);
        var parts = valid.Split('.');
        var forged = parts[0] + "." + parts[1] + "." + B64(untrusted.SignData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.ThrowsExactly<InvalidOperationException>(() => ChatGptOAuthService.ValidateIdToken(forged, jwks.RootElement, "oaiapp_test", "nonce-1", now));
    }

    [TestMethod]
    public void Chatgpt_registration_and_permissions_do_not_accept_codex_ids_or_identity_only_scopes()
    {
        Assert.AreEqual("oaiapp_new", ChatGptOAuthService.ValidateCallbackClient(null, "oaiapp_new"));
        Assert.AreEqual("oaiapp_saved", ChatGptOAuthService.ValidateCallbackClient("oaiapp_saved", null));
        foreach (var invalid in new[] { "dynamic_agent_client", "codex_client", "oaiapp_../path", "" })
            Assert.ThrowsExactly<InvalidOperationException>(() => ChatGptOAuthService.ValidateCallbackClient(null, invalid));
        Assert.ThrowsExactly<InvalidOperationException>(() => ChatGptOAuthService.ValidateCallbackClient("oaiapp_saved", "oaiapp_other"));
        Assert.IsFalse(ChatGptOAuthService.PlanEnabled(["openid", "email"]));
        Assert.IsFalse(ChatGptOAuthService.PlanEnabled(["chatgpt.tokens.use.direct"]));
        Assert.IsTrue(ChatGptOAuthService.PlanEnabled(["chatgpt.tokens.use.direct", "resource.invoke"]));
        Assert.IsTrue(ChatGptOAuthService.IsTerminalRefreshError("refresh_token_reused"));
        Assert.IsFalse(ChatGptOAuthService.IsTerminalRefreshError("temporarily_unavailable"));
        Assert.IsFalse(ChatGptOAuthService.IsTerminalRefreshError("invalid_client"));
    }

    [TestMethod]
    public async Task Chatgpt_requests_pin_the_official_route_and_only_supported_subscription_fields()
    {
        var settings = LlmClient.Defaults(LlmProvider.ChatGpt);
        settings.BaseUrl = "https://evil.example"; settings.Model = "available-account-model";
        using var request = LlmClient.CreateChatGptRequest(settings, "test-token", "Return JSON");
        Assert.AreEqual("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
        Assert.AreEqual("Bearer", request.Headers.Authorization!.Scheme);
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        Assert.IsFalse(body.RootElement.GetProperty("store").GetBoolean());
        Assert.IsTrue(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.AreEqual(JsonValueKind.Array, body.RootElement.GetProperty("input").ValueKind);
        Assert.AreEqual(4, body.RootElement.EnumerateObject().Count());
        Assert.IsFalse(body.RootElement.TryGetProperty("temperature", out _));
        Assert.IsFalse(body.RootElement.TryGetProperty("max_output_tokens", out _));
        CollectionAssert.AreEqual(new[] { "account-first", "account-second" }, LlmClient.ParseChatGptModels("""
            {"models":[{"slug":"account-first","display_name":"First","visibility":"list"},{"slug":"hidden","visibility":"hide"},{"slug":"account-second","visibility":"list"}]}
            """).Keys.ToArray());
    }

    [TestMethod]
    public async Task Model_catalog_metadata_can_exceed_inference_limits_without_unbounding_responses()
    {
        var body = JsonSerializer.Serialize(new { models = new[] { new { slug = "available", visibility = "list", display_name = "Available", metadata = new string('x', 400_000) } } });
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => LlmClient.ReadLimitedAsync(response, CancellationToken.None));
        using var modelsResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) };
        var catalog = await LlmClient.ReadLimitedAsync(modelsResponse, CancellationToken.None, LlmClient.MaxModelCatalogCharacters);
        Assert.AreEqual("Available", LlmClient.ParseChatGptModels(catalog)["available"]);
        using var oversized = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(new string('x', LlmClient.MaxModelCatalogCharacters + 1)) };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => LlmClient.ReadLimitedAsync(oversized, CancellationToken.None, LlmClient.MaxModelCatalogCharacters));
    }

    [TestMethod]
    public async Task Chatgpt_streams_discard_partial_failed_and_tool_outputs_even_after_text_arrives()
    {
        const string delta = "data: {\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"ok\\\":true}\"}\r\n\r\n";
        const string complete = "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}\n\n";
        Assert.AreEqual("{\"ok\":true}", await LlmClient.ReadChatGptStreamAsync(new StringReader(delta + complete)));
        foreach (var suffix in new[] { "", "data: [DONE]\n\n", "data: {\"type\":\"response.incomplete\"}\n\n", "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[{\"type\":\"function_call\"}]}}\n\n" })
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => LlmClient.ReadChatGptStreamAsync(new StringReader(delta + suffix)));
        var failed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => LlmClient.ReadChatGptStreamAsync(new StringReader(delta +
            "data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"subscription_sharing_usage_limit_exceeded\"}}}\n\n")));
        StringAssert.Contains(failed.Message, "subscription_sharing_usage_limit_exceeded");
        StringAssert.Contains(failed.Message, "usage limit has been reached");
        StringAssert.Contains(failed.Message, "Settings > Usage");
        StringAssert.Contains(failed.Message, "No API-key billing fallback");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => LlmClient.ReadChatGptStreamAsync(new StringReader(new string('x', 1_000_001))));
    }

    [TestMethod]
    public void Scewin_parser_reads_current_markers_never_defaults_and_omits_secrets()
    {
        var parsed = ScewinService.Parse("""
            Setup Question = C-states
            Token =1234 // Do NOT change this line
            BIOS Default = [00]Enabled
            Options = [00]Enabled
                     *[01]Disabled // Move * to the desired option
            Setup Question = PBO
            BIOS Default = [00]Auto
            Options = [00]Auto
            Setup Question = CPU limit
            Value = <7>
            Setup Question = Admin Password
            Value = <abcd>
            Setup Question = Ambiguous
            Options = *[00]Yes
                      *[01]No
            """);
        Assert.HasCount(4, parsed.Settings);
        Assert.AreEqual("[01]Disabled", parsed.Settings[0].CurrentValue);
        Assert.AreEqual("1234", parsed.Settings[0].Token);
        Assert.IsNull(parsed.Settings[1].CurrentValue);
        Assert.AreEqual("<7>", parsed.Settings[2].CurrentValue);
        Assert.IsNull(parsed.Settings[3].CurrentValue);
        Assert.AreEqual(1, parsed.Omitted);
        Assert.DoesNotContain("abcd", JsonSerializer.Serialize(parsed.Settings));
        Assert.ThrowsExactly<ArgumentException>(() => ScewinService.Parse(new string('x', ScewinService.MaximumExportBytes + 1)));
        Assert.ThrowsExactly<ArgumentException>(() => ScewinService.Parse("Setup Question = " + new string('x', 513)));
        var imported = ScewinService.Import(new("Setup Question = Setting\nOptions = *[00]Enabled", true));
        StringAssert.Contains(imported.Source, "not verified");
        Assert.HasCount(0, imported.ToolFiles);
        Assert.ThrowsExactly<InvalidOperationException>(() => ScewinService.Import(new("text", false)));
    }

    [TestMethod]
    public void Simulated_MSI_setup_is_parsed_offline_without_promoting_defaults_or_executing_drivers()
    {
        var report = ScewinService.Import(new("""
            // SIMULATED MSI export for tests, not physical-machine observations.
            Setup Question = Precision Boost Overdrive
            BIOS Default = [00]Auto
            Options = [00]Auto
                      *[01]Disabled
            Setup Question = Global C-state Control
            BIOS Default = [00]Enabled
            Options = [00]Enabled
            Setup Question = A-XMP
            Options = *[01]Profile 1
            Setup Question = CPU voltage offset
            Value = <0x0064>
            Setup Question = Ambiguous current state
            Options = *[00]Enabled
                      *[01]Disabled
            Setup Question = Admin Password
            Value = <abcd>
            """, true));
        Assert.HasCount(5, report.Settings);
        Assert.AreEqual("[01]Disabled", report.Settings[0].CurrentValue);
        Assert.IsNull(report.Settings[1].CurrentValue, "A BIOS default is not a current observation.");
        Assert.AreEqual("[01]Profile 1", report.Settings[2].CurrentValue);
        Assert.AreEqual("<0x0064>", report.Settings[3].CurrentValue);
        Assert.IsNull(report.Settings[4].CurrentValue);
        Assert.AreEqual(1, report.SensitiveQuestionsOmitted);
        Assert.IsEmpty(report.ToolFiles, "Offline parsing must not depend on a privileged package or PawnIO.");
        StringAssert.Contains(report.Source, "not verified");
        Assert.IsTrue(report.Notes.Any(note => note.Contains("No tool or driver was executed", StringComparison.Ordinal)));
        Assert.DoesNotContain("abcd", JsonSerializer.Serialize(report));
    }

    [TestMethod]
    public async Task Scewin_export_requires_fresh_hash_approval_and_has_no_write_or_force_argument()
    {
        CollectionAssert.AreEqual(new[] { "/o", "/s", "C:\\temporary\\nvram.txt" }, ScewinService.ExportArguments("C:\\temporary\\nvram.txt"));
        var hashes = new Dictionary<string, string> { ["SCEWIN_64.exe"] = new('a', 64), ["amifldrv64.sys"] = new('b', 64), ["amigendrv64.sys"] = new('c', 64) };
        ScewinService.ValidateHashes(hashes);
        hashes["Import.bat"] = new('d', 64);
        Assert.ThrowsExactly<InvalidOperationException>(() => ScewinService.ValidateHashes(hashes));
        Assert.ThrowsExactly<InvalidOperationException>(() => ScewinService.ValidateHashes(null));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ScewinService.ExportAsync(new("C:\\tools", true, false, [])));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ScewinService.ExportAsync(new("C:\\tools", false, true, [])));
        Assert.ThrowsExactly<ArgumentException>(() => ScewinService.Inspect(@"\\remote\tools"));
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
