namespace NeuroTune;

public sealed record ActionDefinition(
    string Id,
    string Name,
    string Description,
    string Category,
    RiskLevel Risk,
    bool RequiresRestart,
    string? RegistryExportPath,
    IReadOnlyList<string> SupportedWindowsBuilds,
    IReadOnlyList<string> SupportedHardware,
    IReadOnlyList<string> EvidenceRequirements,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> SideEffects)
{
    public int SchemaVersion { get; init; } = 1;

    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Id) || Id.Length > 120 ||
            !Id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-'))
            throw new InvalidOperationException("An action definition has an invalid ID or schema version.");
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Description) ||
            string.IsNullOrWhiteSpace(Category))
            throw new InvalidOperationException($"Action {Id} has incomplete display metadata.");
        if (SupportedWindowsBuilds.Count == 0 || SupportedHardware.Count == 0 ||
            EvidenceRequirements.Count == 0 || Sources.Count == 0 || SideEffects.Count == 0 ||
            SupportedWindowsBuilds.Concat(SupportedHardware).Concat(EvidenceRequirements)
                .Concat(Sources).Concat(SideEffects).Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"Action {Id} has incomplete compatibility or evidence metadata.");
    }
}

public interface IReversibleAction
{
    ActionDefinition Definition { get; }
    ActionAvailability Inspect();
    string Capture();
    void Apply();
    bool Verify();
    void Restore(string capturedState);
}

public static class ActionPolicy
{
    private static readonly string[] ForbiddenPerformanceTargets =
        ["defender", "firewall", "uac", "hpet", "platform-timer"];

    public static ActionPolicyDecision Evaluate(ActionDefinition definition, RiskProfile profile)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (ForbiddenPerformanceTargets.Any(target =>
            definition.Id.Contains(target, StringComparison.OrdinalIgnoreCase)))
            return new(PolicyDisposition.Blocked, false, false,
                "This target is outside NeuroTune's executable performance policy.");

        var preselected = profile switch
        {
            RiskProfile.Safe => definition.Risk == RiskLevel.Low,
            RiskProfile.Balanced => definition.Risk != RiskLevel.High,
            RiskProfile.Aggressive => true,
            _ => false
        };
        var confirmation = definition.Risk == RiskLevel.High;
        return new(confirmation ? PolicyDisposition.ConfirmationRequired : PolicyDisposition.Allowed,
            preselected, confirmation, confirmation
                ? "High-risk capabilities always require separate confirmation."
                : "The capability is registered, compatible, and reversible.");
    }
}
