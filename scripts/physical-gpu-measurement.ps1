#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$AgentDirectory = (Join-Path $PSScriptRoot '..\ui\src-tauri\target\release\agent'),
    [ValidateRange(0, 2147483647)]
    [int]$ProcessId = 0,
    [ValidateSet('Unverified', 'DirectX11', 'DirectX12')]
    [string]$GraphicsApi = 'Unverified',
    [ValidateRange(30, 600)]
    [int]$DurationSeconds = 180,
    [ValidateRange(0, 300)]
    [int]$PreparationSeconds = 30,
    [string]$Scene,
    [string]$GpuName,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $PSScriptRoot ("..\artifacts\physical-gpu-measurement-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
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
    try {
        $process.StandardInput.Write(($Body | ConvertTo-Json -Compress -Depth 8))
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) { $process.Kill(); throw "Agent timed out: $Command" }
        $response = $stdout.GetAwaiter().GetResult() | ConvertFrom-Json
        if ($process.ExitCode -ne 0 -or -not $response -or -not $response.ok) {
            throw "Agent command '$Command' failed: $($response.error) $($stderr.GetAwaiter().GetResult())"
        }
        $response.data
    }
    finally { $process.Dispose() }
}

function Assert-NoNeuroTuneWprSession {
    if ((& wpr.exe -status 2>&1 | Out-String) -match 'NeuroTune-[0-9a-fA-F]{32}') {
        throw 'A named NeuroTune WPR session is still active.'
    }
}

$sessionIds = @()
$completed = @()
$report = $null
try {
    Assert-NoNeuroTuneWprSession
    if ($ProcessId -eq 0) {
        $windowIds = @(Get-Process | Where-Object { $_.MainWindowHandle -ne 0 } | ForEach-Object Id)
        $choices = @(Invoke-Agent 'measurement-workloads' | Where-Object { $windowIds -contains $_.processId })
        if ($choices.Count -eq 0) { throw 'Apri prima il gioco e carica una scena ripetibile.' }
        for ($choice = 0; $choice -lt $choices.Count; $choice++) { Write-Host ("{0}. {1} (PID {2})" -f ($choice + 1), $choices[$choice].name, $choices[$choice].processId) }
        $selection = 0
        if (-not [int]::TryParse((Read-Host 'Numero del gioco da misurare'), [ref]$selection) -or $selection -lt 1 -or $selection -gt $choices.Count) {
            throw 'Selezione non valida. Nessuna cattura avviata.'
        }
        $ProcessId = [int]$choices[$selection - 1].processId
    }
    if ([string]::IsNullOrWhiteSpace($Scene)) { $Scene = Read-Host 'Descrivi scena, risoluzione, preset e limite FPS (salvati solo localmente)' }
    if ([string]::IsNullOrWhiteSpace($Scene) -or $Scene.Length -gt 2000) { throw 'Scene description is required (maximum 2000 characters).' }
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

    Write-Host "Torna nel gioco entro $PreparationSeconds secondi. Mantieni la stessa scena per tre catture da $DurationSeconds secondi."
    if ($PreparationSeconds -gt 0) { Start-Sleep -Seconds $PreparationSeconds }

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
        # Retain every completed report locally, including rejected traces, for diagnosis and later comparisons.
        $completed += $analyzed
        $quality = $analyzed.report.quality
        if ($analyzed.state -ne 'completed' -or -not $quality.isValid -or [long]$quality.eventsLost -ne 0 -or @($quality.missingProviders).Count -ne 0) {
            throw "Baseline $_ failed the deterministic trace quality gate."
        }
        $etl = Join-Path $env:LOCALAPPDATA "NeuroTune\measurements\$($capture.id)\capture.etl"
        if (Test-Path -LiteralPath $etl) { throw 'A raw ETL remained without consent.' }
        Assert-NoNeuroTuneWprSession
        [pscustomobject]@{
            sessionId = [string]$capture.id
            analyzerSchemaVersion = [int]$analyzed.report.schemaVersion
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
        scope = 'Read-only collection; graphics API and workload scene are user-declared, not independently verified. No optimization gain established.'
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
    if ($sessionIds.Count -gt 0) {
        $cleanupError = $null
        try {
            foreach ($item in @(Invoke-Agent 'measurement-list')) {
                if ($sessionIds -notcontains [string]$item.id) { continue }
                if ($item.state -eq 'recording') { Invoke-Agent 'measurement-cancel' @{ sessionId = $item.id } | Out-Null }
                elseif ($item.state -ne 'completed') { Invoke-Agent 'measurement-delete' @{ sessionId = $item.id } | Out-Null }
            }
            Assert-NoNeuroTuneWprSession
        }
        catch { $cleanupError = $_.Exception.Message }
        finally {
            $localPath = [IO.Path]::GetFullPath($ReportPath) + '.local.json'
            New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($localPath)) | Out-Null
            $local = [ordered]@{
                schemaVersion = 1; generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                scope = 'User-declared scene; not independently verified. Local process names included.'
                scene = $Scene; requestedSessionIds = @($sessionIds); completedSessions = @($completed)
                validationCompleted = ($null -ne $report); writesApplied = $false
            }
            [IO.File]::WriteAllText($localPath, ($local | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
            Write-Host "Misure mantenute nella cronologia NeuroTune. Report locale: $localPath"
        }
        if ($cleanupError) { throw "Automatic measurement cleanup failed: $cleanupError" }
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
