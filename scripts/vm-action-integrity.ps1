#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,
    [ValidateSet('NeuroTune-W11')]
    [string]$VmName = 'NeuroTune-W11',
    [string]$CheckpointName = 'Clean-NeuroTune-Alpha2',
    [switch]$SkipCheckpointRestore,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $PSScriptRoot '..\artifacts\vm-action-integrity-w11.json'
}
$credentialPath = Join-Path $env:USERPROFILE '.neurotune-vm\w11-credential.xml'
$resolvedReport = [IO.Path]::GetFullPath($ReportPath)
$report = [ordered]@{
    generatedAt = [DateTimeOffset]::UtcNow.ToString('o')
    vmName = $VmName
    checkpoint = $CheckpointName
    installerSha256 = $null
    status = 'running'
    results = @()
    cleanup = 'pending'
    error = $null
}
$session = $null

function Wait-PowerShellDirect([string]$Name, [pscredential]$Credential) {
    $deadline = (Get-Date).AddMinutes(8)
    do {
        Start-Sleep -Seconds 5
        try {
            Invoke-Command -VMName $Name -Credential $Credential -ScriptBlock { Get-Date } -ErrorAction Stop | Out-Null
            return
        } catch { }
    } while ((Get-Date) -lt $deadline)
    throw "PowerShell Direct unavailable for $Name."
}

