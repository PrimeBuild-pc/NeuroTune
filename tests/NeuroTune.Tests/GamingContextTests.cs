using System.Text;

namespace NeuroTune.Tests;

[TestClass]
public sealed class GamingContextTests
{
    [TestMethod]
    public void Steam_manifest_parser_requires_name_and_install_directory()
    {
        var parsed = GamingContextDetector.ParseSteamManifest("\"appid\" \"1\"\n\"name\" \"Example Game\"\n\"installdir\" \"ExampleGame\"");

        Assert.IsNotNull(parsed);
        Assert.AreEqual("Example Game", parsed.Value.Name);
        Assert.AreEqual("ExampleGame", parsed.Value.InstallDirectory);
        Assert.IsNull(GamingContextDetector.ParseSteamManifest("\"name\" \"Incomplete\""));
    }

    [TestMethod]
    public void Graphics_api_scan_reports_only_detected_import_signals()
    {
        var path = Path.Combine(Path.GetTempPath(), $"neurotune-game-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("binary d3d12.dll payload vulkan-1.dll"));
            var signals = GamingContextDetector.DetectGraphicsApis(path);

            CollectionAssert.Contains(signals.ToList(), "Direct3D 12 import signal");
            CollectionAssert.Contains(signals.ToList(), "Vulkan import signal");
            CollectionAssert.DoesNotContain(signals.ToList(), "OpenGL import signal");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
