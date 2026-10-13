namespace NeuroTune.Tests;

[TestClass]
public sealed class DefenderGuardTests
{
    [TestMethod]
    public void Only_an_active_Defender_with_real_time_protection_off_needs_attention()
    {
        Assert.IsTrue(DefenderGuard.Parse("""{"rt":false,"tamper":false,"mode":"Normal"}""").NeedsAttention);
        Assert.IsFalse(DefenderGuard.Parse("""{"rt":true,"tamper":true,"mode":"Normal"}""").NeedsAttention);
        Assert.IsFalse(DefenderGuard.Parse("""{"rt":false,"tamper":null,"mode":"Passive Mode"}""").NeedsAttention); // Another antivirus is primary.
        Assert.IsNull(DefenderGuard.Parse("""{"mode":"Normal"}""").RealTimeProtection);
    }

    [TestMethod]
    public void Live_status_reads_without_changing_anything() =>
        Assert.IsNotNull(DefenderGuard.Status().Mode);
}
