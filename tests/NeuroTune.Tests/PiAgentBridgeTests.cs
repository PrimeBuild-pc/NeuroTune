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
        RequireHelpers();
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

    // The planner runs through the bundled Pi helper, which an elevated host can only start on a visible desktop.
    internal static void RequireHelpers()
    {
        if (!OptionalWorkerProcess.CanStartHelpers) Assert.Inconclusive("Non-admin helpers need an interactive desktop; this elevated host runs as a service.");
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
