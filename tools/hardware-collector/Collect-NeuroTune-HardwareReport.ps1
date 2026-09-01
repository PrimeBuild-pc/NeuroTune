#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$collectorVersion = '0.1.0'
$warnings = [Collections.Generic.List[string]]::new()
$deviceKeys = @{}

function Get-DeviceKey([string]$Value) {
    if (-not $deviceKeys.ContainsKey($Value)) {
        $deviceKeys[$Value] = 'device-' + [guid]::NewGuid().ToString('N').Substring(0, 16)
    }
    return $deviceKeys[$Value]
}

function Get-CimRows([string]$ClassName, [string]$Namespace = 'root/cimv2') {
    try { return @(Get-CimInstance -Namespace $Namespace -ClassName $ClassName -ErrorAction Stop) }
    catch {
        $warnings.Add("$ClassName unavailable")
        return @()
    }
}

function Convert-Date($Value) {
    if ($null -eq $Value) { return $null }
    try { return ([datetime]$Value).ToUniversalTime().ToString('o') }
    catch { return $null }
}

function Get-RegistryValueSnapshot([string]$SubKey, [string]$Name, [bool]$IncludeValue = $true) {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $base.OpenSubKey($SubKey, $false)
        if ($null -eq $key) { return [ordered]@{ exists = $false; kind = 'None'; byteLength = 0; hexValue = '' } }
        try {
            $value = $key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            if ($null -eq $value) { return [ordered]@{ exists = $false; kind = 'None'; byteLength = 0; hexValue = '' } }
            $kind = $key.GetValueKind($Name).ToString()
            switch ($kind) {
                'Binary' {
                    $bytes = [byte[]]$value
                    $hex = if ($IncludeValue) { ([BitConverter]::ToString($bytes)).Replace('-', '') } else { '' }
                    return [ordered]@{ exists = $true; kind = $kind; byteLength = $bytes.Length; hexValue = $hex }
                }
                'DWord' {
                    $hex = if ($IncludeValue) { '{0:X8}' -f [uint32]$value } else { '' }
                    return [ordered]@{ exists = $true; kind = $kind; byteLength = 4; hexValue = $hex }
                }
                'QWord' {
                    $hex = if ($IncludeValue) { '{0:X16}' -f [uint64]$value } else { '' }
                    return [ordered]@{ exists = $true; kind = $kind; byteLength = 8; hexValue = $hex }
                }
                default { return [ordered]@{ exists = $true; kind = $kind; byteLength = 0; hexValue = '' } }
            }
        }
        finally { $key.Dispose() }
    }
    catch {
        $warnings.Add('An interrupt policy could not be read')
        return [ordered]@{ exists = $false; kind = 'Unavailable'; byteLength = 0; hexValue = '' }
    }
    finally { $base.Dispose() }
}

function Get-InterruptPolicy([string]$DeviceId) {
    $root = "SYSTEM\CurrentControlSet\Enum\$DeviceId\Device Parameters\Interrupt Management"
    $assignment = Get-RegistryValueSnapshot "$root\Affinity Policy" 'AssignmentSetOverride' $false
    $policy = Get-RegistryValueSnapshot "$root\Affinity Policy" 'DevicePolicy'
    $msi = Get-RegistryValueSnapshot "$root\MessageSignaledInterruptProperties" 'MSISupported'
    $assignmentValid = -not $assignment.exists -or
        ($assignment.kind -eq 'Binary' -and $assignment.byteLength -ge 1 -and $assignment.byteLength -le 8) -or
        ($assignment.kind -eq 'DWord' -and $assignment.byteLength -eq 4) -or
        ($assignment.kind -eq 'QWord' -and $assignment.byteLength -eq 8)
    $policyValid = -not $policy.exists -or ($policy.kind -eq 'DWord' -and $policy.byteLength -eq 4)
    $state = if (-not $assignment.exists -and -not $policy.exists) { 'windowsDefault' }
        elseif ($assignmentValid -and $policyValid) { 'configured' }
        else { 'unsupportedValueType' }
    return [ordered]@{
        state = $state
        assignmentSetOverride = $assignment
        devicePolicy = $policy
        msiSupported = $msi
    }
}

function Get-PublicHardwareId([string]$DeviceId) {
    $parts = $DeviceId -split '\\'
    if ($parts.Count -lt 2) { return $parts[0] }
    return "$($parts[0])\$($parts[1])"
}

