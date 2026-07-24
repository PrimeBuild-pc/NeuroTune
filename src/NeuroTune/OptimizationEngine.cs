namespace NeuroTune;

public sealed class OptimizationEngine
{
    private readonly OptimizationCatalog _catalog;
    private readonly BackupService _backup;

    public OptimizationEngine(OptimizationCatalog catalog, BackupService backup)
    {
        _catalog = catalog;
        _backup = backup;
    }

    public Task<OperationManifest> ApplyAsync(IEnumerable<string> actionIds) => Task.Run(() =>
    {
        var actions = actionIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(_catalog.Get).ToList();
        if (actions.Count == 0) throw new InvalidOperationException("Nessuna ottimizzazione selezionata.");

        using var mutex = new Mutex(false, @"Global\NeuroTuneOptimization");
        if (!mutex.WaitOne(TimeSpan.Zero)) throw new InvalidOperationException("È già in corso un'altra ottimizzazione.");
        try
        {
            var manifest = _backup.Prepare(actions);
            try
            {
                manifest.Status = "Applicazione";
                _backup.Save(manifest);
                foreach (var action in actions)
                {
                    var record = new ActionRecord { ActionId = action.Id, OriginalState = action.Capture() };
                    manifest.Actions.Add(record);
                    record.Applied = true; // Un tentativo parziale deve comunque entrare nel rollback.
                    _backup.Save(manifest);
                    action.Apply();
                    if (!action.Verify()) throw new InvalidOperationException($"Verifica fallita per {action.Name}.");
                    _backup.Save(manifest);
                }
                manifest.Status = "Completata";
                _backup.Save(manifest);
                return manifest;
            }
            catch (Exception exception)
            {
                manifest.Status = "Errore: rollback automatico";
                manifest.Error = exception.Message;
                RollbackApplied(manifest);
                manifest.Status = manifest.Actions.Where(x => x.Applied).All(x => x.RolledBack)
                    ? "Errore — rollback completato"
                    : "Errore — rollback incompleto";
                _backup.Save(manifest);
                throw new InvalidOperationException($"Ottimizzazione interrotta: {exception.Message}", exception);
            }
        }
        finally { mutex.ReleaseMutex(); }
    });

    public Task RollbackAsync(OperationManifest manifest) => Task.Run(() =>
    {
        using var mutex = new Mutex(false, @"Global\NeuroTuneOptimization");
        if (!mutex.WaitOne(TimeSpan.Zero)) throw new InvalidOperationException("È già in corso un'altra operazione.");
        try
        {
            _backup.CreateRestorePoint($"NeuroTune prima del rollback {manifest.Id:N}");
            manifest.Status = "Rollback in corso";
            _backup.Save(manifest);
            RollbackApplied(manifest);
            manifest.Status = manifest.Actions.Where(x => x.Applied).All(x => x.RolledBack)
                ? "Rollback completato"
                : "Rollback incompleto";
            _backup.Save(manifest);
            if (manifest.Status == "Rollback incompleto")
                throw new InvalidOperationException("Alcune azioni non sono state ripristinate. Controlla la cronologia.");
        }
        finally { mutex.ReleaseMutex(); }
    });

    private void RollbackApplied(OperationManifest manifest)
    {
        foreach (var record in manifest.Actions.Where(x => x.Applied && !x.RolledBack).Reverse())
        {
            try
            {
                _catalog.Get(record.ActionId).Restore(record.OriginalState);
                record.RolledBack = true;
                record.Error = null;
            }
            catch (Exception exception) { record.Error = exception.Message; }
            _backup.Save(manifest);
        }
    }
}
