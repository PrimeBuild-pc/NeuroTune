using System.Net;
using System.Text;
using System.Text.Json;

namespace NeuroTune.Tests;

[TestClass]
public sealed class ClmClientTests
{
    private const string Reply = """{"model":"clm-latest","answers":{"domain":{"type":"choice","choice":"drivers","confidence":0.8,"probabilities":{"memory":0.05,"drivers":0.78,"network":0.05,"power":0.04,"software":0.04,"unknown":0.04}},"specific":{"type":"noul","noul":0.8}},"usage":{"billing_units":1,"input_tokens":40}}""";

    [TestMethod]
    [DataRow("http://127.0.0.1:8700/", true)]
    [DataRow("http://localhost:8700/", true)]
    [DataRow("http://192.168.1.5:8700/", false)]
    [DataRow("https://example.com/", false)]
    [DataRow("http://user:pw@127.0.0.1:8700/", false)]
    [DataRow("ftp://127.0.0.1/", false)]
    public void Only_loopback_http_endpoints_are_accepted(string url, bool expected) =>
        Assert.AreEqual(expected, ClmClient.IsLoopback(new Uri(url)));

    [TestMethod]
    public void A_confident_choice_becomes_an_advisory_with_matching_evidence_only()
    {
        var advisory = ClmClient.ParseAdvisory(Reply, "evidence-analysis", ["driver:0", "network:adapter", "memory:x"], 0.2,
            (id, domain) => domain == "drivers" && id.StartsWith("driver", StringComparison.Ordinal));
        Assert.AreEqual("ok", advisory.Status);
        Assert.AreEqual("drivers", advisory.Domain);
        Assert.AreEqual(0.78, advisory.Score!.Value, 1e-9);
        CollectionAssert.AreEqual(new[] { "driver:0" }, advisory.EvidenceIds.ToArray());
        Assert.Contains("not causal proof", advisory.Detail);
    }

    [TestMethod]
    [DataRow("""{"memory":0.2,"drivers":0.3,"network":0.2,"power":0.1,"software":0.1,"unknown":0.1}""")]
    [DataRow("""{"memory":0.01,"drivers":0.01,"network":0.01,"power":0.01,"software":0.01,"unknown":0.95}""")]
    public void Low_confidence_or_unknown_abstains_without_evidence(string probabilities)
    {
        var json = "{\"answers\":{\"domain\":{\"type\":\"choice\",\"choice\":\"drivers\",\"confidence\":0.1,\"probabilities\":" + probabilities + "},\"specific\":{\"type\":\"noul\",\"noul\":0.9}}}";
        var advisory = ClmClient.ParseAdvisory(json, "analysis", ["driver:0"], 0.1, (_, _) => true);
        Assert.AreEqual("uncertain", advisory.Status);
        Assert.IsNull(advisory.Domain);
        Assert.IsNull(advisory.Score);
        Assert.IsEmpty(advisory.EvidenceIds);
    }

    [TestMethod]
    public void A_vague_observation_abstains_even_with_a_dominant_topic()
    {
        var vague = Reply.Replace("\"noul\":0.8", "\"noul\":0.3");
        var advisory = ClmClient.ParseAdvisory(vague, "analysis", ["driver:0"], 0.1, (_, _) => true);
        Assert.AreEqual("uncertain", advisory.Status);
        Assert.IsEmpty(advisory.EvidenceIds);
    }

    [TestMethod]
    [DataRow("""{"answers":{"domain":{"type":"noul","noul":0.4}}}""")]
    [DataRow("""{"answers":{"domain":{"type":"choice","choice":"x","probabilities":{"memory":0.5,"hacked":0.5}}}}""")]
    [DataRow("""{"answers":{"domain":{"type":"choice","choice":"memory","probabilities":{"memory":0.9,"drivers":0.9,"network":0,"power":0,"software":0,"unknown":0}}}}""")]
    public void Malformed_or_unexpected_answers_are_rejected(string json) =>
        Assert.ThrowsExactly<InvalidOperationException>(() => ClmClient.ParseAdvisory(json, "analysis", [], 0, (_, _) => true));

    [TestMethod]
    public void State_is_rendered_as_bounded_redacted_prose()
    {
        var json = JsonSerializer.Serialize(new { goals = new { Priority = 0 }, sampledEvidence = new Dictionary<string, string> { ["memory:pressure"] = "available=1 GB" }, note = "Sample only" });
        var text = ClmClient.StateText(json);
        Assert.Contains("memory:pressure: available=1 GB", text);
        Assert.DoesNotContain("{", text);
        Assert.IsLessThanOrEqualTo(3_500, ClmClient.StateText(new string('a', 20_000)).Length);
    }

    [TestMethod]
    public void Options_accept_the_local_clm_source_without_cloud_consent_and_reject_other_models()
    {
        SystemOneService.ValidateOptions(new(true, Source: "clm", Model: "clm-latest"));
        SystemOneService.ValidateOptions(new(false, Source: "clm"));
        Assert.ThrowsExactly<ArgumentException>(() => SystemOneService.ValidateOptions(new(false, Source: "clm", Model: "gpt")));
        Assert.ThrowsExactly<ArgumentException>(() => SystemOneService.ValidateOptions(new(true, Source: "openRouterFree", Model: "openrouter/free", CloudConsent: false)));
        var parsed = SystemOneService.ParseOptions("""{"Enabled":true,"Size":"4b","Device":"auto","Source":"clm","Model":"","CloudConsent":false}""");
        Assert.IsTrue(parsed.Enabled);
    }

    [TestMethod]
    public async Task The_request_asks_one_typed_choice_over_the_fixed_topics_on_the_local_server()
    {
        using var listener = new HttpListener();
        var port = FreePort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        string? captured = null;
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            captured = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
            var bytes = Encoding.UTF8.GetBytes(Reply);
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        });
        var previous = Environment.GetEnvironmentVariable("NEUROTUNE_CLM_URL");
        Environment.SetEnvironmentVariable("NEUROTUNE_CLM_URL", $"http://127.0.0.1:{port}/");
        try
        {
            var output = await ClmClient.DecideAsync("""{"note":"DPC spikes"}""", CancellationToken.None);
            await server;
            Assert.AreEqual(Reply, output);
            using var request = JsonDocument.Parse(captured!);
            var question = request.RootElement.GetProperty("questions").GetProperty("domain");
            Assert.AreEqual("choice", question.GetProperty("type").GetString());
            Assert.AreEqual(6, question.GetProperty("criteria").EnumerateObject().Count());
            Assert.AreEqual("noul", request.RootElement.GetProperty("questions").GetProperty("specific").GetProperty("type").GetString());
            Assert.Contains("note: DPC spikes", request.RootElement.GetProperty("state").GetString()!);
        }
        finally { Environment.SetEnvironmentVariable("NEUROTUNE_CLM_URL", previous); }
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
