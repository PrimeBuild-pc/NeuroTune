using System.Diagnostics;
using System.Text;

namespace NeuroTune.Tests;

[TestClass]
public sealed class PiAgentBridgeTests
{
    [TestMethod]
    public async Task Host_accepts_only_ordered_generated_envelopes_and_validated_completion()
    {
        using var input = Reader("""
            {"type":"generate","id":1,"turn":1}
            {"type":"handle","id":2,"turn":1,"response":"diagnosis"}
            {"type":"done"}
            """ + "\n");
        using var output = new MemoryStream();
        using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        var handled = 0;
        await PiAgentBridge.ExchangeAsync(input, writer, 2, turn => Task.FromResult("diagnosis"),
            (turn, response) => { Assert.AreEqual(1, turn); Assert.AreEqual("diagnosis", response); handled++; return Task.FromResult(true); }, CancellationToken.None);
        Assert.AreEqual(1, handled);
        StringAssert.Contains(Encoding.UTF8.GetString(output.ToArray()), "\"finished\":true");
    }

    [TestMethod]
    [DataRow("{\"type\":\"generate\",\"id\":2,\"turn\":1}\n")]
    [DataRow("{\"type\":\"generate\",\"id\":1,\"turn\":3}\n")]
    [DataRow("{\"type\":\"handle\",\"id\":1,\"turn\":0,\"response\":\"invented\"}\n")]
    [DataRow("{\"type\":\"done\"}\n")]
    [DataRow("{\"type\":\"generate\",\"id\":1,\"turn\":1}\n{\"type\":\"handle\",\"id\":2,\"turn\":1,\"response\":\"modified\"}\n")]
    [DataRow("{\"type\":\"generate\",\"id\":1,\"turn\":1}\n{\"type\":\"done\"}\n")]
    public async Task Host_rejects_out_of_order_over_budget_modified_or_incomplete_frames(string frames)
    {
        using var input = Reader(frames);
        using var output = new MemoryStream();
        using var writer = new StreamWriter(output);
        var handled = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => PiAgentBridge.ExchangeAsync(input, writer, 2,
            _ => Task.FromResult("original"), (_, _) => { handled++; return Task.FromResult(true); }, CancellationToken.None));
        Assert.AreEqual(0, handled);
    }

    [TestMethod]
    public async Task Cancelled_pipe_EOF_is_cancellation_not_a_planner_failure()
    {
        using var stop = new CancellationTokenSource();
        using var input = new EofOnCancellationReader();
        using var writer = new StreamWriter(new MemoryStream());
        var exchange = PiAgentBridge.ExchangeAsync(input, writer, 2, _ => Task.FromResult(""), (_, _) => Task.FromResult(true), stop.Token);
        stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => exchange);
    }

    [TestMethod]
    public void Startup_failures_retain_only_safe_local_causes_and_numeric_codes()
    {
        var cases = new (Exception Error, string Cause)[]
        {
            (new InvalidOperationException(PiAgentBridge.MissingRuntime), "runtime is missing"),
            (new InvalidOperationException(OptionalWorkerProcess.TokenUnavailable), "medium-integrity UAC token"),
            (new System.ComponentModel.Win32Exception(5, "secret-host-path"), "Win32 5"),
            (new OptionalWorkerProcess.ExitException(unchecked((int)0xC0000135), "secret-stderr"), "exit 0xC0000135"),
            (new UnauthorizedAccessException("secret-host-path"), "Access to the bundled Pi runtime was denied"),
            (new IOException("secret-host-path"), "path was refused"),
            (new OperationCanceledException("secret-host-path"), "before the investigation deadline")
        };
        foreach (var (error, cause) in cases)
        {
            var reason = LlmClient.PlannerFailureReason(error, "Pi agent startup", LlmClient.Defaults(LlmProvider.DeepSeek));
            Assert.Contains(cause, reason);
            Assert.Contains("no model request was sent", reason);
            Assert.DoesNotContain("secret-", reason);
            Assert.DoesNotContain("returned output", reason);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Early_worker_exit_retains_its_code_without_exporting_stderr(bool interactive)
    {
        RequireHelpers();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var script = "[Console]::Error.WriteLine('secret-stderr'); exit 37";
        var error = await Assert.ThrowsAsync<OptionalWorkerProcess.ExitException>(() => OptionalWorkerProcess.RunAsync(
            Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
            Path.GetTempPath(), "", new Dictionary<string, string> { ["SystemRoot"] = windows }, TimeSpan.FromSeconds(10), null, CancellationToken.None,
            interactive ? (reader, writer, token) => PiAgentBridge.ExchangeAsync(reader, writer, 2,
                _ => throw new AssertFailedException("A failed startup must not call a model."), (_, _) => Task.FromResult(true), token) : null));
        Assert.AreEqual(37, error.ExitCode);
        var reason = PiAgentBridge.StartupFailureReason(error);
        Assert.Contains("exit 0x00000025", reason);
        Assert.DoesNotContain("secret-stderr", reason);
    }

    [TestMethod]
    public async Task Pi_worker_can_inherit_host_privileges_without_inheriting_secret_environment()
    {
        const string secret = "NEUROTUNE_PI_TEST_SECRET";
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var script = "$p=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()); [Console]::Write($p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator).ToString().ToLower()+'|secret='+$env:NEUROTUNE_PI_TEST_SECRET)";
        Environment.SetEnvironmentVariable(secret, "not-for-pi");
        try
        {
            var result = await OptionalWorkerProcess.RunAsync(
                Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
                Path.GetTempPath(), "", new Dictionary<string, string> { ["SystemRoot"] = windows },
                TimeSpan.FromSeconds(10), null, CancellationToken.None, inheritHostToken: true);
            Assert.AreEqual(LogService.IsAdministrator().ToString().ToLowerInvariant() + "|secret=", result);
        }
        finally { Environment.SetEnvironmentVariable(secret, null); }
    }

    [TestMethod]
    public async Task Bundled_Pi_completes_a_host_validated_turn_without_non_admin_helpers()
    {
        var generated = 0; var handled = 0;
        await PiAgentBridge.RunAsync(2,
            turn => { Assert.AreEqual(1, turn); generated++; return Task.FromResult("{\"kind\":\"diagnosis\"}"); },
            (turn, response) => { Assert.AreEqual(1, turn); Assert.AreEqual("{\"kind\":\"diagnosis\"}", response); handled++; return Task.FromResult(true); },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, generated);
        Assert.AreEqual(1, handled);
    }

    [TestMethod]
    public async Task Bundled_Pi_stops_immediately_when_host_transport_fails()
    {
        var clock = Stopwatch.StartNew();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PiAgentBridge.RunAsync(2,
            _ => throw new InvalidOperationException("test transport failure"), (_, _) => Task.FromResult(true), CancellationToken.None));
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(10), "Failed interactive workers must not wait for the investigation deadline.");
    }

    [TestMethod]
    public async Task Cancellation_stops_the_bundled_Pi_child_and_preserves_cancellation()
    {
        using var stop = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = PiAgentBridge.RunAsync(2, async _ => { started.SetResult(); await Task.Delay(Timeout.Infinite, stop.Token); return ""; },
            (_, _) => Task.FromResult(true), stop.Token);
        // Surface why Pi never asked for a turn instead of a bare timeout.
        if (await Task.WhenAny(started.Task, operation, Task.Delay(TimeSpan.FromSeconds(30))) != started.Task)
            Assert.Fail($"Pi did not request a turn: {(operation.IsFaulted ? operation.Exception!.InnerException : "still running")}");
        stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    // The planner runs through the bundled Pi helper. An elevated host whose desktop refuses the filtered token (service-hosted
    // CI runners) cannot start any helper, so probe once with a real one and report those tests as inconclusive there.
    private static readonly Lazy<bool> HelpersAvailable = new(() =>
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP" })
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        try
        {
            OptionalWorkerProcess.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "exit 0"], AppContext.BaseDirectory, "", environment, TimeSpan.FromMinutes(1), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            return true;
        }
        catch (InvalidOperationException error) when (error.Message == OptionalWorkerProcess.TokenUnavailable) { return false; }
    });
    internal static bool CanStartHelpers => HelpersAvailable.Value;
    internal static void RequireHelpers()
    {
        if (!CanStartHelpers) Assert.Inconclusive("Non-admin helpers cannot open this host's desktop (service-hosted runner).");
    }

    private static StreamReader Reader(string text) => new(new MemoryStream(Encoding.UTF8.GetBytes(text)));
    private sealed class EofOnCancellationReader() : StreamReader(new MemoryStream())
    {
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { }
            return 0;
        }
    }
}
