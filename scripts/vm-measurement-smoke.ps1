#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AgentDirectory,
    [string[]]$VmNames = @('NeuroTune-W11', 'NeuroTune-W10'),
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $PSScriptRoot '..\artifacts\vm-measurement-smoke.json'
}
$resolvedAgentDirectory = [IO.Path]::GetFullPath($AgentDirectory)
$agentPath = Join-Path $resolvedAgentDirectory 'NeuroTune.Agent.exe'
$profilePath = Join-Path $resolvedAgentDirectory 'NeuroTuneLatency.wprp'
if (-not (Test-Path -LiteralPath $agentPath -PathType Leaf)) { throw "Agent not found: $agentPath" }
if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) { throw "WPR profile not found: $profilePath" }

function Get-VmCredential([string]$VmName) {
    $unattendPath = 'C:\VmLab\NeuroTune-W11-diagnostics\unattend.xml'
    if ($VmName -eq 'NeuroTune-W11' -and (Test-Path -LiteralPath $unattendPath -PathType Leaf)) {
        [xml]$unattend = Get-Content -Raw -LiteralPath $unattendPath
        $account = $unattend.SelectSingleNode("//*[local-name()='LocalAccount'][*[local-name()='Name']='NeuroTuneTest']")
        $value = $account.SelectSingleNode("./*[local-name()='Password']/*[local-name()='Value']").InnerText
        if ([string]::IsNullOrWhiteSpace($value)) { throw 'The W11 lab credential is unavailable.' }
        return [pscredential]::new("$VmName\NeuroTuneTest", ($value | ConvertTo-SecureString -AsPlainText -Force))
    }
    $file = if ($VmName -match 'W11') { 'w11-credential.xml' } else { 'w10-credential.xml' }
    $path = Join-Path $env:USERPROFILE ".neurotune-vm\$file"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Credential file not found for $VmName." }
    Import-Clixml -LiteralPath $path
}

function Wait-PowerShellDirect([string]$VmName, [PSCredential]$Credential) {
    $deadline = (Get-Date).AddMinutes(3)
    do {
        try {
            Invoke-Command -VMName $VmName -Credential $Credential -ScriptBlock { $env:COMPUTERNAME } -ErrorAction Stop | Out-Null
            return
        }
        catch {
            if ($_.Exception.Message -like '*credential is invalid*') { throw }
            Start-Sleep -Seconds 3
        }
    } until ((Get-Date) -gt $deadline)
    throw "PowerShell Direct unavailable for $VmName."
}

