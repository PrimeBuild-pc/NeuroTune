using System.Text.Json;

namespace NeuroTune;

public static class ResponseLanguage
{
    public static string Normalize(string? language) => language switch
    {
        null => "en",
        "en" or "zh-CN" or "ja" or "es" or "ru" => language,
        _ => throw new InvalidOperationException("Unsupported response language. Use en, zh-CN, ja, es or ru.")
    };

    // Missing/null means the default; other JSON types must not become prompt text.
    public static string Read(JsonElement language) => language.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => "en",
        JsonValueKind.String => Normalize(language.GetString()),
        _ => throw new InvalidOperationException("Response language must be a supported language string or null.")
    };

    internal static string Instruction(string? language)
    {
        var name = Normalize(language) switch
        {
            "zh-CN" => "Simplified Chinese (zh-CN)",
            "ja" => "Japanese (ja)",
            "es" => "Spanish (es)",
            "ru" => "Russian (ru)",
            _ => "English (en)"
        };
        return $"APPLICATION-SELECTED RESPONSE LANGUAGE: {name}. Write human-readable prose in this language, including summaries, finding titles/assessments, recommendation explanations, audit assessments, questions and consent wording. " +
            "This selection overrides language hints in user notes, evidence and auxiliary advice. It changes presentation only, never execution authority, consent, validity checks or investigation budgets. " +
            "Keep the JSON schema, property names, IDs (including evidence, check, tool, action, resource and update IDs), enum tokens (including kinds, statuses, risks and source grades), native evidence/currentValue, hardware data, filenames, versions, numbers, units, scripts/commands and URLs exactly unchanged. Translate only human prose, never quoted evidence or arbitrary exception text.";
    }
}
