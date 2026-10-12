namespace NeuroTune.Tests;

[TestClass]
public sealed class LiveFrameRateTests
{
    [TestMethod]
    public void Sample_reports_last_second_fps_and_slow_frame_tail()
    {
        // 120 frames at ~8.33 ms with one 50 ms hitch.
        var times = Enumerable.Range(0, 120).Select(i => i * 8.333).ToList();
        times.Add(times[^1] + 50);
        var sample = LiveFrameRate.Sample(times);
        Assert.AreEqual(121, sample.Frames);
        Assert.IsTrue(sample.Fps is >= 115 and <= 121, $"fps {sample.Fps}");
        Assert.AreEqual(8.33, sample.FrameTimeP99Ms, 0.01);
        Assert.AreEqual(0, LiveFrameRate.Sample([5]).Fps);
        Assert.ThrowsExactly<ArgumentException>(() => LiveFrameRate.Run(new(0, 10), _ => { }));
    }
}
