using System.Security.Cryptography;

namespace NeuroTune;

public sealed record CustomPowerPlanFile(string Name, string Path, long SizeBytes, string Sha256);

public sealed class PowerPlanStore
{
    private const int MaximumPlanBytes = 1024 * 1024;
    private readonly string _directory;

    public PowerPlanStore(string? directory = null) =>
        _directory = directory ?? Path.Combine(SettingsService.DataDirectory, "power-plans");

    public static string SuggestedSourceDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Projects", "ZapTweaks", "resources", "Powerplans");

    public IReadOnlyList<CustomPowerPlanFile> ListSource(string? directory = null)
    {
        var source = Path.GetFullPath(string.IsNullOrWhiteSpace(directory) ? SuggestedSourceDirectory : directory.Trim());
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The power-plan directory was not found.");
        return Directory.EnumerateFiles(source, "*.pow", SearchOption.TopDirectoryOnly)
            .Select(Read).OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase).Take(512).ToList();
    }

    public CustomPowerPlanFile Stage(string path)
    {
        var plan = Read(Path.GetFullPath(path));
        Directory.CreateDirectory(_directory);
        var existing = Directory.EnumerateFiles(_directory, $"{plan.Sha256}--*.pow", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (existing is not null) return Read(existing);
        var name = Path.GetFileNameWithoutExtension(plan.Name);
        if (name.Length > 100) name = name[..100];
        var destination = Path.Combine(_directory, $"{plan.Sha256}--{name}.pow");
        File.WriteAllBytes(destination, File.ReadAllBytes(plan.Path));
        var staged = Read(destination);
        if (!staged.Sha256.Equals(plan.Sha256, StringComparison.Ordinal))
        {
            File.Delete(destination);
            throw new IOException("The power-plan file changed while it was being staged.");
        }
        return staged;
    }

    public IReadOnlyList<CustomPowerPlanFile> ListStaged()
    {
        if (!Directory.Exists(_directory)) return [];
        return Directory.EnumerateFiles(_directory, "*.pow", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                try
                {
                    var plan = Read(path);
                    return Path.GetFileName(path).StartsWith($"{plan.Sha256}--", StringComparison.OrdinalIgnoreCase) ? plan : null;
                }
                catch { return null; }
            })
            .Where(plan => plan is not null).Cast<CustomPowerPlanFile>().ToList();
    }

    public static bool Matches(CustomPowerPlanFile plan)
    {
        try
        {
            var current = Read(plan.Path);
            return current.SizeBytes == plan.SizeBytes && current.Sha256.Equals(plan.Sha256, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static CustomPowerPlanFile Read(string path)
    {
        if (!Path.GetExtension(path).Equals(".pow", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .pow power-plan files are supported.");
        var file = new FileInfo(path);
        if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.Length is <= 0 or > MaximumPlanBytes)
            throw new InvalidOperationException("The power-plan file is missing, linked, empty, or too large.");
        using var stream = file.OpenRead();
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        var separator = file.Name.IndexOf("--", StringComparison.Ordinal);
        var name = separator == 64 ? file.Name[(separator + 2)..] : file.Name;
        return new(name, file.FullName, file.Length, hash);
    }
}
