using NeuroTune;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};

try
{
    var command = args.FirstOrDefault() ?? throw new InvalidOperationException("Missing agent command.");
    if (command == "measurement-watchdog")
    {
        if (args.Length != 2 || !Guid.TryParse(args[1], out var watchdogId)) throw new InvalidOperationException("Invalid watchdog session ID.");
        new MeasurementService().Watchdog(watchdogId);
        return;
    }
    var input = await Console.In.ReadToEndAsync();
    var settingsService = new SettingsService();
    var catalog = new OptimizationCatalog();
    var backup = new BackupService();
    object? result = command switch
    {
        "get-state" => GetState(settingsService),
        "provider-defaults" => LlmClient.Defaults(Read<LlmProvider>(input)),
        "save-provider" => SaveProvider(Read<SaveProviderRequest>(input), settingsService),
        "oauth-openrouter" => await OAuth(settingsService),
        "models" => await Models(settingsService, catalog),
        "scan" => await Scan(catalog, settingsService, ReadOptional<ScanRequest>(input)),
        "analyze-local" => AnalyzeLocal(Read<DiagnoseRequest>(input)),
        "diagnose" => await Diagnose(Read<DiagnoseRequest>(input), settingsService, catalog),
        "actions" => Actions(catalog),
        "apply" => await Apply(Read<ApplyRequest>(input), catalog, backup),
        "history" => backup.LoadHistory(),
        "rollback" => await Rollback(Read<RollbackRequest>(input), catalog, backup),
        "measurement-workloads" => new MeasurementService().Workloads(),
        "measurement-start" => StartMeasurement(Read<MeasurementStartRequest>(input)),
        "measurement-stop" => new MeasurementService().Stop(Read<MeasurementIdRequest>(input).SessionId),
        "measurement-cancel" => new MeasurementService().Cancel(Read<MeasurementIdRequest>(input).SessionId),
        "measurement-analyze" => AnalyzeMeasurement(Read<MeasurementIdRequest>(input)),
        "measurement-list" => new MeasurementService().List(),
        "measurement-compare" => new MeasurementService().Compare(Read<MeasurementCompareRequest>(input)),
        "measurement-topology" => new MeasurementService().Topology(),
        "measurement-gpu-candidates" => new MeasurementService().GpuAffinityCandidates(Read<GpuCandidateRequest>(input)),
        "measurement-gpu-affinity-inspect" => new MeasurementService().GpuAffinityPolicy(Read<GpuAffinityInspectRequest>(input)),
        "measurement-delete" => DeleteMeasurement(Read<MeasurementIdRequest>(input)),
        _ => throw new InvalidOperationException($"Unknown agent command: {command}")
    };
    Write(new AgentResponse(true, result, null));
}
catch (Exception exception)
{
    Write(new AgentResponse(false, null, exception.Message));
    Environment.ExitCode = 1;
}

T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, json)
    ?? throw new InvalidOperationException("The agent request was empty or invalid.");

T? ReadOptional<T>(string value) where T : class =>
    string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<T>(value, json);

void Write(object value) => Console.Write(JsonSerializer.Serialize(value, json));

object GetState(SettingsService service)
{
    var settings = service.Load();
    return new { settings, hasCredential = !string.IsNullOrWhiteSpace(service.LoadApiKey(settings)) };
}

object SaveProvider(SaveProviderRequest request, SettingsService service)
{
    LlmClient.ValidateBaseUrl(request.Settings);
    var existing = service.LoadApiKey(request.Settings);
    if (request.Settings.RequiresApiKey && string.IsNullOrWhiteSpace(request.ApiKey) && string.IsNullOrWhiteSpace(existing))
        throw new InvalidOperationException("This provider requires an API key or browser sign-in.");
    service.Save(request.Settings, request.ApiKey);
    return new { saved = true, hasCredential = request.Settings.RequiresApiKey };
}

async Task<object> OAuth(SettingsService service)
{
    var key = await new OpenRouterOAuthService().SignInAsync();
    var settings = LlmClient.Defaults(LlmProvider.OpenRouter);
    service.Save(settings, key);
    return new { settings, hasCredential = true };
}

async Task<object> Models(SettingsService service, OptimizationCatalog actionCatalog)
{
    var settings = service.Load();
    var key = service.LoadApiKey(settings);
    var models = await new LlmClient(actionCatalog).ListModelsAsync(settings, key);
    return new { models };
}

