using System.Management;

namespace NeuroTune;

public sealed record FirmwareReadRequest(bool ReadConsent);
public sealed record FirmwareInspectionResult(bool ReadEnabled, bool WriteSupported,
    IReadOnlyDictionary<string, string> Facts, IReadOnlyList<string> Interfaces, string SettingsStatus, string GuidanceUrl);

public static class FirmwareInspection
{
    public static FirmwareInspectionResult Read(bool consent)
    {
        if (!consent) return new(false, false, new Dictionary<string, string>(), [], "Reading is disabled.", "");
        var facts = SystemProfiler.ReadFirmwareAndMemory();
        var interfaces = new List<string>();
        var status = "No validated BIOS setup reader is available for this firmware. Current setup values remain Unknown.";
        var board = facts.GetValueOrDefault("Motherboard", "");
        var url = "";
        if (board.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase))
        {
            url = GuidanceUrl(board);
            try
            {
                using var provider = new ManagementClass(@"\\.\root\wmi:MSI_BiosSetting");
                provider.Get();
                if (provider.Methods.Cast<MethodData>().Any(method => method.Name == "GetBiosSetting"))
                {
                    interfaces.Add("MSI_BiosSetting.GetBiosSetting");
                    status = "MSI read interface detected. Its presence does not prove readable setup values; no validated item mapping is available for this BIOS.";
                }
            }
            catch (Exception error) when (error is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                status = "MSI setup interface unavailable or inaccessible. SMBIOS and Windows-observed firmware facts are shown below.";
            }
        }
        return new(true, false, facts, interfaces, status, url);
    }

    internal static string GuidanceUrl(string board) => board.Contains("MPG X570 GAMING EDGE WIFI (MS-7C37)", StringComparison.OrdinalIgnoreCase)
        ? "https://download.msi.com/archive/mnu_exe/mb/E7C37v1.1.pdf"
        : "https://www.msi.com/support/technical_details/MB_BIOS_Manual";
}
