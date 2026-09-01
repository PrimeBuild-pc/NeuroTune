using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NeuroTune.Tests;

[TestClass]
public sealed class PlannerLoopTests
{
    [TestMethod]
    public async Task Planner_requests_registered_evidence_then_returns_a_validated_diagnosis()
    {
        var responses = new[]
        {
            """{"kind":"requestEvidence","evidenceIds":["gaming:Game Mode"]}""",
            """{"kind":"diagnosis","diagnosis":{"summary":"Checked requested evidence","findings":[{"title":"Game Mode","evidenceId":"gaming:Game Mode","currentValue":"1","assessment":"Enabled"}],"recommendations":[{"id":"game-mode","kind":"executableAction","title":"Game Mode","actionId":"gaming.game-mode-off","evidenceIds":["gaming:Game Mode"],"reason":"Test the alternative against a Baseline"}],"consentQuestion":"Measure and apply the selected action?"}}"""
        };
        var (listener, server, settings) = StartServer(responses);
        using (listener)
        {
            var profile = new SystemProfile { Cpu = "CPU", GamingSettings = { ["Game Mode"] = "1" } };

            var outcome = await new LlmClient(new OptimizationCatalog()).PlanAsync(
                profile, new TuningGoals(), settings, null);
            await server;

            Assert.IsFalse(outcome.UsedLocalFallback);
            Assert.AreEqual("Checked requested evidence", outcome.Diagnosis.Summary);
            Assert.HasCount(2, outcome.Audit);
            Assert.AreEqual("requestEvidence", outcome.Audit[0].Kind);
            Assert.AreEqual("gaming:Game Mode", outcome.Audit[0].EvidenceIds.Single());
        }
    }

    [TestMethod]
    public async Task Planner_surfaces_missing_configuration_and_audits_rejected_requests()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LlmClient(new OptimizationCatalog()).PlanAsync(
            new SystemProfile { Cpu = "CPU" }, new TuningGoals(), LlmClient.Defaults(LlmProvider.OpenAI), null));

        var (listener, server, settings) = StartServer(["""{"kind":"requestEvidence","evidenceIds":["not:registered"]}"""]);
        using (listener)
        {
            var rejected = await new LlmClient(new OptimizationCatalog()).PlanAsync(
                new SystemProfile { Cpu = "CPU" }, new TuningGoals(), settings, null);
            await server;
            Assert.IsTrue(rejected.UsedLocalFallback);
            Assert.AreEqual("not:registered", rejected.Audit.Single().EvidenceIds.Single());
            Assert.IsFalse(rejected.Audit.Single().Accepted);
        }
    }

    [TestMethod]
    public void Conflict_references_cannot_bypass_initial_evidence_privacy()
    {
        var facts = new Dictionary<string, string>
        {
            ["system:cpu"] = "CPU",
            ["software:signal"] = "Private inventory"
        };
        var provided = LlmClient.SelectInitialEvidence(facts,
            [new ConflictPattern { EvidenceIds = ["software:signal"] }]);

        Assert.AreEqual("CPU", provided["system:cpu"]);
        Assert.IsFalse(provided.ContainsKey("software:signal"));
    }

    private static (TcpListener Listener, Task Server, UserSettings Settings) StartServer(IReadOnlyList<string> responses)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            foreach (var content in responses)
            {
                using var client = await listener.AcceptTcpClientAsync();
                await ReadRequest(client.GetStream());
                var body = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });
                var bytes = Encoding.UTF8.GetBytes(body);
                var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await client.GetStream().WriteAsync(header);
                await client.GetStream().WriteAsync(bytes);
            }
        });
        var settings = LlmClient.Defaults(LlmProvider.Local);
        settings.BaseUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/v1";
        settings.Model = "test-model";
        return (listener, server, settings);
    }

    private static async Task ReadRequest(NetworkStream stream)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
        var contentLength = 0;
        while (await reader.ReadLineAsync() is { } line && line.Length > 0)
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(line.Split(':', 2)[1].Trim());
        if (contentLength > 0)
        {
            var content = new char[contentLength];
            await reader.ReadBlockAsync(content);
        }
    }
}
