using System.Text.Json;

namespace NeuroTune;

public sealed record KeptDrift(Guid RunId, string ActionId, string Name);

// F10: kept changes that Windows no longer reports (an update or driver install reset them) are offered again.
public static class KeptChanges
{
    // Newest kept runs first; an action applied again later is judged by its newest record only.
    internal static List<(Guid RunId, ActionRecord Record, OptimizationAction Action)> Find(OptimizationCatalog catalog, BackupService backup, OptimizationRunService runs)
    {
        var drifted = new List<(Guid, ActionRecord, OptimizationAction)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var run in runs.List().Where(run => run.State == OptimizationRunState.Completed &&
            run.Decision == OptimizationRunDecision.Keep && run.OperationId is not null).Take(10))
        {
            OperationManifest? manifest;
            try { manifest = backup.Load(run.OperationId!.Value); }
            catch (InvalidOperationException) { continue; }
            foreach (var record in manifest?.Actions.Where(record => record.Applied && !record.RolledBack) ?? [])
            {
                if (!seen.Add(record.ActionId)) continue;
                try
                {
                    var action = catalog.ResolveRollback(record);
                    if (!action.Verify()) drifted.Add((run.Id, record, action));
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException) { /* Unverifiable is not drift. */ }
            }
        }
        return drifted;
    }

    public static List<KeptDrift> Drift(OptimizationCatalog catalog, BackupService backup, OptimizationRunService runs) =>
        Find(catalog, backup, runs).Select(item => new KeptDrift(item.RunId, item.Record.ActionId, item.Action.Name)).ToList();

    public static OptimizationRun Reapply(OptimizationCatalog catalog, BackupService backup, OptimizationRunService runs)
    {
        var drifted = Find(catalog, backup, runs);
        if (drifted.Count == 0) throw new InvalidOperationException("Every kept change still verifies; nothing to reapply.");
        var facts = drifted.ToDictionary(item => $"persistence:{item.Record.ActionId}",
            item => $"Applied and kept in run {item.RunId:D}; Verify() reported a different value on {DateTimeOffset.UtcNow:O}.", StringComparer.Ordinal);
        var recommendations = drifted.Select(item => new PlanRecommendation
        {
            Id = item.Record.ActionId,
            Kind = PlanRecommendationKind.ExecutableAction,
            Title = item.Action.Name,
            ActionId = item.Action.Id,
            ActionKind = item.Record.Kind ?? "",
            Parameters = item.Record.Parameters is { } json ? JsonDocument.Parse(json).RootElement.Clone() : null,
            ActionSummary = item.Action.Name,
            Risk = item.Action.Risk,
            RequiresRestart = item.Action.RequiresRestart,
            EvidenceIds = [$"persistence:{item.Record.ActionId}"],
            Reason = "You kept this change, but Windows no longer reports it; an update or driver install may have reset it.",
            Reversibility = "Journaled like any NeuroTune change; restore it from Activity & restore."
        }).ToList();
        return runs.CreateReapply(drifted[0].RunId, recommendations, facts);
    }
}
