using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace NeuroTune;

// F5: NVIDIA driver profiles through NVIDIA Profile Inspector (MIT) from the command line. Its import is "Replace": it
// resets the profile and writes only what the .nip lists (including the application list), so every import starts
// from an export of the same profile and a restore re-imports the exported profile exactly.
public static class NvidiaProfileInspector
{
    internal const string BaseProfile = "Base Profile";
    private const string ZipUrl = "https://github.com/Orbmu2k/nvidiaProfileInspector/releases/download/v3.0.2.1/nvidiaProfileInspector.zip";
    private const string ZipSha256 = "88dcf3514111e8de630688467c03c36d8c2a8ad9ebc8073f27c069f82b75bb40";
    private const string ExeSha256 = "1ebd8129b3c564bf226291fb3344819fd59668066f0c5e03334a69a04a62859e";

    // Under the per-machine install directory: an elevated agent must never run an exe a standard user can replace.
    internal static string Directory => Path.Combine(AppContext.BaseDirectory, "tools", "nvpi");
    private static string Exe => Path.Combine(Directory, "nvidiaProfileInspector.exe");

    public static object Status() => new { installed = Unavailable() is null, reason = Unavailable() ?? "" };

    internal static string? Unavailable()
    {
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "nvapi64.dll"))) return "No NVIDIA driver (nvapi64.dll) is installed";
        if (!File.Exists(Exe)) return "NVIDIA Profile Inspector is not installed; install it from Advanced tools";
        return Sha256(Exe) == ExeSha256 ? null : "The NVIDIA Profile Inspector file does not match the pinned release";
    }

    public static async Task<object> InstallAsync(CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var zip = await http.GetByteArrayAsync(ZipUrl, cancellationToken);
        if (Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant() != ZipSha256)
            throw new InvalidOperationException("The downloaded NVIDIA Profile Inspector archive does not match the pinned SHA-256.");
        System.IO.Directory.CreateDirectory(Directory);
        using (var archive = new ZipArchive(new MemoryStream(zip)))
            foreach (var entry in archive.Entries.Where(entry => entry.Name is "nvidiaProfileInspector.exe" or "nvidiaProfileInspector.exe.config" or "Reference.xml"))
                entry.ExtractToFile(Path.Combine(Directory, entry.Name), overwrite: true);
        if (Sha256(Exe) != ExeSha256) { File.Delete(Exe); throw new InvalidOperationException("The extracted executable does not match the pinned SHA-256."); }
        return Status();
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Run(params string[] arguments)
    {
        if (Unavailable() is { } reason) throw new InvalidOperationException(reason);
        var start = new ProcessStartInfo(Exe) { WorkingDirectory = Directory };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return WindowsCommand.RunAsync(start, TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
    }

    // -exportCustomized writes CustomProfiles_<time>.nip next to the executable and exits.
    internal static string ExportCustomized()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        Run("-exportCustomized");
        var file = new DirectoryInfo(Directory).GetFiles("CustomProfiles_*.nip").Where(item => item.LastWriteTimeUtc >= before)
            .OrderByDescending(item => item.LastWriteTimeUtc).FirstOrDefault() ?? throw new InvalidOperationException("NVIDIA Profile Inspector did not export the profiles.");
        try { return File.ReadAllText(file.FullName); }
        finally { file.Delete(); }
    }

    internal static void Import(string nip)
    {
        var file = Path.Combine(Directory, $"neurotune-{Guid.NewGuid():N}.nip");
        File.WriteAllText(file, nip);
        try { Run(file, "-silentImport"); }
        finally { File.Delete(file); }
    }

    internal static XElement? FindProfile(string exportXml, string profile) => XDocument.Parse(exportXml).Root?.Elements("Profile")
        .FirstOrDefault(item => string.Equals((string?)item.Element("ProfileName"), profile, StringComparison.Ordinal));

    internal static Dictionary<uint, uint> Settings(XElement? profile) => (profile?.Element("Settings")?.Elements("ProfileSetting") ?? [])
        .Where(item => (string?)item.Element("ValueType") is null or "Dword" && uint.TryParse((string?)item.Element("SettingValue"), out _))
        .ToDictionary(item => (uint)item.Element("SettingID")!, item => uint.Parse((string)item.Element("SettingValue")!));

    // The exported node keeps its applications and other customizations; only the requested DWORDs are set on top.
    internal static string BuildImport(XElement? exported, string profile, IReadOnlyDictionary<uint, uint> values)
    {
        var node = exported is null
            ? new XElement("Profile", new XElement("ProfileName", profile), new XElement("Executeables"), new XElement("Settings"))
            : new XElement(exported);
        var settings = node.Element("Settings") ?? new XElement("Settings");
        if (settings.Parent is null) node.Add(settings);
        foreach (var (id, value) in values)
        {
            settings.Elements("ProfileSetting").Where(item => (uint?)item.Element("SettingID") == id).Remove();
            settings.Add(new XElement("ProfileSetting", new XElement("SettingNameInfo", ""), new XElement("SettingID", id),
                new XElement("SettingValue", value), new XElement("ValueType", "Dword")));
        }
        return new XDocument(new XElement("ArrayOfProfile", node)).ToString();
    }
}
