#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$Windows11Iso = (Join-Path $env:USERPROFILE 'Desktop\ISO Original\Win11_25H2_EnglishInternational_x64_v2.iso'),
    [string]$VmRoot = 'C:\VmLab',
    [string]$CheckpointName = 'Clean-NeuroTune-Alpha2'
)

$ErrorActionPreference = 'Stop'
$credentialRoot = Join-Path $env:USERPROFILE '.neurotune-vm'
New-Item -ItemType Directory -Force -Path $credentialRoot | Out-Null

function New-RandomPassword {
    $alphabet = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!#%+-_'
    -join (1..28 | ForEach-Object { $alphabet[[System.Security.Cryptography.RandomNumberGenerator]::GetInt32($alphabet.Length)] })
}

function Get-InstallImage {
    param([string]$IsoPath)
    if (-not (Test-Path -LiteralPath $IsoPath)) { throw "ISO not found: $IsoPath" }
    $image = Mount-DiskImage -ImagePath $IsoPath -PassThru
    $volume = $image | Get-Volume
    $install = @(
        Join-Path ($volume.DriveLetter + ':') 'sources\install.wim'
        Join-Path ($volume.DriveLetter + ':') 'sources\install.esd'
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $install) { Dismount-DiskImage -ImagePath $IsoPath; throw "No install.wim or install.esd in $IsoPath" }
    $edition = Get-WindowsImage -ImagePath $install |
        Where-Object { $_.ImageName -match 'Windows 11 Pro$' } |
        Select-Object -First 1
    if (-not $edition) { Dismount-DiskImage -ImagePath $IsoPath; throw "Windows Pro image not found in $IsoPath" }
    [pscustomobject]@{ DiskImage = $image; InstallPath = $install; ImageIndex = $edition.ImageIndex }
}

