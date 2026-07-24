using Microsoft.Win32;
using System.Diagnostics;
using System.Management;
using System.Text.Json;

namespace NeuroTune;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static readonly string OperationsDirectory = Path.Combine(SettingsService.DataDirectory, "operations");

    public OperationManifest Prepare(IEnumerable<OptimizationAction> actions)
    {
        var manifest = new OperationManifest();
        manifest.DirectoryPath = Path.Combine(OperationsDirectory, $"{manifest.CreatedAt:yyyyMMdd-HHmmss}-{manifest.Id:N}");
        Directory.CreateDirectory(manifest.DirectoryPath);
        manifest.RestorePoint = CreateRestorePoint($"NeuroTune {manifest.Id:N}");

        var backupDirectory = Path.Combine(manifest.DirectoryPath, "registry");
        Directory.CreateDirectory(backupDirectory);
        foreach (var path in actions.Select(x => x.RegistryExportPath).Where(x => x is not null).Distinct())
            ExportRegistry(path!, backupDirectory);

        manifest.Status = "Backup completato";
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
            if (returnValue != 0) throw new InvalidOperationException($"codice Windows {returnValue}");
            return description;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Punto di ripristino non creato ({exception.Message}). Abilita Protezione sistema e riprova.", exception);
        }
    }

    public void Save(OperationManifest manifest)
    {
        Directory.CreateDirectory(manifest.DirectoryPath);
        AtomicWrite(Path.Combine(manifest.DirectoryPath, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
    }

    public IReadOnlyList<OperationManifest> LoadHistory()
    {
        if (!Directory.Exists(OperationsDirectory)) return [];
        return Directory.GetFiles(OperationsDirectory, "manifest.json", SearchOption.AllDirectories)
            .Select(path =>
            {
                try
                {
                    var item = JsonSerializer.Deserialize<OperationManifest>(File.ReadAllText(path));
                    if (item is not null) item.DirectoryPath = Path.GetDirectoryName(path)!;
                    return item;
                }
                catch { return null; }
            })
            .Where(x => x is not null).Cast<OperationManifest>()
            .OrderByDescending(x => x.CreatedAt).ToList();
    }

    private static void ExportRegistry(string registryPath, string outputDirectory)
    {
        if (!RegistryPathExists(registryPath))
        {
            File.WriteAllText(Path.Combine(outputDirectory, SafeName(registryPath) + ".missing"), registryPath);
            return;
        }

        var output = Path.Combine(outputDirectory, SafeName(registryPath) + ".reg");
        var start = new ProcessStartInfo("reg.exe") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "export", registryPath, output, "/y" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Impossibile avviare reg.exe.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Backup registro fallito: {error.Trim()}");
    }

    private static bool RegistryPathExists(string registryPath)
    {
        var split = registryPath.Split('\\', 2);
        var hive = split[0].Equals("HKLM", StringComparison.OrdinalIgnoreCase) ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(split[1]);
        return key is not null;
    }

    private static string SafeName(string path) => string.Concat(path.Select(x => char.IsLetterOrDigit(x) ? x : '_'));

    private static void AtomicWrite(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, true);
    }
}
