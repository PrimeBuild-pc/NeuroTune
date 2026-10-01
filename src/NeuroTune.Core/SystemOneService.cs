using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NeuroTune;

public sealed record SystemOneOptions(bool Enabled, string Size = "4b", string Device = "auto", string Source = "local", string Model = "");
public sealed record SystemOneConfigureRequest(SystemOneOptions Options, string? ApiKey = null, bool ForgetApiKey = false);
public sealed record SystemOneStatus(bool Enabled, bool Installed, string Size, string Device, string Directory, string Detail, bool HasFiles,
    string Source, string Model, bool HasApiKey);
public sealed record SystemOneAdvisory(string Phase, string Status, string? Domain, double? Score, double Seconds, string Detail, IReadOnlyList<string> EvidenceIds);

public static class SystemOneService
{
    internal const string Revision = "b9ba007ee4d2928bbab5b1d8bfe9009c3696b6de";
    private const string SourceHash = "1d593250b6d78d442217a418cf779a975fedb57192570f867c6ca0e873de2c00";
    private const string UvHash = "5d223efa0bf00208c3853246af09420419dfbd352536aa6bb8163d6170e23890";
    private const string RuntimeHash = "4259a1dda3ef3fcfd8b007a16329d5bdcef07da8f5f95fddd85ff2954263f01a";
    private static readonly string Root = Path.Combine(SettingsService.DataDirectory, "system-one");
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly string[] Domains = ["memory", "drivers", "network", "power", "software", "unknown"];
    private static string Project => Path.Combine(Root, "source");
    private static string Python => Path.Combine(Project, ".venv", "Scripts", "python.exe");
    internal const string OpenRouterCredentialId = "system-one-openrouter";
    private static string OptionsPath => Path.Combine(Root, "options.json");

    public static SystemOneStatus Status()
    {
        var options = LoadOptions();
        var installed = InstalledSize() == options.Size && File.Exists(Python) && File.Exists(Path.Combine(Root, "model.gguf"));
        var hasKey = !string.IsNullOrWhiteSpace(new SettingsService().LoadSecret(OpenRouterCredentialId));
        var ready = options.Source == "openRouter" ? hasKey && !string.IsNullOrWhiteSpace(options.Model) : installed;
        var detail = options.Source == "openRouter" ? "Optional cloud classifier via OpenRouter: token-based API usage, not local Rizzo scoring. Only sampled context is sent; no automatic local/provider fallback."
            : installed ? "One-shot local worker; exits and unloads after each analysis. No background server." : "Optional component not installed. No model download or inference until explicitly requested.";
        return new(options.Enabled && ready, installed, options.Size, options.Device, Root, detail, Directory.Exists(Root), options.Source, options.Model, hasKey);
    }
    public static SystemOneStatus Configure(SystemOneOptions options)
    {
        ValidateOptions(options);
        if (options.Enabled && options.Source == "local" && InstalledSize() != options.Size) throw new InvalidOperationException("Install the selected optional model before enabling it.");
        if (options.Enabled && options.Source == "openRouter" && (string.IsNullOrWhiteSpace(options.Model) || string.IsNullOrWhiteSpace(new SettingsService().LoadSecret(OpenRouterCredentialId))))
            throw new InvalidOperationException("Save a separate OpenRouter API key and choose the optional classifier model before enabling it.");
        Directory.CreateDirectory(Root); CheckDirectory(Root);
        AtomicWrite(OptionsPath, JsonSerializer.Serialize(options));
        return Status();
    }
    public static SystemOneStatus Configure(SystemOneConfigureRequest request)
    {
        ValidateOptions(request.Options);
        if (request.ForgetApiKey && (!string.IsNullOrWhiteSpace(request.ApiKey) || request.Options.Enabled)) throw new ArgumentException("Disable the optional assistant before removing its API key.");
        var settings = new SettingsService();
        if (!string.IsNullOrWhiteSpace(request.ApiKey)) settings.SaveSecret(OpenRouterCredentialId, request.ApiKey);
        if (request.ForgetApiKey) settings.DeleteSecret(OpenRouterCredentialId);
        return Configure(request.Options);
    }
    public static Task<IReadOnlyList<string>> ListOpenRouterModelsAsync(OptimizationCatalog catalog, CancellationToken cancellationToken = default) =>
        new LlmClient(catalog).ListModelsAsync(LlmClient.Defaults(LlmProvider.OpenRouter), new SettingsService().LoadSecret(OpenRouterCredentialId), cancellationToken);

