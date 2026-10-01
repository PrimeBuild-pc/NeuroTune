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

    [TestMethod]
    public async Task Planner_collects_new_read_only_evidence_and_persists_it_without_granting_an_executor()
    {
        var (listener, server, settings) = StartServer(new Func<string, string>[]
        {
            _ => """{"kind":"requestInvestigation","toolId":"memory-pressure","question":"Check current paging pressure before any memory-policy proposal"}""",
            request =>
            {
                using var body = JsonDocument.Parse(request);
                var prompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
                Assert.DoesNotContain("preferred low-latency policy", prompt);
                Assert.Contains("contestable hypotheses", prompt);
                var id = System.Text.RegularExpressions.Regex.Match(prompt, @"investigation:memory-pressure:[a-f0-9]+:turn1").Value;
                Assert.IsNotEmpty(id);
                return JsonSerializer.Serialize(new { kind = "diagnosis", diagnosis = new { summary = "Investigated current pressure; heuristic is not a conclusion", findings = new[] { new { title = "Pressure", evidenceId = id, currentValue = "Current observation: paging unavailable", assessment = "Do not infer zero or disable compression by policy" } }, recommendations = new[] { new { id = "custom", kind = "executableAction", title = "Manual investigation beyond catalog", actionId = "outside.catalog", evidenceIds = new[] { id }, reason = "Requires manual verification", uncertainty = "Capture-time pressure remains unknown", reversibility = "No change proposed yet" } }, consentQuestion = "Review only?" } });
            }
        });
        var directory = Path.Combine(Path.GetTempPath(), $"neurotune-investigation-{Guid.NewGuid():N}");
        try
        {
            using (listener)
            {
                var profile = new SystemProfile { Cpu = "CPU" };
                var service = new OptimizationRunService(directory);
                var run = service.Create(profile, new TuningGoals()); service.BeginDiagnosis(run.Id);
                var outcome = await new LlmClient(new OptimizationCatalog()).PlanAsync(profile, run.Goals, settings, null,
                    investigate: (request, cancellation) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["investigation:" + request.ToolId] = "Current observation: paging unavailable" }));
                await server;
                Assert.IsFalse(outcome.UsedLocalFallback);
                var completed = service.RecordDiagnosis(run.Id, outcome);
                Assert.AreEqual(OptimizationRunState.BaselinePending, completed.State);
                Assert.Contains("paging unavailable", completed.EvidenceFacts[outcome.Audit[0].EvidenceIds.Single()]);
                Assert.AreEqual(PlanRecommendationKind.ManualGuidance, completed.Diagnosis!.Recommendations.Single().Kind);
                Assert.AreEqual("", completed.Diagnosis.Recommendations.Single().ActionId);
                Assert.IsNotEmpty(service.Load(run.Id).RequestedProbeIds);
            }
            Assert.ThrowsExactly<InvalidOperationException>(() => PlannerProtocol.Parse("""{"kind":"requestInvestigation","toolId":"driver-details","module":"../bad.sys","question":"Unsafe path"}"""));
            var unsupported = await InvestigationService.ReadAsync(new("arbitrary-shell", "Check an unsupported reader"));
            Assert.Contains("unavailable", unsupported.Values.Single());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow(OptimizationPriority.Balanced, "OVERALL PERFORMANCE")]
    [DataRow(OptimizationPriority.SystemLatency, "SYSTEM LATENCY")]
    [DataRow(OptimizationPriority.NetworkLatency, "NETWORK OPTIMIZATION")]
    [DataRow(OptimizationPriority.Stability, "SYSTEM STABILITY")]
    [DataRow(OptimizationPriority.Fps, "LEGACY FRAME-RATE FOCUS")]
    [DataRow(OptimizationPriority.Efficiency, "LEGACY EFFICIENCY FOCUS")]
    public async Task Selected_focus_specializes_every_turn_without_changing_goals_tools_or_write_authority(OptimizationPriority priority, string marker)
    {
        var prompts = new List<string>();
        var (listener, server, settings) = StartServer(new Func<string, string>[]
        {
            request => { Capture(request); return """{"kind":"requestInvestigation","toolId":"system-events","question":"Check relevant recent event metadata"}"""; },
            request => { Capture(request); return """{"kind":"diagnosis","diagnosis":{"summary":"No supported change","findings":[{"title":"CPU","evidenceId":"system:cpu","currentValue":"CPU","assessment":"Observed metadata, not a measured improvement"}],"recommendations":[],"consentQuestion":"Finish without changes?"}}"""; }
        });
        var goals = new TuningGoals { Priority = priority, RiskProfile = RiskProfile.Safe, Games = ["Workload"], Notes = "Preserve security and image quality" };
        var directory = Path.Combine(Path.GetTempPath(), $"neurotune-focus-{Guid.NewGuid():N}");
        try
        {
            using (listener)
            {
                var service = new OptimizationRunService(directory);
                var profile = new SystemProfile { Cpu = "CPU" };
                var run = service.Create(profile, goals); service.BeginDiagnosis(run.Id);
                var outcome = await new LlmClient(new OptimizationCatalog()).PlanAsync(profile, goals, settings, null,
                    investigate: (request, cancellation) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["investigation:" + request.ToolId] = "Recent event metadata unavailable" }));
                await server;
                Assert.IsFalse(outcome.UsedLocalFallback);
                Assert.HasCount(2, prompts);
                foreach (var prompt in prompts)
                {
                    Assert.Contains("APPLICATION-SELECTED ANALYSIS FOCUS", prompt);
                    Assert.Contains(marker, prompt);
                    Assert.Contains(LlmClient.AnalysisFocus(priority), prompt);
                    Assert.Contains("not execution permissions", prompt);
                    Assert.Contains("not a fixed tweak recipe", prompt);
                    Assert.Contains("Unknown is not zero", prompt);
                    Assert.Contains("Preserve security and image quality", prompt);
                    Assert.Contains("system-events", prompt);
                }
                var saved = service.RecordDiagnosis(run.Id, outcome);
                Assert.AreEqual(OptimizationRunState.BaselinePending, saved.State);
                Assert.HasCount(0, saved.ApprovedActionIds);
                Assert.HasCount(0, saved.Diagnosis!.Recommendations);
                var restored = service.Load(run.Id);
                Assert.AreEqual(priority, restored.Goals.Priority);
                Assert.AreEqual(RiskProfile.Safe, restored.Goals.RiskProfile);
                Assert.AreEqual(goals.Notes, restored.Goals.Notes);
                Assert.AreEqual(12, settings.InvestigationMaxTurns);
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        void Capture(string request)
        {
            using var body = JsonDocument.Parse(request);
            prompts.Add(body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!);
        }
    }

    [TestMethod]
    public async Task Comparison_explanation_uses_selected_focus_but_leaves_original_metrics_and_decision_untouched()
    {
        string? prompt = null;
        var (listener, server, settings) = StartServer(new Func<string, string>[] { request =>
        {
            using var body = JsonDocument.Parse(request);
            prompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
            return """{"summary":"Stability cannot be established by scheduling metrics alone","decision":"keep"}""";
        }});
        using (listener)
        {
            var comparison = new MeasurementComparison { Metrics = [new("measurement:test:cpu", 10, 11, 10, ComparisonOutcome.Inconclusive)] };
            var original = JsonSerializer.Serialize(comparison);
            var result = await new LlmClient(new OptimizationCatalog()).ExplainComparisonAsync(comparison, new TuningGoals { Priority = OptimizationPriority.Stability }, settings, null);
            await server;
            Assert.Contains("SYSTEM STABILITY", prompt!);
            Assert.Contains("does not replace its metrics or decision gates", prompt!);
            Assert.AreEqual("Stability cannot be established by scheduling metrics alone", result);
            Assert.AreEqual(original, JsonSerializer.Serialize(comparison));
        }
    }

    [TestMethod]
    public void Goal_validation_preserves_legacy_values_and_rejects_unknown_objectives_or_risk_profiles()
    {
        foreach (var (value, priority) in new[] { (1, OptimizationPriority.Fps), (4, OptimizationPriority.Efficiency), (5, OptimizationPriority.Stability) })
        {
            var restored = JsonSerializer.Deserialize<TuningGoals>($$"""{"Priority":{{value}},"RiskProfile":0}""")!;
            restored.Validate(); Assert.AreEqual(priority, restored.Priority);
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => new TuningGoals { Priority = (OptimizationPriority)99 }.Validate());
        Assert.ThrowsExactly<InvalidOperationException>(() => new TuningGoals { RiskProfile = (RiskProfile)99 }.Validate());
        Assert.ThrowsExactly<InvalidOperationException>(() => LlmClient.AnalysisFocus((OptimizationPriority)99));
    }

    private static (TcpListener Listener, Task Server, UserSettings Settings) StartServer(IReadOnlyList<string> responses) =>
        StartServer(responses.Select<string, Func<string, string>>(response => _ => response).ToList());

    private static (TcpListener Listener, Task Server, UserSettings Settings) StartServer(IReadOnlyList<Func<string, string>> responses)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            foreach (var content in responses)
            {
                using var client = await listener.AcceptTcpClientAsync();
                var request = await ReadRequest(client.GetStream());
                var body = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = content(request) } } } });
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

    private static async Task<string> ReadRequest(NetworkStream stream)
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
            return new string(content);
        }
        return "";
    }
}
