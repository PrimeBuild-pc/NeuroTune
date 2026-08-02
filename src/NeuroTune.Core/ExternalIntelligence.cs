using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NeuroTune;

public enum ExternalArtifactKind { Cfg, Text, Patch }
public enum ArtifactDestinationMode { AppResolved, UserSelected }

public sealed record ExternalArtifactDefinition(
    string Id,
    ExternalArtifactKind Kind,
    RiskLevel Risk,
    bool RequiresRestart,
    string SourceUrl,
    string Sha256,
    string ContentType,
    long SizeBytes,
    IReadOnlyList<string> CompatibleGames,
    IReadOnlyList<string> CompatibleBuilds,
    ArtifactDestinationMode DestinationMode,
    string DestinationExtension,
    string BackupStrategy,
    string Verification)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 120 ||
            !Id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-'))
            throw new InvalidOperationException("An external artifact has an invalid ID.");
        if (!Uri.TryCreate(SourceUrl, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(source.UserInfo) || source.IsDefaultPort is false || source.Fragment.Length > 0)
            throw new InvalidOperationException($"Artifact {Id} does not use a canonical HTTPS source.");
        if (Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit) || SizeBytes is <= 0 or > 1_048_576)
            throw new InvalidOperationException($"Artifact {Id} has an invalid size or SHA-256.");
        var expectedExtension = Kind switch
        {
            ExternalArtifactKind.Cfg => ".cfg",
            ExternalArtifactKind.Text => ".txt",
            ExternalArtifactKind.Patch => ".patch",
            _ => throw new InvalidOperationException($"Artifact {Id} has an unsupported type.")
        };
        if (!DestinationExtension.Equals(expectedExtension, StringComparison.OrdinalIgnoreCase) ||
            ContentType is not ("text/plain" or "application/octet-stream") ||
            CompatibleGames.Count == 0 || CompatibleBuilds.Count == 0 ||
            string.IsNullOrWhiteSpace(BackupStrategy) || string.IsNullOrWhiteSpace(Verification))
            throw new InvalidOperationException($"Artifact {Id} has incomplete type or compatibility metadata.");
    }
}

public sealed class ExternalArtifactCatalog
{
    private readonly Dictionary<string, ExternalArtifactDefinition> _definitions;

