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
        Timeout = TimeSpan.FromSeconds(90)
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

    public async Task<DiagnosisResult> DiagnoseAsync(SystemProfile profile, UserSettings settings, string? apiKey,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings, apiKey);
        if (string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Select a model.");

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
            Analyze this Windows profile. Return valid JSON only, without Markdown, using this exact shape:
            {"summary":"clear English summary","findings":["finding"],"recommendations":[{"actionId":"ID from the catalog","reason":"specific English reason"}]}
            Recommend only actionId values present in the catalog. Never produce commands, scripts, file paths, Registry paths, or Registry values.
            Prefer no recommendation over an unsupported or speculative optimization. Do not promise performance gains that the profile cannot support.

            ALLOWLISTED AND COMPATIBLE ACTIONS:
            {{catalogJson}}

            SANITIZED PROFILE:
            {{ProfileSanitizer.Serialize(profile)}}
            """;

        using var request = settings.Protocol == ApiProtocol.Anthropic
            ? CreateAnthropicRequest(settings, apiKey, prompt)
            : CreateOpenAiRequest(settings, apiKey, prompt);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"The provider returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

        var body = await ReadLimitedAsync(response, cancellationToken);
        return ParseDiagnosis(ExtractContent(settings.Protocol, body), _catalog);
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

    public static DiagnosisResult ParseDiagnosis(string content, OptimizationCatalog catalog)
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
        if (result.Summary.Length is 0 or > 4_000)
            throw new InvalidOperationException("The diagnosis summary was empty or too long.");
        if (result.Recommendations.Any(x => !catalog.Contains(x.ActionId)))
            throw new InvalidOperationException("The model proposed an action outside the allowlist.");
        if (result.Recommendations.Any(x => string.IsNullOrWhiteSpace(x.Reason)))
            throw new InvalidOperationException("A recommendation did not include a reason.");
        result.Findings = result.Findings.Where(x => !string.IsNullOrWhiteSpace(x)).Take(20)
            .Select(x => x.Trim()).ToList();
        result.Recommendations = result.Recommendations
            .Where(x => !string.IsNullOrWhiteSpace(x.ActionId))
            .DistinctBy(x => x.ActionId, StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        return result;
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
            max_tokens = 1800,
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
