using System.Globalization;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace NeuroTune;

public sealed record LiveFrameRateRequest(int ProcessId, int Seconds);
public sealed record LiveFrameRateSample(double Fps, double OnePercentLowFps, double FrameTimeP99Ms, int Frames);

// Display-only FPS while a game is measured: counts DXGI Present calls of one process in a real-time ETW session, like
// PresentMon's DXGI source. It never feeds reports or decisions; the recorded trace remains the measurement.
public static class LiveFrameRate
{
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private const int PresentStart = 42;
    private const string SessionName = "NeuroTune-LiveFrameRate"; // Fixed: a session orphaned by a killed agent is reclaimed next time.

    public static void Run(LiveFrameRateRequest request, Action<string> emit, CancellationToken cancellationToken = default)
    {
        if (request.ProcessId <= 0 || request.Seconds is < 1 or > 900) throw new ArgumentException("Choose a running process and 1-900 seconds.");
        var presents = new List<double>();
        using var session = new TraceEventSession(SessionName) { StopOnDispose = true };
        session.EnableProvider(Dxgi, TraceEventLevel.Informational);
        session.Source.AllEvents += data =>
        {
            if (data.ProviderGuid == Dxgi && (int)data.ID == PresentStart && data.ProcessID == request.ProcessId)
                lock (presents) presents.Add(data.TimeStampRelativeMSec);
        };
        var processing = Task.Run(() => session.Source.Process(), CancellationToken.None);
        using var stop = cancellationToken.Register(() => session.Stop());
        for (var second = 0; second < request.Seconds && !cancellationToken.IsCancellationRequested && !processing.IsCompleted; second++)
        {
            Thread.Sleep(1000);
            double[] window;
            lock (presents)
            {
                if (presents.Count > 0) presents.RemoveAll(time => time < presents[^1] - 5000); // Keep a 5-second window.
                window = [.. presents];
            }
            emit("live-fps " + JsonSerializer.Serialize(Sample(window)));
        }
    }

    internal static LiveFrameRateSample Sample(IReadOnlyList<double> presentTimes)
    {
        if (presentTimes.Count < 2) return new(0, 0, 0, presentTimes.Count);
        var last = presentTimes[^1];
        var frames = presentTimes.Zip(presentTimes.Skip(1), (a, b) => b - a).Where(gap => gap > 0).OrderBy(gap => gap).ToArray();
        var p99 = frames[Math.Min(frames.Length - 1, (int)Math.Ceiling(frames.Length * 0.99) - 1)];
        var fps = presentTimes.Count(time => time > last - 1000);
        return new(fps, Math.Round(1000 / p99, 1), Math.Round(p99, 2), presentTimes.Count);
    }
}
