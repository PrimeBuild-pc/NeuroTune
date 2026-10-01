using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace NeuroTune;

public sealed record ScewinDirectoryRequest(string Directory);
public sealed record ScewinExportRequest(string Directory, bool ReadConsent, bool RiskAccepted, Dictionary<string, string> ExpectedHashes);
public sealed record ScewinImportRequest(string Text, bool ReadConsent);
public sealed record ScewinFile(string Name, string Sha256, long Bytes);
public sealed record ScewinSetting(string Question, string Token, string? CurrentValue, string Status);
public sealed record ScewinReport(string Source, DateTimeOffset ReadAtUtc, string ExportSha256, IReadOnlyList<ScewinSetting> Settings,
    int SensitiveQuestionsOmitted, IReadOnlyList<ScewinFile> ToolFiles, IReadOnlyList<string> Notes);

public static class ScewinService
{
    public const int MaximumExportBytes = 16 * 1024 * 1024;
    private static readonly string[] RequiredFiles = ["SCEWIN_64.exe", "amifldrv64.sys", "amigendrv64.sys"];
    private static readonly Regex Question = new(@"^\s*Setup Question\s*=\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex SelectedOption = new(@"^\s*(?:Options\s*=\s*)?\*\s*(\[[^\]]+\].+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Sensitive = new(@"password|passphrase|secret|serial|uuid|asset\s*tag|mac\s*address|recovery\s*key|access\s*key|email|user\s*name|ssid|network\s*key|\bpin(?:\s*code)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Numeric = new(@"^(?:<(?:0x)?[0-9a-f]+>|(?:0x)?[0-9a-f]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static IReadOnlyList<ScewinFile> Inspect(string directory)
    {
        var root = LocalDirectory(directory);
        return RequiredFiles.Select(name => InspectFile(Path.Combine(root, name))).ToList();
    }

    public static ScewinReport Import(ScewinImportRequest request)
    {
        if (!request.ReadConsent) throw new InvalidOperationException("Enable firmware reading before importing an export.");
        var parsed = Parse(request.Text);
        if (parsed.Settings.Count == 0 && parsed.Omitted == 0) throw new ArgumentException("No SCEWIN setup questions were recognized in this export.");
        return new("Imported text — origin, freshness and board association are not verified", DateTimeOffset.UtcNow,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Text))).ToLowerInvariant(), parsed.Settings, parsed.Omitted, [],
            ["Only the marked current option or a raw numeric Value is read. BIOS Default is never substituted for a missing current value.",
                "Imported values are untrusted observations, not live readings. No tool or driver was executed, and no BIOS setting was written.",
                "The import hash identifies decoded text re-encoded as UTF-8, not the original file bytes. Sensitive-label filtering is not a guarantee of anonymization; do not share raw exports."]);
    }

    public static async Task<ScewinReport> ExportAsync(ScewinExportRequest request, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!request.ReadConsent || !request.RiskAccepted) throw new InvalidOperationException("Firmware-read consent and explicit privileged/unsigned-tool risk approval are required.");
        if (!OperatingSystem.IsWindows() || !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Run NeuroTune as administrator to use SCEWIN export. Offline text import does not require elevation.");
        ValidateHashes(request.ExpectedHashes);
        return await Task.Run(() =>
        {
            ScewinReport? report = null;
            // Keep the existing recorder mutex on this thread while the external tool runs.
            MeasurementService.WithCaptureMutex(() =>
            {
                if (new MeasurementService().List().Any(session => session.State == MeasurementSessionState.Recording))
                    throw new InvalidOperationException("Stop/cancel the active latency recording before running SCEWIN.");
                report = ExportCoreAsync(request, progress, cancellationToken).GetAwaiter().GetResult();
            });
            return report!;
        }, cancellationToken);
    }

