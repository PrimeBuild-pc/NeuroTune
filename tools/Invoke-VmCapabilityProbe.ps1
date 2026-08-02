[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $VMName,
    [Parameter(Mandatory)] [string] $CredentialPath,
    [Parameter(Mandatory)] [string] $ProbePath,
    [Parameter(Mandatory)] [string] $ResultPath,
    [ValidateRange(2048, 65536)] [int] $TemporaryStartupMemoryMB
)

$ErrorActionPreference = 'Stop'
$vm = $null
$wasRunning = $false
$originalStartupBytes = $null
function Restore-VMConfiguration {
    if ($null -eq $vm -or $wasRunning) { return }
    $current = Get-VM -Name $VMName
    if ($current.State -ne 'Off') {
        Stop-VM -VM $current -Force
        while ((Get-VM -Name $VMName).State -ne 'Off') { Start-Sleep -Milliseconds 500 }
    }
    if ($null -ne $originalStartupBytes) {
        Set-VMMemory -VMName $VMName -StartupBytes $originalStartupBytes
    }
}
trap {
    try { Restore-VMConfiguration } catch { }
    if (-not (Test-Path -LiteralPath $ResultPath)) {
        [pscustomobject]@{ ExitCode = 1; RunnerError = $_.Exception.Message } |
            ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ResultPath -Encoding utf8
    }
    exit 1
}
$credential = Import-Clixml -LiteralPath $CredentialPath
$vm = Get-VM -Name $VMName
$wasRunning = $vm.State -eq 'Running'
$originalStartupBytes = (Get-VMMemory -VMName $VMName).Startup
if (-not $wasRunning -and $TemporaryStartupMemoryMB -gt 0) {
    Set-VMMemory -VMName $VMName -StartupBytes ($TemporaryStartupMemoryMB * 1MB)
}
if ($vm.State -ne 'Running') {
    Start-VM -VM $vm | Out-Null
}

$deadline = [DateTimeOffset]::Now.AddMinutes(3)
do {
    $heartbeat = Get-VMIntegrationService -VMName $VMName -Name 'Heartbeat'
    if ($heartbeat.PrimaryStatusDescription -eq 'OK') { break }
    Start-Sleep -Seconds 2
} while ([DateTimeOffset]::Now -lt $deadline)
if ($heartbeat.PrimaryStatusDescription -ne 'OK') {
    throw "The VM heartbeat did not become ready."
}

$guestProbe = 'C:\NeuroTuneLab\NeuroTune.CapabilityProbe.exe'
$session = New-PSSession -VMName $VMName -Credential $credential
Invoke-Command -Session $session -ScriptBlock { New-Item -ItemType Directory -Path 'C:\NeuroTuneLab' -Force | Out-Null }
Copy-Item -LiteralPath $ProbePath -Destination $guestProbe -ToSession $session -Force

$result = Invoke-Command -Session $session -ScriptBlock {
    $output = & 'C:\NeuroTuneLab\NeuroTune.CapabilityProbe.exe' --accept-disposable-vm 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    $windows = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    [pscustomobject]@{
        ExitCode = $exitCode
        WindowsProductName = $windows.ProductName
        DisplayVersion = $windows.DisplayVersion
        CurrentBuild = $windows.CurrentBuild
        ProbeOutput = $output.Trim()
    }
}
Remove-PSSession $session

Restore-VMConfiguration
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ResultPath -Encoding utf8
if ($result.ExitCode -ne 0) { throw "The VM capability probe reported one or more failures." }
