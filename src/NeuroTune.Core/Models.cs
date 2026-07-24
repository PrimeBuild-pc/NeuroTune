using System.Text.Json.Serialization;

namespace NeuroTune;

public enum LlmProvider { OpenRouter, OpenAI, Anthropic, DeepSeek, Custom, Local }
public enum ApiProtocol { OpenAiCompatible, Anthropic }
public enum RiskLevel { Low, Medium, High }
public enum OptimizationPreset { Balanced, Gaming, Custom }

public sealed class UserSettings
{
    public LlmProvider Provider { get; set; } = LlmProvider.OpenRouter;
    public string ProviderName { get; set; } = "OpenRouter";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public ApiProtocol Protocol { get; set; } = ApiProtocol.OpenAiCompatible;
    public string Model { get; set; } = "openai/gpt-4o-mini";
    public bool RequiresApiKey { get; set; } = true;

    [JsonIgnore]
    public string CredentialId => Provider switch
    {
        LlmProvider.Custom => "custom",
        LlmProvider.Local => "local",
        _ => Provider.ToString().ToLowerInvariant()
    };
}

public sealed class SystemProfile
{
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.Now;
    public string OperatingSystem { get; set; } = "Unavailable";
    public string Cpu { get; set; } = "Unavailable";
    public List<string> Gpus { get; set; } = [];
    public string Memory { get; set; } = "Unavailable";
    public List<string> Disks { get; set; } = [];
    public string ActivePowerPlan { get; set; } = "Unavailable";
    public Dictionary<string, string> WindowsSettings { get; set; } = [];
    public Dictionary<string, string> GamingSettings { get; set; } = [];
    public List<string> NetworkAdapters { get; set; } = [];
    public Dictionary<string, string> NetworkSettings { get; set; } = [];
    public List<string> TopProcesses { get; set; } = [];
    public List<string> StartupItems { get; set; } = [];
    public List<string> AutomaticServices { get; set; } = [];
}

public sealed class DiagnosisResult
{
    public string Summary { get; set; } = "";
    public List<string> Findings { get; set; } = [];
    public List<OptimizationRecommendation> Recommendations { get; set; } = [];
}

public sealed class OptimizationRecommendation
{
    public string ActionId { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed record ActionAvailability(bool CanApply, bool AlreadyApplied, string Status, string CurrentValue)
{
    public static ActionAvailability Ready(string current) => new(true, false, "Ready", current);
    public static ActionAvailability Applied(string current) => new(false, true, "Already configured", current);
    public static ActionAvailability Unavailable(string reason, string current = "Unknown") => new(false, false, reason, current);
}

public sealed class OptimizationOption
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required RiskLevel Risk { get; init; }
    public required bool RequiresRestart { get; init; }
    public string Reason { get; set; } = "Not recommended by the current diagnosis";
    public string Availability { get; set; } = "Not checked";
    public string CurrentValue { get; set; } = "Unknown";
    public bool IsRecommended { get; set; }
    public bool CanApply { get; set; }
    public bool IsSelected { get; set; }
    public string RiskLabel => Risk switch { RiskLevel.Low => "Low", RiskLevel.Medium => "Medium", _ => "High" };
    public string RecommendationLabel => IsRecommended ? "AI recommended" : "Optional";
}

public sealed class PerformanceSnapshot
{
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.Now;
    public int? CpuLoadPercent { get; set; }
    public double? UsedMemoryGb { get; set; }
    public double? TotalMemoryGb { get; set; }
    public int ProcessCount { get; set; }
    public long? LatencyMs { get; set; }
    public string ActivePowerPlan { get; set; } = "Unavailable";
}

public sealed class ActionRecord
{
    public string ActionId { get; set; } = "";
    public string OriginalState { get; set; } = "";
    public bool Attempted { get; set; }
    public bool Applied { get; set; }
    public bool RolledBack { get; set; }
    public string? Error { get; set; }
}

public sealed class OperationManifest
{
    public int SchemaVersion { get; set; } = 2;
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string Status { get; set; } = "Preparing";
    public string RestorePoint { get; set; } = "";
    public List<ActionRecord> Actions { get; set; } = [];
    public PerformanceSnapshot? Before { get; set; }
    public PerformanceSnapshot? After { get; set; }
    public string? Error { get; set; }

    [JsonIgnore]
    public string DirectoryPath { get; set; } = "";

    [JsonIgnore]
    public bool HasPendingRollback => Actions.Any(x => (x.Attempted || x.Applied) && !x.RolledBack) &&
        (Status is "Preparing" or "Applying" or "Rolling back" or "Preparazione" or "Applicazione" ||
         Status.Contains("incomplet", StringComparison.OrdinalIgnoreCase) ||
         Status.Contains("in corso", StringComparison.OrdinalIgnoreCase));
}