if (-not ('NeuroTuneCollector.CpuSets' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NeuroTuneCollector {
    public sealed class CpuSetEntry {
        public uint Id { get; set; }
        public ushort ProcessorGroup { get; set; }
        public byte LogicalProcessor { get; set; }
        public byte PhysicalCore { get; set; }
        public byte LastLevelCache { get; set; }
        public byte NumaNode { get; set; }
        public byte EfficiencyClass { get; set; }
        public bool Parked { get; set; }
    }

    public static class CpuSets {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemCpuSetInformation(IntPtr information, uint bufferLength, out uint returnedLength, IntPtr process, uint flags);

        public static List<CpuSetEntry> Read() {
            uint length;
            GetSystemCpuSetInformation(IntPtr.Zero, 0, out length, IntPtr.Zero, 0);
            if (length == 0 || Marshal.GetLastWin32Error() != 122) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try {
                if (!GetSystemCpuSetInformation(buffer, length, out length, IntPtr.Zero, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var result = new List<CpuSetEntry>();
                for (int offset = 0; offset < length;) {
                    IntPtr current = IntPtr.Add(buffer, offset);
                    int size = Marshal.ReadInt32(current);
                    if (size < 24 || offset + size > length) throw new InvalidOperationException("Invalid CPU-set data.");
                    if (Marshal.ReadInt32(current, 4) == 0) {
                        byte flags = Marshal.ReadByte(current, 19);
                        result.Add(new CpuSetEntry {
                            Id = unchecked((uint)Marshal.ReadInt32(current, 8)),
                            ProcessorGroup = unchecked((ushort)Marshal.ReadInt16(current, 12)),
                            LogicalProcessor = Marshal.ReadByte(current, 14),
                            PhysicalCore = Marshal.ReadByte(current, 15),
                            LastLevelCache = Marshal.ReadByte(current, 16),
                            NumaNode = Marshal.ReadByte(current, 17),
                            EfficiencyClass = Marshal.ReadByte(current, 18),
                            Parked = (flags & 1) != 0
                        });
                    }
                    offset += size;
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
}
'@
}

$operatingSystem = (Get-CimRows 'Win32_OperatingSystem' | Select-Object -First 1)
$computer = (Get-CimRows 'Win32_ComputerSystem' | Select-Object -First 1)
$baseboard = (Get-CimRows 'Win32_BaseBoard' | Select-Object -First 1)
$bios = (Get-CimRows 'Win32_BIOS' | Select-Object -First 1)
$processors = @(Get-CimRows 'Win32_Processor' | ForEach-Object {
    [pscustomobject][ordered]@{
        name = [string]$_.Name
        manufacturer = [string]$_.Manufacturer
        physicalCores = [int]$_.NumberOfCores
        logicalProcessors = [int]$_.NumberOfLogicalProcessors
        maxClockMhz = [int]$_.MaxClockSpeed
    }
})

try { $cpuSets = @([NeuroTuneCollector.CpuSets]::Read()) }
catch { $warnings.Add('Windows CPU-set topology unavailable'); $cpuSets = @() }

$signedDrivers = Get-CimRows 'Win32_PnPSignedDriver'
$deviceClasses = @('DISPLAY', 'NET', 'MEDIA', 'USB', 'HDC', 'SCSIADAPTER')
$devices = @($signedDrivers | Where-Object {
    $class = ([string]$_.DeviceClass).ToUpperInvariant()
    $id = [string]$_.DeviceID
    $deviceClasses -contains $class -and
        ($id.StartsWith('PCI\', [StringComparison]::OrdinalIgnoreCase) -or
         ($class -eq 'MEDIA' -and $id.StartsWith('HDAUDIO\', [StringComparison]::OrdinalIgnoreCase)))
} | ForEach-Object {
    $id = [string]$_.DeviceID
    [pscustomobject][ordered]@{
        deviceKey = Get-DeviceKey $id
        hardwareId = Get-PublicHardwareId $id
        class = ([string]$_.DeviceClass).ToUpperInvariant()
        name = [string]$_.DeviceName
        manufacturer = [string]$_.Manufacturer
        driverVersion = [string]$_.DriverVersion
        driverDateUtc = Convert-Date $_.DriverDate
        interrupt = Get-InterruptPolicy $id
    }
} | Sort-Object class, hardwareId, name -Unique)

$gpus = @(Get-CimRows 'Win32_VideoController' | Where-Object { [string]$_.PNPDeviceID -like 'PCI\*' } | ForEach-Object {
    $id = [string]$_.PNPDeviceID
    $vendor = if ($id -match 'VEN_10DE') { 'NVIDIA' } elseif ($id -match 'VEN_1002') { 'AMD' } elseif ($id -match 'VEN_8086') { 'Intel' } else { 'Other' }
    [pscustomobject][ordered]@{
        deviceKey = Get-DeviceKey $id
        hardwareId = Get-PublicHardwareId $id
        vendor = $vendor
        name = [string]$_.Name
        driverVersion = [string]$_.DriverVersion
        driverDateUtc = Convert-Date $_.DriverDate
    }
})

$secureBoot = $null
try { $secureBoot = [bool](Confirm-SecureBootUEFI -ErrorAction Stop) }
catch { $warnings.Add('Secure Boot state unavailable') }
$deviceGuard = (Get-CimRows 'Win32_DeviceGuard' 'root/Microsoft/Windows/DeviceGuard' | Select-Object -First 1)

$report = [ordered]@{
    schemaVersion = 1
    collectorVersion = $collectorVersion
    reportId = [guid]::NewGuid().ToString('D')
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    collectorGuarantees = [ordered]@{
        requiresAdministrator = $false
        internetAccess = $false
        writesSystemState = $false
        excludedFields = @('user name', 'computer name', 'serial numbers', 'MAC/IP addresses', 'full paths', 'raw PnP instance IDs')
        deviceKey = 'Random report-local identifier; the raw PnP identity is not exported'
        assignmentSetOverrideValueExported = $false
    }
    windows = [ordered]@{
        caption = [string]$operatingSystem.Caption
        version = [string]$operatingSystem.Version
        build = [string]$operatingSystem.BuildNumber
        architecture = [string]$operatingSystem.OSArchitecture
        secureBoot = $secureBoot
        virtualizationBasedSecurityStatus = if ($null -eq $deviceGuard) { $null } else { [int]$deviceGuard.VirtualizationBasedSecurityStatus }
        securityServicesConfigured = if ($null -eq $deviceGuard) { @() } else { @($deviceGuard.SecurityServicesConfigured | ForEach-Object { [int]$_ }) }
        securityServicesRunning = if ($null -eq $deviceGuard) { @() } else { @($deviceGuard.SecurityServicesRunning | ForEach-Object { [int]$_ }) }
    }
    platform = [ordered]@{
        manufacturer = [string]$computer.Manufacturer
        model = [string]$computer.Model
        totalPhysicalMemoryBytes = if ($null -eq $computer.TotalPhysicalMemory) { $null } else { [uint64]$computer.TotalPhysicalMemory }
        hypervisorPresent = if ($null -eq $computer.HypervisorPresent) { $null } else { [bool]$computer.HypervisorPresent }
        motherboardManufacturer = [string]$baseboard.Manufacturer
        motherboardModel = [string]$baseboard.Product
        motherboardVersion = [string]$baseboard.Version
        biosManufacturer = [string]$bios.Manufacturer
        biosVersion = [string]$bios.SMBIOSBIOSVersion
        biosReleaseDateUtc = Convert-Date $bios.ReleaseDate
    }
    processors = $processors
    cpuSets = @($cpuSets | ForEach-Object {
        [pscustomobject][ordered]@{
            id = [uint32]$_.Id
            processorGroup = [uint16]$_.ProcessorGroup
            logicalProcessor = [byte]$_.LogicalProcessor
            physicalCore = [byte]$_.PhysicalCore
            lastLevelCache = [byte]$_.LastLevelCache
            numaNode = [byte]$_.NumaNode
            efficiencyClass = [byte]$_.EfficiencyClass
            parked = [bool]$_.Parked
        }
    })
    gpus = $gpus
    interruptDevices = $devices
    warnings = @($warnings | Sort-Object -Unique)
}

$json = $report | ConvertTo-Json -Depth 9
$forbidden = '(?i)([A-Z]:\\\\Users\\\\|\\\\Users\\\\|/home/|computerName|userName|serialNumber|macAddress|ipAddress|pnpDeviceId|deviceInstanceId|registryPath|pathName)'
if ($json -match $forbidden) { throw 'Privacy self-check failed; no report was written.' }

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $OutputPath = Join-Path $PSScriptRoot "NeuroTune-HardwareReport-$stamp.json"
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.File]::WriteAllText($resolvedOutput, $json, [Text.UTF8Encoding]::new($false))
Write-Host "NeuroTune report created: $resolvedOutput" -ForegroundColor Green
Write-Host 'No settings, services, drivers, Registry values, or network state were changed.'
