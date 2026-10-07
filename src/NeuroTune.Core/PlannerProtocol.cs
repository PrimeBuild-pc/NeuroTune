using System.Text.Json;

namespace NeuroTune;

public enum PlannerTurnKind { RequestEvidence, RequestInvestigation, Diagnosis }

public sealed record PlannerTurn(
    PlannerTurnKind Kind,
    IReadOnlyList<string> EvidenceIds,
    string DiagnosisJson,
    InvestigationRequest? Investigation = null);

public sealed record PlannerAuditEntry(
    int Turn,
    string Kind,
    IReadOnlyList<string> EvidenceIds,
    bool Accepted,
    string Reason);

public sealed record PlannerDiagnosisOutcome(
    DiagnosisResult Diagnosis,
    IReadOnlyList<PlannerAuditEntry> Audit,
    string StopReason,
    bool UsedLocalFallback,
    IReadOnlyDictionary<string, string>? AdditionalEvidence = null);

public static class PlannerProtocol
{
    public const int MaxTurns = 32; // Resource ceiling; the user-selected investigation budget is lower by default.
    public const int MaxEvidencePerTurn = 40;

    public static PlannerTurn Parse(string content)
    {
        content = StripFence(content?.Trim() ?? "");
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var kind = root.GetProperty("kind").GetString();
            if (kind == "requestEvidence")
            {
                var ids = root.GetProperty("evidenceIds").EnumerateArray()
                    .Select(item => item.GetString()?.Trim() ?? "")
                    .Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                if (ids.Count == 0) throw new InvalidOperationException("The planner requested no usable evidence facts.");
                if (ids.Count > MaxEvidencePerTurn) throw new InvalidOperationException("The planner exceeded the 40-fact evidence request limit.");
                if (ids.Any(id => id.Length > 500)) throw new InvalidOperationException("The planner returned an evidence ID exceeding 500 characters.");
                return new(PlannerTurnKind.RequestEvidence, ids, "");
            }
            if (kind == "requestInvestigation")
            {
                var request = new InvestigationRequest(root.GetProperty("toolId").GetString() ?? "",
                    root.GetProperty("question").GetString() ?? "",
                    root.TryGetProperty("module", out var module) ? module.GetString() : null);
                InvestigationService.Validate(request);
                return new(PlannerTurnKind.RequestInvestigation, [], "", request);
            }
            if (kind == "diagnosis")
                return new(PlannerTurnKind.Diagnosis, [], root.GetProperty("diagnosis").GetRawText());
            throw new InvalidOperationException("The planner returned an unknown turn kind.");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            var reason = exception switch
            {
                JsonException => "The planner envelope contains invalid JSON syntax.",
                KeyNotFoundException => "The planner envelope is missing a required field.",
                ArgumentException when exception.Message == "This investigation tool does not accept a module parameter." => exception.Message,
                ArgumentException { ParamName: "module" } => "The planner driver-details request needs a .sys filename without a path.",
                ArgumentException => "The planner returned an invalid read-only investigation request.",
                InvalidOperationException when exception.Message is "The planner returned an unknown turn kind." or
                    "The planner requested no usable evidence facts." or
                    "The planner exceeded the 40-fact evidence request limit." or
                    "The planner returned an evidence ID exceeding 500 characters." => exception.Message,
                _ => "The planner envelope contains invalid field types."
            };
            throw new InvalidOperationException(reason, exception);
        }
    }

    public static IReadOnlyList<string> ValidateRequest(PlannerTurn turn,
        IReadOnlyDictionary<string, string> available, IReadOnlyDictionary<string, string> provided)
    {
        if (turn.Kind != PlannerTurnKind.RequestEvidence || turn.EvidenceIds.Count is 0 or > MaxEvidencePerTurn ||
            turn.EvidenceIds.Any(id => id.Length > 500 || !available.ContainsKey(id) || provided.ContainsKey(id)))
            throw new InvalidOperationException("The planner requested unknown or repeated evidence.");
        return turn.EvidenceIds;
    }

    private static string StripFence(string content)
    {
        if (!content.StartsWith("```", StringComparison.Ordinal)) return content;
        var firstLine = content.IndexOf('\n');
        var closing = content.LastIndexOf("```", StringComparison.Ordinal);
        return firstLine >= 0 && closing > firstLine ? content[(firstLine + 1)..closing].Trim() : content;
    }
}
