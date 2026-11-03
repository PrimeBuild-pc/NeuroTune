# NeuroTune: read-only full system scan for a Windows 11 gaming PC. It changes nothing.
# Launched by `cli.mjs scan-system`, which summarizes the result with `system-findings`.
# Sections that need administrator rights (driver store, BCD) are reported as unavailable when not elevated.
param([Parameter(Mandatory = $true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$scan = [ordered]@{ schemaVersion = 1; kind = 'system-scan'; scannerVersion = '1'; generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
  isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
  unavailable = [ordered]@{} }
function Clip($value, [int]$max = 256) { if ($null -eq $value) { return $null }; $s = ([string]$value).Trim(); if ($s.Length -gt $max) { $s.Substring(0, $max) } else { $s } }
function Section([string]$name, [scriptblock]$body) {
  try { $scan[$name] = & $body } catch { $scan[$name] = $null; $scan.unavailable[$name] = Clip $_.Exception.Message 200 }
}
function RegValue([string]$path, [string]$name) { try { (Get-ItemProperty -LiteralPath $path -Name $name -ErrorAction Stop).$name } catch { $null } }

Section 'hardware' {
  $os = Get-CimInstance Win32_OperatingSystem
  $board = Get-CimInstance Win32_BaseBoard | Select-Object -First 1
  $bios = Get-CimInstance Win32_BIOS | Select-Object -First 1
  [ordered]@{
    os = [ordered]@{ caption = Clip $os.Caption; version = Clip $os.Version; build = Clip $os.BuildNumber; lastBoot = $os.LastBootUpTime.ToUniversalTime().ToString('o') }
    cpus = @(Get-CimInstance Win32_Processor | ForEach-Object { [ordered]@{ name = Clip $_.Name; cores = $_.NumberOfCores; logical = $_.NumberOfLogicalProcessors; maxClockMHz = $_.MaxClockSpeed } })
    gpus = @(Get-CimInstance Win32_VideoController | ForEach-Object { [ordered]@{ name = Clip $_.Name; driverVersion = Clip $_.DriverVersion
      driverDate = if ($_.DriverDate) { $_.DriverDate.ToString('yyyy-MM-dd') } else { $null }; refreshHz = $_.CurrentRefreshRate
      resolution = if ($_.CurrentHorizontalResolution) { "$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)" } else { $null } } })
    memory = @(Get-CimInstance Win32_PhysicalMemory | ForEach-Object { [ordered]@{ slot = Clip $_.DeviceLocator 64; gb = [math]::Round($_.Capacity / 1GB, 1)
      ratedMHz = $_.Speed; configuredMHz = $_.ConfiguredClockSpeed; manufacturer = Clip $_.Manufacturer 64; part = Clip $_.PartNumber 64 } })
    memorySlots = (Get-CimInstance Win32_PhysicalMemoryArray | Measure-Object -Property MemoryDevices -Sum).Sum
    board = [ordered]@{ manufacturer = Clip $board.Manufacturer 64; product = Clip $board.Product 64 }
    bios = [ordered]@{ version = Clip $bios.SMBIOSBIOSVersion 64; date = if ($bios.ReleaseDate) { $bios.ReleaseDate.ToString('yyyy-MM-dd') } else { $null } }
    disks = @(Get-PhysicalDisk | ForEach-Object { [ordered]@{ name = Clip $_.FriendlyName 128; media = [string]$_.MediaType; bus = [string]$_.BusType; gb = [math]::Round($_.Size / 1GB) } })
  }
}

Section 'devices' {
  $all = @(Get-PnpDevice -ErrorAction SilentlyContinue)
  $props = @{}
  $keys = 'DEVPKEY_Device_Parent', 'DEVPKEY_Device_DriverInfPath', 'DEVPKEY_Device_DriverVersion', 'DEVPKEY_Device_DriverDate', 'DEVPKEY_Device_DriverProvider', 'DEVPKEY_Device_ProblemCode', 'DEVPKEY_Device_Service'
  $all | Where-Object Present | Get-PnpDeviceProperty -KeyName $keys -ErrorAction SilentlyContinue | ForEach-Object {
    if (-not $props.ContainsKey($_.InstanceId)) { $props[$_.InstanceId] = @{} }; $props[$_.InstanceId][$_.KeyName] = $_.Data }
  ,@($all | ForEach-Object {
    $p = $props[$_.InstanceId]
    [ordered]@{ id = Clip $_.InstanceId 400; name = Clip $_.FriendlyName; class = Clip $_.Class 64; status = [string]$_.Status; present = [bool]$_.Present
      problem = if ($p) { $p['DEVPKEY_Device_ProblemCode'] } else { $null }; parent = if ($p) { Clip $p['DEVPKEY_Device_Parent'] 400 } else { $null }
      service = if ($p) { Clip $p['DEVPKEY_Device_Service'] 64 } else { $null }; inf = if ($p) { Clip $p['DEVPKEY_Device_DriverInfPath'] 64 } else { $null }
      driverVersion = if ($p) { Clip $p['DEVPKEY_Device_DriverVersion'] 64 } else { $null }; driverProvider = if ($p) { Clip $p['DEVPKEY_Device_DriverProvider'] 128 } else { $null }
      driverDate = if ($p -and $p['DEVPKEY_Device_DriverDate']) { ([datetime]$p['DEVPKEY_Device_DriverDate']).ToString('yyyy-MM-dd') } else { $null } }
  })
}

Section 'driverStore' {
  if (-not $scan.isAdmin) { throw 'needs administrator rights (Get-WindowsDriver)' }
  ,@(Get-WindowsDriver -Online | ForEach-Object { [ordered]@{ inf = Clip $_.Driver 64; original = Clip ([IO.Path]::GetFileName([string]$_.OriginalFileName)) 128
    provider = Clip $_.ProviderName 128; class = Clip $_.ClassName 64; version = Clip $_.Version 64; date = if ($_.Date) { $_.Date.ToString('yyyy-MM-dd') } else { $null } } })
}

Section 'interrupts' {
  ,@(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'PCI\*' } | ForEach-Object {
    $base = "HKLM:\SYSTEM\CurrentControlSet\Enum\$($_.InstanceId)\Device Parameters\Interrupt Management"
    $msi = $null; $aff = $null
    try { $msi = Get-ItemProperty -LiteralPath "$base\MessageSignaledInterruptProperties" -ErrorAction Stop } catch {}
    try { $aff = Get-ItemProperty -LiteralPath "$base\Affinity Policy" -ErrorAction Stop } catch {}
    [ordered]@{ id = Clip $_.InstanceId 400; name = Clip $_.FriendlyName; class = Clip $_.Class 64; msiCapable = [bool]$msi
      msiEnabled = if ($msi) { $msi.MSISupported -eq 1 } else { $false }; messageLimit = if ($msi) { $msi.MessageNumberLimit } else { $null }
      priority = if ($aff) { $aff.DevicePriority } else { $null }; policy = if ($aff) { $aff.DevicePolicy } else { $null }
      affinityMask = if ($aff -and $aff.AssignmentSetOverride) { [Convert]::ToBase64String([byte[]]$aff.AssignmentSetOverride) } else { $null } }
  })
}

Section 'services' {
  ,@(Get-CimInstance Win32_Service | ForEach-Object {
    $path = Clip $_.PathName 400
    [ordered]@{ name = Clip $_.Name 128; display = Clip $_.DisplayName; startMode = [string]$_.StartMode; state = [string]$_.State
      delayed = [bool]$_.DelayedAutoStart; path = $path; windowsPath = [bool]($path -match '(?i)^"?[a-z]:\\windows\\') }
  })
}

Section 'scheduledTasks' {
  ,@(Get-ScheduledTask | ForEach-Object { [ordered]@{ path = Clip $_.TaskPath; name = Clip $_.TaskName; state = [string]$_.State; author = Clip $_.Author 128
    microsoft = [bool]($_.TaskPath -like '\Microsoft\*') } })
}

Section 'startup' {
  $approved = @{}
  foreach ($k in 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run',
                 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder') {
    try { $item = Get-ItemProperty -LiteralPath $k -ErrorAction Stop; $item.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' } | ForEach-Object { $approved[$_.Name] = ([byte[]]$_.Value)[0] } } catch {}
  }
  ,@(Get-CimInstance Win32_StartupCommand | ForEach-Object { [ordered]@{ name = Clip $_.Name 128; command = Clip $_.Command 400; location = Clip $_.Location 200
    enabled = -not ($approved.ContainsKey($_.Name) -and ($approved[$_.Name] % 2) -eq 1) } })
}

Section 'apps' {
  $roots = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
  $desktop = @(Get-ItemProperty -Path $roots -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -and -not $_.SystemComponent -and -not $_.ParentKeyName } | ForEach-Object {
    [ordered]@{ name = Clip $_.DisplayName; publisher = Clip $_.Publisher 128; version = Clip $_.DisplayVersion 64; installDate = Clip $_.InstallDate 16
      sizeMB = if ($_.EstimatedSize) { [math]::Round($_.EstimatedSize / 1024) } else { $null } } } | Sort-Object { $_.name } -Unique)
  $store = @(Get-AppxPackage -ErrorAction SilentlyContinue | Where-Object { -not $_.IsFramework } | ForEach-Object {
    [ordered]@{ name = Clip $_.Name 128; publisher = Clip ($_.Publisher -replace '^CN=([^,]+).*', '$1') 128; nonRemovable = [bool]$_.NonRemovable; signature = [string]$_.SignatureKind } })
  [ordered]@{ desktop = $desktop; store = $store }
}

Section 'updates' {
  $hotfixes = @(Get-HotFix -ErrorAction SilentlyContinue | Where-Object { $_.InstalledOn } | Sort-Object InstalledOn -Descending)
  $ux = 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'; $policy = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'
  [ordered]@{ lastInstalled = if ($hotfixes) { $hotfixes[0].InstalledOn.ToString('yyyy-MM-dd') } else { $null }
    lastSecurity = ($hotfixes | Where-Object { $_.Description -match 'Security' } | Select-Object -First 1 | ForEach-Object { $_.InstalledOn.ToString('yyyy-MM-dd') })
    recent = @($hotfixes | Select-Object -First 8 | ForEach-Object { [ordered]@{ kb = Clip $_.HotFixID 16; type = Clip $_.Description 64; date = $_.InstalledOn.ToString('yyyy-MM-dd') } })
    serviceStartMode = (Get-CimInstance Win32_Service -Filter "Name='wuauserv'").StartMode
    pausedUntil = Clip (RegValue $ux 'PauseUpdatesExpiryTime') 40; noAutoUpdatePolicy = RegValue $policy 'NoAutoUpdate'
    deferFeatureDays = RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' 'DeferFeatureUpdatesPeriodInDays' }
}

Section 'privacy' {
  $cdm = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
  [ordered]@{
    telemetryPolicy = RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'AllowTelemetry'
    telemetrySetting = RegValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection' 'AllowTelemetry'
    diagTrackService = (Get-CimInstance Win32_Service -Filter "Name='DiagTrack'").StartMode
    advertisingId = RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' 'Enabled'
    activityHistory = RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'PublishUserActivities'
    tailoredExperiences = RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy' 'TailoredExperiencesWithDiagnosticDataEnabled'
    backgroundAppsGlobal = RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications' 'GlobalUserDisabled'
    backgroundAppsPolicy = RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy' 'LetAppsRunInBackground'
    suggestionsStart = RegValue $cdm 'SubscribedContent-338388Enabled'; suggestionsSettings = RegValue $cdm 'SubscribedContent-338393Enabled'
    silentAppInstalls = RegValue $cdm 'SilentInstalledAppsEnabled'
  }
}

Section 'gaming' {
  $vbs = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction SilentlyContinue
  [ordered]@{
    activePowerPlan = Clip ((powercfg /getactivescheme) -join ' ') 200
    powerPlans = @((powercfg /list) | Where-Object { $_ -match 'GUID' } | ForEach-Object { Clip $_ 200 })
    gameMode = RegValue 'HKCU:\Software\Microsoft\GameBar' 'AutoGameModeEnabled'
    gameDvr = RegValue 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled'; appCapture = RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' 'AppCaptureEnabled'
    hags = RegValue 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode'
    vbsStatus = if ($vbs) { $vbs.VirtualizationBasedSecurityStatus } else { $null }
    hvciRunning = if ($vbs) { [bool](@($vbs.SecurityServicesRunning) -contains 2) } else { $null }
    mouseAcceleration = RegValue 'HKCU:\Control Panel\Mouse' 'MouseSpeed'
    visualEffects = RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' 'VisualFXSetting'
  }
}

Section 'riskyTweaks' {
  # Leftovers of popular "optimizer" tweaks that often hurt latency or stability. Values present = flagged for review, not proof of harm.
  $control = 'HKLM:\SYSTEM\CurrentControlSet\Control'; $kernel = "$control\Session Manager\kernel"; $mm = "$control\Session Manager\Memory Management"
  $bcd = $null
  if ($scan.isAdmin) { $out = (& "$env:SystemRoot\System32\bcdedit.exe" /enum '{current}') -join "`n"
    $bcd = @('useplatformclock', 'disabledynamictick', 'useplatformtick', 'tscsyncpolicy' | Where-Object { $out -match "(?m)^$_\s" }) }
  [ordered]@{
    bcdTimerOverrides = $bcd
    spectreMitigationOverride = RegValue $mm 'FeatureSettingsOverride'
    svcHostSplitThresholdKB = RegValue $control 'SvcHostSplitThresholdInKB'
    threadDpcEnable = RegValue $kernel 'ThreadDpcEnable'
    globalTimerResolutionRequests = RegValue $kernel 'GlobalTimerResolutionRequests'
    mouseDataQueueSize = RegValue 'HKLM:\SYSTEM\CurrentControlSet\Services\mouclass\Parameters' 'MouseDataQueueSize'
    keyboardDataQueueSize = RegValue 'HKLM:\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters' 'KeyboardDataQueueSize'
    csrssCpuPriority = RegValue 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\csrss.exe\PerfOptions' 'CpuPriorityClass'
    mpoDisabled = RegValue 'HKLM:\SOFTWARE\Microsoft\Windows\Dwm' 'OverlayTestMode'
    disablePagingExecutive = RegValue $mm 'DisablePagingExecutive'; largeSystemCache = RegValue $mm 'LargeSystemCache'
    errorReportingDisabled = RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting' 'Disabled'
    tcpAutoTuning = Clip ((Get-NetTCPSetting -SettingName Internet -ErrorAction SilentlyContinue).AutoTuningLevelLocal) 32
  }
}

Section 'network' {
  ,@(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | ForEach-Object {
    $adapter = $_
    [ordered]@{ name = Clip $_.Name 128; description = Clip $_.InterfaceDescription; status = [string]$_.Status; linkSpeed = Clip $_.LinkSpeed 32
      driverVersion = Clip $_.DriverVersionString 64; driverDate = Clip $_.DriverDate 32
      advanced = @(Get-NetAdapterAdvancedProperty -Name $adapter.Name -ErrorAction SilentlyContinue | ForEach-Object {
        [ordered]@{ name = Clip $_.DisplayName 128; value = Clip $_.DisplayValue 64; keyword = Clip $_.RegistryKeyword 64 } }) }
  })
}

Section 'audio' {
  ,@(Get-PnpDevice -Class AudioEndpoint -ErrorAction SilentlyContinue | ForEach-Object { [ordered]@{ id = Clip $_.InstanceId 400; name = Clip $_.FriendlyName; status = [string]$_.Status; present = [bool]$_.Present
    direction = if ($_.InstanceId -match '\{0\.0\.1\.') { 'input' } elseif ($_.InstanceId -match '\{0\.0\.0\.') { 'output' } else { $null } } })
}

Section 'games' {
  $found = [System.Collections.Generic.List[object]]::new()
  $steam = RegValue 'HKCU:\Software\Valve\Steam' 'SteamPath'
  if ($steam) {
    $libraries = @($steam) + @((Select-String -LiteralPath (Join-Path $steam 'steamapps\libraryfolders.vdf') -Pattern '"path"\s+"([^"]+)"' -ErrorAction SilentlyContinue).Matches | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' })
    foreach ($lib in ($libraries | Select-Object -Unique)) {
      Get-ChildItem -LiteralPath (Join-Path $lib 'steamapps') -Filter 'appmanifest_*.acf' -ErrorAction SilentlyContinue | ForEach-Object {
        $name = (Select-String -LiteralPath $_.FullName -Pattern '"name"\s+"([^"]+)"' | Select-Object -First 1).Matches.Groups[1].Value
        if ($name) { $found.Add([ordered]@{ store = 'steam'; name = Clip $name 128 }) } } }
  }
  Get-ChildItem -LiteralPath "$env:ProgramData\Epic\EpicGamesLauncher\Data\Manifests" -Filter '*.item' -ErrorAction SilentlyContinue | ForEach-Object {
    try { $m = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json; $found.Add([ordered]@{ store = 'epic'; name = Clip $m.DisplayName 128 }) } catch {} }
  ,@($found)
}

$json = $scan | ConvertTo-Json -Depth 8 -Compress
[IO.File]::WriteAllText($OutputPath, $json, [Text.UTF8Encoding]::new($false))
