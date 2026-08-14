#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,
    [ValidateSet('NeuroTune-W11')]
    [string[]]$VmNames = @('NeuroTune-W11'),
    [string]$CheckpointName = 'Clean-NeuroTune-Alpha2',
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $PSScriptRoot '..\artifacts\vm-validation.json'
}
$actionIds = @(
    'system.high-performance',
    'gaming.game-mode',
    'gaming.hags',
    'gaming.game-dvr-off',
    'system.visual-effects',
    'system.large-cache-default',
    'system.paging-executive-default',
    'graphics.mpo-default',
    'gaming.app-capture-off',
    'graphics.tdr-default',
    'network.tcp-default',
    'system.power-throttling-default'
)
if (-not (Test-Path -LiteralPath $InstallerPath)) { throw "Installer not found: $InstallerPath" }

function Get-CredentialPath([string]$VmName) {
    Join-Path $env:USERPROFILE '.neurotune-vm\w11-credential.xml'
}

$results = foreach ($vmName in $VmNames) {
    $vm = Get-VM -Name $vmName -ErrorAction Stop
    $snapshot = Get-VMSnapshot -VMName $vmName -Name $CheckpointName -ErrorAction Stop
    if ($vm.State -ne 'Off') { Stop-VM -Name $vmName -TurnOff }
    Restore-VMSnapshot -VMSnapshot $snapshot -Confirm:$false
    Start-VM -Name $vmName | Out-Null

    $credentialPath = Get-CredentialPath $vmName
    if (-not (Test-Path -LiteralPath $credentialPath)) { throw "Credential file not found: $credentialPath" }
    $credential = Import-Clixml -LiteralPath $credentialPath
    $deadline = (Get-Date).AddMinutes(8)
    do {
        Start-Sleep -Seconds 5
        try {
            Invoke-Command -VMName $vmName -Credential $credential -ScriptBlock { Get-Date } -ErrorAction Stop | Out-Null
            $ready = $true
        } catch { $ready = $false }
    } until ($ready -or (Get-Date) -gt $deadline)
    if (-not $ready) { throw "PowerShell Direct unavailable for $vmName" }

    $session = New-PSSession -VMName $vmName -Credential $credential
    try {
        Copy-Item -LiteralPath $InstallerPath -Destination 'C:\NeuroTuneTest\NeuroTune-setup.exe' -ToSession $session
        Invoke-Command -Session $session -ScriptBlock {
            $ErrorActionPreference = 'Stop'
            Write-Host 'stage: install'
            $installer = 'C:\NeuroTuneTest\NeuroTune-setup.exe'
            $install = Start-Process -FilePath $installer -ArgumentList '/S' -Wait -PassThru
            if ($install.ExitCode -ne 0) { throw "NSIS exited with $($install.ExitCode)" }

            $uninstall = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
                'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
                Where-Object DisplayName -eq 'NeuroTune' | Select-Object -First 1
            if (-not $uninstall) { throw 'Per-machine NeuroTune uninstall registration not found.' }
            $agent = Get-ChildItem -Path $env:ProgramFiles -Filter NeuroTune.Agent.exe -Recurse -ErrorAction SilentlyContinue |
                Select-Object -First 1 -ExpandProperty FullName
            $app = Get-ChildItem -Path $env:ProgramFiles -Filter NeuroTune.exe -Recurse -ErrorAction SilentlyContinue |
                Select-Object -First 1 -ExpandProperty FullName
            if (-not $agent -or -not $app) { throw 'Installed app or agent executable not found.' }
            if (Get-Service -Name PawnIO -ErrorAction SilentlyContinue) { throw 'PawnIO service was unexpectedly installed.' }

            function Invoke-Agent([string]$Command, [object]$Body = @{}) {
                $start = [Diagnostics.ProcessStartInfo]::new($agent, $Command)
                $start.UseShellExecute = $false
                $start.RedirectStandardInput = $true
                $start.RedirectStandardOutput = $true
                $start.RedirectStandardError = $true
                $start.CreateNoWindow = $true
                $process = [Diagnostics.Process]::Start($start)
                $process.StandardInput.Write(($Body | ConvertTo-Json -Compress -Depth 8))
                $process.StandardInput.Close()
                $stdout = $process.StandardOutput.ReadToEnd()
                $stderr = $process.StandardError.ReadToEnd()
                $process.WaitForExit()
                if ($process.ExitCode -ne 0) { throw "Agent $Command failed: $stderr $stdout" }
                $response = $stdout | ConvertFrom-Json
                if (-not $response.ok) { throw "Agent $Command rejected: $($response.error)" }
                $response.data
            }

            function Set-KnownInitialState {
                powercfg.exe /setactive SCHEME_MIN | Out-Null
                powercfg.exe /setactive SCHEME_BALANCED | Out-Null
                $values = @(
                    @('HKCU:\Software\Microsoft\GameBar','AutoGameModeEnabled',0),
                    @('HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers','HwSchMode',1),
                    @('HKCU:\System\GameConfigStore','GameDVR_Enabled',1),
                    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects','VisualFXSetting',1),
                    @('HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management','LargeSystemCache',1),
                    @('HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management','DisablePagingExecutive',1),
                    @('HKLM:\SOFTWARE\Microsoft\Windows\Dwm','OverlayTestMode',5),
                    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR','AppCaptureEnabled',1),
                    @('HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers','TdrDelay',12),
                    @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','DefaultTTL',32),
                    @('HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling','PowerThrottlingOff',1)
                )
                foreach ($item in $values) {
                    if (-not (Test-Path -Path $item[0])) { New-Item -Path $item[0] | Out-Null }
                    Set-ItemProperty -Path $item[0] -Name $item[1] -Type DWord -Value $item[2]
                }
            }

            function Start-AgentForCrash([string]$Command, [object]$Body) {
                $start = [Diagnostics.ProcessStartInfo]::new($agent, $Command)
                $start.UseShellExecute = $false
                $start.RedirectStandardInput = $true
                $start.RedirectStandardOutput = $true
                $start.RedirectStandardError = $true
                $start.CreateNoWindow = $true
                $start.Environment['NEUROTUNE_TEST_STEP_DELAY_MS'] = '6000'
                $process = [Diagnostics.Process]::Start($start)
                $process.StandardInput.Write(($Body | ConvertTo-Json -Compress -Depth 8))
                $process.StandardInput.Close()
                $process
            }

            $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
            Write-Host 'stage: ui smoke'
            $ui = Start-Process -FilePath $app -PassThru
            Start-Sleep -Seconds 3
            if ($ui.HasExited) { throw 'Installed UI did not remain running for smoke test.' }
            Stop-Process -Id $ui.Id -Force

            Set-KnownInitialState
            Write-Host 'stage: apply verify rollback'
            $before = Invoke-Agent actions
            $restoreBefore = @(Get-ComputerRestorePoint).Count
            $applied = Invoke-Agent apply @{ actionIds = $using:actionIds; highRiskConfirmed = $true }
            if ($applied.status -ne 'Completed' -or @($applied.actions).Count -ne 12 -or @($applied.actions | Where-Object { -not $_.applied }).Count) {
                throw 'All-action Apply/Verify did not complete.'
            }
            $manifest = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'NeuroTune\operations') -Filter manifest.json -Recurse |
                Where-Object { (Get-Content -Raw $_.FullName | ConvertFrom-Json).id -eq $applied.id } |
                Select-Object -First 1 -ExpandProperty FullName
            if (-not (Test-Path -LiteralPath $manifest)) { throw 'Operation manifest missing.' }
            if (@(Get-ComputerRestorePoint).Count -le $restoreBefore) { throw 'Apply restore point was not created.' }
            Invoke-Agent rollback @{ operationId = $applied.id } | Out-Null
            $rolled = (Invoke-Agent history | Where-Object id -eq $applied.id)
            if ($rolled.status -ne 'Rollback completed' -or @($rolled.actions | Where-Object { -not $_.rolledBack }).Count) {
                throw 'All-action rollback did not complete.'
            }
            $after = Invoke-Agent actions
            for ($i = 0; $i -lt $before.Count; $i++) {
                if ($before[$i].id -ne $after[$i].id -or $before[$i].availability.currentValue -ne $after[$i].availability.currentValue) {
                    throw "Rollback state mismatch for $($before[$i].id)"
                }
            }

            Set-KnownInitialState
            Write-Host 'stage: crash apply recovery'
            $crashApply = Start-AgentForCrash apply @{ actionIds = $using:actionIds; highRiskConfirmed = $true }
            $operations = Join-Path $env:LOCALAPPDATA 'NeuroTune\operations'
            $deadline = (Get-Date).AddSeconds(20)
            do {
                $candidate = Get-ChildItem -Path $operations -Filter manifest.json -Recurse -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
                $state = if ($candidate) { Get-Content -Raw $candidate.FullName | ConvertFrom-Json } else { $null }
                if (-not $state -or $state.status -ne 'Applying') { Start-Sleep -Milliseconds 20 }
            } until (($state -and $state.status -eq 'Applying') -or (Get-Date) -gt $deadline)
            if (-not $state -or $state.status -ne 'Applying') { throw 'Could not capture Applying state.' }
            Stop-Process -Id $crashApply.Id -Force
            $pendingApply = Invoke-Agent history | Where-Object id -eq $state.id
            if (-not $pendingApply) { throw 'Interrupted Apply was not recovered from history.' }
            Invoke-Agent rollback @{ operationId = $state.id } | Out-Null

            Set-KnownInitialState
            Write-Host 'stage: crash rollback recovery'
            $forRollback = Invoke-Agent apply @{ actionIds = $using:actionIds; highRiskConfirmed = $true }
            $crashRollback = Start-AgentForCrash rollback @{ operationId = $forRollback.id }
            $deadline = (Get-Date).AddSeconds(20)
            do {
                $rolling = Invoke-Agent history | Where-Object id -eq $forRollback.id
                if (-not $rolling -or $rolling.status -ne 'Rolling back') { Start-Sleep -Milliseconds 20 }
            } until (($rolling -and $rolling.status -eq 'Rolling back') -or (Get-Date) -gt $deadline)
            if (-not $rolling -or $rolling.status -ne 'Rolling back') { throw 'Could not capture Rolling back state.' }
            Stop-Process -Id $crashRollback.Id -Force
            Invoke-Agent rollback @{ operationId = $forRollback.id } | Out-Null
            $recovered = Invoke-Agent history | Where-Object id -eq $forRollback.id
            if ($recovered.status -ne 'Rollback completed') { throw 'Interrupted rollback recovery failed.' }

            $orphanNames = 'NeuroTune.Agent','powercfg','netsh','bcdedit','fsutil','fltmc','netcfg'
            $orphans = Get-Process -Name $orphanNames -ErrorAction SilentlyContinue
            if ($orphans) { throw "Orphan processes remain: $($orphans.Name -join ', ')" }
            $defenderThreats = Get-MpThreatDetection -ErrorAction SilentlyContinue |
                Where-Object { $_.Resources -match 'NeuroTune' }
            if ($defenderThreats) { throw 'Microsoft Defender reported the NeuroTune installer or binaries.' }
            $uninstaller = Get-ChildItem -Path (Split-Path $app) -Filter 'uninstall*.exe' -ErrorAction SilentlyContinue |
                Select-Object -First 1 -ExpandProperty FullName
            if (-not $uninstaller) { throw 'NSIS uninstaller not found.' }
            $remove = Start-Process -FilePath $uninstaller -ArgumentList '/S' -Wait -PassThru
            if ($remove.ExitCode -ne 0) { throw "Uninstaller exited with $($remove.ExitCode)" }
            Start-Sleep -Seconds 2
            if (Test-Path -LiteralPath $app) { throw 'Application binary remains after uninstall.' }
            if (Get-Service -Name PawnIO -ErrorAction SilentlyContinue) { throw 'PawnIO service exists after uninstall.' }

            $hvciConfigured = Get-ItemPropertyValue `
                'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity' `
                -Name Enabled -ErrorAction SilentlyContinue
            if ($null -eq $hvciConfigured) { $hvciConfigured = 0 }

            [pscustomobject]@{
                windows = (Get-CimInstance Win32_OperatingSystem).Caption
                build = [Environment]::OSVersion.Version.ToString()
                version = $version
                actionCount = 12
                applyVerifyRollback = 'passed'
                crashApplyRecovery = 'passed'
                crashRollbackRecovery = 'passed'
                pawnIoService = 'absent'
                uninstall = 'passed'
                defender = 'no-detection'
                hvciConfigured = [int]$hvciConfigured
            }
        }
    }
    finally { Remove-PSSession $session }
}

$report = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::UtcNow.ToString('o')
    installerSha256 = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash
    checkpoint = $CheckpointName
    results = @($results | Select-Object windows, build, version, actionCount,
        applyVerifyRollback, crashApplyRecovery, crashRollbackRecovery,
        pawnIoService, uninstall, defender, hvciConfigured)
}
$resolvedReport = [IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($resolvedReport)) | Out-Null
[IO.File]::WriteAllText($resolvedReport, ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$report
