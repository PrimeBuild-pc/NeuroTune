using System.Text.Json.Serialization;

namespace NeuroTune;

public enum LlmProvider { OpenRouter, OpenAI, Anthropic }
public enum RiskLevel { Low, Medium, High }
public enum OptimizationPreset { Balanced, Gaming, Custom }

public sealed class UserSettings
{
    public LlmProvider Provider { get; set; } = LlmProvider.OpenRouter;
    public string Model { get; set; } = "openai/gpt-4o-mini";
}

public sealed class SystemProfile
{
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.Now;
    public string OperatingSystem { get; set; } = "Non disponibile";
    public string Cpu { get; set; } = "Non disponibile";
    public List<string> Gpus { get; set; } = [];
    public string Memory { get; set; } = "Non disponibile";
    public List<string> Disks { get; set; } = [];
    public string ActivePowerPlan { get; set; } = "Non disponibile";
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

public sealed class OptimizationOption
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required RiskLevel Risk { get; init; }
    public required bool RequiresRestart { get; init; }
    public string Reason { get; set; } = "Non raccomandata dall'analisi corrente";
    public bool IsRecommended { get; set; }
    public bool IsSelected { get; set; }
    public string RiskLabel => Risk switch { RiskLevel.Low => "Basso", RiskLevel.Medium => "Medio", _ => "Alto" };
}

public sealed class ActionRecord
{
    public string ActionId { get; set; } = "";
    public string OriginalState { get; set; } = "";
    public bool Applied { get; set; }
    public bool RolledBack { get; set; }
    public string? Error { get; set; }
}

public sealed class OperationManifest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string Status { get; set; } = "Preparazione";
    public string RestorePoint { get; set; } = "";
    public List<ActionRecord> Actions { get; set; } = [];
    public string? Error { get; set; }

    [JsonIgnore]
    public string DirectoryPath { get; set; } = "";
}
