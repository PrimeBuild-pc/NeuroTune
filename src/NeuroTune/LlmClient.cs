using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NeuroTune;

public sealed class LlmClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly OptimizationCatalog _catalog;

    public LlmClient(OptimizationCatalog catalog) => _catalog = catalog;

    public async Task<DiagnosisResult> DiagnoseAsync(SystemProfile profile, UserSettings settings, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Inserisci una API key.");
        if (string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Seleziona un modello.");

        var catalogJson = JsonSerializer.Serialize(_catalog.All.Select(x => new
        {
            actionId = x.Id,
            x.Name,
            x.Description,
            x.Category,
            risk = x.Risk.ToString(),
            x.RequiresRestart
        }));
        var prompt = $$"""
            Analizza il profilo Windows seguente. Rispondi esclusivamente con JSON valido, senza Markdown, nel formato:
            {"summary":"sintesi in italiano","findings":["rilievo"],"recommendations":[{"actionId":"id dal catalogo","reason":"motivazione in italiano"}]}
            Puoi raccomandare soltanto actionId presenti nel catalogo. Non produrre comandi, script, percorsi o valori di registro.

            CATALOGO:
            {{catalogJson}}

            PROFILO:
            {{ProfileSanitizer.Serialize(profile)}}
            """;

        using var request = settings.Provider == LlmProvider.Anthropic
            ? CreateAnthropicRequest(settings.Model, apiKey, prompt)
            : CreateOpenAiRequest(settings.Provider, settings.Model, apiKey, prompt);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Il provider ha risposto con HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var content = ExtractContent(settings.Provider, body);
        return ParseDiagnosis(content, _catalog);
    }

    public static DiagnosisResult ParseDiagnosis(string content, OptimizationCatalog catalog)
    {
        content = content.Trim();
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
            throw new InvalidOperationException("Il modello non ha restituito una diagnosi JSON valida.");
        }

        if (string.IsNullOrWhiteSpace(result.Summary))
            throw new InvalidOperationException("La diagnosi non contiene una sintesi.");
        result.Findings ??= [];
        result.Recommendations ??= [];
        if (result.Recommendations.Any(x => !catalog.Contains(x.ActionId)))
            throw new InvalidOperationException("Il modello ha proposto un'azione non consentita.");
        result.Recommendations = result.Recommendations
            .Where(x => !string.IsNullOrWhiteSpace(x.ActionId))
            .DistinctBy(x => x.ActionId, StringComparer.OrdinalIgnoreCase).ToList();
        return result;
    }

    private static HttpRequestMessage CreateOpenAiRequest(LlmProvider provider, string model, string apiKey, string prompt)
    {
        var endpoint = provider == LlmProvider.OpenRouter
            ? "https://openrouter.ai/api/v1/chat/completions"
            : "https://api.openai.com/v1/chat/completions";
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (provider == LlmProvider.OpenRouter)
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/PrimeBuild-pc/NeuroTune");
            request.Headers.TryAddWithoutValidation("X-Title", "NeuroTune");
        }
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
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        request.Content = JsonContent(new
        {
            model,
            max_tokens = 1800,
            temperature = 0.1,
            messages = new[] { new { role = "user", content = prompt } }
        });
        return request;
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
            throw new InvalidOperationException("Risposta del provider non riconosciuta.");
        }
    }
}
