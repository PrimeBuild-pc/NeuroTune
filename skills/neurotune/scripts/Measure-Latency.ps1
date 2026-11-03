# NeuroTune: record kernel DPC/ISR timing with built-in Windows tools (logman + tracerpt), no third-party software.
# Read-only for the system: it starts and stops the "NT Kernel Logger" trace session and writes files only under -WorkDirectory.
# Requires administrator rights. Launched by `cli.mjs measure-latency`, which parses the CSV and deletes the work files.
param(
  [Parameter(Mandatory = $true)][string]$WorkDirectory,
  [ValidateRange(5, 300)][int]$Seconds = 30
)
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Write-Error 'NEEDS_ADMIN'; exit 3 }
if (-not (Test-Path -LiteralPath $WorkDirectory -PathType Container)) { Write-Error 'WORKDIR_MISSING'; exit 2 }

$etl = Join-Path $WorkDirectory 'kernel.etl'
$csv = Join-Path $WorkDirectory 'kernel.csv'
$summary = Join-Path $WorkDirectory 'summary.txt'
$logman = Join-Path $env:SystemRoot 'System32\logman.exe'
$tracerpt = Join-Path $env:SystemRoot 'System32\tracerpt.exe'

# Only one kernel logger can run; never stop a session another tool (LatencyMon, WPR, xperf) owns.
& $logman query 'NT Kernel Logger' -ets *> $null
if ($LASTEXITCODE -eq 0) { Write-Error 'KERNEL_LOGGER_BUSY'; exit 4 }

$started = $false
try {
  & $logman start 'NT Kernel Logger' -p 'Windows Kernel Trace' '(dpc,isr,img)' -bs 1024 -nb 64 512 -o $etl -ets | Out-Null
  if ($LASTEXITCODE -ne 0) { Write-Error "LOGMAN_START_FAILED $LASTEXITCODE"; exit 5 }
  $started = $true
  Start-Sleep -Seconds $Seconds
} finally {
  if ($started) { & $logman stop 'NT Kernel Logger' -ets | Out-Null }
}
& $tracerpt $etl -o $csv -of CSV -summary $summary -y | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $csv)) { Write-Error "TRACERPT_FAILED $LASTEXITCODE"; exit 6 }
Remove-Item -LiteralPath $etl -Force -ErrorAction SilentlyContinue
$lost = (Select-String -LiteralPath $summary -Pattern 'Events\s+Lost\s+(\d+)' | Select-Object -First 1).Matches.Groups[1].Value
[pscustomobject]@{ csv = $csv; seconds = $Seconds; eventsLost = if ($lost) { [int64]$lost } else { $null } } | ConvertTo-Json -Compress
