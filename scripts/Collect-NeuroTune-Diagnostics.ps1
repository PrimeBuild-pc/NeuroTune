[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$InstallDirectory = (Join-Path $env:ProgramFiles 'NeuroTune'),
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

function Protect-Text($Value) {
    $text = [string]$Value
    foreach ($identity in @($env:USERNAME, $env:COMPUTERNAME)) {
        if ($identity) { $text = [regex]::Replace($text, [regex]::Escape($identity), '[redacted]', 'IgnoreCase') }
    }
    $text = $text -replace '(?i)sk-[a-z0-9_-]+', '[redacted-key]'
    $text = $text -replace '(?i)Bearer\s+\S+', 'Bearer [redacted]'
    $text = $text -replace '(?i)(api[_-]?key|token|secret|authorization)\s*[:=]\s*[^\s,;]+', '$1=[redacted]'
    $text = $text -replace '(?i)[a-z]:[\\/][^\r\n"<>|]*', '[redacted-path]'
    $text = $text -replace 'S-1-5-21-(\d+-){3}\d+', '[redacted-sid]'
    return $text.Substring(0, [Math]::Min(4000, $text.Length))
}

function Convert-Run($Run) {
    [ordered]@{
        createdAtUtc = $Run.CreatedAtUtc
        updatedAtUtc = $Run.UpdatedAtUtc
        state = $Run.State
        mode = $Run.Mode
        error = Protect-Text $Run.Error
        plannerStopReason = Protect-Text $Run.PlannerStopReason
        usedLocalFallback = $Run.UsedLocalFallback
        hasOperation = [bool]$Run.OperationId
        requiresRecovery = $Run.RequiresRecovery
        audit = @($Run.PlannerAudit | Select-Object -Last 15 | ForEach-Object {
            [ordered]@{ turn = $_.Turn; kind = Protect-Text $_.Kind; accepted = $_.Accepted; reason = Protect-Text $_.Reason }
        })
    }
}

if ($SelfTest) {
    $fixture = [pscustomobject]@{
        Error = 'Pi startup failed: sk-test-secret Bearer private-token'
        EvidenceFacts = @{ private = 'never-export-this' }
        Goals = @{ Notes = 'never-export-this' }
        PlannerAudit = @([pscustomobject]@{ Turn = 0; Kind = 'rejected'; Accepted = $false; Reason = 'exit 0xC0000135' })
    }
    $result = Convert-Run $fixture | ConvertTo-Json -Depth 8
    if ($result -match 'never-export-this|sk-test-secret|private-token|EvidenceFacts|Goals') { throw 'Privacy self-check failed.' }
    if ($result -notmatch '0xC0000135' -or ($result | ConvertFrom-Json).audit[0].turn -ne 0) { throw 'Diagnostic self-check failed.' }
    if ((Protect-Text 'C:\Users\Private\file.json') -match 'Private' -or (Protect-Text 'token=private-value') -match 'private-value') { throw 'Redaction self-check failed.' }
    Write-Host 'Diagnostics collector self-test passed.'
    return
}

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Open PowerShell as administrator, using the same Windows account that ran NeuroTune, then run this script again.'
}

$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$runsDirectory = Join-Path $env:ProgramData "NeuroTune-journals\$sid\runs"
$issues = @()
$runs = @()
try {
    # No recursive copy or wildcard fallback: only this account's last ten run.json files.
    if (-not (Test-Path -LiteralPath $runsDirectory -PathType Container)) {
        $issues += 'No run directory for this Windows account. Confirm the app was tested under the same account.'
    }
    else {
        foreach ($directory in (Get-ChildItem -LiteralPath $runsDirectory -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 10)) {
            try {
                $file = Get-Item -LiteralPath (Join-Path $directory.FullName 'run.json')
                if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $file.Length -gt 16MB) {
                    throw 'Unsafe or oversized journal.'
                }
                $runs += Convert-Run (Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json)
            }
            catch { $issues += 'One run could not be read; no raw exception or path was exported.' }
        }
    }
}
catch { $issues += 'Run journals could not be listed. Check administrator access and the Windows account.' }

$files = @('NeuroTune.exe', 'agent\NeuroTune.Agent.exe', 'agent\pi\node.exe', 'agent\pi\agent.mjs') | ForEach-Object {
    $relative = $_
    $path = Join-Path $InstallDirectory $relative
    try {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $file = Get-Item -LiteralPath $path
            if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked runtime file.' }
            [ordered]@{ name = $relative; present = $true; bytes = $file.Length; version = $file.VersionInfo.FileVersion; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        }
        else { [ordered]@{ name = $relative; present = $false } }
    }
    catch {
        $issues += 'One runtime file could not be inspected; no raw exception or path was exported.'
        [ordered]@{ name = $relative; present = $null; status = 'unreadable-or-refused' }
    }
}
$windows = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$report = [ordered]@{
    schemaVersion = 1
    collectedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    windows = [ordered]@{ displayVersion = $windows.DisplayVersion; build = $windows.CurrentBuildNumber; revision = $windows.UBR }
    files = @($files)
    runs = @($runs)
    issues = @($issues)
    privacy = 'No credentials/settings, full profiles, raw journals, traces or Windows files copied. Text redaction is best-effort; review before sharing.'
}
if (-not $OutputPath) {
    $OutputPath = Join-Path ([Environment]::GetFolderPath('Desktop')) ("NeuroTune-diagnostics-{0}-{1}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), [guid]::NewGuid().ToString('N').Substring(0, 6))
}
if (Test-Path -LiteralPath $OutputPath) { throw 'The output file already exists; choose a new filename.' }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host "Created: $OutputPath"
Write-Host 'Review this JSON before sharing it, together with the error screenshot and the selected provider/model (never the API key).'
if ($issues.Count) { Write-Warning ($issues -join ' ') }
