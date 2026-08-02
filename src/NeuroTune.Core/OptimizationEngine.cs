namespace NeuroTune;

public sealed class OptimizationEngine
{
    private readonly OptimizationCatalog _catalog;
    private readonly BackupService _backup;
    private readonly PerformanceSnapshotService _performance;

    public OptimizationEngine(OptimizationCatalog catalog, BackupService backup, PerformanceSnapshotService? performance = null)
    {
        _catalog = catalog;
        _backup = backup;
        _performance = performance ?? new PerformanceSnapshotService();
    }

    public Task<OperationManifest> ApplyAsync(IEnumerable<string> actionIds, bool highRiskConfirmed = false) => Task.Run(() =>
    {
        var actions = actionIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(_catalog.Get).ToList();
        if (actions.Count == 0) throw new InvalidOperationException("Select at least one optimization.");
        if (actions.Any(action => action.Risk == RiskLevel.High) && !highRiskConfirmed)
            throw new InvalidOperationException("High-risk capabilities require a separate explicit confirmation.");

        var blocked = actions.Select(x => (Action: x, Availability: x.Inspect()))
            .Where(x => !x.Availability.CanApply).ToList();
        if (blocked.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                blocked.Select(x => $"{x.Action.Name}: {x.Availability.Status}")));

        using var mutex = new Mutex(false, @"Global\NeuroTuneOptimization");
        var acquired = Acquire(mutex);
        if (!acquired) throw new InvalidOperationException("Another NeuroTune operation is already running.");
        try
        {
            var before = _performance.Collect();
            var manifest = _backup.Prepare(actions);
            manifest.Before = before;
            try
            {
                manifest.Status = "Applying";
                _backup.Save(manifest);
                TestDelay();
                foreach (var action in actions)
                {
                    var record = new ActionRecord { ActionId = action.Id, OriginalState = action.Capture(), Attempted = true };
                    manifest.Actions.Add(record);
                    _backup.Save(manifest);
                    action.Apply();
                    if (!action.Verify()) throw new InvalidOperationException($"Verification failed for {action.Name}.");
                    record.Applied = true;
                    _backup.Save(manifest);
                }
                manifest.After = _performance.Collect();
                manifest.Status = "Completed";
                _backup.Save(manifest);
                return manifest;
            }
            catch (Exception exception)
            {
                manifest.Status = "Error — automatic rollback";
                manifest.Error = exception.Message;
                RollbackApplied(manifest);
                manifest.Status = Pending(manifest).Any()
                    ? "Error — rollback incomplete"
                    : "Error — rollback completed";
                _backup.Save(manifest);
                throw new InvalidOperationException($"Optimization stopped: {exception.Message}", exception);
            }
        }
        finally { mutex.ReleaseMutex(); }
    });

    public Task RollbackAsync(OperationManifest manifest) => Task.Run(() =>
    {
        using var mutex = new Mutex(false, @"Global\NeuroTuneOptimization");
        var acquired = Acquire(mutex);
        if (!acquired) throw new InvalidOperationException("Another NeuroTune operation is already running.");
        try
        {
            _backup.CreateRestorePoint($"NeuroTune before rollback {manifest.Id:N}");
            manifest.Status = "Rolling back";
            _backup.Save(manifest);
            TestDelay();
            RollbackApplied(manifest);
            manifest.Status = Pending(manifest).Any() ? "Rollback incomplete" : "Rollback completed";
            _backup.Save(manifest);
            if (manifest.Status == "Rollback incomplete")
                throw new InvalidOperationException("Some actions could not be restored. Review the operation details.");
        }
        finally { mutex.ReleaseMutex(); }
    });

    private void RollbackApplied(OperationManifest manifest)
    {
        foreach (var record in Pending(manifest).Reverse())
        {
            try
            {
                var action = _catalog.Get(record.ActionId);
                action.Restore(record.OriginalState);
                if (!string.Equals(action.Capture(), record.OriginalState, StringComparison.Ordinal))
                    throw new InvalidOperationException("The restored state did not match the saved snapshot.");
                record.RolledBack = true;
                record.Error = null;
            }
            catch (Exception exception) { record.Error = exception.Message; }
            _backup.Save(manifest);
        }
    }

    private static IEnumerable<ActionRecord> Pending(OperationManifest manifest) =>
        manifest.Actions.Where(x => (x.Attempted || x.Applied) && !x.RolledBack);

    private static bool Acquire(Mutex mutex)
    {
        try { return mutex.WaitOne(TimeSpan.Zero); }
        catch (AbandonedMutexException) { return true; }
    }

    private static void TestDelay()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("NEUROTUNE_TEST_STEP_DELAY_MS"), out var milliseconds) &&
            milliseconds is >= 1 and <= 10_000)
            Thread.Sleep(milliseconds);
    }
}