try {
    if (-not (Test-Path -LiteralPath $InstallerPath)) { throw "Installer not found: $InstallerPath" }
    $report.installerSha256 = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash
    $credential = Import-Clixml -LiteralPath $credentialPath
    if (-not $SkipCheckpointRestore) {
        $snapshot = Get-VMSnapshot -VMName $VmName -Name $CheckpointName -ErrorAction Stop
        if ((Get-VM -Name $VmName).State -ne 'Off') { Stop-VM -Name $VmName -TurnOff }
        Restore-VMSnapshot -VMSnapshot $snapshot -Confirm:$false
        Start-VM -Name $VmName | Out-Null
    }
    Wait-PowerShellDirect $VmName $credential

    $session = New-PSSession -VMName $VmName -Credential $credential
    Invoke-Command -Session $session -ScriptBlock { New-Item -ItemType Directory -Force C:\NeuroTuneTest | Out-Null }
    Copy-Item -LiteralPath $InstallerPath -Destination 'C:\NeuroTuneTest\NeuroTune-setup.exe' -ToSession $session
    $guestResults = @(Invoke-Command -Session $session -ScriptBlock {
        $ErrorActionPreference = 'Stop'
        $install = Start-Process C:\NeuroTuneTest\NeuroTune-setup.exe -ArgumentList '/S' -Wait -PassThru
        if ($install.ExitCode -ne 0) { throw "NSIS exited with $($install.ExitCode)." }
        Get-Process NeuroTune -ErrorAction SilentlyContinue | Stop-Process -Force
        $agent = Get-ChildItem $env:ProgramFiles -Filter NeuroTune.Agent.exe -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
        if (-not $agent) { throw 'Installed NeuroTune.Agent.exe was not found.' }
        $dataRoot = Join-Path $env:LOCALAPPDATA 'NeuroTune'

        function Invoke-Agent([string]$Command, [object]$Body = @{}) {
            $start = [Diagnostics.ProcessStartInfo]::new($agent, $Command)
            $start.UseShellExecute = $false
            $start.RedirectStandardInput = $true
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $start.CreateNoWindow = $true
            $process = [Diagnostics.Process]::Start($start)
            $process.StandardInput.Write(($Body | ConvertTo-Json -Compress -Depth 12))
            $process.StandardInput.Close()
            $stdout = $process.StandardOutput.ReadToEnd()
            $stderr = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            if ($process.ExitCode -ne 0) { throw "Agent $Command failed: $stderr $stdout" }
            $response = $stdout | ConvertFrom-Json
            if (-not $response.ok) { throw "Agent $Command rejected: $($response.error)" }
            $response.data
        }

        function Value([string]$Name, [bool]$Exists, [string]$Kind = 'Unknown', [string]$Data = $null) {
            [pscustomobject][ordered]@{ name = $Name; exists = $Exists; kind = $Kind; value = $Data }
        }

        function New-ValidationRun {
            if (-not $script:validationProfile) { $script:validationProfile = (Invoke-Agent scan).profile }
            $run = Invoke-Agent run-create @{ profile = $script:validationProfile; goals = @{} }
            $path = Join-Path $dataRoot "runs\$($run.id)\run.json"
            $journal = [IO.File]::ReadAllText($path)
            $journal = [regex]::Replace($journal, '"State":\s*1', '"State": 5', 1)
            $journal = $journal.Replace('"Diagnosis": null', '"Diagnosis": {"Summary":"Deterministic VM writer validation.","Findings":[],"Recommendations":[],"Conflicts":[],"ConsentQuestion":"Apply the selected registered action?"}')
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

        function Open-RegistryBase([string]$Hive) {
            $enum = if ($Hive -eq 'CurrentUser') {
                [Microsoft.Win32.RegistryHive]::CurrentUser
            } else {
                [Microsoft.Win32.RegistryHive]::LocalMachine
            }
            [Microsoft.Win32.RegistryKey]::OpenBaseKey($enum, [Microsoft.Win32.RegistryView]::Registry64)
        }

        function Set-RegistryStates([object]$Definition, [object[]]$States = @($Definition.original)) {
            $base = Open-RegistryBase $Definition.hive
            try {
                $key = $base.CreateSubKey($Definition.path, $true)
                try {
                    foreach ($item in @($States)) {
                        if (-not $item.exists) {
                            $key.DeleteValue($item.name, $false)
                            continue
                        }
                        $kind = [Enum]::Parse([Microsoft.Win32.RegistryValueKind], $item.kind)
                        if ($kind -eq [Microsoft.Win32.RegistryValueKind]::MultiString) {
                            $values = [string[]]@($item.value | ConvertFrom-Json)
                            $key.SetValue($item.name, $values, [Microsoft.Win32.RegistryValueKind]::MultiString)
                            continue
                        }
                        $value = switch ($kind) {
                            Binary { [Convert]::FromBase64String([string]$item.value) }
                            DWord { [int]$item.value }
                            QWord { [long]$item.value }
                            String { [string]$item.value }
                            ExpandString { [string]$item.value }
                            default { throw "Unsupported test Registry kind: $kind" }
                        }
                        $key.SetValue($item.name, $value, $kind)
                    }
                } finally { $key.Dispose() }
            } finally { $base.Dispose() }
        }

        function Get-RegistryStates([object]$Definition) {
            $base = Open-RegistryBase $Definition.hive
            try {
                $key = $base.OpenSubKey($Definition.path)
                try {
                    foreach ($item in @($Definition.original)) {
                        $raw = if ($key) {
                            $key.GetValue($item.name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                        } else { $null }
                        if ($null -eq $raw) {
                            Value $item.name $false
                        } else {
                            $kind = $key.GetValueKind($item.name)
                            if ($kind -in @([Microsoft.Win32.RegistryValueKind]::None, [Microsoft.Win32.RegistryValueKind]::Unknown)) {
                                throw "Unsupported existing Registry kind for $($item.name): $kind"
                            }
                            $data = switch ($kind) {
                                Binary { [Convert]::ToBase64String([byte[]]$raw) }
                                MultiString { ConvertTo-Json -InputObject @($raw) -Compress }
                                default { [Convert]::ToString($raw, [Globalization.CultureInfo]::InvariantCulture) }
                            }
                            Value $item.name $true $kind.ToString() $data
                        }
                    }
                } finally { if ($key) { $key.Dispose() } }
            } finally { $base.Dispose() }
        }

        function Json-State([object[]]$State) {
            @(foreach ($item in $State) {
                [pscustomobject][ordered]@{
                    name = [string]$item.name
                    exists = [bool]$item.exists
                    kind = [string]$item.kind
                    value = if ($null -eq $item.value) { $null } else { [string]$item.value }
                }
            }) | ConvertTo-Json -Compress -Depth 5
        }

        function Active-Scheme {
            $text = & powercfg.exe /getactivescheme
            [regex]::Match(($text -join ' '), '[0-9a-fA-F-]{36}').Value.ToLowerInvariant()
        }

        function Core-ParkingValue {
            $scheme = Active-Scheme
            $output = & powercfg.exe /qh $scheme 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583
            if ($LASTEXITCODE -ne 0) { throw 'powercfg could not query the effective core-parking value.' }
            $values = [regex]::Matches(($output -join ' '), '0x([0-9a-fA-F]{8})')
            if ($values.Count -lt 2) { throw 'powercfg returned an unreadable core-parking value.' }
            [Convert]::ToInt32($values[$values.Count - 2].Groups[1].Value, 16)
        }

        $activeRuns = @(Invoke-Agent run-list | Where-Object state -notin @('completed', 'failed'))
        if ($activeRuns.Count) { throw 'Finish or recover the existing optimization run before VM validation.' }
        $gpuExecutable = "$env:WINDIR\System32\notepad.exe"
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $gpuHash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($gpuExecutable).ToUpperInvariant()))
        } finally { $sha.Dispose() }
        $gpuId = ([BitConverter]::ToString($gpuHash).Replace('-', '').Substring(0, 16)).ToLowerInvariant()
        New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
        $gpuStore = Join-Path $dataRoot 'game-gpu-targets.json'
        $gpuStoreExisted = Test-Path -LiteralPath $gpuStore
        $gpuStoreOriginal = if ($gpuStoreExisted) { [IO.File]::ReadAllText($gpuStore) } else { $null }
        try {
            $gpuTargetJson = ([ordered]@{
                Id = $gpuId; ExecutableName = 'notepad.exe'; ExecutablePath = $gpuExecutable
            } | ConvertTo-Json -Compress)
            [IO.File]::WriteAllText($gpuStore, "[$gpuTargetJson]", [Text.UTF8Encoding]::new($false))

            $definitions = @(
            [pscustomobject]@{ id='system.high-performance'; power=$true },
            [pscustomobject]@{ id='system.core-parking-off'; coreParking=$true },
            [pscustomobject]@{ id='system.pagefile-managed-sizes'; hive='LocalMachine'; path='SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; original=@((Value 'PagingFiles' $true 'MultiString' '["C:\\pagefile.sys 1024 2048"]')); applied=@((Value 'PagingFiles' $true 'MultiString' '["C:\\pagefile.sys 0 0"]')) },
            [pscustomobject]@{ id="gaming.gpu-$gpuId.high"; hive='CurrentUser'; path='Software\Microsoft\DirectX\UserGpuPreferences'; original=@((Value $gpuExecutable $false)); applied=@((Value $gpuExecutable $true 'String' 'GpuPreference=2;')) },
            [pscustomobject]@{ id="gaming.gpu-$gpuId.default"; hive='CurrentUser'; path='Software\Microsoft\DirectX\UserGpuPreferences'; original=@((Value $gpuExecutable $true 'String' 'GpuPreference=2;')); applied=@((Value $gpuExecutable $false)) },
            [pscustomobject]@{ id='gaming.game-mode'; hive='CurrentUser'; path='Software\Microsoft\GameBar'; original=@((Value 'AutoGameModeEnabled' $true 'QWord' '0')); applied=@((Value 'AutoGameModeEnabled' $true 'DWord' '1')) },
            [pscustomobject]@{ id='gaming.hags'; hive='LocalMachine'; path='SYSTEM\CurrentControlSet\Control\GraphicsDrivers'; original=@((Value 'HwSchMode' $true 'String' '1')); applied=@((Value 'HwSchMode' $true 'DWord' '2')) },
            [pscustomobject]@{ id='gaming.game-dvr-off'; hive='CurrentUser'; path='System\GameConfigStore'; original=@((Value 'GameDVR_Enabled' $false)); applied=@((Value 'GameDVR_Enabled' $true 'DWord' '0')) },
            [pscustomobject]@{ id='system.visual-effects'; hive='CurrentUser'; path='Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'; original=@((Value 'VisualFXSetting' $true 'QWord' '1')); applied=@((Value 'VisualFXSetting' $true 'DWord' '2')) },
            [pscustomobject]@{ id='system.large-cache-default'; hive='LocalMachine'; path='SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; original=@((Value 'LargeSystemCache' $true 'String' '1')); applied=@((Value 'LargeSystemCache' $true 'DWord' '0')) },
            [pscustomobject]@{ id='system.paging-executive-default'; hive='LocalMachine'; path='SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; original=@((Value 'DisablePagingExecutive' $false)); applied=@((Value 'DisablePagingExecutive' $true 'DWord' '0')) },
            [pscustomobject]@{ id='graphics.mpo-default'; hive='LocalMachine'; path='SOFTWARE\Microsoft\Windows\Dwm'; original=@((Value 'OverlayTestMode' $true 'QWord' '5')); applied=@((Value 'OverlayTestMode' $false)) },
            [pscustomobject]@{ id='gaming.app-capture-off'; hive='CurrentUser'; path='Software\Microsoft\Windows\CurrentVersion\GameDVR'; original=@((Value 'AppCaptureEnabled' $true 'QWord' '1')); applied=@((Value 'AppCaptureEnabled' $true 'DWord' '0')) },
            [pscustomobject]@{ id='graphics.tdr-default'; hive='LocalMachine'; path='SYSTEM\CurrentControlSet\Control\GraphicsDrivers'; original=@((Value 'TdrDelay' $true 'DWord' '12'),(Value 'TdrDdiDelay' $true 'QWord' '14'),(Value 'TdrLevel' $true 'String' '3'),(Value 'TdrDebugMode' $false)); applied=@((Value 'TdrDelay' $false),(Value 'TdrDdiDelay' $false),(Value 'TdrLevel' $false),(Value 'TdrDebugMode' $false)) },
            [pscustomobject]@{ id='network.tcp-default'; hive='LocalMachine'; path='SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; original=@((Value 'TcpTimedWaitDelay' $false),(Value 'MaxUserPort' $true 'QWord' '5000'),(Value 'DefaultTTL' $true 'DWord' '32'),(Value 'Tcp1323Opts' $false),(Value 'EnablePMTUDiscovery' $false),(Value 'DisableTaskOffload' $false),(Value 'EnableTCPChimney' $false),(Value 'EnableRSS' $false),(Value 'EnableDCA' $false),(Value 'SackOpts' $false),(Value 'GlobalMaxTcpWindowSize' $false),(Value 'TcpWindowSize' $true 'String' '65535'),(Value 'KeepAliveTime' $false)); applied=@((Value 'TcpTimedWaitDelay' $false),(Value 'MaxUserPort' $false),(Value 'DefaultTTL' $false),(Value 'Tcp1323Opts' $false),(Value 'EnablePMTUDiscovery' $false),(Value 'DisableTaskOffload' $false),(Value 'EnableTCPChimney' $false),(Value 'EnableRSS' $false),(Value 'EnableDCA' $false),(Value 'SackOpts' $false),(Value 'GlobalMaxTcpWindowSize' $false),(Value 'TcpWindowSize' $false),(Value 'KeepAliveTime' $false)) },
            [pscustomobject]@{ id='system.power-throttling-default'; hive='LocalMachine'; path='SYSTEM\CurrentControlSet\Control\Power\PowerThrottling'; original=@((Value 'PowerThrottlingOff' $true 'String' '1')); applied=@((Value 'PowerThrottlingOff' $false)) }
        )

            $operationRoot = Join-Path $env:LOCALAPPDATA 'NeuroTune\operations'
            foreach ($definition in $definitions) {
                $actualState = $null
                $stateCaptured = $false
                try {
                    $actualState = if ($definition.power) { Active-Scheme } elseif ($definition.coreParking) {
                        Core-ParkingValue
                    } else { @(Get-RegistryStates $definition) }
                    $stateCaptured = $true
                    if ($definition.power) {
                        & powercfg.exe /setactive SCHEME_BALANCED | Out-Null
                        $beforeJson = Active-Scheme
                        $expectedAppliedJson = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
                    } elseif ($definition.coreParking) {
                        & powercfg.exe /setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 25 | Out-Null
                        & powercfg.exe /setactive SCHEME_CURRENT | Out-Null
                        $beforeJson = Core-ParkingValue
                        $expectedAppliedJson = 100
                    } else {
                        Set-RegistryStates $definition
                        $beforeJson = Json-State @(Get-RegistryStates $definition)
                        $expectedAppliedJson = Json-State @($definition.applied)
                    }

                    $beforeInspect = Invoke-Agent actions | Where-Object id -eq $definition.id
                    if (-not $beforeInspect.availability.canApply -or $beforeInspect.availability.alreadyApplied) {
                        throw "Inspect did not report a ready state for $($definition.id): $($beforeInspect.availability)"
                    }

                    $run = New-ValidationRun
                    $applied = Invoke-Agent apply @{ actionIds = @($definition.id); highRiskConfirmed = $true; runId = $run.Id }
                    $record = @($applied.actions)[0]
                    if ($applied.status -ne 'Completed' -or -not $record.applied -or $record.error) {
                        throw "Apply/Verify failed for $($definition.id)."
                    }
                    $afterApplyJson = if ($definition.power) { Active-Scheme } elseif ($definition.coreParking) { Core-ParkingValue } else { Json-State @(Get-RegistryStates $definition) }
                    if ($afterApplyJson -cne $expectedAppliedJson) {
                        throw "Applied raw state mismatch for $($definition.id): $afterApplyJson"
                    }
                    $afterInspect = Invoke-Agent actions | Where-Object id -eq $definition.id
                    if (-not $afterInspect.availability.alreadyApplied -or $afterInspect.availability.canApply) {
                        throw "Inspect did not read back the applied state for $($definition.id)."
                    }

                    $operation = Get-ChildItem $operationRoot -Filter manifest.json -Recurse |
                        Where-Object { (Get-Content -Raw $_.FullName | ConvertFrom-Json).id -eq $applied.id } |
                        Select-Object -First 1
                    if (-not $operation) { throw "Manifest missing for $($definition.id)." }
                    $operationDirectory = Split-Path $operation.FullName
                    $exports = @(Get-ChildItem (Join-Path $operationDirectory 'registry') -File -ErrorAction SilentlyContinue)
                    if ($definition.hive -and $exports.Count -lt 1) {
                        throw "Registry export missing for $($definition.id)."
                    }
                    $applyRestorePoint = "NeuroTune $(([guid]$applied.id).ToString('N'))"
                    if (-not (Get-ComputerRestorePoint | Where-Object Description -eq $applyRestorePoint)) {
                        throw "Apply restore point missing for $($definition.id)."
                    }

                    Invoke-Agent rollback @{ operationId = $applied.id; runId = $run.Id } | Out-Null
                    $afterRollbackJson = if ($definition.power) { Active-Scheme } elseif ($definition.coreParking) { Core-ParkingValue } else { Json-State @(Get-RegistryStates $definition) }
                    if ($afterRollbackJson -cne $beforeJson) {
                        throw "Rollback raw state/type mismatch for $($definition.id): $afterRollbackJson"
                    }
                    $finalManifest = Get-Content -Raw $operation.FullName | ConvertFrom-Json
                    if ($finalManifest.status -ne 'Rollback completed' -or -not @($finalManifest.actions)[0].rolledBack) {
                        throw "Rollback manifest is incomplete for $($definition.id)."
                    }
                    $rollbackRestorePoint = "NeuroTune before rollback $(([guid]$applied.id).ToString('N'))"
                    if (-not (Get-ComputerRestorePoint | Where-Object Description -eq $rollbackRestorePoint)) {
                        throw "Rollback restore point missing for $($definition.id)."
                    }
                    $rollbackInspect = Invoke-Agent actions | Where-Object id -eq $definition.id
                    if (-not $rollbackInspect.availability.canApply -or $rollbackInspect.availability.alreadyApplied) {
                        throw "Inspect did not read back the restored state for $($definition.id)."
                    }

                [pscustomobject]@{
                    actionId = $definition.id
                    inspectBefore = 'ready'
                    applyAndVerify = 'passed'
                    rawAppliedState = 'passed'
                    registryExport = if ($definition.hive) { 'passed' } else { 'not-applicable' }
                    applyRestorePoint = 'passed'
                    rollbackValueAndKind = 'passed'
                    rollbackRestorePoint = 'passed'
                    manifest = 'Rollback completed'
                }
                } finally {
                    if ($stateCaptured) {
                        if ($definition.power) { & powercfg.exe /setactive $actualState | Out-Null }
                        elseif ($definition.coreParking) {
                            & powercfg.exe /setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 $actualState | Out-Null
                            & powercfg.exe /setactive SCHEME_CURRENT | Out-Null
                        } else { Set-RegistryStates $definition @($actualState) }
                    }
                }
            }
        } finally {
            if ($gpuStoreExisted) {
                [IO.File]::WriteAllText($gpuStore, $gpuStoreOriginal, [Text.UTF8Encoding]::new($false))
            } elseif (Test-Path -LiteralPath $gpuStore) {
                Remove-Item -LiteralPath $gpuStore -Force
            }
        }
    })
    $report.results = @($guestResults | Select-Object actionId, inspectBefore, applyAndVerify,
        rawAppliedState, registryExport, applyRestorePoint, rollbackValueAndKind,
        rollbackRestorePoint, manifest)
    $report.status = 'passed'
}
catch {
    $report.status = 'failed'
    $report.error = $_.Exception.Message
}
finally {
    if ($session) { Remove-PSSession $session -ErrorAction SilentlyContinue }
    try {
        if ($SkipCheckpointRestore) {
            $report.cleanup = 'action rollback completed; checkpoint restore skipped'
        } else {
            $snapshot = Get-VMSnapshot -VMName $VmName -Name $CheckpointName -ErrorAction Stop
            if ((Get-VM -Name $VmName).State -ne 'Off') { Stop-VM -Name $VmName -TurnOff }
            Restore-VMSnapshot -VMSnapshot $snapshot -Confirm:$false
            Start-VM -Name $VmName | Out-Null
            $report.cleanup = 'checkpoint restored and VM restarted'
        }
    }
    catch {
        $report.cleanup = "failed: $($_.Exception.Message)"
        if (-not $report.error) { $report.error = $_.Exception.Message; $report.status = 'failed' }
    }
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($resolvedReport)) | Out-Null
    [IO.File]::WriteAllText($resolvedReport, ($report | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}

if ($report.status -ne 'passed') { throw $report.error }
$report
