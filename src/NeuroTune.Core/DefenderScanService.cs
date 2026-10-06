using System.Diagnostics;
using System.Text.Json;

namespace NeuroTune;

public sealed record DefenderScanRequest(string ScanType, bool ScanConsent, bool RemediationConsent, bool NetworkConsent);
public sealed record DefenderScanOperation(int SchemaVersion, Guid Id, string ScanType, DateTimeOffset RequestedAtUtc,
    string State, string Detail, bool ScanConsent, bool RemediationConsent, bool NetworkConsent);
public sealed record DefenderScanResult(DefenderScanOperation Operation, DefenderReport Report);

// Separate from InvestigationService: the AI cannot invoke this scanner through a reader request.
public sealed class DefenderScanService
{
    private const string GateName = @"Global\NeuroTuneDefenderScan";
    private const string ScanDisclosure = "Defender may quarantine/remove detections and use network/cloud/sample submission under its existing policy. Closing NeuroTune or stopping its command is not guaranteed to stop the antivirus service. Review progress and Protection history in Windows Security; no malware-removal rollback is promised.";
    private readonly JournalStorage _storage;
    private readonly Func<DefenderReport> _read;
    private readonly Func<string, TimeSpan, Task<string>> _execute;
    private readonly Func<bool> _recording;
    private readonly Func<bool> _measuredRun;
    private readonly string _gateName;
    private string PathForCurrent => Path.Combine(_storage.DirectoryPath, "current.json");

    public DefenderScanService() : this(Path.Combine(JournalStorage.UserDirectory, "defender-scans"), true,
        WindowsSecurityAudit.ReadDefender, Execute, MeasurementService.HasActiveRecording,
        () => new OptimizationRunService().List().Any(run => run.Mode != InvestigationMode.AuditOnly && !OptimizationRunStateMachine.IsTerminal(run.State)), GateName)
    { }

    internal DefenderScanService(string directory, bool privileged, Func<DefenderReport> read,
        Func<string, TimeSpan, Task<string>> execute, Func<bool> recording, Func<bool> measuredRun, string gateName)
    {
        _storage = new(directory, privileged); _read = read; _execute = execute;
        _recording = recording; _measuredRun = measuredRun; _gateName = gateName;
    }

    public DefenderScanOperation? Current()
    {
        _storage.CheckLegacyJournals();
        _storage.CheckPath(PathForCurrent);
        if (!File.Exists(PathForCurrent)) return null;
        var operation = JsonSerializer.Deserialize<DefenderScanOperation>(_storage.Read(PathForCurrent));
        ValidateOperation(operation);
        return operation;
    }

    public Task<DefenderScanResult> StartAsync(DefenderScanRequest request) => Task.Run(() =>
    {
        ValidateRequest(request); // Before Windows reads, journal creation or process execution.
        using var gate = AcquireGate(_gateName);
        if (_recording() || _measuredRun()) throw new InvalidOperationException("Finish the capture/measured optimization run before starting Defender. It was not cancelled.");
        var prior = Current();
        if (prior?.State is "Running" or "InterruptedOrFailed")
            throw new InvalidOperationException("The previous Defender request needs review in Windows Security before another scan. A stopped command does not prove the service stopped.");
        ValidateProtection(_read());
        var operation = new DefenderScanOperation(1, Guid.NewGuid(), request.ScanType, DateTimeOffset.UtcNow, "Running", ScanDisclosure,
            request.ScanConsent, request.RemediationConsent, request.NetworkConsent);
        Save(operation); // Failure here must prevent the first scanner command.
        try
        {
            _execute(Command(request.ScanType), request.ScanType == "quick" ? TimeSpan.FromHours(1) : TimeSpan.FromHours(24)).GetAwaiter().GetResult();
            operation = operation with { State = "CommandReturned", Detail = "The Defender command returned successfully; this does not prove no malware, exact scan coverage or complete remediation. " + ScanDisclosure };
            Save(operation);
        }
        catch (Exception error)
        {
            operation = operation with { State = "InterruptedOrFailed", Detail = $"Defender command did not complete reliably ({error.GetType().Name}). Scan/remediation may still be active; inspect Windows Security before continuing. " + ScanDisclosure };
            Save(operation);
            return new DefenderScanResult(operation, _read());
        }
        return new DefenderScanResult(operation, _read());
    });