function New-NeuroTuneVm {
    param(
        [string]$Name,
        [string]$IsoPath,
        [string]$CredentialFile,
        [string]$Locale,
        [bool]$EnableHvci
    )
    if (Get-VM -Name $Name -ErrorAction SilentlyContinue) { throw "VM already exists; refusing to overwrite: $Name" }
    $directory = Join-Path $VmRoot $Name
    if (Test-Path -LiteralPath $directory) { throw "VM directory already exists; refusing to overwrite: $directory" }
    New-Item -ItemType Directory -Path $directory | Out-Null

    $password = New-RandomPassword
    $secure = ConvertTo-SecureString $password -AsPlainText -Force
    [pscredential]::new('NeuroTuneTest', $secure) | Export-Clixml -LiteralPath $CredentialFile

    $mounted = Get-InstallImage $IsoPath
    $vhdPath = Join-Path $directory "$Name.vhdx"
    try {
        New-VHD -Path $vhdPath -Dynamic -SizeBytes 80GB | Out-Null
        $disk = Mount-VHD -Path $vhdPath -Passthru | Get-Disk
        Initialize-Disk -Number $disk.Number -PartitionStyle GPT
        $efi = New-Partition -DiskNumber $disk.Number -Size 260MB -AssignDriveLetter -GptType '{C12A7328-F81F-11D2-BA4B-00A0C93EC93B}'
        Format-Volume -Partition $efi -FileSystem FAT32 -NewFileSystemLabel 'SYSTEM' -Confirm:$false | Out-Null
        New-Partition -DiskNumber $disk.Number -Size 16MB -GptType '{E3C9E316-0B5C-4DB8-817D-F92DF00215AE}' | Out-Null
        $windows = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter
        Format-Volume -Partition $windows -FileSystem NTFS -NewFileSystemLabel 'Windows' -Confirm:$false | Out-Null
        $windowsDrive = $windows.DriveLetter + ':'
        $efiDrive = $efi.DriveLetter + ':'
        Expand-WindowsImage -ImagePath $mounted.InstallPath -Index $mounted.ImageIndex -ApplyPath ($windowsDrive + '\')
        & bcdboot.exe ($windowsDrive + '\Windows') /s $efiDrive /f UEFI | Out-Null

        $panther = Join-Path $windowsDrive 'Windows\Panther'
        New-Item -ItemType Directory -Force -Path $panther | Out-Null
        $unattend = @"
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend">
  <settings pass="specialize">
    <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
      <ComputerName>$Name</ComputerName><TimeZone>W. Europe Standard Time</TimeZone>
    </component>
  </settings>
  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-International-Core" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
      <InputLocale>$Locale</InputLocale><SystemLocale>$Locale</SystemLocale><UILanguage>$Locale</UILanguage><UserLocale>$Locale</UserLocale>
    </component>
    <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
      <OOBE><HideEULAPage>true</HideEULAPage><HideOnlineAccountScreens>true</HideOnlineAccountScreens><HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE><ProtectYourPC>3</ProtectYourPC></OOBE>
      <UserAccounts><LocalAccounts><LocalAccount wcm:action="add" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State"><Name>NeuroTuneTest</Name><Group>Administrators</Group><Password><Value>$password</Value><PlainText>true</PlainText></Password></LocalAccount></LocalAccounts></UserAccounts>
      <AutoLogon><Enabled>true</Enabled><Username>NeuroTuneTest</Username><LogonCount>2</LogonCount><Password><Value>$password</Value><PlainText>true</PlainText></Password></AutoLogon>
    </component>
  </settings>
</unattend>
"@
        [IO.File]::WriteAllText((Join-Path $panther 'unattend.xml'), $unattend, [Text.UTF8Encoding]::new($false))
    }
    finally {
        Dismount-VHD -Path $vhdPath -ErrorAction SilentlyContinue
        Dismount-DiskImage -ImagePath $IsoPath -ErrorAction SilentlyContinue
    }

    New-VM -Name $Name -Generation 2 -MemoryStartupBytes 8GB -VHDPath $vhdPath -SwitchName 'Default Switch' -Path $directory | Out-Null
    Set-VMProcessor -VMName $Name -Count 4
    Set-VMMemory -VMName $Name -DynamicMemoryEnabled $false
    Set-VMFirmware -VMName $Name -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows
    Set-VM -Name $Name -CheckpointType ProductionOnly -AutomaticCheckpointsEnabled $false
    Set-VMKeyProtector -VMName $Name -NewLocalKeyProtector
    Enable-VMTPM -VMName $Name
    Start-VM -Name $Name | Out-Null

    $credential = Import-Clixml -LiteralPath $CredentialFile
    $deadline = (Get-Date).AddMinutes(35)
    do {
        Start-Sleep -Seconds 10
        try {
            Invoke-Command -VMName $Name -Credential $credential -ScriptBlock { $env:COMPUTERNAME } -ErrorAction Stop | Out-Null
            $ready = $true
        } catch { $ready = $false }
    } until ($ready -or (Get-Date) -gt $deadline)
    if (-not $ready) { throw "PowerShell Direct did not become ready for $Name within 35 minutes." }

    Invoke-Command -VMName $Name -Credential $credential -ScriptBlock {
        param($Hvci)
        New-Item -ItemType Directory -Force C:\NeuroTuneTest | Out-Null
        Remove-Item -LiteralPath C:\Windows\Panther\unattend.xml -Force -ErrorAction SilentlyContinue
        Enable-ComputerRestore -Drive 'C:\'
        & vssadmin.exe Resize ShadowStorage /For=C: /On=C: /MaxSize=10% | Out-Null
        New-Item 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' -Force | Out-Null
        New-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' -Name SystemRestorePointCreationFrequency -PropertyType DWord -Value 0 -Force | Out-Null
        if ($Hvci) {
            New-Item 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity' -Force | Out-Null
            New-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity' -Name Enabled -PropertyType DWord -Value 1 -Force | Out-Null
        }
        Checkpoint-Computer -Description 'NeuroTune VM preflight' -RestorePointType MODIFY_SETTINGS
        powercfg.exe /hibernate off
        Restart-Computer -Force
    } -ArgumentList $EnableHvci

    $restartDeadline = (Get-Date).AddMinutes(8)
    $ready = $false
    do {
        Start-Sleep -Seconds 10
        try {
            Invoke-Command -VMName $Name -Credential $credential -ScriptBlock { Get-Date } -ErrorAction Stop | Out-Null
            $ready = $true
        } catch { $ready = $false }
    } until ($ready -or (Get-Date) -gt $restartDeadline)
    if (-not $ready) { throw "PowerShell Direct did not recover after restart for $Name." }

    Stop-VM -Name $Name
    $stopDeadline = (Get-Date).AddMinutes(5)
    while ((Get-VM -Name $Name).State -ne 'Off' -and (Get-Date) -lt $stopDeadline) {
        Start-Sleep -Seconds 2
    }
    if ((Get-VM -Name $Name).State -ne 'Off') { throw "Guest shutdown timed out for $Name." }
    Checkpoint-VM -Name $Name -SnapshotName $CheckpointName
    Start-VM -Name $Name | Out-Null
}

New-NeuroTuneVm -Name 'NeuroTune-W11' -IsoPath $Windows11Iso -CredentialFile (Join-Path $credentialRoot 'w11-credential.xml') -Locale 'en-GB' -EnableHvci $true
