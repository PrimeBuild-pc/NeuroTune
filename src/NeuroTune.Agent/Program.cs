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
        "system-one-status" => SystemOneService.Status(),
        "system-one-install" => await SystemOneService.InstallAsync(Read<SystemOneOptions>(input), Console.Error.WriteLine),
        "system-one-configure" => SystemOneService.Configure(Read<SystemOneConfigureRequest>(input)),
        "system-one-models" => await SystemOneService.ListOpenRouterModelsAsync(catalog),
        "system-one-remove" => SystemOneService.Remove(),
        "provider-defaults" => LlmClient.Defaults(Read<LlmProvider>(input)),
        "save-provider" => SaveProvider(Read<SaveProviderRequest>(input), settingsService),
        "oauth-openrouter" => await OAuth(settingsService),
        "oauth-chatgpt" => await ChatGptSignIn(Read<ChatGptSignInRequest>(input), settingsService),
        "chatgpt-signout" => await ChatGptSignOut(Read<ChatGptSignInRequest>(input), settingsService),
        "models" => await Models(settingsService, catalog),
        "scan" => await Scan(catalog, settingsService, ReadOptional<ScanRequest>(input)),
        "defender-status" => WindowsSecurityAudit.ReadDefender(),
        "defender-scan-current" => new DefenderScanService().Current(),
        "defender-scan" => await new DefenderScanService().StartAsync(Read<DefenderScanRequest>(input)),
        "defender-scan-review" => new DefenderScanService().AcknowledgeInterruption(Read<DefenderScanReviewRequest>(input).ReviewedInWindowsSecurity),
        "support-preview" => SupportingAttachments.Preview(Read<SupportingAttachmentsRequest>(input)),
        "run-create" => CreateRun(Read<RunCreateRequest>(input)),
        "run-get" => new OptimizationRunService().Load(Read<RunIdRequest>(input).RunId),
        "run-list" => new OptimizationRunService().List(),
        "run-reconcile" => ReconcileRun(Read<RunIdRequest>(input), catalog, backup),
        "run-resume-after-restart" => new OptimizationRunService().ResumeAfterRestart(Read<RunIdRequest>(input).RunId),
        "run-dismiss" => new OptimizationRunService().Dismiss(Read<RunIdRequest>(input).RunId),
        "run-keep" => new OptimizationRunService().Keep(Read<RunIdRequest>(input).RunId),
        "analyze-local" => AnalyzeLocal(Read<DiagnoseRequest>(input)),
        "diagnose" => await Diagnose(Read<DiagnoseRequest>(input), settingsService, catalog),
        "actions" => Actions(catalog),
        "apply" => await Apply(Read<ApplyRequest>(input), catalog, backup),
        "history" => backup.LoadHistory(),
        "rollback" => await Rollback(Read<RollbackRequest>(input), catalog, backup),
        "measurement-workloads" => new MeasurementService().Workloads(),
        "measurement-live" => await new PerformanceSnapshotService().CollectProcessorsAsync(),
        "measurement-driver-details" => DriverInspection.Read(Read<DriverInspectionRequest>(input).Module),
        "firmware-read" => FirmwareInspection.Read(Read<FirmwareReadRequest>(input).ReadConsent),
        "scewin-inspect" => ScewinService.Inspect(Read<ScewinDirectoryRequest>(input).Directory),
        "scewin-import" => ScewinService.Import(Read<ScewinImportRequest>(input)),
        "scewin-export" => await ScewinService.ExportAsync(Read<ScewinExportRequest>(input), Console.Error.WriteLine),
        "measurement-start" => StartMeasurement(Read<MeasurementStartRequest>(input)),
        "measurement-stop" => new MeasurementService(Console.Error.WriteLine).Stop(Read<MeasurementIdRequest>(input).SessionId),
        "measurement-cancel" => new MeasurementService(Console.Error.WriteLine).Cancel(Read<MeasurementIdRequest>(input).SessionId),
        "measurement-analyze" => AnalyzeMeasurement(Read<MeasurementIdRequest>(input)),
        "measurement-frame-import" => new MeasurementService().ImportFrameTimes(Read<FrameTimeImportRequest>(input)),
        "measurement-list" => new MeasurementService().List(),
        "measurement-compare" => await CompareMeasurement(Read<MeasurementCompareRequest>(input)),
        "measurement-explain" => await ExplainMeasurement(Read<MeasurementExplanationAgentRequest>(input), settingsService, catalog),
        "measurement-topology" => new MeasurementService().Topology(),
        "measurement-gpu-candidates" => new MeasurementService().GpuAffinityCandidates(Read<GpuCandidateRequest>(input)),
        "measurement-gpu-affinity-inspect" => new MeasurementService().GpuAffinityPolicy(Read<GpuAffinityInspectRequest>(input)),
        "measurement-delete" => DeleteMeasurement(Read<MeasurementIdRequest>(input)),
        "power-plan-list" => ListPowerPlans(ReadOptional<PowerPlanDirectoryRequest>(input)),
        "power-plan-stage" => StagePowerPlan(Read<PowerPlanPathRequest>(input)),
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
    var chatGptAccounts = new ChatGptOAuthService().Accounts();
    return new
    {
        settings,
        isRecording = MeasurementService.HasActiveRecording(),
        chatGptAccounts,
        hasCredential = settings.Provider == LlmProvider.ChatGpt
        ? chatGptAccounts.Any(account => account.Id == settings.ChatGptAccountId && account.Connected)
        : !string.IsNullOrWhiteSpace(service.LoadApiKey(settings))
    };
}

