[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ResultPath)

$ErrorActionPreference = 'Stop'
Get-VM | Select-Object Name, State, Version, Generation,
    @{ Name = 'StartupMemoryBytes'; Expression = { (Get-VMMemory -VMName $_.Name).Startup } } |
    ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding utf8
