using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NeuroTune;

// Contrastive Language Model (https://github.com/Contrastive-LM/CLM) as the optional System One classifier.
// The model runs in a local server the user starts; NeuroTune only talks to a loopback endpoint, never to a cloud
// service, and the answer is an advisory topic distribution, never a decision.
internal static class ClmClient
{
    internal const string Model = "clm-latest";
    internal const double AbstainBelow = 0.6;
    private const int MaxStateCharacters = 3_500; // The reference encoder is served with a 2048-token window.
    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private static (DateTime At, bool Ready)? _health;

    // CLM is a question/answer ranker: the heads score a candidate that reads like an answer to the question. On a
    // 12-case labeled panel this phrasing reached 10/12 topics where bare category labels reached 4/12.
    internal const string TopicQuestion = "Which area of the PC does this observation point to?";
    internal const string SpecificQuestion = "Is the problem described clearly enough to name a specific area of the PC?";
    internal static readonly (string Key, string Description)[] Topics =
    [
        ("memory", "The observation is about memory pressure, paging, RAM or the page file."),
        ("drivers", "The observation is about device drivers, interrupts, DPC or ISR latency."),
        ("network", "The observation is about the network connection: latency, packet loss, Wi-Fi or DNS."),
        ("power", "The observation is about power, thermals, throttling, clocks or power plans."),
        ("software", "The observation is about background applications, overlays, startup programs or antivirus."),
        ("unknown", "The observation is vague and does not identify one area."),
    ];

