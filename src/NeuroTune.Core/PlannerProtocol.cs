using System.Text.Json;

namespace NeuroTune;

public enum PlannerTurnKind { RequestEvidence, Diagnosis }

public sealed record PlannerTurn(
    PlannerTurnKind Kind,
    IReadOnlyList<string> EvidenceIds,
    string DiagnosisJson);

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
    bool UsedLocalFallback);

public static class PlannerProtocol
{
    public const int MaxTurns = 4;
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
                if (ids.Count is 0 or > MaxEvidencePerTurn || ids.Any(id => id.Length > 500))
                    throw new InvalidOperationException("The planner requested an invalid number of evidence facts.");
                return new(PlannerTurnKind.RequestEvidence, ids, "");
            }
            if (kind == "diagnosis")
                return new(PlannerTurnKind.Diagnosis, [], root.GetProperty("diagnosis").GetRawText());
            throw new InvalidOperationException("The planner returned an unknown turn kind.");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException("The planner did not return a valid JSON turn.", exception);
        }
    }

    public static IReadOnlyList<string> ValidateRequest(PlannerTurn turn,
        IReadOnlyDictionary<string, string> available, IReadOnlyDictionary<string, string> provided)
    {
        if (turn.Kind != PlannerTurnKind.RequestEvidence ||
            turn.EvidenceIds.Any(id => !available.ContainsKey(id) || provided.ContainsKey(id)))
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
