using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NeuroTune;

// Contrastive Language Model (https://github.com/Contrastive-LM/CLM) as the optional System One classifier.
// The model runs in a local server the user starts; NeuroTune only talks to a loopback endpoint, never to a cloud
// service, and the answer is an advisory topic distribution, never a decision.
internal static class ClmClient
{
    internal const string Model = "clm-latest";
    internal const double AbstainBelow = 0.6;
    private const int MaxStateCharacters = 3_500; // The reference encoder is served with a 2048-token window.
    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private static (DateTime At, bool Ready)? _health;

    // CLM is a question/answer ranker: the heads score a candidate that reads like an answer to the question. On a
    // 12-case labeled panel this phrasing reached 10/12 topics where bare category labels reached 4/12.
    internal const string TopicQuestion = "Which area of the PC does this observation point to?";
    internal const string SpecificQuestion = "Is the problem described clearly enough to name a specific area of the PC?";
    internal static readonly (string Key, string Description)[] Topics =
    [
        ("memory", "The observation is about memory pressure, paging, RAM or the page file."),
        ("drivers", "The observation is about device drivers, interrupts, DPC or ISR latency."),
        ("network", "The observation is about the network connection: latency, packet loss, Wi-Fi or DNS."),
        ("power", "The observation is about power, thermals, throttling, clocks or power plans."),
        ("software", "The observation is about background applications, overlays, startup programs or antivirus."),
        ("unknown", "The observation is vague and does not identify one area."),
    ];

    internal static Uri Endpoint
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("NEUROTUNE_CLM_URL");
            return Uri.TryCreate(configured, UriKind.Absolute, out var uri) && IsLoopback(uri) ? uri : new Uri("http://127.0.0.1:8700/");
        }
    }

    internal static bool IsLoopback(Uri uri) => uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) &&
        uri.Host is "127.0.0.1" or "localhost" or "[::1]" or "::1";

    // True when the CLM server answers and its encoder is up. Cached briefly because status is read often.
    internal static bool Reachable(bool refresh = false)
    {
        if (!refresh && _health is { } cached && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(10)) return cached.Ready;
        var ready = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var body = Http.GetStringAsync(new Uri(Endpoint, "health"), timeout.Token).GetAwaiter().GetResult();
            using var json = JsonDocument.Parse(body);
            ready = json.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True &&
                json.RootElement.TryGetProperty("embedder", out var embedder) && embedder.ValueKind == JsonValueKind.True;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { }
        _health = (DateTime.UtcNow, ready);
        return ready;
    }

    internal static async Task<string> DecideAsync(string context, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = Model,
            state = StateText(context),
            questions = new Dictionary<string, object>
            {
                ["domain"] = new
                {
                    type = "choice",
                    instructions = TopicQuestion,
                    criteria = Topics.ToDictionary(topic => topic.Key, topic => topic.Description)
                },
                ["specific"] = new { type = "noul", instructions = SpecificQuestion }
            }
        };
        using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(new Uri(Endpoint, "v1/systemone"), content, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"The local CLM server returned HTTP {(int)response.StatusCode}.");
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Length > 65_536) throw new InvalidOperationException("The local CLM response was too large.");
        return text;
    }

    // The heads are trained on prose, not JSON: render objects as "key: value" lines, newest context last.
    internal static string StateText(string context)
    {
        string text;
        try { using var json = JsonDocument.Parse(context); text = Render(json.RootElement, 0).Trim(); }
        catch (JsonException) { text = context.Trim(); }
        text = ProfileSanitizer.Redact(text);
        return text.Length <= MaxStateCharacters ? text : text[..MaxStateCharacters];
    }

    private static string Render(JsonElement value, int indent)
    {
        var pad = new string(' ', indent);
        switch (value.ValueKind)
        {
            case JsonValueKind.String: return value.GetString() ?? "";
            case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False: return value.ToString();
            case JsonValueKind.Object:
                return string.Join(indent == 0 ? "\n\n" : "\n", value.EnumerateObject().Select(property =>
                    property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array && property.Value.GetRawText().Length > 2
                        ? $"{pad}{property.Name}:\n{Render(property.Value, indent + 2)}" : $"{pad}{property.Name}: {Render(property.Value, 0)}"));
            case JsonValueKind.Array:
                return string.Join("\n", value.EnumerateArray().Select(item => $"{pad}- {Render(item, 0)}"));
            default: return "";
        }
    }

    internal static SystemOneAdvisory ParseAdvisory(string output, string phase, IReadOnlyList<string> knownIds, double seconds, Func<string, string, bool> matchesDomain)
    {
        if (output.Length > 65_536) throw new InvalidOperationException("The local CLM response was too large.");
        using var json = JsonDocument.Parse(output);
        var answer = json.RootElement.GetProperty("answers").GetProperty("domain");
        if (answer.GetProperty("type").GetString() != "choice") throw new InvalidOperationException("Unexpected CLM answer type.");
        var probabilities = answer.GetProperty("probabilities").EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetDouble());
        var keys = Topics.Select(topic => topic.Key).ToHashSet(StringComparer.Ordinal);
        if (probabilities.Count != keys.Count || probabilities.Keys.Any(key => !keys.Contains(key)) ||
            probabilities.Values.Any(value => !double.IsFinite(value) || value is < 0 or > 1) || Math.Abs(probabilities.Values.Sum() - 1) > 0.001)
            throw new InvalidOperationException("Invalid CLM probability distribution.");
        var specific = json.RootElement.GetProperty("answers").GetProperty("specific");
        if (specific.GetProperty("type").GetString() != "noul" || specific.GetProperty("noul").GetDouble() is var gate && (!double.IsFinite(gate) || gate is < 0 or > 1))
            throw new InvalidOperationException("Unexpected CLM specificity answer.");
        var top = probabilities.MaxBy(item => item.Value);
        var confident = top.Key != "unknown" && top.Value >= AbstainBelow && specific.GetProperty("noul").GetDouble() >= AbstainBelow;
        var ids = confident ? knownIds.Where(id => matchesDomain(id, top.Key)).Take(24).ToList() : [];
        return new(phase, confident ? "ok" : "uncertain", confident ? top.Key : null, confident ? top.Value : null, seconds,
            $"CLM topic classification (local contrastive model, softmax over fixed topics; top option {top.Value.ToString("P0", CultureInfo.InvariantCulture)}). Abstains when the observation is vague. Uncalibrated, not causal proof, risk approval or authority to change any setting.", ids);
    }
}
