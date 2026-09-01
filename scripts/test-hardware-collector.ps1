[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$collector = Join-Path $PSScriptRoot '..\tools\hardware-collector\Collect-NeuroTune-HardwareReport.ps1'
$source = Get-Content -LiteralPath $collector -Raw
$forbiddenCommands = '(?im)^\s*(Set-ItemProperty|New-ItemProperty|Remove-Item(Property)?|Start-Process|Invoke-WebRequest|Invoke-RestMethod|Enable-|Disable-|Install-)\b'
if ($source -match $forbiddenCommands) { throw "Collector contains a mutating or network command: $($Matches[1])" }

$output = Join-Path ([IO.Path]::GetTempPath()) "NeuroTune-collector-test-$([guid]::NewGuid().ToString('N')).json"
try {
    & $collector -OutputPath $output
    $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    if ($report.schemaVersion -ne 1 -or $report.collectorGuarantees.writesSystemState -ne $false) { throw 'Collector contract is invalid.' }
    if (@($report.processors).Count -lt 1 -or @($report.cpuSets).Count -lt 1) { throw 'CPU inventory is empty.' }
    if (@($report.interruptDevices | Where-Object { $_.interrupt.assignmentSetOverride.hexValue }).Count -ne 0) {
        throw 'An interrupt mask value escaped the report.'
    }
    if ((Get-Content -LiteralPath $output -Raw) -match '(?i)([A-Z]:\\Users\\|computerName|userName|serialNumber|macAddress|ipAddress|pnpDeviceId|deviceInstanceId|registryPath|pathName)') {
        throw 'Report privacy check failed.'
    }
}
finally {
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
}

Write-Host 'Hardware collector self-test passed.' -ForegroundColor Green
