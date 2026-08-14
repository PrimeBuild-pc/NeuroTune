#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AgentDirectory,
    [Parameter(Mandatory)]
    [ValidateRange(5, 2147483647)]
    [int]$ProcessId,
    [ValidateSet('DirectX11', 'DirectX12')]
    [string]$GraphicsApi = 'DirectX12',
    [ValidateRange(30, 600)]
    [int]$DurationSeconds = 180,
    [string]$GpuName,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $PSScriptRoot '..\artifacts\physical-gpu-measurement.json'
}
$agentDirectoryPath = [IO.Path]::GetFullPath($AgentDirectory)
$agentPath = Join-Path $agentDirectoryPath 'NeuroTune.Agent.exe'
$profilePath = Join-Path $agentDirectoryPath 'NeuroTuneLatency.wprp'
if (-not (Test-Path -LiteralPath $agentPath -PathType Leaf)) { throw "Agent not found: $agentPath" }
if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) { throw "WPR profile not found: $profilePath" }

function Invoke-Agent([string]$Command, [object]$Body = @{}) {
    $start = [Diagnostics.ProcessStartInfo]::new($agentPath, $Command)
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
    if ((& wpr.exe -status 2>&1 | Out-String) -match 'NeuroTune-[0-9a-fA-F]{32}') {
        throw 'A named NeuroTune WPR session is still active.'
    }
}

