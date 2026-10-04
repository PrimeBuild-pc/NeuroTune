using Microsoft.Win32;
using System.Management;
using System.Globalization;
using System.Text.Json;

namespace NeuroTune;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static readonly string OperationsDirectory = Path.Combine(JournalStorage.UserDirectory, "operations");
    private readonly JournalStorage _storage;
    private readonly OptimizationCatalog _recoveryCatalog = new(includeDynamic: false);

    public BackupService() => _storage = new(OperationsDirectory, privileged: true);
    internal BackupService(string directory) => _storage = new(directory);

    public OperationManifest Prepare(IEnumerable<OptimizationAction> actions, Guid? operationId = null,
        Guid? optimizationRunId = null)
    {
        var history = LoadHistory();
        var manifest = new OperationManifest
        {
            Id = operationId ?? Guid.NewGuid(),
            OptimizationRunId = optimizationRunId
        };
        if (history.Any(item => item.Id == manifest.Id)) throw new InvalidOperationException("An operation journal already uses this ID.");
        manifest.DirectoryPath = Path.Combine(_storage.DirectoryPath, $"{manifest.CreatedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{manifest.Id:N}");
        _storage.EnsureDirectory(manifest.DirectoryPath);
        Save(manifest);

        manifest.RestorePoint = CreateRestorePoint($"NeuroTune {manifest.Id:N}");
        var backupDirectory = Path.Combine(manifest.DirectoryPath, "registry");
        _storage.EnsureDirectory(backupDirectory);
        foreach (var path in actions.Select(x => x.RegistryExportPath).Where(x => x is not null).Distinct())
            ExportRegistry(path!, backupDirectory);

        manifest.Status = "Backup completed";
        Save(manifest);
        return manifest;
    }

    public string CreateRestorePoint(string description)
    {
        try
        {
            using var restore = new ManagementClass(@"\\localhost\root\default", "SystemRestore", new ObjectGetOptions());
            using var parameters = restore.GetMethodParameters("CreateRestorePoint");
            parameters["Description"] = description;
            parameters["RestorePointType"] = 0;
            parameters["EventType"] = 100;
            using var result = restore.InvokeMethod("CreateRestorePoint", parameters, null);
            var returnValue = Convert.ToUInt32(result?["ReturnValue"] ?? uint.MaxValue);
            if (returnValue != 0) throw new InvalidOperationException($"Windows error code {returnValue}");

            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (RestorePointExists(description)) return description;
                Thread.Sleep(500);
            }
            throw new InvalidOperationException("Windows accepted the request but the restore point was not found");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"A verified restore point could not be created ({exception.Message}). Enable System Protection and try again.", exception);
        }
    }

    public void Save(OperationManifest manifest)
    {
        Validate(manifest);
        _storage.Write(Path.Combine(manifest.DirectoryPath, "manifest.json"), manifest, JsonOptions);
    }

    internal void RecordAttempt(OperationManifest manifest, ActionRecord record)
    {
        manifest.Actions.Add(record);
        try { Save(manifest); }
        catch
        {
            // This action has not run. Keep older valid records usable for automatic rollback.
            manifest.Actions.Remove(record);
            throw;
        }
    }

    public IReadOnlyList<OperationManifest> LoadHistory()
    {
        // Corrupt recovery state must remain visible as an error, never disappear from the write gate.
        _storage.CheckLegacyJournals();
        return _storage.Directories().Select(path => LoadPath(Path.Combine(path, "manifest.json")))
            .OrderByDescending(item => item.CreatedAt).ToList();
    }

    internal void Validate(OperationManifest manifest)
    {
        if (manifest.SchemaVersion != 3 || manifest.Id == Guid.Empty || manifest.OptimizationRunId == Guid.Empty ||
            manifest.Actions is null || manifest.Actions.Count > 100 || manifest.SystemOneAdvisories is null ||
            manifest.SystemOneAdvisories.Count > 100 || manifest.RestorePoint is null || manifest.RestorePoint.Length > 300 ||
            manifest.Status is not ("Preparing" or "Backup completed" or "Applying" or "Completed" or "Error — automatic rollback" or
                "Error — rollback incomplete" or "Error — rollback completed" or "Rolling back" or "Rollback incomplete" or "Rollback completed") ||
            manifest.Error?.Length > 16_384 || manifest.Actions.Any(record => record is null) ||
            manifest.Actions.Select(record => record.ActionId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Actions.Count ||
            manifest.Actions.Sum(record => (long)(record.OriginalState?.Length ?? 0)) > 4 * 1024 * 1024)
            throw new InvalidOperationException("The operation journal schema or records are invalid.");
        if (manifest.Status is "Preparing" or "Backup completed" && manifest.Actions.Count != 0 ||
            manifest.Status == "Completed" && (manifest.Actions.Count == 0 || manifest.Actions.Any(record => !record.Applied || record.RolledBack)) ||
            manifest.Status is "Rollback completed" or "Error — rollback completed" && manifest.HasPendingRollback ||
            manifest.Status is "Rollback incomplete" or "Error — rollback incomplete" && !manifest.HasPendingRollback)
            throw new InvalidOperationException("The operation status does not match its records.");
        var expected = Path.Combine(_storage.DirectoryPath, $"{manifest.CreatedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{manifest.Id:N}");
        if (!Path.GetFullPath(manifest.DirectoryPath).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The operation journal identity does not match its directory.");
        _storage.CheckPath(expected);
        foreach (var record in manifest.Actions)
        {
            if (string.IsNullOrWhiteSpace(record.ActionId) || record.ActionId.Length > 120 || record.Error?.Length > 16_384 ||
                !record.Attempted)
                throw new InvalidOperationException("The operation record is invalid.");
            _recoveryCatalog.ResolveRollback(record);
        }
    }

    public bool RequiresRestart(OperationManifest manifest)
    {
        Validate(manifest);
        return manifest.Actions.Any(record => _recoveryCatalog.ResolveRollback(record).RequiresRestart);
    }

    private OperationManifest LoadPath(string path)
    {
        try
        {
            var content = _storage.Read(path);
            using var json = JsonDocument.Parse(content);
            if (!json.RootElement.TryGetProperty("SchemaVersion", out _) || !json.RootElement.TryGetProperty("Id", out _) ||
                !json.RootElement.TryGetProperty("CreatedAt", out _) || !json.RootElement.TryGetProperty("Status", out _) ||
                !json.RootElement.TryGetProperty("Actions", out _)) throw new JsonException("Missing required journal fields.");
            var manifest = JsonSerializer.Deserialize<OperationManifest>(content) ?? throw new JsonException("The journal was empty.");
            manifest.DirectoryPath = Path.GetDirectoryName(path)!;
            Validate(manifest);
            return manifest;
        }
        catch (Exception exception)
        { throw new InvalidOperationException($"The operation journal is corrupt or untrusted: {path}", exception); }
    }

    public OperationManifest? Load(Guid id)
    {
        if (id == Guid.Empty) throw new InvalidOperationException("The operation ID was invalid.");
        _storage.CheckLegacyJournals();
        var paths = _storage.Directories()
            .Where(path => Path.GetFileName(path).EndsWith($"-{id:N}", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.Combine(path, "manifest.json")).ToList();
        if (paths.Count == 0) return null;
        if (paths.Count > 1) throw new InvalidOperationException("Multiple operation journals use the same ID.");
        try
        {
            var manifest = LoadPath(paths[0]);
            if (manifest.Id != id) throw new InvalidOperationException("The operation journal ID does not match its directory.");
            return manifest;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"The operation journal is corrupt: {paths[0]}", exception);
        }
    }

    private static bool RestorePointExists(string description)
    {
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(@"\\localhost\root\default"),
            new ObjectQuery("SELECT Description FROM SystemRestore"));
        return searcher.Get().Cast<ManagementBaseObject>()
            .Any(x => string.Equals(x["Description"]?.ToString(), description, StringComparison.Ordinal));
    }

    private static void ExportRegistry(string registryPath, string outputDirectory)
    {
        if (!RegistryPathExists(registryPath))
        {
            File.WriteAllText(Path.Combine(outputDirectory, SafeName(registryPath) + ".missing"), registryPath);
            return;
        }

        var output = Path.Combine(outputDirectory, SafeName(registryPath) + ".reg");
        _ = WindowsCommand.Run("reg.exe", "export", registryPath, output, "/y");
    }

    private static bool RegistryPathExists(string registryPath)
    {
        var split = registryPath.Split('\\', 2);
        var hive = split[0].Equals("HKLM", StringComparison.OrdinalIgnoreCase) ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(split[1]);
        return key is not null;
    }

    private static string SafeName(string path) => string.Concat(path.Select(x => char.IsLetterOrDigit(x) ? x : '_'));

}
