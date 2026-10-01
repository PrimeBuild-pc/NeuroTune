#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$AgentDirectory = (Join-Path $PSScriptRoot '..\src\NeuroTune.Agent\bin\Release\net8.0-windows'),
    [ValidateRange(30, 600)][int]$DurationSeconds = 30,
    [ValidateRange(1, 3)][int]$Count = 3,
    [string]$ReportPath = (Join-Path $PSScriptRoot '..\artifacts\local-latency-validation.json')
)
$ErrorActionPreference = 'Stop'
$agentPath = Join-Path ([IO.Path]::GetFullPath($AgentDirectory)) 'NeuroTune.Agent.exe'
function Invoke-Agent([string]$Command, [object]$Body = @{}) {
    $start = [Diagnostics.ProcessStartInfo]::new($agentPath, $Command)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $process.StandardInput.Write(($Body | ConvertTo-Json -Depth 8 -Compress))
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) { $process.Kill(); throw "Agent timed out: $Command" }
        $response = $stdout.GetAwaiter().GetResult() | ConvertFrom-Json
        if ($process.ExitCode -ne 0 -or -not $response.ok) { throw "${Command}: $($response.error) $($stderr.GetAwaiter().GetResult())" }
        $response.data
    }
    finally { $process.Dispose() }
}
$firmware = Invoke-Agent 'firmware-read' @{readConsent=$true}
$topology = Invoke-Agent 'measurement-topology'
$captures = @()
for ($index = 1; $index -le $Count; $index++) {
    $createdId = $null
    try {
        $startClock = [Diagnostics.Stopwatch]::StartNew()
        $session = Invoke-Agent 'measurement-start' @{processId=0; processStartTimeUtc='0001-01-01T00:00:00Z'; systemWide=$true; label='baseline'; durationSeconds=$DurationSeconds; keepRawTrace=$false}
        $createdId = [string]$session.id
        $startClock.Stop()
        $current = @(Invoke-Agent 'measurement-list' | Where-Object id -eq $createdId)[0]
        if ($current.state -ne 'recording') { throw 'Start did not return while capture was still recording; check watchdog pipe inheritance' }
        Write-Host "System diagnostic $index/$Count started ($DurationSeconds seconds)."
        $live = @(Invoke-Agent 'measurement-live')
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds + 30)
        do {
            Start-Sleep -Seconds 2
            $session = @(Invoke-Agent 'measurement-list' | Where-Object id -eq $createdId)[0]
            if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'Capture watchdog deadline exceeded' }
        } while ($session.state -eq 'recording')
        if ($session.state -ne 'captured') { throw "Unexpected capture state: $($session.state)" }
        $session = Invoke-Agent 'measurement-analyze' @{sessionId=$createdId}
        if (-not $session.report.quality.isValid) { throw 'Trace quality gate failed' }
        $etl = Join-Path $env:LOCALAPPDATA "NeuroTune\measurements\$createdId\capture.etl"
        if (Test-Path -LiteralPath $etl) { throw 'Raw trace retained unexpectedly' }
        $captures += [ordered]@{
            quality = $session.report.quality
            processors = $session.report.processors
            interrupts = $session.report.interrupts
            hardFaultCount = ($session.report.hardFaults | Measure-Object -Property {$_.resolution.count} -Sum).Sum
            hardFaultMaxMicroseconds = ($session.report.hardFaults | Measure-Object -Property {$_.resolution.maxMicroseconds} -Maximum).Maximum
            liveProcessorTimes = $live
            startResponseMilliseconds = $startClock.Elapsed.TotalMilliseconds
            startReturnedWhileRecording = $true
            rawTraceDeleted = $true
        }
        Write-Host "System diagnostic $index/$Count passed; raw ETL deleted."
    }
    finally {
        if ($createdId) {
            $current = @(Invoke-Agent 'measurement-list' | Where-Object id -eq $createdId)[0]
            if ($current.state -eq 'recording') { Invoke-Agent 'measurement-cancel' @{sessionId=$createdId} | Out-Null }
        }
    }
}
$report = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    scope = 'System-wide desktop/background diagnostic snapshots; not a controlled DirectX benchmark or proof of gain'
    agentSha256 = (Get-FileHash -LiteralPath $agentPath).Hash
    codeSha256 = @('NeuroTune.Agent.exe', 'NeuroTune.Agent.dll', 'NeuroTune.Core.dll', 'Microsoft.Diagnostics.Tracing.TraceEvent.dll', 'NeuroTuneLatency.wprp') | ForEach-Object {
        $file = Join-Path ([IO.Path]::GetDirectoryName($agentPath)) $_
        if (Test-Path -LiteralPath $file) { [ordered]@{file=$_; sha256=(Get-FileHash -LiteralPath $file).Hash} }
    }
    gpus = @($topology.gpus | Select-Object name,vendor,driverVersion)
    firmware = $firmware
    captures = $captures
    writesApplied = $false
}
$destination = [IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText($destination, ($report | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Write-Host "Saved $destination"
