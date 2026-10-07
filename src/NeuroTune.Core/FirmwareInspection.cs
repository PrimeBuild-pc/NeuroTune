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
        facts["System model"] = SystemProfiler.Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem",
            row => $"{row["Manufacturer"]} {row["Model"]}").FirstOrDefault() ?? "Unavailable";
        var board = facts.GetValueOrDefault("Motherboard", "");
        var status = SetupStatus(board, facts["System model"]);
        facts["BIOS setup reader status"] = status;
        facts["BIOS reader driver requirement"] = "No kernel driver is required by this Windows reader. Importing an existing SCEWIN export also needs no driver. PawnIO is not a validated MSI setup reader; privileged SCEWIN export uses its own external-tool path.";
        // No documented MSI_BiosSetting/GetBiosSetting contract: never invent or invoke a vendor method.
        return new(true, false, facts, [], status,
            IsMsiBoard(board) ? GuidanceUrl(board) : "");
    }

    internal static string SetupStatus(string board, string systemModel)
    {
        if (new[] { "virtual machine", "vmware", "virtualbox", "qemu", "kvm", "hyper-v" }
            .Any(marker => systemModel.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return "Virtual firmware only: the physical host BIOS and DIMMs are not accessible from this guest. Do not run motherboard export tools here to inspect the host.";
        return IsMsiBoard(board)
            ? "MSI board identified. No documented, validated MSI setup API is available. Windows exposes identity and observed state, not PBO/C-states/XMP settings. Use an existing SCEWIN export for compatible AMI/HII firmware, or verify in the physical BIOS. No BIOS writing is supported."
            : "Firmware identity and Windows-observed state are readable, but exact BIOS setup settings require a compatible external export. No BIOS writing is supported.";
    }

    private static bool IsMsiBoard(string board) => board.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) ||
        board.Trim().Equals("MSI", StringComparison.OrdinalIgnoreCase) || board.Trim().StartsWith("MSI ", StringComparison.OrdinalIgnoreCase);

    internal static string ObservedBoolean(object? value) => value is bool enabled ? (enabled ? "Yes" : "No") : "Unknown";

    internal static string GuidanceUrl(string board) => board.Contains("MPG X570 GAMING EDGE WIFI (MS-7C37)", StringComparison.OrdinalIgnoreCase)
        ? "https://download.msi.com/archive/mnu_exe/mb/E7C37v1.1.pdf"
        : "https://www.msi.com/support/technical_details/MB_BIOS_Manual";
}
