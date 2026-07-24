using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NeuroTune;

public sealed class LlmClient
{
    private const int MaxResponseCharacters = 256_000;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly OptimizationCatalog _catalog;

    public LlmClient(OptimizationCatalog catalog) => _catalog = catalog;

    public async Task<IReadOnlyList<string>> ListModelsAsync(LlmProvider provider, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Enter an API key first.");
        var endpoint = provider switch
        {
            LlmProvider.OpenRouter => "https://openrouter.ai/api/v1/models",
            LlmProvider.OpenAI => "https://api.openai.com/v1/models",
            _ => "https://api.anthropic.com/v1/models?limit=1000"
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddAuthentication(request, provider, apiKey);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Provider connection failed: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        var body = await ReadLimitedAsync(response, cancellationToken);
        var models = ParseModels(body);
        if (models.Count == 0) throw new InvalidOperationException("The provider returned no selectable models.");
        return models;
    }

    public async Task<DiagnosisResult> DiagnoseAsync(SystemProfile profile, UserSettings settings, string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Enter an API key.");
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

        using var request = settings.Provider == LlmProvider.Anthropic
            ? CreateAnthropicRequest(settings.Model, apiKey, prompt)
            : CreateOpenAiRequest(settings.Provider, settings.Model, apiKey, prompt);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"The provider returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

        var body = await ReadLimitedAsync(response, cancellationToken);
        return ParseDiagnosis(ExtractContent(settings.Provider, body), _catalog);
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

    private static HttpRequestMessage CreateOpenAiRequest(LlmProvider provider, string model, string apiKey, string prompt)
    {
        var endpoint = provider == LlmProvider.OpenRouter
            ? "https://openrouter.ai/api/v1/chat/completions"
            : "https://api.openai.com/v1/chat/completions";
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        AddAuthentication(request, provider, apiKey);
        request.Content = JsonContent(new
        {
            model,
            temperature = 0.1,
            messages = new[] { new { role = "user", content = prompt } }
        });
        return request;
    }

    private static HttpRequestMessage CreateAnthropicRequest(string model, string apiKey, string prompt)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        AddAuthentication(request, LlmProvider.Anthropic, apiKey);
        request.Content = JsonContent(new
        {
            model,
            max_tokens = 1800,
            temperature = 0.1,
            messages = new[] { new { role = "user", content = prompt } }
        });
        return request;
    }

    private static void AddAuthentication(HttpRequestMessage request, LlmProvider provider, string apiKey)
    {
        if (provider == LlmProvider.Anthropic)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            return;
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (provider == LlmProvider.OpenRouter)
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

    private static string ExtractContent(LlmProvider provider, string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return provider == LlmProvider.Anthropic
                ? json.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? ""
                : json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("The provider response was not recognized.");
        }
    }
}
