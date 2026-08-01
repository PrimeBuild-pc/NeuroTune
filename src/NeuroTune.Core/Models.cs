using System.Text.Json.Serialization;

namespace NeuroTune;

public enum LlmProvider { OpenRouter, OpenAI, Anthropic, DeepSeek, Custom, Local }
public enum ApiProtocol { OpenAiCompatible, Anthropic }
public enum RiskLevel { Low, Medium, High }
public enum OptimizationPriority { Balanced, Fps, SystemLatency, NetworkLatency, Efficiency }
public enum ConflictKind { Confirmed, Conditional, SuspiciousOverride, MissingEvidence }
public enum EvidencePrivacy { General, SystemConfiguration, SoftwareInventory }
public enum TelemetryStatus { Supported, Unavailable, BlockedByHvci, DriverNotApproved }

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
    public int SchemaVersion { get; set; } = 4;
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
    public Dictionary<string, string> HardwareCapabilities { get; set; } = [];
    public Dictionary<string, string> FirmwareAndMemory { get; set; } = [];
    public Dictionary<string, string> ComponentIdentities { get; set; } = [];
    public Dictionary<string, string> FactoryBaselines { get; set; } = [];
    public List<TelemetryCapability> TelemetryCapabilities { get; set; } = [];
    public Dictionary<string, string> BootConfiguration { get; set; } = [];
    public Dictionary<string, string> PerformanceRegistry { get; set; } = [];
    public List<string> PolicyConflicts { get; set; } = [];
    public List<string> InstalledSoftware { get; set; } = [];
    public List<string> RelevantDrivers { get; set; } = [];
    public List<string> DeviceIssues { get; set; } = [];
    public List<string> SoftwareSignals { get; set; } = [];
    public List<ScanPhase> ScanPhases { get; set; } = [];
    public List<string> TopProcesses { get; set; } = [];
    public List<string> StartupItems { get; set; } = [];
    public List<string> AutomaticServices { get; set; } = [];
}

public sealed record ScanPhase(string Name, long DurationMilliseconds, int FactsCollected);
public sealed record TelemetryCapability(string Name, TelemetryStatus Status, string Detail);
public sealed record EvidencePayloadReport(int FactCount, int Utf8Bytes, int SinglePassLimitBytes, bool FitsSinglePass, Dictionary<EvidencePrivacy, int> PrivacyClasses);

public sealed class ConflictPattern
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public ConflictKind Kind { get; set; }
    public List<string> EvidenceIds { get; set; } = [];
    public Dictionary<string, string> Evidence { get; set; } = [];
    public List<OptimizationPriority> Objectives { get; set; } = [];
    public string Explanation { get; set; } = "";
    public string WhyCounterproductive { get; set; } = "";
    public string Confidence { get; set; } = "Medium";
    public List<string> SuggestedActionIds { get; set; } = [];
}

public sealed class TuningGoals
{
    public OptimizationPriority Priority { get; set; } = OptimizationPriority.Balanced;
    public RiskProfile RiskProfile { get; set; } = RiskProfile.Balanced;
    public List<string> Games { get; set; } = [];
    public GameContext GameContext { get; set; } = new();
    public UserPerformanceInput PerformanceInput { get; set; } = new();
    public string Notes { get; set; } = "";

    public void Validate()
    {
        Games ??= [];
        GameContext ??= new();
        PerformanceInput ??= new();
        Notes ??= "";
        if (Games.Count > 100) throw new InvalidOperationException("Too many tuning goals.");
        Games = Games.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        if (Games.Any(x => x.Length > 100) || Notes.Length > 1_000)
            throw new InvalidOperationException("Tuning goals are too long.");
        Notes = Notes.Trim();
        GameContext.Validate();
        PerformanceInput.Validate();
    }
}

public sealed class GameContext
{
    public string Game { get; set; } = "";
    public string Version { get; set; } = "";
    public string Launcher { get; set; } = "";
    public string GraphicsApi { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? RefreshRateHz { get; set; }
    public string DisplayMode { get; set; } = "";
    public string Vrr { get; set; } = "";
    public string VSync { get; set; } = "";
    public int? FrameCap { get; set; }
    public List<string> Symptoms { get; set; } = [];
    public string Preserve { get; set; } = "";

    public void Validate()
    {
        Game = Bound(Game, 120, "Game name");
        Version = Bound(Version, 100, "Game version");
        Launcher = Bound(Launcher, 100, "Launcher");
        GraphicsApi = Bound(GraphicsApi, 40, "Graphics API");
        DisplayMode = Bound(DisplayMode, 40, "Display mode");
        Vrr = Bound(Vrr, 40, "VRR state");
        VSync = Bound(VSync, 40, "V-Sync state");
        Preserve = Bound(Preserve, 500, "Preservation notes");
        Symptoms ??= [];
        Symptoms = Symptoms.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Bound(x, 200, "Symptom"))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        CheckRange(Width, 320, 16_384, "Resolution width");
        CheckRange(Height, 200, 16_384, "Resolution height");
        CheckRange(RefreshRateHz, 20, 1_000, "Refresh rate");
        CheckRange(FrameCap, 10, 2_000, "Frame cap");
    }

    private static string Bound(string? value, int maximum, string name)
    {
        value = value?.Trim() ?? "";
        if (value.Length > maximum) throw new InvalidOperationException($"{name} was too long.");
        return value;
    }

    private static void CheckRange(int? value, int minimum, int maximum, string name)
    {
        if (value is not null && (value < minimum || value > maximum))
            throw new InvalidOperationException($"{name} was outside the supported range.");
    }
}

public sealed class UserPerformanceInput
{
    public bool UserProvided { get; set; } = true;
    public double? AverageFps { get; set; }
    public double? OnePercentLowFps { get; set; }
    public double? AverageFrameTimeMs { get; set; }
    public double? InputLatencyMs { get; set; }
    public double? NetworkLatencyMs { get; set; }
    public double? PacketLossPercent { get; set; }
    public string Notes { get; set; } = "";

    public void Validate()
    {
        UserProvided = true;
        Range(AverageFps, 0, 10_000, "Average FPS");
        Range(OnePercentLowFps, 0, 10_000, "1% low FPS");
        Range(AverageFrameTimeMs, 0, 60_000, "Frame time");
        Range(InputLatencyMs, 0, 60_000, "Input latency");
        Range(NetworkLatencyMs, 0, 60_000, "Network latency");
        Range(PacketLossPercent, 0, 100, "Packet loss");
        Notes = Notes?.Trim() ?? "";
        if (Notes.Length > 1_000) throw new InvalidOperationException("Measurement notes were too long.");
    }

    private static void Range(double? value, double minimum, double maximum, string name)
    {
        if (value is not null && (double.IsNaN(value.Value) || double.IsInfinity(value.Value) ||
            value < minimum || value > maximum))
            throw new InvalidOperationException($"{name} was outside the supported range.");
    }
}

public sealed class DiagnosisResult
{
    public string Summary { get; set; } = "";
    public List<DiagnosisFinding> Findings { get; set; } = [];
    public List<PlanRecommendation> Recommendations { get; set; } = [];
    public List<ConflictPattern> Conflicts { get; set; } = [];
    public string ConsentQuestion { get; set; } = "May NeuroTune apply the selected allowlisted fixes after creating a restore point?";
}

public sealed class DiagnosisFinding
{
    public string Title { get; set; } = "";
    public string EvidenceId { get; set; } = "";
    public string CurrentValue { get; set; } = "";
    public string Assessment { get; set; } = "";
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
