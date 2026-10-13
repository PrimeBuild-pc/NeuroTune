using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuroTune.Tests;

// Manual prompt-regression suite (plans/agentic-tuner-v2.md, F11). It calls a real provider, so it only runs when
// NEUROTUNE_EVAL_PROVIDER is set; see tests/eval/README.md. It never writes to this PC: readers return canned output.
[TestClass]
public sealed class EvalRunner
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal sealed record Matcher(string? ActionId = null, string? ActionKind = null, Dictionary<string, JsonElement>? Parameters = null);
    internal sealed record Expectation(string Label, List<Matcher> AnyOf);
    internal sealed record EvalCase(string Id, TuningGoals Goals, SystemProfile Profile, Dictionary<string, string>? Observations,
        List<Expectation> Expected, List<Expectation>? Forbidden);

    internal static bool Matches(Matcher matcher, PlanRecommendation item) =>
        (matcher.ActionId is null || item.ActionId.StartsWith(matcher.ActionId, StringComparison.OrdinalIgnoreCase)) &&
        (matcher.ActionKind is null || item.ActionKind.Equals(matcher.ActionKind, StringComparison.OrdinalIgnoreCase)) &&
        (matcher.Parameters is null || item.Parameters is { ValueKind: JsonValueKind.Object } parameters && matcher.Parameters.All(wanted =>
            parameters.EnumerateObject().Any(actual => actual.Name.Equals(wanted.Key, StringComparison.OrdinalIgnoreCase) &&
                Text(actual.Value).Equals(Text(wanted.Value), StringComparison.OrdinalIgnoreCase))));

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    internal static string CasesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NeuroTune.sln"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found."), "tests", "eval");
    }

    internal static List<EvalCase> LoadCases() => Directory.GetFiles(CasesDirectory(), "*.json").Order(StringComparer.Ordinal)
        .Select(path => JsonSerializer.Deserialize<EvalCase>(File.ReadAllText(path), Options) ?? throw new InvalidDataException(path)).ToList();

    [TestMethod]
    public void Eval_cases_load_and_their_matchers_are_well_formed()
    {
        var cases = LoadCases();
        Assert.IsNotEmpty(cases);
        foreach (var item in cases)
        {
            item.Goals.Validate();
            Assert.IsNotEmpty(item.Expected, item.Id);
            foreach (var matcher in item.Expected.Concat(item.Forbidden ?? []).SelectMany(expectation => expectation.AnyOf))
                Assert.IsTrue(matcher.ActionId is not null || matcher.ActionKind is not null, item.Id);
        }
    }

    [TestMethod]
    public void Matchers_compare_kind_id_prefix_and_a_parameter_subset()
    {
        var proposal = new PlanRecommendation
        {
            ActionId = "qos.policy.0123",
            ActionKind = "qos.policy",
            Parameters = JsonDocument.Parse("""{"Exe":"cs2.exe","Dscp":46}""").RootElement
        };
        Assert.IsTrue(Matches(new(ActionKind: "QOS.policy", Parameters: new() { ["exe"] = JsonDocument.Parse("\"CS2.exe\"").RootElement }), proposal));
        Assert.IsTrue(Matches(new(ActionId: "qos.policy.", Parameters: new() { ["dscp"] = JsonDocument.Parse("46").RootElement }), proposal));
        Assert.IsFalse(Matches(new(ActionKind: "qos.policy", Parameters: new() { ["dscp"] = JsonDocument.Parse("8").RootElement }), proposal));
        Assert.IsFalse(Matches(new(ActionKind: "registry.set"), proposal));
    }

    [TestMethod]
    [Timeout(3_600_000, CooperativeCancellation = true)]
    public async Task Run_eval_with_the_configured_provider()
    {
        if (!Enum.TryParse<LlmProvider>(Environment.GetEnvironmentVariable("NEUROTUNE_EVAL_PROVIDER"), true, out var provider))
            Assert.Inconclusive("Set NEUROTUNE_EVAL_PROVIDER (and MODEL, optional BASEURL/KEY) to run the manual eval.");
        var settings = LlmClient.Defaults(provider);
        settings.Model = Environment.GetEnvironmentVariable("NEUROTUNE_EVAL_MODEL") ?? settings.Model;
        if (Environment.GetEnvironmentVariable("NEUROTUNE_EVAL_BASEURL") is { Length: > 0 } baseUrl) settings.BaseUrl = baseUrl;
        var key = Environment.GetEnvironmentVariable("NEUROTUNE_EVAL_KEY");

        var rows = new List<object>();
        foreach (var item in LoadCases())
        {
            var outcome = await new LlmClient(new OptimizationCatalog()).PlanAsync(item.Profile, item.Goals, settings, key,
                investigate: (request, _) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>
                {
                    ["investigation:" + request.ToolId] = item.Observations?.GetValueOrDefault(request.ToolId) ?? "Unavailable in the eval fixture."
                }));
            var proposals = outcome.Diagnosis.Recommendations;
            var hits = item.Expected.Where(expectation => proposals.Any(proposal => expectation.AnyOf.Any(matcher => Matches(matcher, proposal)))).Select(expectation => expectation.Label).ToList();
            var violations = (item.Forbidden ?? []).Where(expectation => proposals.Any(proposal => expectation.AnyOf.Any(matcher => Matches(matcher, proposal)))).Select(expectation => expectation.Label).ToList();
            rows.Add(new
            {
                item.Id,
                recall = $"{hits.Count}/{item.Expected.Count}",
                missed = item.Expected.Select(expectation => expectation.Label).Except(hits).ToList(),
                violations,
                parseFailure = outcome.UsedLocalFallback,
                outcome.StopReason,
                turns = outcome.Audit.Select(entry => entry.Turn).DefaultIfEmpty(0).Max(),
                rejectedTurns = outcome.Audit.Count(entry => !entry.Accepted),
                proposals = proposals.Select(proposal => new { proposal.Title, proposal.Kind, proposal.ActionId, proposal.ActionKind, proposal.ExecutionIssue }).ToList()
            });
        }
        var report = JsonSerializer.Serialize(new { provider = provider.ToString(), settings.Model, runAtUtc = DateTimeOffset.UtcNow, rows },
            new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
        var path = Path.Combine(CasesDirectory(), "results", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{string.Concat(settings.Model.Split(Path.GetInvalidFileNameChars()))}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report);
        Console.WriteLine(report);
    }
}
