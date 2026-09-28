using System.Diagnostics;

namespace NeuroTune;

public sealed record DriverInspectionRequest(string Module);
public sealed record DriverServiceDetails(string Name, string DisplayName, string State, string StartMode,
    string FileVersion, string Company, IReadOnlyList<string> Devices);
public sealed record DriverInspectionResult(string Module, DateTimeOffset InspectedAtUtc,
    IReadOnlyList<DriverServiceDetails> Services, string Limitation);

public static class DriverInspection
{
    public static DriverInspectionResult Read(string module)
    {
        ValidateModule(module);
        var drivers = SystemProfiler.Query("SELECT Name, DisplayName, State, StartMode, PathName FROM Win32_SystemDriver",
            row => (Name: row["Name"]?.ToString() ?? "", Display: row["DisplayName"]?.ToString() ?? "",
                State: row["State"]?.ToString() ?? "Unknown", Mode: row["StartMode"]?.ToString() ?? "Unknown",
                Path: ResolveLocalDriverPath(row["PathName"]?.ToString() ?? "")))
            .Where(item => item.Path is not null && string.Equals(Path.GetFileName(item.Path), module, StringComparison.OrdinalIgnoreCase)).ToList();
        var devices = drivers.Count == 0 ? [] : SystemProfiler.Query("SELECT Name, Service FROM Win32_PnPEntity",
            row => (Name: row["Name"]?.ToString() ?? "Unknown device", Service: row["Service"]?.ToString() ?? ""));
        var details = drivers.Select(item =>
        {
            var version = "Unavailable";
            var company = "Unavailable";
            try
            {
                var file = FileVersionInfo.GetVersionInfo(item.Path!);
                version = file.FileVersion ?? "Unavailable";
                company = file.CompanyName ?? "Unavailable";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
            return new DriverServiceDetails(item.Name, item.Display, item.State, item.Mode, version, company,
                devices.Where(device => string.Equals(device.Service, item.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(device => device.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList());
        }).ToList();
        return new(module, DateTimeOffset.UtcNow, details,
            "Current Windows inventory, not the driver state at capture time. File version/company are metadata, not signature verification. " +
            "A device's configured driver service does not prove that it caused an interrupt; framework/shared drivers require further tracing. " +
            "Empty results mean no readable direct association was found, not that no device uses this module. No service or device was changed.");
    }

    internal static void ValidateModule(string module)
    {
        if (string.IsNullOrWhiteSpace(module) || module.Length > 128 || !module.EndsWith(".sys", StringComparison.OrdinalIgnoreCase) ||
            module.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')) || module.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Select a driver module filename ending in .sys, without a path.", nameof(module));
    }

    internal static string? ResolveLocalDriverPath(string raw)
    {
        var path = raw.Trim().Trim('"');
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(windows, path[12..]);
        else if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(windows, path);
        if (path.StartsWith(@"\??\", StringComparison.Ordinal)) path = path[4..];
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        try
        {
            path = Path.GetFullPath(path);
            // Limit metadata reads to local Windows driver locations; never follow a service path to a network share.
            return path.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".sys", StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
