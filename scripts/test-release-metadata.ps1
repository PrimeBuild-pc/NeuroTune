[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$tauriConfig = Get-Content -LiteralPath (Join-Path $root 'ui/src-tauri/tauri.conf.json') -Raw | ConvertFrom-Json
$package = Get-Content -LiteralPath (Join-Path $root 'ui/package.json') -Raw | ConvertFrom-Json
$cargo = Get-Content -LiteralPath (Join-Path $root 'ui/src-tauri/Cargo.toml') -Raw
$license = Get-Content -LiteralPath (Join-Path $root 'LICENSE') -Raw
$readme = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw
$releaseNotes = Get-Content -LiteralPath (Join-Path $root 'RELEASE_NOTES.md') -Raw
$app = Get-Content -LiteralPath (Join-Path $root 'ui/src/App.tsx') -Raw

if ($license -notmatch 'MIT License' -or $license -notmatch 'Copyright \(c\) 2026 PrimeBuild') {
    throw 'LICENSE is not the approved PrimeBuild MIT license.'
}
if ($package.license -ne 'MIT') { throw 'ui/package.json must declare MIT.' }
if ($cargo -notmatch '(?m)^license[ \t]*=[ \t]*"MIT"[ \t]*\r?$') { throw 'Cargo.toml must declare MIT.' }
if ($tauriConfig.bundle.windows.nsis.installMode -ne 'perMachine') {
    throw 'The NSIS install mode must remain perMachine.'
}

$versions = [ordered]@{
    npm = [string]$package.version
    tauri = [string]$tauriConfig.version
    cargo = [regex]::Match($cargo, '(?m)^version[ \t]*=[ \t]*"([^"]+)"[ \t]*\r?$').Groups[1].Value
}

Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.csproj' -Recurse | ForEach-Object {
    [xml]$project = Get-Content -LiteralPath $_.FullName -Raw
    $licenseExpression = [string]$project.Project.PropertyGroup.PackageLicenseExpression
    if ($licenseExpression -ne 'MIT') { throw "$($_.Name) must declare MIT." }
    $versions[$_.BaseName] = [string]$project.Project.PropertyGroup.Version
}

$expectedVersion = $versions.npm
$mismatches = $versions.GetEnumerator() | Where-Object Value -ne $expectedVersion
if ($mismatches) {
    throw "Release versions are inconsistent: $($versions | ConvertTo-Json -Compress)"
}
if ($readme -notmatch [regex]::Escape("NeuroTune v$expectedVersion") -or
    $releaseNotes -notmatch [regex]::Escape("# NeuroTune v$expectedVersion") -or
    $app -notmatch [regex]::Escape("v$expectedVersion")) {
    throw "README, release notes, or UI version does not match v$expectedVersion."
}
if ($readme -match 'Windows 10 or Windows 11' -or $app -match 'Windows 10/11') {
    throw 'Active product requirements must remain Windows 11-only.'
}

Write-Host "Release metadata verified: v$expectedVersion, MIT, unsigned NSIS perMachine."