    private static async Task<ScewinReport> ExportCoreAsync(ScewinExportRequest request, Action<string>? progress, CancellationToken cancellationToken)
    {
        var source = LocalDirectory(request.Directory);
        var staging = Path.Combine(SettingsService.DataDirectory, "firmware", "scewin", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var notes = new List<string>
        {
            "NeuroTune invoked only /o /s to request an export. It did not invoke /i, force compatibility, or disable Windows/firmware protections.",
            "SCEWIN is an external privileged program that can load kernel drivers, access local data/credentials and change the system. Export-only arguments cannot sandbox its internals; a driver may remain loaded until restart.",
            "Hashes bind the inspected package to this approval; they do not prove vendor authenticity or signature validity. Board/firmware compatibility is not guaranteed.",
            "Restart Windows before a new latency capture if a driver was loaded, to avoid measuring driver residue. SCEWIN export is blocked while a NeuroTune recording is active.",
            "Sensitive-label filtering is not a guarantee of anonymization. Do not share raw exports."
        };
        ScewinReport? result = null;
        try
        {
            progress?.Invoke("Staging the three approved SCEWIN files and verifying their SHA-256 hashes…");
            var files = new List<ScewinFile>();
            foreach (var name in RequiredFiles)
            {
                var original = InspectFile(Path.Combine(source, name));
                using (var input = new FileStream(Path.Combine(source, name), FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(Path.Combine(staging, name), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (input.Length is <= 2 or > 64 * 1024 * 1024) throw new IOException("The approved file size changed.");
                    await input.CopyToAsync(output, cancellationToken);
                }
                var staged = InspectFile(Path.Combine(staging, name));
                if (!original.Sha256.Equals(staged.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    !request.ExpectedHashes.Single(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value.Equals(staged.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The SCEWIN package changed after inspection. Inspect it again and explicitly confirm the new hashes.");
                files.Add(staged);
            }
            var exportPath = Path.Combine(staging, "nvram.txt");
            var start = new ProcessStartInfo(Path.Combine(staging, "SCEWIN_64.exe"))
            {
                WorkingDirectory = staging,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in ExportArguments(exportPath)) start.ArgumentList.Add(argument);
            progress?.Invoke("Requesting a SCEWIN export. Windows may reject the driver; no security bypass will be attempted…");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("SCEWIN could not be started.");
            process.StandardInput.Close();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(120));
            var stdout = ReadOutputAsync(process.StandardOutput, deadline.Token);
            var stderr = ReadOutputAsync(process.StandardError, deadline.Token);
            try { await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                throw new TimeoutException("SCEWIN did not finish within the permitted time. The tool was stopped; any loaded driver may require a Windows restart.");
            }
            if (process.ExitCode != 0 || !File.Exists(exportPath))
                throw new InvalidOperationException($"SCEWIN export failed (exit {process.ExitCode}). {FirstDiagnostic(await stderr, await stdout)} No protection was disabled and no BIOS import was attempted.");
            if (new FileInfo(exportPath).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("A linked SCEWIN export is not accepted.");
            using var export = new FileStream(exportPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (export.Length is <= 0 or > MaximumExportBytes) throw new InvalidOperationException("The SCEWIN export was empty or exceeded 16 MiB.");
            var exportHash = Convert.ToHexString(SHA256.HashData(export)).ToLowerInvariant();
            export.Position = 0;
            using var reader = new StreamReader(export, new UTF8Encoding(false, true));
            var text = await reader.ReadToEndAsync(cancellationToken);
            var parsed = Parse(text);
            if (parsed.Settings.Count == 0) throw new InvalidOperationException("SCEWIN returned no readable setup questions. The firmware/HII interface may be incompatible; no setup values were guessed.");
            progress?.Invoke($"Parsed {parsed.Settings.Count} local setup questions; cleaning temporary export files…");
            result = new("SCEWIN /o export requested now — external-tool observations, not validated tuning recommendations", DateTimeOffset.UtcNow,
                exportHash, parsed.Settings, parsed.Omitted, files, notes);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                notes.Add($"Temporary export cleanup was incomplete: {staging}. Close SCEWIN/restart Windows and remove it manually; raw exports can contain sensitive data.");
                progress?.Invoke(notes[^1]);
            }
        }
        return result!;
    }

    internal static string[] ExportArguments(string exportPath) => ["/o", "/s", exportPath];
    internal static void ValidateHashes(IReadOnlyDictionary<string, string>? hashes)
    {
        if (hashes is null || hashes.Count != RequiredFiles.Length || RequiredFiles.Any(name => !hashes.Any(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase))) ||
            hashes.Values.Any(value => value is null || value.Length != 64 || !value.All(Uri.IsHexDigit)))
            throw new InvalidOperationException("Inspect all three SCEWIN files and approve their exact SHA-256 hashes before export.");
    }

    internal static (IReadOnlyList<ScewinSetting> Settings, int Omitted) Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumExportBytes || Encoding.UTF8.GetByteCount(text) > MaximumExportBytes)
            throw new ArgumentException("An export must contain between 1 byte and 16 MiB of text.");
        var result = new List<ScewinSetting>();
        var omitted = 0;
        string? question = null; var token = ""; var values = new List<string>(); var sensitive = false;
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 32768) throw new ArgumentException("An export line exceeded the local parser limit.");
            var match = Question.Match(line);
            if (match.Success)
            {
                Finish();
                question = match.Groups[1].Value.Trim();
                if (question.Length is 0 or > 512) throw new ArgumentException("A setup-question label was empty or too long.");
                token = ""; values.Clear(); sensitive = Sensitive.IsMatch(question);
                continue;
            }
            if (question is null) continue;
            var content = line.Split("//", 2)[0].Trim();
            if (content.StartsWith("Help String", StringComparison.OrdinalIgnoreCase) && Sensitive.IsMatch(content)) sensitive = true;
            if (content.StartsWith("Token", StringComparison.OrdinalIgnoreCase) && content.Contains('=')) token = content[(content.IndexOf('=') + 1)..].Trim();
            var option = SelectedOption.Match(content);
            if (option.Success) values.Add(option.Groups[1].Value.Trim());
            else if (content.StartsWith("Value", StringComparison.OrdinalIgnoreCase) && content.Contains('='))
            {
                var value = content[(content.IndexOf('=') + 1)..].Trim();
                if (Numeric.IsMatch(value)) values.Add(value);
            }
            if (token.Length > 256 || values.Any(value => value.Length > 2048) || values.Count > 256) throw new ArgumentException("A setup question exceeded the local parser limits.");
        }
        Finish();
        return (result, omitted);

        void Finish()
        {
            if (question is null) return;
            if (result.Count + omitted >= 10000) throw new ArgumentException("An export exceeded 10,000 setup questions.");
            if (sensitive) { omitted++; return; }
            result.Add(new(question, token, values.Count == 1 ? values[0] : null,
                values.Count == 1 ? "Reported by export" : values.Count == 0 ? "Unknown — no marked current value" : "Unknown — conflicting current-value markers"));
        }
    }

    private static string LocalDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Choose a fully qualified local folder, not a network path.");
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        if (!directory.Exists) throw new DirectoryNotFoundException("The selected SCEWIN folder does not exist.");
        var drive = new DriveInfo(Path.GetPathRoot(directory.FullName)!);
        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) throw new ArgumentException("SCEWIN must be read from a local disk.");
        for (var parent = directory; parent is not null; parent = parent.Parent)
            if (parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new ArgumentException("Linked SCEWIN folders are not accepted.");
        return directory.FullName;
    }
    private static ScewinFile InspectFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.Length is <= 2 or > 64 * 1024 * 1024)
            throw new InvalidOperationException($"{file.Name} is missing, linked, empty or too large.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 2 or > 64 * 1024 * 1024) throw new IOException($"{file.Name} changed to an unsupported size.");
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') throw new InvalidOperationException($"{file.Name} is not a Windows executable/driver image.");
        stream.Position = 0;
        // ponytail: hashes bind consent, not authenticity; require a verified vendor package if distribution becomes part of the app.
        return new(file.Name, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), stream.Length);
    }
    private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder(); var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) is var count && count > 0)
            if (text.Length < 65536) text.Append(buffer, 0, Math.Min(count, 65536 - text.Length));
        return text.ToString();
    }
    private static string FirstDiagnostic(params string[] messages)
    {
        var line = messages.SelectMany(message => message.Split('\r', '\n')).Select(line => line.Trim())
            .FirstOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("warning", StringComparison.OrdinalIgnoreCase) || line.Contains("driver", StringComparison.OrdinalIgnoreCase));
        return line is null ? "No usable diagnostic was returned; check driver admission and firmware compatibility." : line[..Math.Min(500, line.Length)];
    }
}
