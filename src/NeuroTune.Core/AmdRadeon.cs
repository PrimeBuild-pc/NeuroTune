using System.Reflection;
using System.Runtime.ExceptionServices;

namespace NeuroTune;

// F5 AMD: global Radeon 3D settings. The ADLX calls live in NeuroTune.AmdAdlx.dll, a separate component under the
// AMD ADLX SDK license (licenses/AMD-COMPONENT-EULA.txt, source not public). Official builds ship it in agent\amd;
// builds from this repository run without it and refuse amd.setting / amd-3d with a clear message.
internal static class AmdRadeon
{
    private const string ComponentFile = "NeuroTune.AmdAdlx.dll";

    internal static readonly string[] Names = ["antiLag", "chill", "boost", "imageSharpening", "enhancedSync", "waitForVerticalRefresh", "frameRateTargetControl", "frameRateTargetControlFps", "morphologicalAntiAliasing"];
    internal const string Guide = "antiLag, chill, boost, imageSharpening, enhancedSync, frameRateTargetControl, morphologicalAntiAliasing: 0 off, 1 on; waitForVerticalRefresh: 0 always off, 1 off unless the application specifies, 2 on unless the application specifies, 3 always on; frameRateTargetControlFps: FPS within the range read by amd-3d, only while frameRateTargetControl is 1 (the driver refuses it otherwise)";

    // Only next to the agent (amd\ in the install folder, which only administrators can write) or a test output folder.
    private static readonly Lazy<Type?> Component = new(() =>
        new[] { Path.Combine(AppContext.BaseDirectory, "amd", ComponentFile), Path.Combine(AppContext.BaseDirectory, ComponentFile) }
            .FirstOrDefault(File.Exists) is { } path ? Assembly.LoadFrom(path).GetType("NeuroTune.AmdAdlxNative", throwOnError: true) : null);

    internal static bool Included => Component.Value is not null;

    private static object? Call(string method, params object[] arguments)
    {
        var type = Component.Value ?? throw new InvalidOperationException("AMD Radeon settings need the AMD component of the official NeuroTune build; this build does not include it.");
        try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, arguments); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(error.InnerException);
            throw;
        }
    }

    internal static int Get(string name) => (int)Call(nameof(Get), name)!;

    internal static void Set(string name, int value) => Call(nameof(Set), name, value);

    internal static object Read() => Call(nameof(Read))!;
}
