namespace NeuroTune.Tests;

[TestClass]
public sealed class GamePackageTests
{
    [TestMethod]
    public void Packages_load_and_attach_only_for_a_running_detected_or_named_game()
    {
        CollectionAssert.AreEquivalent(new[] { "apex", "call-of-duty", "cs2", "fortnite", "valorant" }, GamePackages.All.Select(item => item.Id).ToArray());

        var running = LlmClient.BuildEvidenceFacts(new SystemProfile { GameExecutables = ["cs2.exe"] });
        StringAssert.Contains(running["game-package:cs2"], "VAC");
        Assert.IsFalse(running.Keys.Any(key => key.StartsWith("game-package:fortnite", StringComparison.Ordinal)));

        var named = LlmClient.BuildEvidenceFacts(new SystemProfile(), new TuningGoals { Games = ["VALORANT ranked"] });
        Assert.IsTrue(named.ContainsKey("game-package:valorant"));
        Assert.IsEmpty(LlmClient.BuildEvidenceFacts(new SystemProfile(), new TuningGoals { Games = ["Minecraft"] }).Keys.Where(key => key.StartsWith("game-package:", StringComparison.Ordinal)));
    }
}
