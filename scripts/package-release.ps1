[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/release' }
$config = Get-Content -LiteralPath (Join-Path $root 'ui/src-tauri/tauri.conf.json') -Raw | ConvertFrom-Json
$version = [string]$config.version
$releaseDirectory = Join-Path $root 'ui/src-tauri/target/release'
$mainExecutable = Join-Path $releaseDirectory 'neurotune.exe'
$agentDirectory = Join-Path $releaseDirectory 'agent'
$installer = Get-ChildItem -LiteralPath (Join-Path $releaseDirectory 'bundle/nsis') -Filter "NeuroTune_${version}_*-setup.exe" | Select-Object -First 1

$required = @(
    $mainExecutable,
    (Join-Path $agentDirectory 'NeuroTune.Agent.exe'),
    (Join-Path $agentDirectory 'NeuroTune.Telemetry.exe'),
    (Join-Path $agentDirectory 'NeuroTuneLatency.wprp'),
    (Join-Path $root 'LICENSE'),
    (Join-Path $root 'README.md'),
    (Join-Path $root 'RELEASE_NOTES.md')
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required release file is missing: $path" }
}
if (-not $installer) { throw "NSIS installer for v$version was not found." }

$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$staging = Join-Path $output ".portable-staging-$version"
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
$portableName = "NeuroTune-$version-win-x64"
$portableRoot = Join-Path $staging $portableName
New-Item -ItemType Directory -Path (Join-Path $portableRoot 'agent') -Force | Out-Null

Copy-Item -LiteralPath $mainExecutable -Destination (Join-Path $portableRoot 'NeuroTune.exe')
Copy-Item -LiteralPath (Join-Path $agentDirectory 'NeuroTune.Agent.exe') -Destination (Join-Path $portableRoot 'agent/NeuroTune.Agent.exe')
Copy-Item -LiteralPath (Join-Path $agentDirectory 'NeuroTune.Telemetry.exe') -Destination (Join-Path $portableRoot 'agent/NeuroTune.Telemetry.exe')
Copy-Item -LiteralPath (Join-Path $agentDirectory 'NeuroTuneLatency.wprp') -Destination (Join-Path $portableRoot 'agent/NeuroTuneLatency.wprp')
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $portableRoot
Copy-Item -LiteralPath (Join-Path $root 'RELEASE_NOTES.md') -Destination $portableRoot

$zip = Join-Path $output "$portableName.zip"
$installerOutput = Join-Path $output $installer.Name
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath $portableRoot -DestinationPath $zip -CompressionLevel Optimal
Copy-Item -LiteralPath $installer.FullName -Destination $installerOutput -Force
Remove-Item -LiteralPath $staging -Recurse -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = $archive.Entries.FullName -replace '\\', '/'
    foreach ($relative in @('NeuroTune.exe', 'agent/NeuroTune.Agent.exe', 'agent/NeuroTune.Telemetry.exe', 'agent/NeuroTuneLatency.wprp', 'LICENSE')) {
        $expected = "$portableName/$relative"
        if ($entries -notcontains $expected) { throw "Portable archive is incomplete: $expected is missing." }
    }
}
finally {
    $archive.Dispose()
}

$assets = @($installerOutput, $zip) | Get-Item | Sort-Object Name
$checksumPath = Join-Path $output 'SHA256SUMS'
$checksums = $assets | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
}
[System.IO.File]::WriteAllLines($checksumPath, [string[]]$checksums, [System.Text.UTF8Encoding]::new($false))

Write-Host "Release assets created in $output"
$assets + (Get-Item -LiteralPath $checksumPath) | Select-Object Name, Length