    public static async Task<SystemOneStatus> InstallAsync(SystemOneOptions options, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        if (!options.Enabled || options.Source != "local") throw new InvalidOperationException("Explicit local-model installation consent is required.");
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new NotSupportedException("The managed optional runtime currently supports Windows x64.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromHours(3));
        return await Task.Run(() =>
        {
            SystemOneStatus? result = null;
            MeasurementService.WithCaptureMutex(() =>
            {
                if (MeasurementService.HasActiveRecording()) throw new InvalidOperationException("Stop/cancel the recording before installing System One.");
                result = InstallCoreAsync(options, progress, deadline.Token).GetAwaiter().GetResult();
            });
            return result!;
        }, deadline.Token);
    }
    private static async Task<SystemOneStatus> InstallCoreAsync(SystemOneOptions options, Action<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root); CheckDirectory(Root);
        Configure(options with { Enabled = false });
        if (new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace < (options.Size == "4b" ? 7L : 4L) * 1024 * 1024 * 1024)
            throw new IOException("Not enough free disk space: reserve 7 GiB for 4B or 4 GiB for 1.7B (including environment/download cache).");
        await DownloadAsync($"https://codeload.github.com/Rizzo-AI-Academy/rizzo-flow/zip/{Revision}", Path.Combine(Root, "source.zip"), SourceHash, 64L << 20, progress, cancellationToken);
        if (!File.Exists(Path.Combine(Project, "uv.lock")))
        {
            var unpacked = Path.Combine(Root, "source.partial");
            if (Directory.Exists(unpacked)) Directory.Delete(unpacked, true);
            ExtractZip(Path.Combine(Root, "source.zip"), unpacked);
            if (Directory.Exists(Project)) Directory.Delete(Project, true);
            Directory.Move(Directory.GetDirectories(unpacked).Single(), Project); Directory.Delete(unpacked);
        }
        VerifySourceFiles();
        var uv = Path.Combine(Root, "uv", "uv.exe");
        await DownloadAsync("https://github.com/astral-sh/uv/releases/download/0.12.21/uv-x86_64-pc-windows-msvc.zip", Path.Combine(Root, "uv.zip"), UvHash, 64L << 20, progress, cancellationToken);
        ExtractZip(Path.Combine(Root, "uv.zip"), Path.Combine(Root, "uv"));
        if (!File.Exists(uv)) throw new IOException("The pinned uv archive did not contain uv.exe.");
        progress?.Invoke("Installing a private Python 3.12.12 environment and locked Rizzo dependencies as a non-administrator…");
        await OptionalWorkerProcess.RunAsync(uv, ["sync", "--locked", "--no-dev", "--no-editable", "--python", "3.12.12", "--directory", Project], Root, "", EnvironmentForWorker(), TimeSpan.FromMinutes(15), progress, cancellationToken);
        await DownloadAsync("https://github.com/ggml-org/llama.cpp/releases/download/b11081/llama-b11081-bin-win-vulkan-x64.zip", Path.Combine(Root, "runtime.zip"), RuntimeHash, 128L << 20, progress, cancellationToken);
        ExtractZip(Path.Combine(Root, "runtime.zip"), Path.Combine(Root, "runtime"));
        if (!File.Exists(Path.Combine(Root, "runtime", "llama.dll")))
        {
            var library = Directory.EnumerateFiles(Path.Combine(Root, "runtime"), "llama.dll", SearchOption.AllDirectories).Single();
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(library)!)) File.Copy(file, Path.Combine(Root, "runtime", Path.GetFileName(file)), true);
        }
        var model = Model(options.Size);
        await DownloadAsync(model.Url, Path.Combine(Root, "model.gguf"), model.Hash, 6L << 30, progress, cancellationToken);
        using var resource = typeof(SystemOneService).Assembly.GetManifestResourceStream("NeuroTune.system-one-worker.py") ?? throw new IOException("The embedded model adapter is missing.");
        using var reader = new StreamReader(resource);
        AtomicWrite(Path.Combine(Root, "worker.py"), await reader.ReadToEndAsync(cancellationToken));
        progress?.Invoke("Verifying the installed interpreter, runtime and typed-decision adapter with a real local smoke test…");
        var smoke = await InvokeAsync("Local adapter installation check; no system changes requested.", options, null, cancellationToken);
        ParseAdvisory(smoke, "installation", [], 0);
        var hashes = new[] { uv, Python, Path.Combine(Root, "runtime", "llama.dll"), Path.Combine(Root, "worker.py") }.ToDictionary(file => Path.GetFileName(file)!, FileHash);
        AtomicWrite(Path.Combine(Root, "installed.json"), JsonSerializer.Serialize(new { revision = Revision, size = options.Size, hashes }));
        progress?.Invoke("Installation verified. The local model has exited and released its process resources.");
        return Configure(options);
    }

    public static async Task<SystemOneAdvisory?> AnalyzeAsync(string phase, string context, IReadOnlyDictionary<string, string>? facts = null,
        Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var status = Status();
        if (!status.Enabled) return null;
        var watch = Stopwatch.StartNew();
        try
        {
            return await Task.Run(() =>
            {
                SystemOneAdvisory? result = null;
                MeasurementService.WithCaptureMutex(() =>
                {
                    if (MeasurementService.HasActiveRecording()) throw new InvalidOperationException("Optional classification skipped while recording.");
                    if (status.Source == "openRouter")
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(30));
                        progress?.Invoke("Optional OpenRouter classifier: sending sampled context using its separate API key; API billing applies.");
                        var key = new SettingsService().LoadSecret(OpenRouterCredentialId) ?? throw new InvalidOperationException("The optional OpenRouter key is unavailable.");
                        var cloud = LlmClient.ClassifyTopicAsync(status.Model, key, CloudContext(context), deadline.Token).GetAwaiter().GetResult();
                        result = ParseCloudAdvisory(cloud, phase, facts?.Keys.ToList() ?? [], watch.Elapsed.TotalSeconds, status.Model);
                        return;
                    }
                    CheckDirectory(Root); VerifySourceFiles(); VerifyWorkerFiles();
                    progress?.Invoke($"System One · {phase}: loading the optional local classifier; original evidence and user choices remain unchanged…");
                    // ponytail: one cold worker per phase, no resident cache; add a reusable session only after measured startup cost justifies it.
                    var output = InvokeAsync(context.Length > 12000 ? context[..12000] : context, new(status.Enabled, status.Size, status.Device), progress, cancellationToken).GetAwaiter().GetResult();
                    result = ParseAdvisory(output, phase, facts?.Keys.ToList() ?? [], watch.Elapsed.TotalSeconds);
                });
                progress?.Invoke(status.Source == "local" ? "System One unloaded. Its classification is advisory, not an execution decision." : "Optional OpenRouter classification complete; no local model was loaded.");
                return result;
            }, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            progress?.Invoke("Optional System One unavailable; continuing with the original evidence and selected provider.");
            return new(phase, "unavailable", null, null, watch.Elapsed.TotalSeconds, $"Optional classification was not used: {error.Message[..Math.Min(800, error.Message.Length)]}", []);
        }
    }
    private static Task<string> InvokeAsync(string state, SystemOneOptions options, Action<string>? progress, CancellationToken cancellationToken) =>
        OptionalWorkerProcess.RunAsync(Python, ["-I", "-u", Path.Combine(Root, "worker.py"), Root], Root,
            JsonSerializer.Serialize(new { state, device = options.Device }), EnvironmentForWorker(), TimeSpan.FromMinutes(3), progress, cancellationToken);

    internal static SystemOneAdvisory ParseAdvisory(string output, string phase, IReadOnlyList<string> knownIds, double seconds)
    {
        if (output.Length > 256000) throw new InvalidOperationException("Local classifier output was too large.");
        using var json = JsonDocument.Parse(output);
        var answer = json.RootElement.GetProperty("answers").GetProperty("domain");
        if (answer.GetProperty("type").GetString() != "choice") throw new InvalidOperationException("Unexpected optional classifier answer type.");
        var status = answer.GetProperty("status").GetString();
        if (status is not ("ok" or "insufficient_evidence" or "out_of_range" or "uncertain")) throw new InvalidOperationException("Unexpected local answer status.");
        var probabilities = answer.GetProperty("probabilities").EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetDouble());
        if (probabilities.Count is < 2 or > 8 || probabilities.Keys.Any(key => !Domains.Contains(key) && key != "__insufficient__") ||
            probabilities.Values.Any(value => !double.IsFinite(value) || value is < 0 or > 1) || Math.Abs(probabilities.Values.Sum() - 1) > 0.00001)
            throw new InvalidOperationException("Invalid local probability distribution.");
        var choice = answer.GetProperty("choice").GetString();
        if ((status == "ok") != (choice is not null) || choice is not null && (!Domains.Contains(choice) || !probabilities.ContainsKey(choice)))
            throw new InvalidOperationException("The local model returned an unknown choice.");
        var ids = choice is null or "unknown" ? [] : knownIds.Where(id => MatchesDomain(id, choice)).Take(24).ToList();
        return new(phase, status!, choice, choice is null ? null : probabilities[choice], seconds,
            "Uncalibrated optional topic classification of sampled context; not causal proof, risk approval or authority to change any setting.", ids);
    }
    internal static string CloudContext(string context)
    {
        var redacted = ProfileSanitizer.Redact(context);
        redacted = System.Text.RegularExpressions.Regex.Replace(redacted, @"(?i)\b[a-z]:[\\/][^\s""<>]*", "[local-path]");
        return redacted[..Math.Min(redacted.Length, 12000)];
    }
    internal static SystemOneAdvisory ParseCloudAdvisory(string output, string phase, IReadOnlyList<string> knownIds, double seconds, string model)
    {
        if (output.Length > 4096) throw new InvalidOperationException("Optional cloud classifier output exceeded its limit.");
        using var json = JsonDocument.Parse(LlmClient.UnwrapJson(output));
        var domain = json.RootElement.GetProperty("domain").GetString();
        if (domain is null || !Domains.Contains(domain)) throw new InvalidOperationException("Optional cloud classifier returned an unsupported topic.");
        return new(phase, domain == "unknown" ? "uncertain" : "ok", domain, null, seconds,
            $"OpenRouter API topic classification ({model}); token-based, no calibrated probability or causal proof. It cannot approve or execute changes.",
            domain == "unknown" ? [] : knownIds.Where(id => MatchesDomain(id, domain)).Take(24).ToList());
    }
    private static bool MatchesDomain(string id, string domain) => domain switch
    {
        "memory" => id.Contains("memory", StringComparison.OrdinalIgnoreCase) || id.Contains("fault", StringComparison.OrdinalIgnoreCase),
        "drivers" => id.Contains("driver", StringComparison.OrdinalIgnoreCase) || id.Contains("dpc", StringComparison.OrdinalIgnoreCase) || id.Contains("isr", StringComparison.OrdinalIgnoreCase),
        "network" => id.StartsWith("network:", StringComparison.Ordinal),
        "power" => id.Contains("power", StringComparison.OrdinalIgnoreCase) || id.Contains("thermal", StringComparison.OrdinalIgnoreCase),
        "software" => id.StartsWith("software:", StringComparison.Ordinal),
        _ => false
    };
    internal static void ValidateOptions(SystemOneOptions options)
    {
        if (options.Source is not ("local" or "openRouter") || options.Model is null || options.Model.Length > 512 || options.Model.Any(char.IsControl)) throw new ArgumentException("Select a supported assistant source and valid model ID.");
        if (options.Size is not ("4b" or "1.7b") || options.Device is not ("auto" or "cpu")) throw new ArgumentException("Select a supported optional model and CPU/automatic compute mode.");
    }
    private static SystemOneOptions LoadOptions()
    {
        if (!File.Exists(OptionsPath)) return new(false);
        try { var options = JsonSerializer.Deserialize<SystemOneOptions>(File.ReadAllText(OptionsPath)) ?? new(false); ValidateOptions(options); return options; }
        catch (Exception error) when (error is JsonException or ArgumentException or IOException) { return new(false); }
    }
    private static string? InstalledSize()
    {
        try { using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "installed.json"))); return json.RootElement.GetProperty("revision").GetString() == Revision ? json.RootElement.GetProperty("size").GetString() : null; }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException) { return null; }
    }
    private static void VerifySourceFiles()
    {
        var archive = Path.Combine(Root, "source.zip");
        if (FileHash(archive) != SourceHash) throw new IOException("The optional source archive changed; remove/reinstall the component.");
        using var zip = ZipFile.OpenRead(archive);
        var prefix = $"rizzo-flow-{Revision}/";
        foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var relative = entry.FullName[prefix.Length..];
            if (!(relative.StartsWith("src/rizzo_flow/", StringComparison.Ordinal) && relative.EndsWith(".py", StringComparison.Ordinal)) && relative is not ("uv.lock" or "pyproject.toml")) continue;
            using var bytes = entry.Open();
            var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var file = Path.Combine(Project, relative); CheckDirectory(Path.GetDirectoryName(file)!);
            if (FileHash(file) != expected) throw new IOException("Pinned optional source files changed; remove/reinstall the managed component.");
        }
    }
    private static void VerifyWorkerFiles()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "installed.json")));
        foreach (var file in new[] { Path.Combine(Root, "uv", "uv.exe"), Python, Path.Combine(Root, "runtime", "llama.dll"), Path.Combine(Root, "worker.py") })
            if (FileHash(file) != json.RootElement.GetProperty("hashes").GetProperty(Path.GetFileName(file)).GetString()) throw new IOException("Optional worker files changed; reinstall the managed component.");
    }
    public static SystemOneStatus Remove()
    {
        MeasurementService.WithCaptureMutex(() => { if (MeasurementService.HasActiveRecording()) throw new InvalidOperationException("Stop/cancel the recording before removing the optional component."); if (Directory.Exists(Root)) { CheckDirectory(Root); Directory.Delete(Root, true); } });
        return Status();
    }
    private static Dictionary<string, string> EnvironmentForWorker()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "SystemRoot", "WINDIR", "USERPROFILE", "LOCALAPPDATA", "TEMP", "TMP" })
            if (System.Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        environment["PATH"] = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), "System32");
        environment["UV_CACHE_DIR"] = Path.Combine(Root, "cache"); environment["UV_PYTHON_INSTALL_DIR"] = Path.Combine(Root, "python");
        environment["UV_PYTHON_PREFERENCE"] = "only-managed"; environment["UV_NO_PROGRESS"] = "1"; environment["PYTHONUTF8"] = "1"; environment["HF_HOME"] = Path.Combine(Root, "hf-empty");
        return environment;
    }
    private static (string Url, string Hash) Model(string size) => size == "4b"
        ? ("https://huggingface.co/rizzoaiacademy/rizzo-flow/resolve/55633c8cbd2b826bd3eefdeb05310450996649df/spark-x2.5-4b-rizzo-flow-lora-q8_0.gguf", "dbec3c89d33984772324e65a8ed56b48e958e301b856cb870a7c1b385d01691a")
        : ("https://huggingface.co/rizzoaiacademy/rizzo-flow-1.7b/resolve/532e1586cc60787375ec7d86a5a302deb9a1adec/spark-x2.5-1.7b-rizzo-flow-lora-q8_0.gguf", "685403da9c62e0745cb77817ee2d66418e3ce85002ef403cf2680481e22bf16d");
    private static string FileHash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static void AtomicWrite(string file, string text) { File.WriteAllText(file + ".tmp", text, new UTF8Encoding(false)); File.Move(file + ".tmp", file, true); }
    internal static void CheckDirectory(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked optional-runtime directories are not accepted.");
    }
    internal static void ExtractZip(string archive, string directory)
    {
        Directory.CreateDirectory(directory); CheckDirectory(directory);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Sum(entry => entry.Length) > 512L << 20) throw new IOException("Optional-runtime archive exceeded its extraction limit.");
        foreach (var entry in zip.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(directory, entry.FullName));
            if (!destination.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.Contains(':') || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new IOException("Unsafe optional-runtime archive member.");
            CheckDirectory(Path.GetDirectoryName(destination)!);
            if (entry.Name.Length == 0) Directory.CreateDirectory(destination);
            else { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); if (File.Exists(destination) && new FileInfo(destination).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked runtime file."); entry.ExtractToFile(destination, true); }
        }
    }
    private static async Task DownloadAsync(string url, string destination, string expectedHash, long maximum, Action<string>? progress, CancellationToken cancellationToken)
    {
        CheckDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) && FileHash(destination) == expectedHash) return;
        var partial = destination + "." + expectedHash[..12] + ".part";
        if (File.Exists(partial) && new FileInfo(partial).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked partial download.");
        var have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (have > 0 && FileHash(partial) == expectedHash) { File.Move(partial, destination, true); return; }
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new IOException($"Optional component download failed: HTTP {(int)response.StatusCode}. No model was enabled.");
        var resumed = response.StatusCode == HttpStatusCode.PartialContent && have > 0 && response.Content.Headers.ContentRange?.From == have;
        var done = resumed ? have : 0; var total = done + response.Content.Headers.ContentLength;
        if (total > maximum || done > maximum) throw new IOException("Optional component download exceeded its size limit.");
        await using (var output = new FileStream(partial, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            var buffer = new byte[1024 * 1024]; var timer = Stopwatch.StartNew();
            while (await source.ReadAsync(buffer, cancellationToken) is var count && count > 0)
            {
                done += count; if (done > maximum) throw new IOException("Optional download exceeded its size limit.");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                if (timer.ElapsedMilliseconds >= 1000) { progress?.Invoke($"Downloading {Path.GetFileName(destination)}: {done >> 20} MiB{(total is > 0 ? $" / {total.Value >> 20} MiB ({100d * done / total.Value:F1}%)" : "")}"); timer.Restart(); }
            }
        }
        progress?.Invoke($"Verifying SHA-256: {Path.GetFileName(destination)}…");
        if (FileHash(partial) != expectedHash) { File.Delete(partial); throw new IOException("Optional component checksum mismatch; unverified data was removed."); }
        File.Move(partial, destination, true);
    }
}