object SaveProvider(SaveProviderRequest request, SettingsService service)
{
    LlmClient.ValidateBaseUrl(request.Settings);
    LlmClient.ValidateInvestigationBudget(request.Settings);
    if (request.Settings.Provider == LlmProvider.ChatGpt)
    {
        if (!string.IsNullOrWhiteSpace(request.ApiKey)) throw new InvalidOperationException("ChatGPT plan authorization is separate from API-key billing.");
        var account = new ChatGptOAuthService().Accounts().SingleOrDefault(account => account.Id == request.Settings.ChatGptAccountId)
            ?? throw new InvalidOperationException("Sign in with ChatGPT or select a saved account.");
        service.Save(request.Settings);
        return new { saved = true, hasCredential = account.Connected };
    }
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

async Task<object> ChatGptSignIn(ChatGptSignInRequest request, SettingsService service)
{
    var oauth = new ChatGptOAuthService();
    var accountId = await oauth.SignInAsync(request.AccountId);
    var current = service.Load();
    var settings = LlmClient.Defaults(LlmProvider.ChatGpt);
    if (current.Provider == LlmProvider.ChatGpt && current.ChatGptAccountId == accountId) settings.Model = current.Model;
    settings.ChatGptAccountId = accountId;
    service.Save(settings);
    var accounts = oauth.Accounts();
    return new { settings, chatGptAccounts = accounts, hasCredential = accounts.Single(account => account.Id == accountId).Connected };
}

async Task<object> ChatGptSignOut(ChatGptSignInRequest request, SettingsService service)
{
    var revoked = await new ChatGptOAuthService().SignOutAsync(request.AccountId);
    return new { remoteRevocationConfirmed = revoked, chatGptAccounts = new ChatGptOAuthService().Accounts(), state = GetState(service) };
}

async Task<string?> Credential(SettingsService service, UserSettings settings) => settings.Provider == LlmProvider.ChatGpt
    ? await new ChatGptOAuthService().AccessTokenAsync(settings.ChatGptAccountId) : service.LoadApiKey(settings);

async Task<object> Models(SettingsService service, OptimizationCatalog actionCatalog)
{
    var settings = service.Load();
    var key = await Credential(service, settings);
    var client = new LlmClient(actionCatalog);
    var models = await client.ListModelsAsync(settings, key);
    return new { models, modelLabels = client.ModelLabels };
}

async Task<object> Scan(OptimizationCatalog actionCatalog, SettingsService settingsService, ScanRequest? request)
{
    var profileTask = Task.Run(() => new SystemProfiler().Collect(
        phase => Console.Error.WriteLine(phase), request?.OptionalTelemetryConsent == true, true, request?.FirmwareReadConsent == true,
        request?.PrivacySecurityReadConsent == true));
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
        actions = Actions(new OptimizationCatalog())
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
    var language = ResponseLanguage.Read(request.Language);
    if (request.Profile is null || request.Goals is null)
        throw new InvalidOperationException("Diagnosis requires a system profile and tuning goals.");
    var settings = service.Load();
    var key = await Credential(service, settings);
    var runService = new OptimizationRunService();
    var run = request.RunId is { } runId ? runService.Load(runId) : null;
    var measurementIds = run is null
        ? request.MeasurementSessionIds ?? []
        : run.BaselineSessionIds.Concat(run.CandidateSessionIds).Concat(run.DiagnosticSessionIds).ToList();
    var measurementEvidence = new MeasurementService().BuildNormalizedEvidence(measurementIds);
    var attachments = SupportingAttachments.Normalize(request.Attachments, request.ImagesConfirmed);
    var evidence = SupportingAttachments.MergeEvidence(LlmClient.MergeEvidenceFacts(LlmClient.BuildEvidenceFacts(request.Profile), measurementEvidence), attachments);
    if (run is not null)
    {
        if (JsonSerializer.Serialize(run.Goals, json) != JsonSerializer.Serialize(request.Goals, json))
            throw new InvalidOperationException("The diagnosis goals do not match the persisted optimization run.");
        if (JsonSerializer.Serialize(run.SupportingAttachments, json) != JsonSerializer.Serialize(SupportingAttachments.Describe(attachments), json))
            throw new InvalidOperationException("Supporting files do not match this run. Screenshots are not stored locally; reattach matching prepared files or dismiss and start a new diagnosis.");
        var originalEvidence = run.EvidenceFacts.Where(fact => !fact.Key.StartsWith("investigation:", StringComparison.Ordinal)).ToList();
        if (originalEvidence.Count != evidence.Count || originalEvidence.Any(fact =>
            !evidence.TryGetValue(fact.Key, out var value) || value != fact.Value))
            throw new InvalidOperationException("The optimization run evidence no longer matches this diagnosis request.");
        runService.BeginDiagnosis(run.Id);
    }
    var report = LlmClient.MeasureEvidence(evidence);
    try
    {
        var outcome = await new LlmClient(actionCatalog).PlanAsync(request.Profile, request.Goals, settings, key, measurementEvidence, attachments: attachments, imagesConfirmed: request.ImagesConfirmed,
            mode: run?.Mode ?? InvestigationMode.MeasuredOptimization, language: language);
        if (run is not null)
        {
            if (outcome.UsedLocalFallback)
            {
                var reason = outcome.Audit.LastOrDefault(entry => !entry.Accepted)?.Reason ?? outcome.StopReason;
                runService.RecordDiagnosisAttemptFailure(run.Id, reason, outcome);
            }
            else runService.RecordDiagnosis(run.Id, outcome);
        }
        new PayloadMetricsService().Record(report, settings,
            outcome.UsedLocalFallback ? "provider-fallback" : "diagnosis-completed");
        return outcome.Diagnosis;
    }
    catch (Exception exception)
    {
        if (run is not null)
            runService.RecordDiagnosisAttemptFailure(run.Id, exception.Message);
        new PayloadMetricsService().Record(report, settings, "provider-error");
        throw;
    }
}

OptimizationRun CreateRun(RunCreateRequest request)
{
    if (request.Profile is null || request.Goals is null)
        throw new InvalidOperationException("An optimization run requires a system profile and tuning goals.");
    request.Goals.Validate();
    var measurementService = new MeasurementService();
    var ids = (request.MeasurementSessionIds ?? []).Distinct().ToList();
    var sessions = measurementService.List().Where(session => ids.Contains(session.Id)).ToList();
    if (sessions.Count != ids.Count)
        throw new InvalidOperationException("Every optimization run measurement must exist locally.");
    return new OptimizationRunService().Create(request.Profile, request.Goals, sessions, request.Attachments, request.ImagesConfirmed, request.Mode);
}

MeasurementSession StartMeasurement(MeasurementStartRequest request)
{
    using var securityGate = DefenderScanService.EnterMeasurementGate();
    if (request.OptimizationRunId is { } runId)
    {
        var run = new OptimizationRunService().Load(runId);
        var expectedLabel = run.State switch
        {
            OptimizationRunState.BaselinePending or OptimizationRunState.BaselineReady => MeasurementLabel.Baseline,
            OptimizationRunState.CandidatePending => MeasurementLabel.Candidate,
            _ => throw new InvalidOperationException("This optimization run is not ready to record a measurement.")
        };
        if (request.Label != expectedLabel)
            throw new InvalidOperationException($"This optimization run requires a {expectedLabel} measurement.");
    }
    // Redirecting watchdog stdio alone still lets .NET inherit the Agent's original Rust IPC handles.
    // Clear their inheritance before WPR starts; failure must not leave a capture without its watchdog.
    ResponsePipes.PreventInheritance();
    var service = new MeasurementService(Console.Error.WriteLine);
    var session = service.Start(request, Path.Combine(AppContext.BaseDirectory, "NeuroTuneLatency.wprp"));
    StartWatchdog(session.Id);
    return session;
}

MeasurementSession AnalyzeMeasurement(MeasurementIdRequest request)
{
    Console.Error.WriteLine("Parsing ETW events and applying trace quality gates…");
    var session = new MeasurementService().Analyze(request.SessionId);
    if (request.OptimizationRunId is { } requestedRunId && session.OptimizationRunId != requestedRunId)
        throw new InvalidOperationException("The measurement does not belong to the requested optimization run.");
    if (session.OptimizationRunId is { } runId)
        new OptimizationRunService().AttachMeasurement(runId, session);
    return session;
}

async Task<MeasurementComparison> CompareMeasurement(MeasurementCompareRequest request)
{
    var comparison = new MeasurementService().Compare(request);
    var advisory = await SystemOneService.AnalyzeAsync("measurement comparison", JsonSerializer.Serialize(comparison),
        comparison.Metrics.ToDictionary(metric => metric.EvidenceId, metric => JsonSerializer.Serialize(metric)), Console.Error.WriteLine);
    if (advisory is not null) comparison.SystemOneAdvisories.Add(advisory);
    if (request.OptimizationRunId is { } runId && comparison.RejectionReasons.Count == 0)
        new OptimizationRunService().RecordComparison(runId, comparison);
    return comparison;
}

async Task<string> ExplainMeasurement(MeasurementExplanationAgentRequest request, SettingsService service, OptimizationCatalog actionCatalog)
{
    var language = ResponseLanguage.Read(request.Language);
    if (MeasurementService.HasActiveRecording()) throw new InvalidOperationException("Stop/cancel recording before asking the selected provider AI to explain results.");
    var comparison = new MeasurementService().Compare(request.Comparison);
    if (comparison.RejectionReasons.Count > 0) throw new InvalidOperationException("The comparison was rejected; the selected AI cannot bypass its validity checks.");
    var settings = service.Load();
    var key = await Credential(service, settings);
    return await new LlmClient(actionCatalog).ExplainComparisonAsync(comparison, request.Goals, settings, key, language: language);
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
        WindowStyle = ProcessWindowStyle.Hidden,
        // The detached watchdog must not inherit the caller's response pipes: UI readers wait for EOF.
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    start.ArgumentList.Add("measurement-watchdog");
    start.ArgumentList.Add(sessionId.ToString("D"));
    using var watchdog = Process.Start(start) ?? throw new InvalidOperationException("The measurement watchdog could not be started.");
    watchdog.StandardInput.Close();
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

object ListPowerPlans(PowerPlanDirectoryRequest? request)
{
    var directory = string.IsNullOrWhiteSpace(request?.Directory) ? PowerPlanStore.SuggestedSourceDirectory : request.Directory.Trim();
    return new { directory, plans = new PowerPlanStore().ListSource(directory) };
}

object StagePowerPlan(PowerPlanPathRequest request)
{
    var plan = new PowerPlanStore().Stage(request.Path);
    return new { plan, actions = Actions(new OptimizationCatalog()) };
}

async Task<OperationManifest> Apply(ApplyRequest request, OptimizationCatalog actionCatalog, BackupService backupService)
{
    var runService = new OptimizationRunService();
    if (request.RunId == Guid.Empty)
        throw new InvalidOperationException("Every system write must belong to an optimization run.");
    var run = runService.Load(request.RunId);
    if (run.Mode == InvestigationMode.AuditOnly) throw new InvalidOperationException("Audit-only runs cannot apply Windows changes.");
    if (run.State == OptimizationRunState.BaselineReady)
        run = runService.Approve(run.Id, request.ActionIds, request.HighRiskConfirmed, actionCatalog);
    else if (run.State == OptimizationRunState.Approved)
    {
        if (!run.ApprovedActionIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(request.ActionIds) || run.HighRiskConfirmed != request.HighRiskConfirmed)
            throw new InvalidOperationException("The requested actions do not match the persisted approval.");
    }
    else throw new InvalidOperationException($"Optimization run {run.Id} is not ready to apply.");

    var operationId = Guid.NewGuid();
    runService.BeginApply(run.Id, operationId);
    try
    {
        var manifest = await new OptimizationEngine(actionCatalog, backupService)
            .ApplyAsync(request.ActionIds, request.HighRiskConfirmed, operationId, run.Id);
        var restart = request.ActionIds.Select(actionCatalog.Get).Any(action => action.RequiresRestart);
        runService.RecordApplyCompleted(run.Id, restart);
        await AnnotateOperation(manifest, "application-result", JsonSerializer.Serialize(new { manifest.Status, verified = manifest.Actions.All(action => action.Applied), actions = request.ActionIds.Select(id => actionCatalog.Get(id).Name) }));
        return manifest;
    }
    catch (Exception exception)
    {
        try
        {
            var manifest = backupService.Load(operationId);
            runService.RecordApplyFailure(run.Id, manifest, exception.Message);
            if (manifest is not null) await AnnotateOperation(manifest, "application-error", exception.Message);
        }
        catch (Exception journalException)
        {
            try { runService.RequireRecovery(run.Id, $"Apply failed and recovery bookkeeping also failed: {journalException.Message}"); }
            catch { /* Preserve the original apply exception; Applying already requires recovery. */ }
        }
        throw;
    }
}

OptimizationRun ReconcileRun(RunIdRequest request, OptimizationCatalog actionCatalog, BackupService backupService)
{
    var service = new OptimizationRunService();
    var measurements = new MeasurementService().List();
    var run = service.ReconcileMeasurements(request.RunId, measurements);
    if (run.State == OptimizationRunState.Scanned) return run;
    if (run.State == OptimizationRunState.Hypothesizing)
        return service.RecordDiagnosisAttemptFailure(run.Id, "The provider diagnosis was interrupted and can be retried.");
    if (!run.RequiresRecovery || run.OperationId is not { } operationId) return run;
    OperationManifest? manifest;
    try { manifest = backupService.Load(operationId); }
    catch (InvalidOperationException exception)
    {
        return service.RequireRecovery(run.Id, exception.Message);
    }
    if (manifest is null && run.State == OptimizationRunState.Applying)
        return service.RecordApplyFailure(run.Id, null, "The apply stopped before its operation journal was created.");
    if (manifest is null) return run;
    if (manifest.OptimizationRunId != run.Id)
        return service.RequireRecovery(run.Id, "The operation journal does not link back to this optimization run.");

    if (run.State == OptimizationRunState.Applying && manifest.Status == "Completed")
    {
        var restart = backupService.RequiresRestart(manifest);
        return service.RecordApplyCompleted(run.Id, restart);
    }
    if (run.State == OptimizationRunState.Applying)
        return service.RecordApplyFailure(run.Id, manifest, manifest.Error ?? "The interrupted apply requires recovery.");
    if (run.State == OptimizationRunState.RollingBack && !manifest.HasPendingRollback &&
        manifest.Status.Contains("Rollback completed", StringComparison.OrdinalIgnoreCase))
        return service.RecordRollbackCompleted(run.Id);
    return run;
}

async Task AnnotateOperation(OperationManifest manifest, string phase, string context)
{
    // Optional advice cannot turn a completed/rolled-back transaction into a failure or change its actions.
    try
    {
        var advisory = await SystemOneService.AnalyzeAsync(phase, context, progress: Console.Error.WriteLine);
        if (advisory is not null) { manifest.SystemOneAdvisories.Add(advisory); new BackupService().Save(manifest); }
    }
    catch (Exception error) { Console.Error.WriteLine($"Optional application annotation unavailable: {error.GetType().Name}. Transaction state unchanged."); }
}

async Task<object?> Rollback(RollbackRequest request, OptimizationCatalog actionCatalog, BackupService backupService)
{
    var manifest = backupService.Load(request.OperationId)
        ?? throw new InvalidOperationException("The selected operation was not found.");
    var runService = new OptimizationRunService();
    if (manifest.OptimizationRunId is { } linkedRunId)
    {
        if (request.RunId != linkedRunId)
            throw new InvalidOperationException("A run-linked operation must be rolled back through its optimization run.");
        var run = runService.Load(linkedRunId);
        if (run.OperationId != manifest.Id || manifest.OptimizationRunId != run.Id)
            throw new InvalidOperationException("The operation does not belong to this optimization run.");
        runService.BeginRollback(linkedRunId);
    }
    else if (request.RunId is not null)
        throw new InvalidOperationException("This legacy operation is not linked to an optimization run.");
    try
    {
        await new OptimizationEngine(actionCatalog, backupService).RollbackAsync(manifest);
        if (manifest.OptimizationRunId is { } completedRunId) runService.RecordRollbackCompleted(completedRunId);
    }
    catch (Exception exception)
    {
        if (manifest.OptimizationRunId is { } failedRunId) runService.RequireRecovery(failedRunId, exception.Message);
        throw;
    }
    return null;
}

sealed record AgentResponse(bool Ok, object? Data, string? Error);
sealed record SaveProviderRequest(UserSettings Settings, string? ApiKey);
sealed record DiagnoseRequest(SystemProfile? Profile, TuningGoals? Goals, List<Guid>? MeasurementSessionIds = null, Guid? RunId = null,
    List<SupportingAttachment>? Attachments = null, bool ImagesConfirmed = false, JsonElement Language = default);
sealed record MeasurementExplanationAgentRequest(MeasurementCompareRequest Comparison, TuningGoals Goals, JsonElement Language = default);
sealed record ApplyRequest(List<string> ActionIds, bool HighRiskConfirmed, Guid RunId);
sealed record RollbackRequest(Guid OperationId, Guid? RunId = null);
sealed record ScanRequest(bool OptionalTelemetryConsent, bool FirmwareReadConsent = false, bool PrivacySecurityReadConsent = false);
sealed record RunCreateRequest(SystemProfile? Profile, TuningGoals? Goals, List<Guid>? MeasurementSessionIds = null,
    List<SupportingAttachment>? Attachments = null, bool ImagesConfirmed = false, InvestigationMode Mode = InvestigationMode.MeasuredOptimization);
sealed record RunIdRequest(Guid RunId);
sealed record DefenderScanReviewRequest(bool ReviewedInWindowsSecurity);
sealed record PowerPlanDirectoryRequest(string Directory);
sealed record PowerPlanPathRequest(string Path);
