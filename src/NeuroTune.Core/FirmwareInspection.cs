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
        facts["Firmware virtualization (Windows-observed)"] = SystemProfiler.Query(
            "SELECT VirtualizationFirmwareEnabled, SecondLevelAddressTranslationExtensions FROM Win32_Processor",
            row => $"Virtualization enabled: {ObservedBoolean(row["VirtualizationFirmwareEnabled"])}; Windows-reported SLAT flag: {ObservedBoolean(row["SecondLevelAddressTranslationExtensions"])} (may be masked by an active hypervisor)")
            .FirstOrDefault() ?? "Unavailable";
        facts["TPM (Windows-observed)"] = SystemProfiler.Query(@"root\CIMV2\Security\MicrosoftTpm",
            "SELECT SpecVersion, ManufacturerVersion, IsEnabled_InitialValue FROM Win32_Tpm",
            row => $"Specification: {row["SpecVersion"]}; firmware: {row["ManufacturerVersion"]}; enabled at initialization: {ObservedBoolean(row["IsEnabled_InitialValue"])}")
            .FirstOrDefault() ?? "Unavailable or not exposed to Windows";
        var interfaces = new List<string>();
        var status = "Firmware identity and Windows-observed state are readable, but exact BIOS setup settings are unavailable: this firmware has no validated setup reader.";
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

    internal static string ObservedBoolean(object? value) => value is bool enabled ? (enabled ? "Yes" : "No") : "Unknown";

    internal static string GuidanceUrl(string board) => board.Contains("MPG X570 GAMING EDGE WIFI (MS-7C37)", StringComparison.OrdinalIgnoreCase)
        ? "https://download.msi.com/archive/mnu_exe/mb/E7C37v1.1.pdf"
        : "https://www.msi.com/support/technical_details/MB_BIOS_Manual";
}
