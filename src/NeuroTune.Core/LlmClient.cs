using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuroTune;

public sealed class LlmClient
{
    private const int MaxResponseCharacters = 256_000;
    public const int MaxSinglePassEvidenceBytes = 256_000;
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(8)
    };
    private readonly OptimizationCatalog _catalog;
    private readonly ExternalArtifactCatalog _artifactCatalog;
    private readonly OfficialUpdateAdvisor _updateAdvisor;

    public LlmClient(OptimizationCatalog catalog, ExternalArtifactCatalog? artifactCatalog = null,
        OfficialUpdateAdvisor? updateAdvisor = null)
    {
        _catalog = catalog;
        _artifactCatalog = artifactCatalog ?? new ExternalArtifactCatalog();
        _updateAdvisor = updateAdvisor ?? new OfficialUpdateAdvisor();
    }

    public static UserSettings Defaults(LlmProvider provider) => provider switch
    {
        LlmProvider.OpenAI => new() { Provider = provider, ProviderName = "OpenAI", BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o-mini" },
        LlmProvider.Anthropic => new() { Provider = provider, ProviderName = "Anthropic", BaseUrl = "https://api.anthropic.com/v1", Protocol = ApiProtocol.Anthropic, Model = "claude-3-5-haiku-latest" },
        LlmProvider.DeepSeek => new() { Provider = provider, ProviderName = "DeepSeek", BaseUrl = "https://api.deepseek.com/v1", Model = "deepseek-chat" },
        LlmProvider.Custom => new() { Provider = provider, ProviderName = "Custom provider", BaseUrl = "https://api.example.com/v1", Model = "", Protocol = ApiProtocol.OpenAiCompatible },
        LlmProvider.Local => new() { Provider = provider, ProviderName = "Local model", BaseUrl = "http://127.0.0.1:11434/v1", Model = "", RequiresApiKey = false },
        _ => new()
    };

    public Task<IReadOnlyList<string>> ListModelsAsync(LlmProvider provider, string apiKey,
        CancellationToken cancellationToken = default) =>
        ListModelsAsync(Defaults(provider), apiKey, cancellationToken);

    public async Task<IReadOnlyList<string>> ListModelsAsync(UserSettings settings, string? apiKey,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings, apiKey);
        var endpoint = BuildEndpoint(settings, "models");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddAuthentication(request, settings, apiKey);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Provider connection failed: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        var body = await ReadLimitedAsync(response, cancellationToken);
        var models = ParseModels(body);
        if (models.Count == 0) throw new InvalidOperationException("The provider returned no selectable models.");
        return models;
    }

    public async Task<DiagnosisResult> DiagnoseAsync(SystemProfile profile, TuningGoals goals, UserSettings settings, string? apiKey,
        IReadOnlyDictionary<string, string>? measurementEvidence = null, CancellationToken cancellationToken = default) =>
        (await PlanAsync(profile, goals, settings, apiKey, measurementEvidence, cancellationToken)).Diagnosis;

    public async Task<PlannerDiagnosisOutcome> PlanAsync(SystemProfile profile, TuningGoals goals, UserSettings settings, string? apiKey,
        IReadOnlyDictionary<string, string>? measurementEvidence = null, CancellationToken cancellationToken = default)
    {
        goals.Validate();
        ValidateSettings(settings, apiKey);
        if (string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Select a model.");

        var evidenceFacts = MergeEvidenceFacts(BuildEvidenceFacts(profile), measurementEvidence);
        if (!MeasureEvidence(evidenceFacts).FitsSinglePass)
            throw new InvalidOperationException("The evidence bundle exceeds NeuroTune's local single-pass safety limit. Review the payload and use a smaller scan; unvalidated character slicing is not allowed.");
        var localConflicts = ConflictAnalyzer.Analyze(profile, goals);
        var resources = _artifactCatalog.All.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var updateNotices = _updateAdvisor.Analyze(profile).ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var catalogJson = JsonSerializer.Serialize(_catalog.All.Select(x => (Action: x, Availability: x.Inspect()))
            .Where(x => x.Availability.CanApply)
            .Select(x => new
            {
                actionId = x.Action.Id,
                x.Action.Name,
                x.Action.Description,
                x.Action.Category,
                risk = x.Action.Risk.ToString(),
                x.Action.RequiresRestart,
                x.Action.Definition.SupportedWindowsBuilds,
                x.Action.Definition.SupportedHardware,
                x.Action.Definition.EvidenceRequirements,
                x.Action.Definition.SideEffects,
                x.Action.Definition.Sources
            }));
        var provided = SelectInitialEvidence(evidenceFacts, localConflicts);
        var audit = new List<PlannerAuditEntry>();

        for (var turnNumber = 1; turnNumber <= PlannerProtocol.MaxTurns; turnNumber++)
        {
            var prompt = BuildPlannerPrompt(goals, evidenceFacts, provided, localConflicts, catalogJson,
                resources.Values, updateNotices.Values, turnNumber);
            PlannerTurn? plannerTurn = null;
            try
            {
                var content = await SendPromptAsync(settings, apiKey, prompt, cancellationToken);
                plannerTurn = PlannerProtocol.Parse(content);
                if (plannerTurn.Kind == PlannerTurnKind.RequestEvidence)
                {
                    var requested = PlannerProtocol.ValidateRequest(plannerTurn, evidenceFacts, provided);
                    foreach (var id in requested) provided[id] = evidenceFacts[id];
                    audit.Add(new(turnNumber, "requestEvidence", requested, true,
                        $"Accepted {requested.Count} registered evidence request(s)."));
                    continue;
                }

                var diagnosis = ParseDiagnosis(plannerTurn.DiagnosisJson, _catalog, provided, resources, updateNotices);
                diagnosis.Conflicts = localConflicts;
                audit.Add(new(turnNumber, "diagnosis", [], true, "Diagnosis passed local evidence and capability validation."));
                return new(diagnosis, audit, "diagnosis-completed", false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                audit.Add(new(turnNumber, "rejected", plannerTurn?.EvidenceIds ?? [], false, PlannerFailureReason(exception)));
                return new(LocalFallback(localConflicts), audit, "local-conflict-fallback", true);
            }
        }
        return new(LocalFallback(localConflicts), audit, "planner-turn-limit", true);

        DiagnosisResult LocalFallback(IReadOnlyList<ConflictPattern> conflicts)
        {
            var recommendations = conflicts.SelectMany(conflict => conflict.SuggestedActionIds)
                .Distinct(StringComparer.OrdinalIgnoreCase).Where(_catalog.Contains).Select(actionId =>
                {
                    var action = _catalog.Get(actionId);
                    var related = conflicts.Where(conflict => conflict.SuggestedActionIds.Contains(actionId,
                        StringComparer.OrdinalIgnoreCase)).ToList();
                    return new PlanRecommendation
                    {
                        Id = $"local-{actionId}",
                        Kind = PlanRecommendationKind.ExecutableAction,
                        Title = action.Name,
                        ActionId = action.Id,
                        EvidenceIds = related.SelectMany(conflict => conflict.EvidenceIds).Distinct(StringComparer.Ordinal).Take(12).ToList(),
                        Reason = string.Join(" ", related.Select(conflict => conflict.Explanation)),
                        Risk = action.Risk,
                        RequiresRestart = action.RequiresRestart,
                        ExpectedImpact = "Restores a locally detected configuration conflict; performance impact requires measurement."
                    };
                }).ToList();
            return new()
            {
                Summary = "The provider planner was unavailable or invalid. NeuroTune kept deterministic local findings for review, but this run cannot apply changes until an AI diagnosis succeeds.",
                Recommendations = recommendations,
                Conflicts = conflicts.ToList(),
                ConsentQuestion = "Review the local evidence, record a Baseline, and apply only selected registered actions?"
            };
        }
    }

    private static string BuildPlannerPrompt(TuningGoals goals,
        IReadOnlyDictionary<string, string> available,
        IReadOnlyDictionary<string, string> provided,
        IReadOnlyList<ConflictPattern> conflicts,
        string catalogJson,
        IEnumerable<ExternalArtifactDefinition> resources,
        IEnumerable<UpdateNoticeDefinition> updates,
        int turn) => $$$"""
        You are the bounded NeuroTune gaming-optimization planner. Return valid JSON only, without Markdown.

        Return exactly one of these envelopes:
        {"kind":"requestEvidence","evidenceIds":["exact available ID"]}
        {"kind":"diagnosis","diagnosis":{"summary":"clear English summary","findings":[{"title":"short finding","evidenceId":"exact PROVIDED EVIDENCE ID","currentValue":"exact associated value","assessment":"confirmed conflict, trade-off, or unavailable evidence"}],"recommendations":[{"id":"stable response-local ID","kind":"executableAction | manualGuidance | scriptArtifact | externalResource | updateNotice","title":"short title","evidenceIds":["exact PROVIDED EVIDENCE ID"],"reason":"specific reason","risk":"low | medium | high","expectedImpact":"bounded, non-promissory impact","tradeoffs":["trade-off"],"prerequisites":["prerequisite"],"requiresRestart":false,"sourceReferences":[{"title":"source title","url":"https://source.example/path","grade":"Official | Reproducible | Corroborated | Anecdotal"}],"actionId":"catalog ID only for executableAction","resourceId":"locally supplied ID only for externalResource","updateId":"locally supplied ID only for updateNotice","scriptLanguage":"powershell | cmd | text only for scriptArtifact","script":"review-only script; never executed by NeuroTune"}],"consentQuestion":"neutral question asking whether NeuroTune may apply only selected registered actions after a Baseline and restore point"}}

        This is turn {{{turn}}} of {{{PlannerProtocol.MaxTurns}}}. Request at most {{{PlannerProtocol.MaxEvidencePerTurn}}} facts. Never request an ID already in PROVIDED EVIDENCE. Request only facts necessary for the user's goal; otherwise return a diagnosis now.
        Every finding and recommendation must cite PROVIDED EVIDENCE. Do not infer game-engine behavior from a game name.
        ExecutableAction may use only supplied actionId values. Scripts are review-only and never executable. Never invent resource/update IDs, versions, URLs, paths, or commands.
        Prefer no recommendation over speculation. A missing Registry value may be a safe Windows default. Do not promise gains without repeated measurements. User performance input is unverified context.
        For FPS or system-latency goals, treat disabled MMAgent MemoryCompression and PageCombining as the preferred low-latency policy only when memory-pressure evidence does not show paging pressure; never claim a guaranteed gain.
        Recommend enabling MemoryCompression only as manual guidance when the user explicitly names an office/general-purpose workload, the CPU evidence clearly establishes a modern generation with headroom, and the goal is not gaming or system latency. Ambiguous workload, CPU, or pressure evidence means no change.
        Application prefetch, prelaunch, OperationAPI, and MaxOperationAPIFiles are observations, not universal tuning targets.
        Custom .pow plans are opaque user files. Never recommend one from its filename or hash; only describe it as a user-selected high-risk measured comparison.

        USER GOALS:
        {{{JsonSerializer.Serialize(goals)}}}

        AVAILABLE EVIDENCE IDS AND PRIVACY CLASSES:
        {{{JsonSerializer.Serialize(available.Keys.Order(StringComparer.Ordinal).Select(id => new { id, privacy = ClassifyEvidence(id).ToString() }))}}}

        PROVIDED EVIDENCE:
        {{{JsonSerializer.Serialize(provided)}}}

        LOCAL CONFLICT SUMMARIES (values remain available only through evidence requests):
        {{{JsonSerializer.Serialize(conflicts.Select(conflict => new { conflict.Id, conflict.Title, conflict.Kind, conflict.EvidenceIds, conflict.Objectives, conflict.Explanation, conflict.WhyCounterproductive, conflict.Confidence, conflict.SuggestedActionIds }))}}}

        ALLOWLISTED AND COMPATIBLE ACTIONS:
        {{{catalogJson}}}

        PRIMEBUILD-VERIFIED EXTERNAL RESOURCES:
        {{{JsonSerializer.Serialize(resources)}}}

        DETERMINISTIC OFFICIAL UPDATE NOTICES:
        {{{JsonSerializer.Serialize(updates)}}}
        """;

    private static async Task<string> SendPromptAsync(UserSettings settings, string? apiKey, string prompt,
        CancellationToken cancellationToken)
    {
        using var request = settings.Protocol == ApiProtocol.Anthropic
            ? CreateAnthropicRequest(settings, apiKey, prompt)
            : CreateOpenAiRequest(settings, apiKey, prompt);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"The provider returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        return ExtractContent(settings.Protocol, await ReadLimitedAsync(response, cancellationToken));
    }

    private static string PlannerFailureReason(Exception exception) => exception switch
    {
        InvalidOperationException => "Planner response or provider request failed local validation.",
        HttpRequestException => "Provider transport failed.",
        _ => "Provider planner failed before a validated diagnosis."
    };

    public static IReadOnlyList<string> ParseModels(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("data").EnumerateArray()
                .Select(x => x.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("The provider model list was not recognized.");
        }
    }

    public static DiagnosisResult ParseDiagnosis(string content, OptimizationCatalog catalog,
        IReadOnlyDictionary<string, string>? evidenceFacts = null,
        IReadOnlyDictionary<string, ExternalArtifactDefinition>? knownResources = null,
        IReadOnlyDictionary<string, UpdateNoticeDefinition>? knownUpdates = null)
    {
        content = content.Trim();
        if (content.Length > MaxResponseCharacters) throw new InvalidOperationException("The model response was too large.");
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
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            }) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The model did not return a valid JSON diagnosis.");
        }

        result.Findings ??= [];
        result.Recommendations ??= [];
        result.Summary = result.Summary.Trim();
        result.ConsentQuestion = result.ConsentQuestion?.Trim() ?? "";
        if (result.Summary.Length is 0 or > 4_000)
            throw new InvalidOperationException("The diagnosis summary was empty or too long.");
        if (result.ConsentQuestion.Length is 0 or > 500)
            throw new InvalidOperationException("The diagnosis consent question was empty or too long.");
        if (result.Recommendations.Count > 40)
            throw new InvalidOperationException("The model returned too many plan items.");
        foreach (var recommendation in result.Recommendations)
        {
            if (recommendation is null) throw new InvalidOperationException("The model returned an empty plan item.");
            recommendation.Id = recommendation.Id?.Trim() ?? "";
            recommendation.Title = recommendation.Title?.Trim() ?? "";
            recommendation.ActionId = recommendation.ActionId?.Trim() ?? "";
            recommendation.ResourceId = recommendation.ResourceId?.Trim() ?? "";
            recommendation.UpdateId = recommendation.UpdateId?.Trim() ?? "";
            recommendation.ScriptLanguage = recommendation.ScriptLanguage?.Trim() ?? "";
            recommendation.Script ??= "";
            recommendation.EvidenceIds ??= [];
            recommendation.EvidenceIds = recommendation.EvidenceIds.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct(StringComparer.Ordinal).Take(12).ToList();
            recommendation.Reason = recommendation.Reason?.Trim() ?? "";
            recommendation.ExpectedImpact = recommendation.ExpectedImpact?.Trim() ?? "";
            recommendation.Tradeoffs = NormalizeList(recommendation.Tradeoffs, 12, 500, "trade-off");
            recommendation.Prerequisites = NormalizeList(recommendation.Prerequisites, 12, 500, "prerequisite");
            recommendation.SourceReferences = ValidateSources(recommendation.SourceReferences);
            if (recommendation.Id.Length is 0 or > 120 || recommendation.Title.Length is 0 or > 300 ||
                recommendation.Reason.Length is 0 or > 2_000 || recommendation.ExpectedImpact.Length > 1_000 ||
                recommendation.EvidenceIds.Any(x => x.Length > 500))
                throw new InvalidOperationException("A recommendation was too long.");
            if (evidenceFacts is not null && (recommendation.EvidenceIds.Count == 0 ||
                recommendation.EvidenceIds.Any(id => !evidenceFacts.ContainsKey(id))))
                throw new InvalidOperationException("A recommendation did not reference verified diagnosis evidence.");

            switch (recommendation.Kind)
            {
                case PlanRecommendationKind.ExecutableAction:
                    if (!catalog.Contains(recommendation.ActionId))
                        throw new InvalidOperationException("The model proposed an action outside the local capability registry.");
                    var action = catalog.Get(recommendation.ActionId);
                    recommendation.Risk = action.Risk;
                    recommendation.RequiresRestart = action.RequiresRestart;
                    recommendation.ResourceId = recommendation.UpdateId = recommendation.ScriptLanguage = recommendation.Script = "";
                    recommendation.ReviewWarnings = [];
                    break;
                case PlanRecommendationKind.ManualGuidance:
                    RejectExecutableFields(recommendation);
                    recommendation.ReviewWarnings = ["Manual guidance is not executed or verified by NeuroTune."];
                    break;
                case PlanRecommendationKind.ScriptArtifact:
                    if (!string.IsNullOrWhiteSpace(recommendation.ActionId) || !string.IsNullOrWhiteSpace(recommendation.ResourceId) ||
                        !string.IsNullOrWhiteSpace(recommendation.UpdateId))
                        throw new InvalidOperationException("A script artifact attempted to masquerade as an executable capability.");
                    recommendation.ReviewWarnings = ScriptReviewService.Analyze(recommendation.ScriptLanguage, recommendation.Script);
                    break;
                case PlanRecommendationKind.ExternalResource:
                    if (knownResources is null || !knownResources.TryGetValue(recommendation.ResourceId, out var resource))
                        throw new InvalidOperationException("The model proposed an unknown external resource.");
                    recommendation.ActionId = recommendation.UpdateId = recommendation.ScriptLanguage = recommendation.Script = "";
                    recommendation.Risk = resource.Risk;
                    recommendation.RequiresRestart = resource.RequiresRestart;
                    recommendation.SourceReferences = [new SourceReference
                    {
                        Title = "PrimeBuild-reviewed artifact source",
                        Url = resource.SourceUrl,
                        Grade = "PrimeBuild verified (URL and SHA-256 pinned)"
                    }];
                    recommendation.ReviewWarnings = [];
                    break;
                case PlanRecommendationKind.UpdateNotice:
                    if (knownUpdates is null || !knownUpdates.TryGetValue(recommendation.UpdateId, out var update))
                        throw new InvalidOperationException("The model proposed an unknown update notice.");
                    recommendation.ActionId = recommendation.ResourceId = recommendation.ScriptLanguage = recommendation.Script = "";
                    recommendation.Title = $"{update.Vendor} {update.Kind}: {update.Model}";
                    recommendation.Risk = RiskLevel.Low;
                    recommendation.RequiresRestart = false;
                    recommendation.SourceReferences = [new SourceReference
                    {
                        Title = $"Official {update.Vendor} support",
                        Url = update.OfficialUrl,
                        Grade = "Official vendor"
                    }];
                    recommendation.ReviewWarnings = [];
                    break;
                default:
                    throw new InvalidOperationException("The model returned an unknown recommendation kind.");
            }
        }
        if (result.Findings.Any(x => x is null || string.IsNullOrWhiteSpace(x.Title) ||
            string.IsNullOrWhiteSpace(x.EvidenceId) || string.IsNullOrWhiteSpace(x.CurrentValue) ||
            string.IsNullOrWhiteSpace(x.Assessment)))
            throw new InvalidOperationException("A diagnosis finding did not include valid evidence.");
        foreach (var finding in result.Findings)
        {
            finding.Title = finding.Title.Trim();
            finding.EvidenceId = finding.EvidenceId.Trim();
            finding.CurrentValue = finding.CurrentValue.Trim();
            finding.Assessment = finding.Assessment.Trim();
            if (finding.Title.Length > 300 || finding.EvidenceId.Length > 500 ||
                finding.CurrentValue.Length > 20_000 || finding.Assessment.Length > 2_000)
                throw new InvalidOperationException("A diagnosis finding was too long.");
            if (evidenceFacts is not null && (!evidenceFacts.TryGetValue(finding.EvidenceId, out var observed) ||
                !observed.Equals(finding.CurrentValue, StringComparison.Ordinal)))
                throw new InvalidOperationException("The model cited evidence that was not present in the local scan.");
        }
        result.Findings = result.Findings.DistinctBy(x => x.EvidenceId, StringComparer.Ordinal).Take(30).ToList();
        result.Recommendations = result.Recommendations
            .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        return result;
    }

    private static List<string> NormalizeList(List<string>? values, int count, int length, string field)
    {
        values ??= [];
        if (values.Count > count || values.Any(value => value is null || value.Trim().Length > length))
            throw new InvalidOperationException($"A recommendation {field} list was too large.");
        return values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal).ToList();
    }

    private static List<SourceReference> ValidateSources(List<SourceReference>? sources)
    {
        sources ??= [];
        if (sources.Count > 12) throw new InvalidOperationException("A recommendation cited too many sources.");
        foreach (var source in sources)
        {
            source.Title = source.Title?.Trim() ?? "";
            source.Url = source.Url?.Trim() ?? "";
            source.Grade = source.Grade?.Trim() ?? "Unrated";
            if (source.Title.Length is 0 or > 300 || source.Url.Length > 2_000 || source.Grade.Length > 100)
                throw new InvalidOperationException("A recommendation source was invalid.");
            if (source.Url.Length > 0 && (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)))
                throw new InvalidOperationException("A recommendation source URL was invalid.");
        }
        return sources;
    }

    private static void RejectExecutableFields(PlanRecommendation recommendation)
    {
        if (!string.IsNullOrWhiteSpace(recommendation.ActionId) || !string.IsNullOrWhiteSpace(recommendation.ResourceId) ||
            !string.IsNullOrWhiteSpace(recommendation.UpdateId) || !string.IsNullOrWhiteSpace(recommendation.Script))
            throw new InvalidOperationException("Manual guidance contained an executable field.");
        recommendation.ScriptLanguage = "";
    }

    public static IReadOnlyDictionary<string, string> BuildEvidenceFacts(SystemProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["system:operating-system"] = profile.OperatingSystem ?? "Unavailable",
            ["system:cpu"] = profile.Cpu ?? "Unavailable",
            ["system:memory"] = profile.Memory ?? "Unavailable",
            ["system:active-power-plan"] = profile.ActivePowerPlan ?? "Unavailable"
        };
        var gpus = profile.Gpus ?? [];
        for (var index = 0; index < gpus.Count; index++) facts[$"hardware:gpu:{index}"] = gpus[index] ?? "Unavailable";
        Add("hardware", profile.HardwareCapabilities);
        Add("firmware", profile.FirmwareAndMemory);
        Add("component", profile.ComponentIdentities);
        Add("baseline", profile.FactoryBaselines);
        Add("boot", profile.BootConfiguration);
        Add("windows", profile.WindowsSettings);
        Add("gaming", profile.GamingSettings);
        Add("network", profile.NetworkSettings);
        Add("registry", profile.PerformanceRegistry);
        AddList("storage", profile.Disks);
        AddList("network-adapter", profile.NetworkAdapters);
        AddList("software", profile.InstalledSoftware);
        AddList("driver", profile.RelevantDrivers);
        AddList("device-issue", profile.DeviceIssues);
        AddList("software-signal", profile.SoftwareSignals);
        AddList("gaming-launcher", profile.DetectedLaunchers);
        AddList("gaming-game", profile.DetectedGames);
        AddList("gaming-executable", profile.GameExecutables);
        AddList("gaming-graphics-api", profile.GraphicsApiSignals);
        AddList("gaming-gpu-preference", profile.PerAppGpuPreferences);
        AddList("gaming-display", profile.DisplayTopology);
        AddList("gaming-active-gpu", profile.ActiveGpuMappings);
        AddList("runtime-process", profile.TopProcesses);
        AddList("startup", profile.StartupItems);
        AddList("service", profile.AutomaticServices);
        AddList("conflict-observation", profile.PolicyConflicts);
        foreach (var capability in profile.TelemetryCapabilities ?? [])
            facts[$"telemetry:{capability.Name}"] = $"{capability.Status}: {capability.Detail}";
        foreach (var key in facts.Keys.ToList()) facts[key] = ProfileSanitizer.Redact(facts[key]);
        return facts;

        void Add(string prefix, Dictionary<string, string>? values)
        {
            foreach (var (key, value) in values ?? []) facts[$"{prefix}:{key}"] = value ?? "Unavailable";
        }

        void AddList(string prefix, List<string>? values)
        {
            var items = values ?? [];
            for (var index = 0; index < items.Count; index++) facts[$"{prefix}:{index}"] = items[index] ?? "Unavailable";
        }
    }

    public static IReadOnlyDictionary<string, string> MergeEvidenceFacts(
        IReadOnlyDictionary<string, string> profileFacts,
        IReadOnlyDictionary<string, string>? measurementFacts)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var fact in profileFacts) merged[fact.Key] = fact.Value;
        if (measurementFacts is null) return merged;
        foreach (var fact in measurementFacts)
        {
            if (!fact.Key.StartsWith("measurement:", StringComparison.Ordinal) ||
                fact.Key.Any(character => !(char.IsLetterOrDigit(character) || character is ':' or '.' or '_' or '-')) ||
                fact.Value.Length > 64 ||
                !(bool.TryParse(fact.Value, out _) || double.TryParse(fact.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _)))
                throw new InvalidOperationException("Measurement evidence was not normalized.");
            merged[fact.Key] = fact.Value;
        }
        return merged;
    }

    public static EvidencePayloadReport MeasureEvidence(IReadOnlyDictionary<string, string> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var classes = facts.Keys.GroupBy(ClassifyEvidence).ToDictionary(group => group.Key, group => group.Count());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(facts).Length;
        return new(facts.Count, bytes, MaxSinglePassEvidenceBytes, bytes <= MaxSinglePassEvidenceBytes, classes);
    }

    public static EvidencePrivacy ClassifyEvidence(string evidenceId) => evidenceId.Split(':', 2)[0] switch
    {
        "software" or "driver" or "device-issue" or "software-signal" or "runtime-process" or "startup" or "service" or
        "gaming-launcher" or "gaming-game" or "gaming-executable"
            => EvidencePrivacy.SoftwareInventory,
        "conflict-observation" => EvidencePrivacy.General,
        _ => EvidencePrivacy.SystemConfiguration
    };

    internal static Dictionary<string, string> SelectInitialEvidence(
        IReadOnlyDictionary<string, string> evidenceFacts, IEnumerable<ConflictPattern> conflicts)
    {
        var conflictEvidence = conflicts.SelectMany(conflict => conflict.EvidenceIds).ToHashSet(StringComparer.Ordinal);
        return evidenceFacts.Where(fact => ClassifyEvidence(fact.Key) == EvidencePrivacy.SystemConfiguration &&
            (fact.Key.StartsWith("system:", StringComparison.Ordinal) ||
             fact.Key.StartsWith("windows:MMAgent ", StringComparison.Ordinal) ||
             fact.Key.StartsWith("windows:Power policy ", StringComparison.Ordinal) ||
             fact.Key == "hardware:Memory pressure sample" ||
             fact.Key is "network:Effective RSS settings" or "network:Effective RSC settings" or "network:Active adapter power management" ||
             conflictEvidence.Contains(fact.Key)))
            .ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.Ordinal);
    }

    public static Uri ValidateBaseUrl(UserSettings settings)
    {
        var raw = settings.Provider switch
        {
            LlmProvider.OpenRouter => "https://openrouter.ai/api/v1",
            LlmProvider.OpenAI => "https://api.openai.com/v1",
            LlmProvider.Anthropic => "https://api.anthropic.com/v1",
            LlmProvider.DeepSeek => "https://api.deepseek.com/v1",
            _ => settings.BaseUrl
        };
        if (!Uri.TryCreate(raw?.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Enter a valid provider base URL.");
        if (uri.Scheme == "http" && !IsLoopbackHost(uri.Host))
            throw new InvalidOperationException("Remote custom providers must use HTTPS. HTTP is allowed only for local models.");
        return uri;
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    private static Uri BuildEndpoint(UserSettings settings, string route) =>
        new($"{ValidateBaseUrl(settings).AbsoluteUri.TrimEnd('/')}/{route}");

    private static void ValidateSettings(UserSettings settings, string? apiKey)
    {
        ValidateBaseUrl(settings);
        if (settings.RequiresApiKey && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Enter an API key or use an available browser sign-in option.");
    }

    private static HttpRequestMessage CreateOpenAiRequest(UserSettings settings, string? apiKey, string prompt)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(settings, "chat/completions"));
        AddAuthentication(request, settings, apiKey);
        request.Content = JsonContent(new
        {
            model = settings.Model,
            temperature = 0.1,
            messages = new[] { new { role = "user", content = prompt } }
        });
        return request;
    }

    private static HttpRequestMessage CreateAnthropicRequest(UserSettings settings, string? apiKey, string prompt)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(settings, "messages"));
        AddAuthentication(request, settings, apiKey);
        request.Content = JsonContent(new
        {
            model = settings.Model,
            max_tokens = 6000,
            temperature = 0.1,
            messages = new[] { new { role = "user", content = prompt } }
        });
        return request;
    }

    private static void AddAuthentication(HttpRequestMessage request, UserSettings settings, string? apiKey)
    {
        if (settings.Protocol == ApiProtocol.Anthropic)
        {
            if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            return;
        }
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (settings.Provider == LlmProvider.OpenRouter)
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/PrimeBuild-pc/NeuroTune");
            request.Headers.TryAddWithoutValidation("X-Title", "NeuroTune");
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxResponseCharacters * 4L)
            throw new InvalidOperationException("The provider response was too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var body = new StringBuilder();
        var buffer = new char[8_192];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) is var read && read > 0)
        {
            body.Append(buffer, 0, read);
            if (body.Length > MaxResponseCharacters)
                throw new InvalidOperationException("The provider response was too large.");
        }
        return body.ToString();
    }

    private static StringContent JsonContent(object value) => new(
        JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static string ExtractContent(ApiProtocol protocol, string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return protocol == ApiProtocol.Anthropic
                ? json.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? ""
                : json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("The provider response was not recognized.");
        }
    }
}