    public DefenderScanOperation AcknowledgeInterruption(bool reviewedInWindowsSecurity)
    {
        if (!reviewedInWindowsSecurity) throw new InvalidOperationException("Confirm that Windows Security progress and Protection history were reviewed.");
        using var gate = AcquireGate(_gateName); // A live NeuroTune-owned scanner cannot be acknowledged away.
        var operation = Current() ?? throw new InvalidOperationException("No Defender request to review.");
        if (operation.State is not ("Running" or "InterruptedOrFailed")) throw new InvalidOperationException("This request does not require interruption review.");
        var report = _read();
        if (report.Status != "observed" || report.ScanState != "idle")
            throw new InvalidOperationException("Defender scan state is busy/unknown. Review it in Windows Security; no automatic reset was performed.");
        operation = operation with { State = "ReviewedAfterInterruption", Detail = "The user reviewed an uncertain/interrupted request; the latest scan event indicates idle, not a clean PC or proven completion of this scan. " + ScanDisclosure };
        Save(operation);
        return operation;
    }

    // Hold the same gate through recorder startup, avoiding a scan/startup check-then-start race.
    public static IDisposable EnterMeasurementGate()
    {
        var gate = AcquireGate(GateName);
        try
        {
            var operation = new DefenderScanService().Current();
            if (operation?.State is "Running" or "InterruptedOrFailed")
                throw new InvalidOperationException("Review the pending Defender scan request in Windows Security before recording. Antivirus work may still be active.");
            if (operation is not null && WindowsSecurityAudit.ReadDefender().ScanState != "idle")
                throw new InvalidOperationException("Defender scan state is busy/unknown after a scan request; recording remains blocked until Windows Security reports idle.");
            return gate;
        }
        catch { gate.Dispose(); throw; }
    }

    internal static void ValidateRequest(DefenderScanRequest request)
    {
        if (request is null || request.ScanType is not ("quick" or "full") || !request.ScanConsent || !request.RemediationConsent || !request.NetworkConsent)
            throw new InvalidOperationException("Choose quick/full and explicitly consent to the scan, possible quarantine/removal and configured network/cloud/sample use.");
    }

    internal static void ValidateProtection(DefenderReport report)
    {
        if (report.Status != "observed" || report.Protection?.AntivirusEnabled != true || report.Protection.RunningMode != "Normal" || report.ScanState != "idle")
            throw new InvalidOperationException("Defender must report active antivirus in Normal mode and an idle scan event. Passive/third-party/unavailable/busy/unknown states need manual Windows Security review; no protection was changed.");
    }

    internal static string Command(string scanType) => scanType switch
    {
        "quick" => "$ErrorActionPreference='Stop'; Import-Module ($PSHOME + '\\Modules\\Defender\\Defender.psd1') -ErrorAction Stop; Defender\\Start-MpScan -ScanType QuickScan -ErrorAction Stop; 'CommandReturned'",
        "full" => "$ErrorActionPreference='Stop'; Import-Module ($PSHOME + '\\Modules\\Defender\\Defender.psd1') -ErrorAction Stop; Defender\\Start-MpScan -ScanType FullScan -ErrorAction Stop; 'CommandReturned'",
        _ => throw new InvalidOperationException("Unsupported Defender scan type.")
    };

    private static Task<string> Execute(string command, TimeSpan deadline)
    {
        var start = new ProcessStartInfo(WindowsCommand.PathFor("powershell.exe"));
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command }) start.ArgumentList.Add(argument);
        return WindowsCommand.RunAsync(start, deadline);
    }

    private void Save(DefenderScanOperation operation)
    {
        ValidateOperation(operation);
        _storage.Write(PathForCurrent, operation, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void ValidateOperation(DefenderScanOperation? operation)
    {
        if (operation is null || operation.SchemaVersion != 1 || operation.Id == Guid.Empty || operation.ScanType is not ("quick" or "full") ||
            operation.State is not ("Running" or "CommandReturned" or "InterruptedOrFailed" or "ReviewedAfterInterruption") ||
            operation.Detail is null || operation.Detail.Length is 0 or > 2000 ||
            !operation.ScanConsent || !operation.RemediationConsent || !operation.NetworkConsent || operation.RequestedAtUtc == default)
            throw new InvalidOperationException("Invalid Defender scan journal; no automatic repair/reset.");
    }

    private static IDisposable AcquireGate(string name)
    {
        var mutex = new Mutex(false, name);
        try
        {
            try { if (!mutex.WaitOne(TimeSpan.Zero)) throw new InvalidOperationException("A NeuroTune Defender/recorder operation is already active. It was not cancelled."); }
            catch (AbandonedMutexException) { }
            return new OwnedGate(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    private sealed class OwnedGate(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}