    internal static Uri Endpoint
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("NEUROTUNE_CLM_URL");
            return Uri.TryCreate(configured, UriKind.Absolute, out var uri) && IsLoopback(uri) ? uri : new Uri("http://127.0.0.1:8700/");
        }
    }

    // Same layout and pins as scripts/clm-stack.ps1, which installs the stack.
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuroTune", "clm");
    private const string EncoderUrl = "https://huggingface.co/Qwen/Qwen3-8B-GGUF/resolve/main/Qwen3-8B-Q8_0.gguf";
    private const string EncoderHash = "408b955510e196121c1c375201744783b5c9a43c7956d73fc78df54c66e883d6";
    private const string HeadUrl = "https://huggingface.co/Contrastive-LM/CLM-v0.1-8B/resolve/main/CLM_v0.1-8B.pt";
    private const string HeadHash = "b2b4a8c9c2d39263eff78a351eb909a342ce9b3bf21a3f07c1d1bf15f1c4eda5";
    private const string ClmCommit = "d5f9ef0fd9bde185df0ceaad4f4ecc6cfe8c34f6";
    private static string Server => Path.Combine(Root, "runtime", "llama-server.exe");
    private static string Encoder => Path.Combine(Root, "models", "Qwen3-8B-Q8_0.gguf");
    private static string Head => Path.Combine(Root, "heads", "CLM_v0.1-8B.pt");
    private static string Serve => Path.Combine(Root, "venv", "Scripts", "clm-serve.exe");
    internal static bool Installed => new[] { Server, Encoder, Head, Serve }.All(File.Exists);

    // Same steps as scripts/clm-stack.ps1 install: every download is SHA-256 pinned, uv runs as a non-administrator helper,
    // and the result is proven by one real classification before the assistant can be enabled.
    internal static async Task InstallAsync(Action<string>? progress, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            throw new NotSupportedException("The local CLM stack currently supports Windows x64.");
        foreach (var directory in new[] { Root, Path.Combine(Root, "models"), Path.Combine(Root, "heads") }) { Directory.CreateDirectory(directory); SystemOneService.CheckDirectory(directory); }
        if (new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace < 14L << 30) throw new IOException("Not enough free disk space: reserve 14 GiB for the local CLM stack.");
        await SystemOneService.DownloadAsync(SystemOneService.RuntimeUrl, Path.Combine(Root, "runtime.zip"), SystemOneService.RuntimeHash, 128L << 20, progress, cancellationToken);
        SystemOneService.ExtractZip(Path.Combine(Root, "runtime.zip"), Path.Combine(Root, "runtime"));
        await SystemOneService.DownloadAsync(EncoderUrl, Encoder, EncoderHash, 10L << 30, progress, cancellationToken);
        await SystemOneService.DownloadAsync(HeadUrl, Head, HeadHash, 256L << 20, progress, cancellationToken);
        await SystemOneService.DownloadAsync(SystemOneService.UvUrl, Path.Combine(Root, "uv.zip"), SystemOneService.UvHash, 64L << 20, progress, cancellationToken);
        SystemOneService.ExtractZip(Path.Combine(Root, "uv.zip"), Path.Combine(Root, "uv"));
        var uv = Path.Combine(Root, "uv", "uv.exe");
        if (!File.Exists(uv)) throw new IOException("The pinned uv archive did not contain uv.exe.");
        var requirements = Path.Combine(Root, "requirements.txt");
        using (var resource = typeof(ClmClient).Assembly.GetManifestResourceStream("NeuroTune.clm-requirements.txt") ?? throw new IOException("The embedded CLM requirements are missing."))
        using (var file = File.Create(requirements)) await resource.CopyToAsync(file, cancellationToken);
        var environment = HelperEnvironment();
        environment["UV_CACHE_DIR"] = Path.Combine(Root, "cache"); environment["UV_PYTHON_INSTALL_DIR"] = Path.Combine(Root, "python");
        environment["UV_PYTHON_PREFERENCE"] = "only-managed"; environment["UV_NO_PROGRESS"] = "1";
        var python = Path.Combine(Root, "venv", "Scripts", "python.exe");
        progress?.Invoke("Creating a private Python 3.12 environment for CLM as a non-administrator…");
        if (!File.Exists(python)) await OptionalWorkerProcess.RunAsync(uv, ["venv", "--python", "3.12.12", Path.Combine(Root, "venv")], Root, "", environment, TimeSpan.FromMinutes(10), progress, cancellationToken);
        progress?.Invoke("Installing pinned CLM dependencies (CPU torch, about 300 MB)…");
        await OptionalWorkerProcess.RunAsync(uv, ["pip", "install", "--python", python, "--requirement", requirements, "--extra-index-url", "https://download.pytorch.org/whl/cpu"],
            Root, "", environment, TimeSpan.FromMinutes(30), progress, cancellationToken);
        await OptionalWorkerProcess.RunAsync(uv, ["pip", "install", "--python", python, "--no-deps", $"https://github.com/Contrastive-LM/CLM/archive/{ClmCommit}.zip"],
            Root, "", environment, TimeSpan.FromMinutes(10), progress, cancellationToken);
        if (!Installed) throw new IOException("The CLM installation finished without all of its files.");
        progress?.Invoke("Verifying the installation with one real local classification…");
        using (await StartAsync(progress, cancellationToken))
        {
            var answer = await DecideAsync("Local CLM installation check: frequent DPC latency spikes from a network driver.", cancellationToken);
            ParseAdvisory(answer, "installation", [], 0, (_, _) => false);
        }
        progress?.Invoke("CLM installed and verified. The model is unloaded until the next analysis.");
    }

    private static Dictionary<string, string> HelperEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "SystemRoot", "WINDIR", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");
        environment["PYTHONUTF8"] = "1"; environment["HF_HUB_OFFLINE"] = "1";
        return environment;
    }

    // Loads the ~9-10 GB encoder only for the classification that needs it, so it never sits in VRAM during
    // benchmarks. The returned job kills both servers when disposed or when this agent process exits; null means
    // a server the user started (scripts/clm-stack.ps1 start) is already answering and stays theirs.
    internal static async Task<IDisposable?> StartAsync(Action<string>? progress, CancellationToken cancellationToken)
    {
        if (Reachable(refresh: true)) return null;
        if (!Installed) throw new InvalidOperationException("The local CLM stack is not installed. Install it once with scripts/clm-stack.ps1 install.");
        if (Endpoint.Port != 8700) throw new InvalidOperationException("NEUROTUNE_CLM_URL points to a server NeuroTune does not manage; start it yourself.");
        SystemOneService.CheckDirectory(Root);
        if (SystemOneService.FileHash(Head) != HeadHash) // The head is a torch pickle: never let clm-serve load an unverified one.
            throw new InvalidOperationException("The CLM head does not match its pinned SHA-256; reinstall with scripts/clm-stack.ps1 install.");
        progress?.Invoke("System One · CLM: loading the local model for this analysis; it is unloaded right after.");
        var environment = HelperEnvironment();
        var job = OptionalWorkerProcess.NewJob();
        try
        {
            OptionalWorkerProcess.StartInJob(job, Server, ["-m", Encoder, "--embedding", "--pooling", "last", "-c", "2048", "-b", "2048", "-ub", "2048",
                "--host", "127.0.0.1", "--port", "8090", "-ngl", "99", "--alias", "qwen3-8b"], Root, environment);
            OptionalWorkerProcess.StartInJob(job, Serve, ["--host", "127.0.0.1", "--port", "8700", "--emb-url", "http://127.0.0.1:8090/v1/embeddings",
                "--emb-model", "qwen3-8b", "--ckpt", Head, "--no-download", "--no-ui"], Root, environment);
            for (var waited = 0; !Reachable(refresh: true); waited++)
            {
                if (waited >= 180) throw new TimeoutException("The local CLM server did not become ready within 3 minutes.");
                await Task.Delay(1000, cancellationToken);
            }
            return job;
        }
        catch { job.Dispose(); throw; }
    }

    internal static bool IsLoopback(Uri uri) => uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) &&
        uri.Host is "127.0.0.1" or "localhost" or "[::1]" or "::1";

    // True when the CLM server answers and its encoder is up. Cached briefly because status is read often.
    internal static bool Reachable(bool refresh = false)
    {
        if (!refresh && _health is { } cached && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(10)) return cached.Ready;
        var ready = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var body = Http.GetStringAsync(new Uri(Endpoint, "health"), timeout.Token).GetAwaiter().GetResult();
            using var json = JsonDocument.Parse(body);
            ready = json.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True &&
                json.RootElement.TryGetProperty("embedder", out var embedder) && embedder.ValueKind == JsonValueKind.True;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { }
        _health = (DateTime.UtcNow, ready);
        return ready;
    }

    internal static async Task<string> DecideAsync(string context, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = Model,
            state = StateText(context),
            questions = new Dictionary<string, object>
            {
                ["domain"] = new
                {
                    type = "choice",
                    instructions = TopicQuestion,
                    criteria = Topics.ToDictionary(topic => topic.Key, topic => topic.Description)
                },
                ["specific"] = new { type = "noul", instructions = SpecificQuestion }
            }
        };
        using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(new Uri(Endpoint, "v1/systemone"), content, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"The local CLM server returned HTTP {(int)response.StatusCode}.");
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Length > 65_536) throw new InvalidOperationException("The local CLM response was too large.");
        return text;
    }

    // The heads are trained on prose, not JSON: render objects as "key: value" lines, newest context last.
    internal static string StateText(string context)
    {
        string text;
        try { using var json = JsonDocument.Parse(context); text = Render(json.RootElement, 0).Trim(); }
        catch (JsonException) { text = context.Trim(); }
        text = ProfileSanitizer.Redact(text);
        return text.Length <= MaxStateCharacters ? text : text[..MaxStateCharacters];
    }

    private static string Render(JsonElement value, int indent)
    {
        var pad = new string(' ', indent);
        switch (value.ValueKind)
        {
            case JsonValueKind.String: return value.GetString() ?? "";
            case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False: return value.ToString();
            case JsonValueKind.Object:
                return string.Join(indent == 0 ? "\n\n" : "\n", value.EnumerateObject().Select(property =>
                    property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array && property.Value.GetRawText().Length > 2
                        ? $"{pad}{property.Name}:\n{Render(property.Value, indent + 2)}" : $"{pad}{property.Name}: {Render(property.Value, 0)}"));
            case JsonValueKind.Array:
                return string.Join("\n", value.EnumerateArray().Select(item => $"{pad}- {Render(item, 0)}"));
            default: return "";
        }
    }

    internal static SystemOneAdvisory ParseAdvisory(string output, string phase, IReadOnlyList<string> knownIds, double seconds, Func<string, string, bool> matchesDomain)
    {
        if (output.Length > 65_536) throw new InvalidOperationException("The local CLM response was too large.");
        using var json = JsonDocument.Parse(output);
        var answer = json.RootElement.GetProperty("answers").GetProperty("domain");
        if (answer.GetProperty("type").GetString() != "choice") throw new InvalidOperationException("Unexpected CLM answer type.");
        var probabilities = answer.GetProperty("probabilities").EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetDouble());
        var keys = Topics.Select(topic => topic.Key).ToHashSet(StringComparer.Ordinal);
        if (probabilities.Count != keys.Count || probabilities.Keys.Any(key => !keys.Contains(key)) ||
            probabilities.Values.Any(value => !double.IsFinite(value) || value is < 0 or > 1) || Math.Abs(probabilities.Values.Sum() - 1) > 0.001)
            throw new InvalidOperationException("Invalid CLM probability distribution.");
        var specific = json.RootElement.GetProperty("answers").GetProperty("specific");
        if (specific.GetProperty("type").GetString() != "noul" || specific.GetProperty("noul").GetDouble() is var gate && (!double.IsFinite(gate) || gate is < 0 or > 1))
            throw new InvalidOperationException("Unexpected CLM specificity answer.");
        var top = probabilities.MaxBy(item => item.Value);
        var confident = top.Key != "unknown" && top.Value >= AbstainBelow && specific.GetProperty("noul").GetDouble() >= AbstainBelow;
        var ids = confident ? knownIds.Where(id => matchesDomain(id, top.Key)).Take(24).ToList() : [];
        return new(phase, confident ? "ok" : "uncertain", confident ? top.Key : null, confident ? top.Value : null, seconds,
            $"CLM topic classification (local contrastive model, softmax over fixed topics; top option {top.Value.ToString("P0", CultureInfo.InvariantCulture)}). Abstains when the observation is vague. Uncalibrated, not causal proof, risk approval or authority to change any setting.", ids);
    }
}
