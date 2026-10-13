using System.Diagnostics;
using System.Text.Json;

namespace NeuroTune;

public sealed record DefenderGuardStatus(bool? RealTimeProtection, bool? TamperProtection, string Mode, bool NeedsAttention);

// F7: a persistent reminder while Microsoft Defender real-time protection is off. Exclusions made by defender.exclusion
// stay until the user rolls them back; turning protection off is always the user's own action in Windows Security.
public static class DefenderGuard
{
    private const string Command = """
        $ErrorActionPreference='Stop';
        Import-Module ($PSHOME + '\Modules\Defender\Defender.psd1') -ErrorAction Stop;
        $s=Defender\Get-MpComputerStatus -ErrorAction Stop;
        ConvertTo-Json -Compress -InputObject ([ordered]@{rt=$s.RealTimeProtectionEnabled;tamper=$s.IsTamperProtected;mode=[string]$s.AMRunningMode})
        """;

    public static DefenderGuardStatus Status()
    {
        try { return Parse(WindowsCommand.Run("powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", Command)); }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or JsonException)
        { return new(null, null, "Unavailable", false); } // A third-party antivirus or a missing module is not a reason to nag.
    }

    internal static DefenderGuardStatus Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        bool? Flag(string name) => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
        var mode = root.TryGetProperty("mode", out var text) ? text.GetString() ?? "" : "";
        var realTime = Flag("rt");
        // Passive/EDR-block modes mean another antivirus is primary; only an active Defender with real-time off needs attention.
        return new(realTime, Flag("tamper"), mode, mode.Equals("Normal", StringComparison.OrdinalIgnoreCase) && realTime == false);
    }

    public static object OpenSettings()
    {
        using var process = Process.Start(new ProcessStartInfo("windowsdefender://threatsettings") { UseShellExecute = true });
        return new { opened = true };
    }
}
