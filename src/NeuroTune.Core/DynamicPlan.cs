namespace NeuroTune;

public enum PlanRecommendationKind
{
    ExecutableAction,
    ManualGuidance,
    ScriptArtifact,
    ExternalResource,
    UpdateNotice
}

public enum RiskProfile { Safe, Balanced, Aggressive }

public enum PolicyDisposition { Allowed, ConfirmationRequired, ManualOnly, Blocked }

public sealed class SourceReference
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Grade { get; set; } = "Unrated";
}

public sealed class PlanRecommendation
{
    public string Id { get; set; } = "";
    public PlanRecommendationKind Kind { get; set; }
    public string Title { get; set; } = "";
    public List<string> EvidenceIds { get; set; } = [];
    public string Reason { get; set; } = "";
    public RiskLevel Risk { get; set; } = RiskLevel.Low;
    public string ExpectedImpact { get; set; } = "";
    public List<string> Tradeoffs { get; set; } = [];
    public List<string> Prerequisites { get; set; } = [];
    public bool RequiresRestart { get; set; }
    public List<SourceReference> SourceReferences { get; set; } = [];
    public string ActionId { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public string UpdateId { get; set; } = "";
    public string ScriptLanguage { get; set; } = "";
    public string Script { get; set; } = "";
    public List<string> ReviewWarnings { get; set; } = [];
}

public sealed record ActionPolicyDecision(
    PolicyDisposition Disposition,
    bool Preselected,
    bool RequiresSeparateConfirmation,
    string Reason);

public static class PlanSelectionPolicy
{
    public static ActionPolicyDecision Evaluate(
        PlanRecommendation recommendation,
        RiskProfile profile,
        OptimizationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        ArgumentNullException.ThrowIfNull(catalog);

        if (recommendation.Kind != PlanRecommendationKind.ExecutableAction)
            return new(PolicyDisposition.ManualOnly, false, false,
                "This plan item is reviewable but is not executable by NeuroTune.");
        if (!catalog.Contains(recommendation.ActionId))
            return new(PolicyDisposition.Blocked, false, false,
                "The action is not present in the local capability registry.");

        var action = catalog.Get(recommendation.ActionId);
        var preselected = profile switch
        {
            RiskProfile.Safe => action.Risk == RiskLevel.Low,
            RiskProfile.Balanced => action.Risk is RiskLevel.Low or RiskLevel.Medium,
            _ => true
        };
        var high = action.Risk == RiskLevel.High;
        return new(high ? PolicyDisposition.ConfirmationRequired : PolicyDisposition.Allowed,
            preselected, high, high
                ? "High-risk capabilities require a separate confirmation."
                : $"Selected by the {profile} risk policy.");
    }
}

public static class ScriptReviewService
{
    private const int MaxScriptCharacters = 20_000;
    private static readonly (string Token, string Warning)[] Signals =
    [
        ("Set-MpPreference", "The script references Microsoft Defender configuration."),
        ("netsh advfirewall", "The script references Windows Firewall configuration."),
        ("bcdedit", "The script references boot configuration."),
        ("Remove-Item", "The script contains a file or Registry deletion command."),
        ("Invoke-WebRequest", "The script can download remote content."),
        ("Start-Process", "The script can start another process."),
        ("reg.exe", "The script invokes the generic Registry command-line tool."),
        ("curl.exe", "The script can download remote content.")
    ];

    public static List<string> Analyze(string language, string script)
    {
        language = language?.Trim().ToLowerInvariant() ?? "";
        script ??= "";
        if (language is not ("powershell" or "cmd" or "text"))
            throw new InvalidOperationException("A script artifact used an unsupported language.");
        if (script.Length is 0 or > MaxScriptCharacters || script.IndexOf('\0') >= 0)
            throw new InvalidOperationException("A script artifact was empty, binary, or too large.");

        var warnings = Signals
            .Where(signal => script.Contains(signal.Token, StringComparison.OrdinalIgnoreCase))
            .Select(signal => signal.Warning)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        warnings.Insert(0, "Unverified model-generated script. NeuroTune cannot execute it or guarantee rollback.");
        return warnings;
    }
}
