#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,
    [ValidateSet('NeuroTune-W11')]
    [string[]]$VmNames = @('NeuroTune-W11'),
    [string]$CheckpointName = 'Clean-NeuroTune-Alpha2',
    [switch]$SkipCheckpointRestore,
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
    if (-not $SkipCheckpointRestore) {
        $snapshot = Get-VMSnapshot -VMName $vmName -Name $CheckpointName -ErrorAction Stop
        if ($vm.State -ne 'Off') { Stop-VM -Name $vmName -TurnOff }
        Restore-VMSnapshot -VMSnapshot $snapshot -Confirm:$false
        Start-VM -Name $vmName | Out-Null
    }

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
        Invoke-Command -Session $session -ScriptBlock { New-Item -ItemType Directory -Force C:\NeuroTuneTest | Out-Null }
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
            $dataRoot = Join-Path $env:LOCALAPPDATA 'NeuroTune'

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

            function New-ValidationRun {
                if (-not $script:validationProfile) { $script:validationProfile = (Invoke-Agent scan).profile }
                $run = Invoke-Agent run-create @{ profile = $script:validationProfile; goals = @{} }
                $path = Join-Path $dataRoot "runs\$($run.id)\run.json"
                $journal = [IO.File]::ReadAllText($path)
                $journal = [regex]::Replace($journal, '"State":\s*1', '"State": 5', 1)
                $journal = $journal.Replace('"Diagnosis": null', '"Diagnosis": {"Summary":"Deterministic VM writer validation.","Findings":[],"Recommendations":[],"Conflicts":[],"ConsentQuestion":"Apply the selected registered actions?"}')
                $journal = $journal.Replace('"PlannerStopReason": ""', '"PlannerStopReason": "vm-validation-fixture"')
                $journal = $journal.Replace('"BaselineSessionIds": []', '"BaselineSessionIds": ["' + [guid]::NewGuid().ToString('D') + '"]')
                $temporary = "$path.vmtest.tmp"
                [IO.File]::WriteAllText($temporary, $journal, [Text.UTF8Encoding]::new($false))
                Move-Item -LiteralPath $temporary -Destination $path -Force
                $prepared = Invoke-Agent run-get @{ runId = $run.id }
                if ($prepared.state -ne 'baselineReady' -or -not $prepared.diagnosis -or @($prepared.baselineSessionIds).Count -ne 1) {
                    throw 'The deterministic VM run fixture did not reach BaselineReady.'
                }
                $prepared
            }

            $activeRuns = @(Invoke-Agent run-list | Where-Object state -notin @('completed', 'failed'))
            if ($activeRuns.Count) { throw 'Finish or recover the existing optimization run before VM validation.' }

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

            $knownStateTargets = @(
                @('HKCU:\Software\Microsoft\GameBar','AutoGameModeEnabled'),
                @('HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers','HwSchMode'),
                @('HKCU:\System\GameConfigStore','GameDVR_Enabled'),
                @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects','VisualFXSetting'),
                @('HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management','LargeSystemCache'),
                @('HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management','DisablePagingExecutive'),
                @('HKLM:\SOFTWARE\Microsoft\Windows\Dwm','OverlayTestMode'),
                @('HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR','AppCaptureEnabled'),
                @('HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers','TdrDelay'),
                @('HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers','TdrDdiDelay'),
                @('HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers','TdrLevel'),
                @('HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers','TdrDebugMode'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','TcpTimedWaitDelay'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','MaxUserPort'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','DefaultTTL'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','Tcp1323Opts'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','EnablePMTUDiscovery'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','DisableTaskOffload'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','EnableTCPChimney'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','EnableRSS'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','EnableDCA'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','SackOpts'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','GlobalMaxTcpWindowSize'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','TcpWindowSize'),
                @('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters','KeepAliveTime'),
                @('HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling','PowerThrottlingOff')
            )

            function Capture-TestState {
                $scheme = [regex]::Match(((& powercfg.exe /getactivescheme) -join ' '), '[0-9a-fA-F-]{36}').Value
                $values = foreach ($target in $knownStateTargets) {
                    $key = Get-Item -LiteralPath $target[0] -ErrorAction SilentlyContinue
                    if (-not $key -or $null -eq $key.GetValue($target[1], $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)) {
                        [pscustomobject]@{ Path=$target[0]; Name=$target[1]; Exists=$false; Kind=$null; Value=$null }
                    } else {
                        $kind = $key.GetValueKind($target[1])
                        if ($kind -in @([Microsoft.Win32.RegistryValueKind]::None, [Microsoft.Win32.RegistryValueKind]::Unknown)) {
                            throw "Unsupported existing Registry kind for $($target[1]): $kind"
                        }
                        [pscustomobject]@{ Path=$target[0]; Name=$target[1]; Exists=$true; Kind=$kind; Value=$key.GetValue($target[1], $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) }
                    }
                }
                [pscustomobject]@{ PowerScheme=$scheme; Values=@($values) }
            }

            function Restore-TestState([object]$Snapshot) {
                foreach ($item in $Snapshot.Values) {
                    if (-not (Test-Path -LiteralPath $item.Path)) { New-Item -Path $item.Path | Out-Null }
                    if ($item.Exists) {
                        New-ItemProperty -LiteralPath $item.Path -Name $item.Name -Value $item.Value `
                            -PropertyType $item.Kind.ToString() -Force | Out-Null
                    } else {
                        Remove-ItemProperty -LiteralPath $item.Path -Name $item.Name -ErrorAction SilentlyContinue
                    }
                }
                & powercfg.exe /setactive $Snapshot.PowerScheme | Out-Null
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

            function Assert-ActionState([object[]]$Expected, [string]$Stage) {
                $actual = @(Invoke-Agent actions)
                foreach ($item in $Expected) {
                    $match = $actual | Where-Object id -eq $item.id
                    if (-not $match -or $item.availability.currentValue -ne $match.availability.currentValue) {
                        throw "$Stage state mismatch for $($item.id)"
                    }
                }
            }

            $originalState = Capture-TestState
            $gpuStore = Join-Path $dataRoot 'game-gpu-targets.json'
            $gpuStoreExisted = Test-Path -LiteralPath $gpuStore
            $gpuStoreOriginal = if ($gpuStoreExisted) { [IO.File]::ReadAllText($gpuStore) } else { $null }
            try {
            $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
            Write-Host 'stage: ui smoke'
            $ui = Start-Process -FilePath $app -PassThru
            Start-Sleep -Seconds 3
            if ($ui.HasExited) { throw 'Installed UI did not remain running for smoke test.' }
            Stop-Process -Id $ui.Id -Force

            Set-KnownInitialState
            Write-Host 'stage: apply verify rollback'
            $before = Invoke-Agent actions
            $run = New-ValidationRun
            $applied = Invoke-Agent apply @{ actionIds = $using:actionIds; highRiskConfirmed = $true; runId = $run.Id }
            if ($applied.status -ne 'Completed' -or @($applied.actions).Count -ne 12 -or @($applied.actions | Where-Object { -not $_.applied }).Count) {
                throw 'All-action Apply/Verify did not complete.'
            }
            $manifest = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'NeuroTune\operations') -Filter manifest.json -Recurse |
                Where-Object { (Get-Content -Raw $_.FullName | ConvertFrom-Json).id -eq $applied.id } |
                Select-Object -First 1 -ExpandProperty FullName
            if (-not (Test-Path -LiteralPath $manifest)) { throw 'Operation manifest missing.' }
            $restorePoint = "NeuroTune $(([guid]$applied.id).ToString('N'))"
            if (-not (Get-ComputerRestorePoint | Where-Object Description -eq $restorePoint)) {
                throw 'Apply restore point was not created.'
            }
            Invoke-Agent rollback @{ operationId = $applied.id; runId = $run.Id } | Out-Null
            $rolled = (Invoke-Agent history | Where-Object id -eq $applied.id)
            if ($rolled.status -ne 'Rollback completed' -or @($rolled.actions | Where-Object { -not $_.rolledBack }).Count) {
                throw 'All-action rollback did not complete.'
            }
            Assert-ActionState $before 'Rollback'

            Set-KnownInitialState
            Write-Host 'stage: crash apply recovery'
            $beforeCrashApply = @(Invoke-Agent actions)
            $crashRun = New-ValidationRun
            $crashApply = Start-AgentForCrash apply @{ actionIds = $using:actionIds; highRiskConfirmed = $true; runId = $crashRun.Id }
            $operations = Join-Path $env:LOCALAPPDATA 'NeuroTune\operations'
            $deadline = (Get-Date).AddSeconds(20)
            do {
                $candidate = Get-ChildItem -Path $operations -Filter manifest.json -Recurse -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTimeUtc -Descending | Where-Object {
                        (Get-Content -Raw $_.FullName | ConvertFrom-Json).optimizationRunId -eq $crashRun.Id
                    } | Select-Object -First 1
                $state = if ($candidate) { Get-Content -Raw $candidate.FullName | ConvertFrom-Json } else { $null }
                $writeStarted = $state -and @($state.actions | Where-Object { $_.attempted -or $_.applied }).Count -gt 0
                if (-not $writeStarted) { Start-Sleep -Milliseconds 20 }
            } until ($writeStarted -or (Get-Date) -gt $deadline)
            if (-not $writeStarted) { throw 'Could not capture an interrupted Apply after its first journaled write.' }
            Stop-Process -Id $crashApply.Id -Force
            Invoke-Agent run-reconcile @{ runId = $crashRun.Id } | Out-Null
            $pendingApply = Invoke-Agent history | Where-Object id -eq $state.id
            if (-not $pendingApply) { throw 'Interrupted Apply was not recovered from history.' }
            Invoke-Agent rollback @{ operationId = $state.id; runId = $crashRun.Id } | Out-Null
            Assert-ActionState $beforeCrashApply 'Interrupted Apply recovery'

            Set-KnownInitialState
            Write-Host 'stage: crash rollback recovery'
            $beforeCrashRollback = @(Invoke-Agent actions)
            $rollbackRun = New-ValidationRun
            $forRollback = Invoke-Agent apply @{ actionIds = $using:actionIds; highRiskConfirmed = $true; runId = $rollbackRun.Id }
            $crashRollback = Start-AgentForCrash rollback @{ operationId = $forRollback.id; runId = $rollbackRun.Id }
            $deadline = (Get-Date).AddSeconds(20)
            do {
                $rolling = Invoke-Agent history | Where-Object id -eq $forRollback.id
                if (-not $rolling -or $rolling.status -ne 'Rolling back') { Start-Sleep -Milliseconds 20 }
            } until (($rolling -and $rolling.status -eq 'Rolling back') -or (Get-Date) -gt $deadline)
            if (-not $rolling -or $rolling.status -ne 'Rolling back') { throw 'Could not capture Rolling back state.' }
            Stop-Process -Id $crashRollback.Id -Force
            Invoke-Agent run-reconcile @{ runId = $rollbackRun.Id } | Out-Null
            Invoke-Agent rollback @{ operationId = $forRollback.id; runId = $rollbackRun.Id } | Out-Null
            $recovered = Invoke-Agent history | Where-Object id -eq $forRollback.id
            if ($recovered.status -ne 'Rollback completed') { throw 'Interrupted rollback recovery failed.' }
            Assert-ActionState $beforeCrashRollback 'Interrupted Rollback recovery'

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
            } finally {
                Restore-TestState $originalState
                if ($gpuStoreExisted) {
                    [IO.File]::WriteAllText($gpuStore, $gpuStoreOriginal, [Text.UTF8Encoding]::new($false))
                } elseif (Test-Path -LiteralPath $gpuStore) {
                    Remove-Item -LiteralPath $gpuStore -Force
                }
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
