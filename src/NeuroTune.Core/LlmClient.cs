using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NeuroTune;

public sealed class LlmClient
{
    private const int MaxResponseCharacters = 256_000;
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(8)
    };
    private readonly OptimizationCatalog _catalog;

    public LlmClient(OptimizationCatalog catalog) => _catalog = catalog;

    public static UserSettings Defaults(LlmProvider provider) => provider switch
    {
        LlmProvider.OpenAI => new() { Provider = provider, ProviderName = "OpenAI", BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o-mini" },
        LlmProvider.Anthropic => new() { Provider = provider, ProviderName = "Anthropic", BaseUrl = "https://api.anthropic.com/v1", Protocol = ApiProtocol.Anthropic, Model = "claude-3-5-haiku-latest" },
        LlmProvider.DeepSeek => new() { Provider = provider, ProviderName = "DeepSeek", BaseUrl = "https://api.deepseek.com/v1", Model = "deepseek-chat" },
        LlmProvider.Custom => new() { Provider = provider, ProviderName = "Custom provider", BaseUrl = "https://api.example.com/v1", Model = "", Protocol = ApiProtocol.OpenAiCompatible },
        LlmProvider.Local => new() { Provider = provider, ProviderName = "Local model", BaseUrl = "http://127.0.0.1:11434/v1", Model = "", RequiresApiKey = false },
        _ => new()
    };

    public Task<IReadOnlyList<string>> ListModelsAsync(LlmProvider provider, string apiKey,
        CancellationToken cancellationToken = default) =>
        ListModelsAsync(Defaults(provider), apiKey, cancellationToken);

    public async Task<IReadOnlyList<string>> ListModelsAsync(UserSettings settings, string? apiKey,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings, apiKey);
        var endpoint = BuildEndpoint(settings, "models");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddAuthentication(request, settings, apiKey);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Provider connection failed: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        var body = await ReadLimitedAsync(response, cancellationToken);
        var models = ParseModels(body);
        if (models.Count == 0) throw new InvalidOperationException("The provider returned no selectable models.");
        return models;
    }

    public async Task<DiagnosisResult> DiagnoseAsync(SystemProfile profile, TuningGoals goals, UserSettings settings, string? apiKey,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings, apiKey);
        goals.Validate();
        if (string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Select a model.");

        var evidenceFacts = BuildEvidenceFacts(profile);
        var localConflicts = ConflictAnalyzer.Analyze(profile, goals);
        var catalogJson = JsonSerializer.Serialize(_catalog.All.Select(x => (Action: x, Availability: x.Inspect()))
            .Where(x => x.Availability.CanApply)
            .Select(x => new
            {
                actionId = x.Action.Id,
                x.Action.Name,
                x.Action.Description,
                x.Action.Category,
                risk = x.Action.Risk.ToString(),
                x.Action.RequiresRestart
            }));
        var prompt = $$"""
            Analyze this Windows profile against the user's explicit goals. Return valid JSON only, without Markdown, using this exact shape:
            {"summary":"clear English summary","findings":[{"title":"short finding","evidenceId":"exact ID from EVIDENCE FACTS","currentValue":"exact associated value","assessment":"confirmed conflict, trade-off, or unavailable evidence"}],"recommendations":[{"actionId":"ID from the catalog","evidenceId":"evidence ID supporting this action","reason":"specific English reason tied to that evidence and the user goal"}],"consentQuestion":"neutral question asking whether NeuroTune may apply the proposed allowlisted fixes after a restore point"}

            Every finding must use an exact evidenceId and currentValue pair from EVIDENCE FACTS. Clearly distinguish a confirmed conflict, a trade-off, and unavailable evidence.
            Treat conflict:* evidence facts as locally detected facts, but explain their relevance to the user's goal. Do not infer game-engine behavior from a game name.
            Recommend only actionId values present in the catalog. Never produce commands, scripts, file paths, Registry paths, Registry values, or instructions to change the system manually.
            Prefer no recommendation over an unsupported or speculative optimization. Do not describe a missing Registry value as wrong when Windows safely manages its default.
            Do not promise FPS, latency, or network gains that this one profile cannot prove.

            USER GOALS:
            {{JsonSerializer.Serialize(goals)}}

            EVIDENCE FACTS:
            {{JsonSerializer.Serialize(evidenceFacts)}}

            LOCALLY DETECTED CONFLICT PATTERNS:
            {{JsonSerializer.Serialize(localConflicts)}}

            ALLOWLISTED AND COMPATIBLE ACTIONS:
            {{catalogJson}}
            """;

        using var request = settings.Protocol == ApiProtocol.Anthropic
            ? CreateAnthropicRequest(settings, apiKey, prompt)
            : CreateOpenAiRequest(settings, apiKey, prompt);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"The provider returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

        var body = await ReadLimitedAsync(response, cancellationToken);
        var diagnosis = ParseDiagnosis(ExtractContent(settings.Protocol, body), _catalog, evidenceFacts);
        diagnosis.Conflicts = localConflicts;
        return diagnosis;
    }

    public static IReadOnlyList<string> ParseModels(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("data").EnumerateArray()
                .Select(x => x.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("The provider model list was not recognized.");
        }
    }

    public static DiagnosisResult ParseDiagnosis(string content, OptimizationCatalog catalog,
        IReadOnlyDictionary<string, string>? evidenceFacts = null)
    {
        content = content.Trim();
        if (content.Length > MaxResponseCharacters) throw new InvalidOperationException("The model response was too large.");
        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLine = content.IndexOf('\n');
            var closing = content.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine >= 0 && closing > firstLine) content = content[(firstLine + 1)..closing].Trim();
        }

        DiagnosisResult result;
        try
        {
            result = JsonSerializer.Deserialize<DiagnosisResult>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The model did not return a valid JSON diagnosis.");
        }

        result.Findings ??= [];
        result.Recommendations ??= [];
        result.Summary = result.Summary.Trim();
        result.ConsentQuestion = result.ConsentQuestion?.Trim() ?? "";
        if (result.Summary.Length is 0 or > 4_000)
            throw new InvalidOperationException("The diagnosis summary was empty or too long.");
        if (result.ConsentQuestion.Length is 0 or > 500)
            throw new InvalidOperationException("The diagnosis consent question was empty or too long.");
        if (result.Recommendations.Any(x => x is null || !catalog.Contains(x.ActionId)))
            throw new InvalidOperationException("The model proposed an action outside the allowlist.");
        if (result.Recommendations.Any(x => string.IsNullOrWhiteSpace(x.Reason)))
            throw new InvalidOperationException("A recommendation did not include a reason.");
        if (evidenceFacts is not null && result.Recommendations.Any(x =>
            string.IsNullOrWhiteSpace(x.EvidenceId) || !evidenceFacts.ContainsKey(x.EvidenceId)))
            throw new InvalidOperationException("A recommendation did not reference verified diagnosis evidence.");
        foreach (var recommendation in result.Recommendations)
        {
            recommendation.ActionId = recommendation.ActionId.Trim();
            recommendation.EvidenceId = recommendation.EvidenceId?.Trim() ?? "";
            recommendation.Reason = recommendation.Reason.Trim();
            if (recommendation.EvidenceId.Length > 500 || recommendation.Reason.Length > 2_000)
                throw new InvalidOperationException("A recommendation was too long.");
        }
        if (result.Findings.Any(x => x is null || string.IsNullOrWhiteSpace(x.Title) ||
            string.IsNullOrWhiteSpace(x.EvidenceId) || string.IsNullOrWhiteSpace(x.CurrentValue) ||
            string.IsNullOrWhiteSpace(x.Assessment)))
            throw new InvalidOperationException("A diagnosis finding did not include valid evidence.");
        foreach (var finding in result.Findings)
        {
            finding.Title = finding.Title.Trim();
            finding.EvidenceId = finding.EvidenceId.Trim();
            finding.CurrentValue = finding.CurrentValue.Trim();
            finding.Assessment = finding.Assessment.Trim();
            if (finding.Title.Length > 300 || finding.EvidenceId.Length > 500 ||
                finding.CurrentValue.Length > 20_000 || finding.Assessment.Length > 2_000)
                throw new InvalidOperationException("A diagnosis finding was too long.");
            if (evidenceFacts is not null && (!evidenceFacts.TryGetValue(finding.EvidenceId, out var observed) ||
                !observed.Equals(finding.CurrentValue, StringComparison.Ordinal)))
                throw new InvalidOperationException("The model cited evidence that was not present in the local scan.");
        }
        result.Findings = result.Findings.DistinctBy(x => x.EvidenceId, StringComparer.Ordinal).Take(30).ToList();
        result.Recommendations = result.Recommendations
            .Where(x => !string.IsNullOrWhiteSpace(x.ActionId))
            .DistinctBy(x => x.ActionId, StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        return result;
    }

    public static IReadOnlyDictionary<string, string> BuildEvidenceFacts(SystemProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["system:operating-system"] = profile.OperatingSystem ?? "Unavailable",
            ["system:cpu"] = profile.Cpu ?? "Unavailable",
            ["system:memory"] = profile.Memory ?? "Unavailable",
            ["system:active-power-plan"] = profile.ActivePowerPlan ?? "Unavailable"
        };
        var gpus = profile.Gpus ?? [];
        for (var index = 0; index < gpus.Count; index++) facts[$"hardware:gpu:{index}"] = gpus[index] ?? "Unavailable";
        Add("hardware", profile.HardwareCapabilities);
        Add("firmware", profile.FirmwareAndMemory);
        Add("boot", profile.BootConfiguration);
        Add("windows", profile.WindowsSettings);
        Add("gaming", profile.GamingSettings);
        Add("network", profile.NetworkSettings);
        Add("registry", profile.PerformanceRegistry);
        AddList("storage", profile.Disks);
        AddList("network-adapter", profile.NetworkAdapters);
        AddList("software", profile.InstalledSoftware);
        AddList("driver", profile.RelevantDrivers);
        AddList("device-issue", profile.DeviceIssues);
        AddList("software-signal", profile.SoftwareSignals);
        AddList("runtime-process", profile.TopProcesses);
        AddList("startup", profile.StartupItems);
        AddList("service", profile.AutomaticServices);
        AddList("conflict-observation", profile.PolicyConflicts);
        foreach (var key in facts.Keys.ToList()) facts[key] = ProfileSanitizer.Redact(facts[key]);
        return facts;

        void Add(string prefix, Dictionary<string, string>? values)
        {
            foreach (var (key, value) in values ?? []) facts[$"{prefix}:{key}"] = value ?? "Unavailable";
        }

        void AddList(string prefix, List<string>? values)
        {
            var items = values ?? [];
            for (var index = 0; index < items.Count; index++) facts[$"{prefix}:{index}"] = items[index] ?? "Unavailable";
        }
    }

    public static Uri ValidateBaseUrl(UserSettings settings)
    {
        var raw = settings.Provider switch
        {
            LlmProvider.OpenRouter => "https://openrouter.ai/api/v1",
            LlmProvider.OpenAI => "https://api.openai.com/v1",
            LlmProvider.Anthropic => "https://api.anthropic.com/v1",
            LlmProvider.DeepSeek => "https://api.deepseek.com/v1",
            _ => settings.BaseUrl
        };
        if (!Uri.TryCreate(raw?.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Enter a valid provider base URL.");
        if (uri.Scheme == "http" && !IsLoopbackHost(uri.Host))
            throw new InvalidOperationException("Remote custom providers must use HTTPS. HTTP is allowed only for local models.");
        return uri;
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    private static Uri BuildEndpoint(UserSettings settings, string route) =>
        new($"{ValidateBaseUrl(settings).AbsoluteUri.TrimEnd('/')}/{route}");

    private static void ValidateSettings(UserSettings settings, string? apiKey)
    {
        ValidateBaseUrl(settings);
        if (settings.RequiresApiKey && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Enter an API key or use an available browser sign-in option.");
    }

    private static HttpRequestMessage CreateOpenAiRequest(UserSettings settings, string? apiKey, string prompt)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(settings, "chat/completions"));
        AddAuthentication(request, settings, apiKey);
        request.Content = JsonContent(new
        {
            model = settings.Model,
            temperature = 0.1,
            messages = new[] { new { role = "user", content = prompt } }
        });
        return request;
    }

    private static HttpRequestMessage CreateAnthropicRequest(UserSettings settings, string? apiKey, string prompt)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(settings, "messages"));
        AddAuthentication(request, settings, apiKey);
        request.Content = JsonContent(new
        {
            model = settings.Model,
            max_tokens = 6000,
            temperature = 0.1,
            messages = new[] { new { role = "user", content = prompt } }
        });
        return request;
    }

    private static void AddAuthentication(HttpRequestMessage request, UserSettings settings, string? apiKey)
    {
        if (settings.Protocol == ApiProtocol.Anthropic)
        {
            if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            return;
        }
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (settings.Provider == LlmProvider.OpenRouter)
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/PrimeBuild-pc/NeuroTune");
            request.Headers.TryAddWithoutValidation("X-Title", "NeuroTune");
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxResponseCharacters * 4L)
            throw new InvalidOperationException("The provider response was too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var body = new StringBuilder();
        var buffer = new char[8_192];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) is var read && read > 0)
        {
            body.Append(buffer, 0, read);
            if (body.Length > MaxResponseCharacters)
                throw new InvalidOperationException("The provider response was too large.");
        }
        return body.ToString();
    }

    private static StringContent JsonContent(object value) => new(
        JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static string ExtractContent(ApiProtocol protocol, string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return protocol == ApiProtocol.Anthropic
                ? json.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? ""
                : json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("The provider response was not recognized.");
        }
    }
}