$initialIds = @()
$sessionIds = @()
$cleanupReady = $false
$report = $null
try {
    Assert-NoNeuroTuneWprSession
    $initialIds = @(Invoke-Agent 'measurement-list' | ForEach-Object { [string]$_.id })
    $cleanupReady = $true
    $workload = @(Invoke-Agent 'measurement-workloads' | Where-Object processId -eq $ProcessId)
    if ($workload.Count -ne 1) { throw 'The selected process is unavailable. Start the DirectX workload and use its current PID.' }

    $topology = Invoke-Agent 'measurement-topology'
    $gpus = @($topology.gpus | Where-Object physicalHost)
    if (-not [string]::IsNullOrWhiteSpace($GpuName)) {
        $gpus = @($gpus | Where-Object name -eq $GpuName)
    }
    if ($gpus.Count -ne 1) { throw 'Select exactly one physical AMD/NVIDIA GPU with -GpuName.' }
    $gpu = $gpus[0]
    $policy = Invoke-Agent 'measurement-gpu-affinity-inspect' @{ deviceKey = $gpu.deviceKey }
    if (-not $policy.restorable -or $policy.applyEnabled) { throw 'The current GPU IRQ policy is not safely restorable or the read-only gate was violated.' }

    $sessions = 1..3 | ForEach-Object {
        Write-Host "Recording baseline $_/3 for $DurationSeconds seconds. Keep the workload scene repeatable."
        $capture = Invoke-Agent 'measurement-start' @{
            processId = $workload[0].processId
            processStartTimeUtc = $workload[0].startTimeUtc
            label = 'baseline'
            durationSeconds = $DurationSeconds
            keepRawTrace = $false
        }
        $sessionIds += [string]$capture.id
        $deadline = (Get-Date).AddSeconds($DurationSeconds + 30)
        do {
            Start-Sleep -Seconds 1
            $captured = @(Invoke-Agent 'measurement-list' | Where-Object id -eq $capture.id | Select-Object -First 1)
        } until (($captured.Count -eq 1 -and $captured[0].state -ne 'recording') -or (Get-Date) -gt $deadline)
        if ($captured.Count -ne 1 -or $captured[0].state -ne 'captured') { throw "Baseline $_ was not captured before its deadline." }

        $analyzed = Invoke-Agent 'measurement-analyze' @{ sessionId = $capture.id }
        $quality = $analyzed.report.quality
        if ($analyzed.state -ne 'completed' -or -not $quality.isValid -or [long]$quality.eventsLost -ne 0 -or @($quality.missingProviders).Count -ne 0) {
            throw "Baseline $_ failed the deterministic trace quality gate."
        }
        $etl = Join-Path $env:LOCALAPPDATA "NeuroTune\measurements\$($capture.id)\capture.etl"
        if (Test-Path -LiteralPath $etl) { throw 'A raw ETL remained without consent.' }
        Assert-NoNeuroTuneWprSession
        [pscustomobject]@{
            durationMilliseconds = [double]$quality.durationMilliseconds
            etlBytes = [long]$quality.etlBytes
            eventsLost = [long]$quality.eventsLost
            targetPresencePercent = [double]$quality.targetPresencePercent
        }
    }

    $candidateSet = Invoke-Agent 'measurement-gpu-candidates' @{
        deviceKey = $gpu.deviceKey
        baselineSessionIds = $sessionIds
    }
    $candidates = @($candidateSet.candidates)
    $duplicateCores = @($candidates | Group-Object processorGroup, physicalCore | Where-Object Count -gt 1)
    if ($candidates.Count -lt 1 -or $candidates.Count -gt 3 -or $duplicateCores.Count -ne 0 -or
        @($candidates | Where-Object applyEnabled).Count -ne 0) {
        throw 'The read-only GPU candidate gate was violated.'
    }

    $os = Get-CimInstance Win32_OperatingSystem
    $report = [ordered]@{
        schemaVersion = 1
        generatedAt = [DateTimeOffset]::UtcNow.ToString('o')
        windows = [ordered]@{ caption = $os.Caption; build = $os.BuildNumber }
        workload = [ordered]@{ executable = $workload[0].name; declaredGraphicsApi = $GraphicsApi; durationSeconds = $DurationSeconds }
        gpu = [ordered]@{
            name = $gpu.name
            vendor = $gpu.vendor
            driverVersion = $gpu.driverVersion
            policyState = $policy.state
            restorable = [bool]$policy.restorable
            assignmentSetOverride = [ordered]@{ exists = [bool]$policy.assignmentSetOverride.exists; kind = $policy.assignmentSetOverride.kind; byteLength = [int]$policy.assignmentSetOverride.byteLength }
            devicePolicy = [ordered]@{ exists = [bool]$policy.devicePolicy.exists; kind = $policy.devicePolicy.kind; byteLength = [int]$policy.devicePolicy.byteLength }
        }
        agentSha256 = (Get-FileHash -LiteralPath $agentPath -Algorithm SHA256).Hash
        sessions = @($sessions)
        candidates = @($candidates | Select-Object processorGroup, logicalProcessor, physicalCore, smtIndex,
            efficiencyClass, cacheCluster, interruptSharePercent, targetRunningMilliseconds, readyOverlapMicroseconds)
        rawTraceRetention = 'none'
        wprOrphanCheck = 'passed'
        applyEnabled = $false
    }
}
finally {
    if ($cleanupReady) {
        try {
            foreach ($item in @(Invoke-Agent 'measurement-list')) {
                if ($initialIds -contains [string]$item.id -or [int]$item.processId -ne $ProcessId) { continue }
                if ($item.state -eq 'recording') { Invoke-Agent 'measurement-cancel' @{ sessionId = $item.id } | Out-Null }
                else { Invoke-Agent 'measurement-delete' @{ sessionId = $item.id } | Out-Null }
            }
            Assert-NoNeuroTuneWprSession
        }
        catch { throw "Automatic measurement cleanup failed: $($_.Exception.Message)" }
    }
}

$resolvedReport = [IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($resolvedReport)) | Out-Null
$reportJson = $report | ConvertTo-Json -Depth 8
if ($reportJson -match '"(processId|deviceKey|deviceInstanceId|affinityRegistryPath|candidateId|hexValue)"\s*:') {
    throw 'The physical validation report contains a forbidden local identifier.'
}
[IO.File]::WriteAllText($resolvedReport, $reportJson, [Text.UTF8Encoding]::new($false))
$report