    public ExternalArtifactCatalog(IEnumerable<ExternalArtifactDefinition>? definitions = null)
    {
        var items = definitions?.ToList() ?? [];
        foreach (var item in items) item.Validate();
        if (items.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw new InvalidOperationException("The external artifact catalog contains duplicate IDs.");
        _definitions = items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ExternalArtifactDefinition> All => _definitions.Values;
    public bool Contains(string id) => _definitions.ContainsKey(id);
    public ExternalArtifactDefinition Get(string id) => _definitions.TryGetValue(id, out var definition)
        ? definition
        : throw new InvalidOperationException($"External artifact is not PrimeBuild-approved: {id}");
}

public static class ExternalArtifactValidator
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Validate(
        ExternalArtifactDefinition definition,
        Uri finalSource,
        string contentType,
        ReadOnlySpan<byte> payload,
        string approvedRoot,
        string destination)
    {
        definition.Validate();
        var expectedSource = new Uri(definition.SourceUrl);
        if (finalSource != expectedSource || finalSource.Scheme != Uri.UriSchemeHttps ||
            !finalSource.Host.Equals(expectedSource.Host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The artifact source or redirect target did not match the reviewed URL.");
        contentType = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (!contentType.Equals(definition.ContentType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The artifact content type did not match the catalog.");
        if (payload.Length != definition.SizeBytes ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(definition.Sha256), SHA256.HashData(payload)))
            throw new InvalidOperationException("The artifact size or SHA-256 did not match the catalog.");

        string text;
        try { text = StrictUtf8.GetString(payload); }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException("The approved artifact was not valid UTF-8 text.", exception);
        }
        if (text.IndexOf('\0') >= 0 || text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            throw new InvalidOperationException("The approved artifact contained binary or unsafe control characters.");

        var root = Path.GetFullPath(approvedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(destination);
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(resolved).Equals(definition.DestinationExtension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The artifact destination escaped its approved root or changed type.");
        if ((File.Exists(resolved) || Directory.Exists(resolved)) &&
            File.GetAttributes(resolved).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("The artifact destination is a reparse point.");
        for (var current = Directory.GetParent(resolved); current is not null &&
            current.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase); current = current.Parent)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("The artifact destination traversed a reparse point.");
        }
        return resolved;
    }
}

public sealed class VerifiedArtifactAction
{
    private readonly ExternalArtifactDefinition _definition;
    private readonly byte[] _payload;
    private readonly string _destination;

    public VerifiedArtifactAction(ExternalArtifactDefinition definition, Uri finalSource, string contentType,
        byte[] payload, string approvedRoot, string destination)
    {
        _definition = definition;
        _payload = payload.ToArray();
        _destination = ExternalArtifactValidator.Validate(
            definition, finalSource, contentType, _payload, approvedRoot, destination);
    }

    public string Capture()
    {
        if (!File.Exists(_destination)) return System.Text.Json.JsonSerializer.Serialize(new ArtifactFileSnapshot(false, ""));
        var current = File.ReadAllBytes(_destination);
        if (current.Length > 4 * 1_048_576)
            throw new InvalidOperationException("The existing artifact is too large for an exact bounded backup.");
        return System.Text.Json.JsonSerializer.Serialize(new ArtifactFileSnapshot(true, Convert.ToBase64String(current)));
    }

    public void Apply() => AtomicWrite(_payload);

    public bool Verify()
    {
        if (!File.Exists(_destination)) return false;
        var current = File.ReadAllBytes(_destination);
        return current.Length == _definition.SizeBytes && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(current), Convert.FromHexString(_definition.Sha256));
    }

    public void Restore(string capturedState)
    {
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<ArtifactFileSnapshot>(capturedState)
            ?? throw new InvalidOperationException("The artifact backup snapshot is invalid.");
        if (!snapshot.Exists)
        {
            File.Delete(_destination);
            return;
        }
        byte[] content;
        try { content = Convert.FromBase64String(snapshot.ContentBase64); }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("The artifact backup snapshot is corrupt.", exception);
        }
        if (content.Length > 4 * 1_048_576)
            throw new InvalidOperationException("The artifact backup snapshot exceeded its bounded size.");
        AtomicWrite(content);
    }

    private void AtomicWrite(byte[] content)
    {
        var directory = Path.GetDirectoryName(_destination)
            ?? throw new InvalidOperationException("The artifact destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(_destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, _destination, true);
        }
        finally { File.Delete(temporary); }
    }

    private sealed record ArtifactFileSnapshot(bool Exists, string ContentBase64);
}

public sealed record ExternalApplicationDefinition(
    string Id, string Name, string UpstreamUrl, string Version, string Sha256,
    IReadOnlyList<string> FixedArguments, string License, string UninstallGuid);

public sealed class ExternalApplicationCatalog
{
    public IReadOnlyCollection<ExternalApplicationDefinition> All { get; } = Array.Empty<ExternalApplicationDefinition>();
}

public enum UpdateComponentKind { GpuDriver, ChipsetDriver, Bios }
public enum UpdateComparisonStatus { UpdateAvailable, Current, ComparisonUnavailable }

public sealed record DetectedUpdateComponent(
    UpdateComponentKind Kind, string Vendor, string Model, string InstalledVersion);

public sealed record OfficialUpdateRecord(
    UpdateComponentKind Kind, string Vendor, string Model, string LatestVersion, string OfficialUrl);

public sealed record UpdateNoticeDefinition(
    string Id,
    UpdateComponentKind Kind,
    string Vendor,
    string Model,
    string InstalledVersion,
    string LatestVersion,
    string OfficialUrl,
    UpdateComparisonStatus Status,
    string Reason);

public sealed class OfficialUpdateAdvisor
{
    private sealed record VendorSource(string CanonicalName, string OfficialUrl, string Domain);

    private static readonly Dictionary<string, VendorSource> Sources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NVIDIA"] = new("NVIDIA", "https://www.nvidia.com/en-us/drivers/", "nvidia.com"),
        ["AMD"] = new("AMD", "https://www.amd.com/en/support/download/drivers.html", "amd.com"),
        ["Intel"] = new("Intel", "https://www.intel.com/content/www/us/en/support/detect.html", "intel.com"),
        ["MSI"] = new("MSI", "https://www.msi.com/support", "msi.com"),
        ["ASUS"] = new("ASUS", "https://www.asus.com/support/download-center/", "asus.com"),
        ["Gigabyte"] = new("Gigabyte", "https://www.gigabyte.com/Support", "gigabyte.com"),
        ["ASRock"] = new("ASRock", "https://www.asrock.com/support/index.asp", "asrock.com")
    };

    private readonly IReadOnlyList<OfficialUpdateRecord> _records;

    public OfficialUpdateAdvisor(IEnumerable<OfficialUpdateRecord>? records = null)
    {
        _records = records?.ToList() ?? [];
        foreach (var record in _records)
        {
            var source = ResolveSource(record.Vendor)
                ?? throw new InvalidOperationException($"Update record vendor is not approved: {record.Vendor}");
            if (!IsOfficial(record.OfficialUrl, source.Domain) || string.IsNullOrWhiteSpace(record.Model) ||
                string.IsNullOrWhiteSpace(record.LatestVersion))
                throw new InvalidOperationException("An update record used an unofficial URL or incomplete identity.");
        }
    }

    public IReadOnlyList<UpdateNoticeDefinition> Analyze(SystemProfile profile)
    {
        var components = new List<DetectedUpdateComponent>();
        var gpuIdentities = profile.ComponentIdentities
            .Where(item => item.Key.StartsWith("GPU ", StringComparison.Ordinal) &&
                item.Key.EndsWith(" specification ID", StringComparison.Ordinal)).ToList();
        for (var index = 0; index < gpuIdentities.Count; index++)
        {
            var identity = gpuIdentities[index].Value;
            var vendor = identity.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                : identity.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ? "AMD"
                : identity.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) ? "Intel" : "";
            if (vendor.Length == 0) continue;
            var model = identity.Split('|').LastOrDefault()?.Trim() ?? "";
            var installed = index < profile.Gpus.Count
                ? Regex.Match(profile.Gpus[index], @"driver\s+([^,)]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim()
                : "";
            if (model.Length > 0) components.Add(new(UpdateComponentKind.GpuDriver, vendor, model, installed));
        }

        var cpuId = profile.ComponentIdentities.GetValueOrDefault("CPU specification ID", "");
        var cpuVendor = cpuId.StartsWith("AuthenticAMD", StringComparison.OrdinalIgnoreCase) ? "AMD"
            : cpuId.StartsWith("GenuineIntel", StringComparison.OrdinalIgnoreCase) ? "Intel" : "";
        if (cpuVendor.Length > 0)
        {
            var driver = profile.RelevantDrivers.FirstOrDefault(item =>
                item.Contains(cpuVendor, StringComparison.OrdinalIgnoreCase) &&
                item.StartsWith("SYSTEM:", StringComparison.OrdinalIgnoreCase));
            if (driver is not null)
            {
                var fields = driver.Split('|');
                var model = fields[0].Split(':', 2).ElementAtOrDefault(1)?.Trim() ?? "";
                var installed = fields.ElementAtOrDefault(2)?.Trim() ?? "";
                if (model.Length > 0)
                    components.Add(new(UpdateComponentKind.ChipsetDriver, cpuVendor, model, installed));
            }
        }

        var board = profile.ComponentIdentities.GetValueOrDefault("Motherboard", "").Split('|');
        var bios = profile.ComponentIdentities.GetValueOrDefault("BIOS", "").Split('|');
        if (board.Length >= 2)
        {
            var boardVendor = NormalizeBoardVendor(board[0]);
            if (boardVendor.Length > 0)
                components.Add(new(UpdateComponentKind.Bios, boardVendor, board[1].Trim(), bios.ElementAtOrDefault(1)?.Trim() ?? ""));
        }
        return AnalyzeComponents(components);
    }

    public IReadOnlyList<UpdateNoticeDefinition> AnalyzeComponents(IEnumerable<DetectedUpdateComponent> components) =>
        components.Where(component => !string.IsNullOrWhiteSpace(component.Model))
            .DistinctBy(component => (component.Kind, component.Vendor.ToUpperInvariant(), component.Model.ToUpperInvariant()))
            .Select(CreateNotice).ToList();

    private UpdateNoticeDefinition CreateNotice(DetectedUpdateComponent component)
    {
        var source = ResolveSource(component.Vendor)
            ?? throw new InvalidOperationException($"Detected update vendor is not approved: {component.Vendor}");
        var record = _records.FirstOrDefault(candidate => candidate.Kind == component.Kind &&
            candidate.Vendor.Equals(source.CanonicalName, StringComparison.OrdinalIgnoreCase) &&
            candidate.Model.Equals(component.Model, StringComparison.OrdinalIgnoreCase));
        var status = UpdateComparisonStatus.ComparisonUnavailable;
        var latest = "";
        var url = source.OfficialUrl;
        var reason = "Exact installed/latest version comparison is unavailable; use the official vendor page for a manual check.";
        if (record is not null && TryCompareVersions(component.InstalledVersion, record.LatestVersion, out var comparison))
        {
            latest = record.LatestVersion;
            url = record.OfficialUrl;
            status = comparison < 0 ? UpdateComparisonStatus.UpdateAvailable : UpdateComparisonStatus.Current;
            reason = comparison < 0
                ? "A newer version exists in the pinned official vendor record; update remains manual."
                : "The installed version is current against the pinned official vendor record.";
        }
        var idSeed = $"{component.Kind}|{source.CanonicalName}|{component.Model}";
        var id = $"update.{component.Kind.ToString().ToLowerInvariant()}.{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idSeed)))[..16].ToLowerInvariant()}";
        return new(id, component.Kind, source.CanonicalName, component.Model,
            component.InstalledVersion, latest, url, status, reason);
    }

    public static bool TryCompareVersions(string installed, string latest, out int comparison)
    {
        comparison = 0;
        installed ??= "";
        latest ??= "";
        if (installed.Length > 100 || latest.Length > 100 ||
            !Regex.IsMatch(installed, @"^[0-9]+(?:\.[0-9]+)*$") ||
            !Regex.IsMatch(latest, @"^[0-9]+(?:\.[0-9]+)*$")) return false;
        if (!TryParts(installed, out var left) || !TryParts(latest, out var right)) return false;
        for (var index = 0; index < Math.Max(left.Count, right.Count); index++)
        {
            var leftPart = index < left.Count ? left[index] : 0;
            var rightPart = index < right.Count ? right[index] : 0;
            if (leftPart == rightPart) continue;
            comparison = leftPart.CompareTo(rightPart);
            return true;
        }
        return true;

        static bool TryParts(string value, out List<int> parts)
        {
            parts = [];
            foreach (var part in value.Split('.'))
            {
                if (!int.TryParse(part, out var number)) return false;
                parts.Add(number);
            }
            return true;
        }
    }

    private static VendorSource? ResolveSource(string vendor)
    {
        if (vendor.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || vendor.Equals("MSI", StringComparison.OrdinalIgnoreCase)) return Sources["MSI"];
        if (vendor.Contains("ASUSTeK", StringComparison.OrdinalIgnoreCase) || vendor.Equals("ASUS", StringComparison.OrdinalIgnoreCase)) return Sources["ASUS"];
        if (vendor.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase)) return Sources["Gigabyte"];
        if (vendor.Contains("ASRock", StringComparison.OrdinalIgnoreCase)) return Sources["ASRock"];
        if (vendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return Sources["NVIDIA"];
        if (vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase) || vendor.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)) return Sources["AMD"];
        if (vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return Sources["Intel"];
        return null;
    }

    private static string NormalizeBoardVendor(string vendor) => ResolveSource(vendor) is { } source &&
        source.CanonicalName is "MSI" or "ASUS" or "Gigabyte" or "ASRock" ? source.CanonicalName : "";

    private static bool IsOfficial(string url, string domain) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) && (uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase));
}
