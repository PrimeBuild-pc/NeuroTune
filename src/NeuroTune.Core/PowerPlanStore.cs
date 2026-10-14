using System.Security.Cryptography;

namespace NeuroTune;

public sealed record CustomPowerPlanFile(string Name, string Path, long SizeBytes, string Sha256);

public sealed class PowerPlanStore
{
    private const int MaximumPlanBytes = 1024 * 1024;
    private readonly string _directory;

    public PowerPlanStore(string? directory = null) =>
        _directory = directory ?? Path.Combine(SettingsService.DataDirectory, "power-plans");

    private const string BundledPrefix = "NeuroTune.power-plans.";

    // Only the Microsoft plans ship with NeuroTune; third-party plans are imported by the user.
    public static string ExtractBundled(string? directory = null)
    {
        directory ??= Path.Combine(SettingsService.DataDirectory, "bundled-power-plans");
        Directory.CreateDirectory(directory);
        var assembly = typeof(PowerPlanStore).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(BundledPrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var content = memory.ToArray();
            var path = Path.Combine(directory, resource[BundledPrefix.Length..]);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(content)) File.WriteAllBytes(path, content);
        }
        return directory;
    }

    public IReadOnlyList<CustomPowerPlanFile> ListSource(string? directory = null)
    {
        var source = Path.GetFullPath(string.IsNullOrWhiteSpace(directory) ? ExtractBundled() : directory.Trim());
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The power-plan directory was not found.");
        return Directory.EnumerateFiles(source, "*.pow", SearchOption.TopDirectoryOnly)
            .Select(Read).OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase).Take(512).ToList();
    }

    public CustomPowerPlanFile Stage(string path)
    {
        var plan = Read(Path.GetFullPath(path));
        var content = File.ReadAllBytes(plan.Path);
        if (!Convert.ToHexString(SHA256.HashData(content)).Equals(plan.Sha256, StringComparison.Ordinal))
            throw new IOException("The power-plan file changed while it was being staged.");
        return Import(plan.Name, content);
    }

    // fileName comes from the UI (a browser File name), so it must be a bare .pow name, never a path.
    public CustomPowerPlanFile Import(string fileName, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !Path.GetExtension(fileName).Equals(".pow", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .pow power-plan files are supported.");
        if (content.Length is <= 0 or > MaximumPlanBytes) throw new InvalidOperationException("The power-plan file is empty or too large.");
        var hash = Convert.ToHexString(SHA256.HashData(content));
        Directory.CreateDirectory(_directory);
        var existing = Directory.EnumerateFiles(_directory, $"{hash}--*.pow", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (existing is not null) return Read(existing);
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (name.Length > 100) name = name[..100];
        var destination = Path.Combine(_directory, $"{hash}--{name}.pow");
        File.WriteAllBytes(destination, content);
        var staged = Read(destination);
        if (!staged.Sha256.Equals(hash, StringComparison.Ordinal))
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