$results = foreach ($vmName in $VmNames) {
    $session = $null
    try {
        $vm = Get-VM -Name $vmName -ErrorAction Stop
        if ($vm.State -ne 'Running') { throw "VM $vmName must already be running." }

        $credential = Get-VmCredential $vmName
        Wait-PowerShellDirect $vmName $credential
        $session = New-PSSession -VMName $vmName -Credential $credential

        Invoke-Command -Session $session -ScriptBlock {
            $testRoot = 'C:\NeuroTuneMeasurementTest'
            if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
            New-Item -ItemType Directory -Path (Join-Path $testRoot 'agent') -Force | Out-Null
        }
        foreach ($file in Get-ChildItem -LiteralPath $resolvedAgentDirectory -File) {
            Copy-Item -LiteralPath $file.FullName -Destination 'C:\NeuroTuneMeasurementTest\agent' -ToSession $session -Force
        }

        Invoke-Command -Session $session -ScriptBlock {
            $ErrorActionPreference = 'Stop'
            $agent = 'C:\NeuroTuneMeasurementTest\agent\NeuroTune.Agent.exe'
            $workload = $null
            $initialIds = @()

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
                $response = if ($stdout) { $stdout | ConvertFrom-Json } else { $null }
                if ($process.ExitCode -ne 0 -or -not $response -or -not $response.ok) {
                    $reason = if ($response.error) { $response.error } else { $stderr.Trim() }
                    throw "Agent command '$Command' failed: $reason"
                }
                $response.data
            }

            function Assert-NoNeuroTuneWprSession {
                $status = (& wpr.exe -status 2>&1 | Out-String)
                if ($status -match 'NeuroTune-[0-9a-fA-F]{32}') {
                    throw 'A named NeuroTune WPR session is still active.'
                }
            }

            try {
                if (-not (Test-Path -LiteralPath $agent -PathType Leaf)) { throw 'Copied agent is unavailable.' }
                if (-not (Test-Path -LiteralPath (Join-Path (Split-Path $agent) 'NeuroTuneLatency.wprp') -PathType Leaf)) {
                    throw 'Copied WPR profile is unavailable.'
                }
                Assert-NoNeuroTuneWprSession
                $initialIds = @(Invoke-Agent 'measurement-list' | ForEach-Object { [string]$_.id })

                $workload = Start-Process -FilePath ping.exe -ArgumentList '-t', '127.0.0.1' -WindowStyle Hidden -PassThru
                Start-Sleep -Seconds 1
                $selectable = @(Invoke-Agent 'measurement-workloads' | Where-Object processId -eq $workload.Id)
                if ($selectable.Count -ne 1) { throw 'The deterministic workload was not selectable.' }

                $qualities = @()
                $sessionIds = @()
                1..3 | ForEach-Object {
                    $capture = Invoke-Agent 'measurement-start' @{
                        processId = $selectable[0].processId
                        processStartTimeUtc = $selectable[0].startTimeUtc
                        label = 'baseline'
                        durationSeconds = 30
                        keepRawTrace = $false
                    }
                    $sessionIds += [string]$capture.id

                    $deadline = (Get-Date).AddSeconds(50)
                    do {
                        Start-Sleep -Seconds 1
                        $captured = @(Invoke-Agent 'measurement-list' | Where-Object id -eq $capture.id | Select-Object -First 1)
                    } until (($captured.Count -eq 1 -and $captured[0].state -ne 'recording') -or (Get-Date) -gt $deadline)
                    if ($captured.Count -ne 1 -or $captured[0].state -ne 'captured') {
                        throw 'The watchdog did not produce a captured trace before its deadline.'
                    }

                    $analyzed = Invoke-Agent 'measurement-analyze' @{ sessionId = $capture.id }
                    if ($analyzed.state -ne 'completed' -or -not $analyzed.report.quality.isValid) {
                        throw 'The analyzed trace did not pass its deterministic quality gate.'
                    }
                    if ([long]$analyzed.report.quality.eventsLost -ne 0 -or @($analyzed.report.quality.missingProviders).Count -ne 0) {
                        throw 'The analyzed trace lost events or missed a required stream.'
                    }
                    $etl = Join-Path $env:LOCALAPPDATA "NeuroTune\measurements\$($capture.id)\capture.etl"
                    if (Test-Path -LiteralPath $etl) { throw 'A raw ETL remained without consent.' }
                    $qualities += $analyzed.report.quality
                    Assert-NoNeuroTuneWprSession
                }

                $topology = Invoke-Agent 'measurement-topology'
                if (@($topology.processors).Count -eq 0) { throw 'CPU topology was empty.' }
                $candidateCount = 0
                foreach ($gpu in @($topology.gpus)) {
                    $candidateSet = Invoke-Agent 'measurement-gpu-candidates' @{
                        deviceKey = $gpu.deviceKey
                        baselineSessionIds = $sessionIds
                    }
                    $candidates = @($candidateSet.candidates)
                    if ($candidates.Count -gt 3 -or @($candidates | Where-Object applyEnabled).Count -ne 0) {
                        throw 'The read-only GPU preview gate was violated.'
                    }
                    $candidateCount += $candidates.Count
                }

                foreach ($id in $sessionIds) { Invoke-Agent 'measurement-delete' @{ sessionId = $id } | Out-Null }
                $remainingIds = @(Invoke-Agent 'measurement-list' | ForEach-Object { [string]$_.id })
                if (@($remainingIds | Where-Object { $initialIds -notcontains $_ }).Count -ne 0) {
                    throw 'Measurement data created by the smoke test remained after cleanup.'
                }
                Assert-NoNeuroTuneWprSession

                $os = Get-CimInstance Win32_OperatingSystem
                [pscustomobject]@{
                    windows = $os.Caption
                    build = $os.BuildNumber
                    agentVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($agent).ProductVersion
                    sessionCount = $qualities.Count
                    validSessionCount = @($qualities | Where-Object isValid).Count
                    maximumEventsLost = [long](($qualities | Measure-Object eventsLost -Maximum).Maximum)
                    maximumEtlBytes = [long](($qualities | Measure-Object etlBytes -Maximum).Maximum)
                    minimumTargetPresencePercent = [double](($qualities | Measure-Object targetPresencePercent -Minimum).Minimum)
                    logicalProcessorCount = @($topology.processors).Count
                    gpuCount = @($topology.gpus).Count
                    gpuPreviewCandidateCount = $candidateCount
                    rawTraceRetention = 'none'
                    wprOrphanCheck = 'passed'
                }
            }
            finally {
                if ($workload -and -not $workload.HasExited) { Stop-Process -Id $workload.Id -Force -ErrorAction SilentlyContinue }
                try {
                    $current = @(Invoke-Agent 'measurement-list')
                    foreach ($item in $current) {
                        if ($initialIds -contains [string]$item.id) { continue }
                        if ($item.state -eq 'recording') {
                            Invoke-Agent 'measurement-cancel' @{ sessionId = $item.id } | Out-Null
                        }
                        else {
                            Invoke-Agent 'measurement-delete' @{ sessionId = $item.id } | Out-Null
                        }
                    }
                }
                catch { Write-Warning 'Automatic measurement cleanup could not be completed.' }
            }
        }
    }
    catch {
        Write-Error "Measurement smoke failed on $vmName`: $($_.Exception.Message)" -ErrorAction Continue
        [pscustomobject]@{
            windows = $vmName
            build = 'unknown'
            agentVersion = 'unknown'
            sessionCount = 0
            validSessionCount = 0
            maximumEventsLost = 0
            maximumEtlBytes = 0
            minimumTargetPresencePercent = 0
            logicalProcessorCount = 0
            gpuCount = 0
            gpuPreviewCandidateCount = 0
            rawTraceRetention = 'unknown'
            wprOrphanCheck = 'failed'
        }
    }
    finally {
        if ($session) {
            try {
                Invoke-Command -Session $session -ScriptBlock {
                    $testRoot = 'C:\NeuroTuneMeasurementTest'
                    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
                } -ErrorAction SilentlyContinue
            }
            finally { Remove-PSSession $session }
        }
    }
}

$report = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::UtcNow.ToString('o')
    agentSha256 = (Get-FileHash -LiteralPath $agentPath -Algorithm SHA256).Hash
    results = @($results)
}
$resolvedReport = [IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($resolvedReport)) | Out-Null
[IO.File]::WriteAllText($resolvedReport, ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$report

if (@($results | Where-Object wprOrphanCheck -ne 'passed').Count -ne 0) {
    throw "One or more VM measurement smoke tests failed. Redacted report: $resolvedReport"
}