async Task<object> Scan(OptimizationCatalog actionCatalog, SettingsService settingsService, ScanRequest? request)
{
    var profileTask = Task.Run(() => new SystemProfiler().Collect(
        phase => Console.Error.WriteLine(phase), request?.OptionalTelemetryConsent == true));
    var snapshotTask = Task.Run(() => new PerformanceSnapshotService().Collect());
    await Task.WhenAll(profileTask, snapshotTask);
    var profile = await profileTask;
    var evidence = LlmClient.BuildEvidenceFacts(profile);
    var updateNotices = new OfficialUpdateAdvisor().Analyze(profile);
    var payloadReport = LlmClient.MeasureEvidence(evidence);
    new PayloadMetricsService().Record(payloadReport, settingsService.Load(), "local-scan-completed");
    return new
    {
        profile,
        sanitizedProfile = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
        payloadReport,
        updateNotices,
        snapshot = await snapshotTask,
        actions = Actions(actionCatalog)
    };
}

object AnalyzeLocal(DiagnoseRequest request)
{
    if (request.Profile is null || request.Goals is null)
        throw new InvalidOperationException("Local analysis requires a system profile and tuning goals.");
    request.Goals.Validate();
    return ConflictAnalyzer.Analyze(request.Profile, request.Goals);
}

async Task<object> Diagnose(DiagnoseRequest request, SettingsService service, OptimizationCatalog actionCatalog)
{
    if (request.Profile is null || request.Goals is null)
        throw new InvalidOperationException("Diagnosis requires a system profile and tuning goals.");
    var settings = service.Load();
    var key = service.LoadApiKey(settings);
    var measurementEvidence = new MeasurementService().BuildNormalizedEvidence(request.MeasurementSessionIds ?? []);
    var evidence = LlmClient.MergeEvidenceFacts(LlmClient.BuildEvidenceFacts(request.Profile), measurementEvidence);
    var report = LlmClient.MeasureEvidence(evidence);
    try
    {
        var result = await new LlmClient(actionCatalog).DiagnoseAsync(request.Profile, request.Goals, settings, key, measurementEvidence);
        new PayloadMetricsService().Record(report, settings, "diagnosis-completed");
        return result;
    }
    catch
    {
        new PayloadMetricsService().Record(report, settings, "provider-error");
        throw;
    }
}

MeasurementSession StartMeasurement(MeasurementStartRequest request)
{
    var service = new MeasurementService();
    var session = service.Start(request, Path.Combine(AppContext.BaseDirectory, "NeuroTuneLatency.wprp"));
    StartWatchdog(session.Id);
    return session;
}

MeasurementSession AnalyzeMeasurement(MeasurementIdRequest request)
{
    Console.Error.WriteLine("Parsing ETW events and applying trace quality gates…");
    return new MeasurementService().Analyze(request.SessionId);
}

object? DeleteMeasurement(MeasurementIdRequest request)
{
    new MeasurementService().Delete(request.SessionId);
    return null;
}

void StartWatchdog(Guid sessionId)
{
    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The NeuroTune agent executable path is unavailable.");
    var start = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden
    };
    start.ArgumentList.Add("measurement-watchdog");
    start.ArgumentList.Add(sessionId.ToString("D"));
    Process.Start(start)?.Dispose();
}

object Actions(OptimizationCatalog actionCatalog) => actionCatalog.All.Select(action => new
{
    action.Id,
    action.Name,
    action.Description,
    action.Category,
    risk = action.Risk,
    action.RequiresRestart,
    availability = action.Inspect()
}).ToList();

Task<OperationManifest> Apply(ApplyRequest request, OptimizationCatalog actionCatalog, BackupService backupService) =>
    new OptimizationEngine(actionCatalog, backupService).ApplyAsync(request.ActionIds, request.HighRiskConfirmed);

async Task<object?> Rollback(RollbackRequest request, OptimizationCatalog actionCatalog, BackupService backupService)
{
    var manifest = backupService.LoadHistory().FirstOrDefault(x => x.Id == request.OperationId)
        ?? throw new InvalidOperationException("The selected operation was not found.");
    await new OptimizationEngine(actionCatalog, backupService).RollbackAsync(manifest);
    return null;
}

sealed record AgentResponse(bool Ok, object? Data, string? Error);
sealed record SaveProviderRequest(UserSettings Settings, string? ApiKey);
sealed record DiagnoseRequest(SystemProfile? Profile, TuningGoals? Goals, List<Guid>? MeasurementSessionIds = null);
sealed record ApplyRequest(List<string> ActionIds, bool HighRiskConfirmed = false);
sealed record RollbackRequest(Guid OperationId);
sealed record ScanRequest(bool OptionalTelemetryConsent);
