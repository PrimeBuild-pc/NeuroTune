#requires -Version 5.1
<#
.SYNOPSIS
  Installs, starts, stops and checks the local CLM (Contrastive Language Model) stack NeuroTune's optional
  System One assistant talks to. Everything binds to 127.0.0.1; nothing is sent to a cloud service.

.DESCRIPTION
  Components (all pinned by SHA-256 or commit):
    * llama.cpp Vulkan runtime  -> serves Qwen3-8B (Q8_0 GGUF) as a last-token embedding endpoint on :8090
    * CLM Python package        -> clm-serve on :8700 with the reference projection head
  vLLM, which the upstream package declares, is not available on Windows and is not needed: any
  OpenAI-compatible /v1/embeddings server can be the encoder.

  Downloads about 9 GB (encoder) plus ~0.3 GB of Python packages and a 75 MB head. Requires uv
  (https://docs.astral.sh/uv/). The 8B Q8 encoder needs roughly 9-10 GB of free VRAM, or system RAM when run with -Cpu.

.EXAMPLE
  ./scripts/clm-stack.ps1 install
  ./scripts/clm-stack.ps1 start
  ./scripts/clm-stack.ps1 status
  ./scripts/clm-stack.ps1 stop
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory)][ValidateSet('install', 'start', 'stop', 'status')][string]$Action,
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'NeuroTune\clm'),
    [switch]$Cpu
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Pins. Changing any of them is a deliberate, reviewed act.
$RuntimeUrl = 'https://github.com/ggml-org/llama.cpp/releases/download/b11081/llama-b11081-bin-win-vulkan-x64.zip'
$RuntimeHash = '4259a1dda3ef3fcfd8b007a16329d5bdcef07da8f5f95fddd85ff2954263f01a'
$EncoderUrl = 'https://huggingface.co/Qwen/Qwen3-8B-GGUF/resolve/main/Qwen3-8B-Q8_0.gguf'
$EncoderHash = '408b955510e196121c1c375201744783b5c9a43c7956d73fc78df54c66e883d6'
$HeadUrl = 'https://huggingface.co/Contrastive-LM/CLM-v0.1-8B/resolve/main/CLM_v0.1-8B.pt'
$HeadHash = 'b2b4a8c9c2d39263eff78a351eb909a342ce9b3bf21a3f07c1d1bf15f1c4eda5' # a .pt is a pickle: never load an unverified one.
$ClmCommit = 'd5f9ef0fd9bde185df0ceaad4f4ecc6cfe8c34f6'
$EncoderPort = 8090
$ClmPort = 8700

$runtimeDir = Join-Path $Root 'runtime'
$encoder = Join-Path $Root 'models\Qwen3-8B-Q8_0.gguf'
$head = Join-Path $Root 'heads\CLM_v0.1-8B.pt'
$python = Join-Path $Root 'venv\Scripts\python.exe'
$pidFile = Join-Path $Root 'pids.json'

function Get-Sha256([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

function Save-Verified([string]$Url, [string]$Destination, [string]$Hash) {
    New-Item -ItemType Directory -Force -Path (Split-Path $Destination) | Out-Null
    if ((Test-Path $Destination) -and (Get-Sha256 $Destination) -eq $Hash) { return }
    Write-Host "Downloading $(Split-Path $Destination -Leaf)..."
    & curl.exe -fL -C - --retry 3 -o $Destination $Url
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $Url" }
    if ((Get-Sha256 $Destination) -ne $Hash) { Remove-Item $Destination -Force; throw "Checksum mismatch for $Destination; the file was removed." }
}

function Test-Port([int]$Port) {
    [bool](Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

function Wait-Url([string]$Url, [int]$Seconds) {
    for ($i = 0; $i -lt $Seconds; $i++) {
        try { Invoke-RestMethod -Uri $Url -TimeoutSec 2 | Out-Null; return $true } catch { Start-Sleep 1 }
    }
    $false
}

function Install-Stack {
    if (-not (Get-Command uv -ErrorAction SilentlyContinue)) { throw 'uv is required: https://docs.astral.sh/uv/getting-started/installation/' }
    New-Item -ItemType Directory -Force -Path $Root | Out-Null
    $zip = Join-Path $Root 'runtime.zip'
    Save-Verified $RuntimeUrl $zip $RuntimeHash
    if (-not (Test-Path (Join-Path $runtimeDir 'llama-server.exe'))) { Expand-Archive -Force -LiteralPath $zip -DestinationPath $runtimeDir }
    Save-Verified $EncoderUrl $encoder $EncoderHash
    Save-Verified $HeadUrl $head $HeadHash
    if (-not (Test-Path $python)) { & uv venv --python 3.12 (Join-Path $Root 'venv') }
    $requirements = Join-Path $PSScriptRoot 'clm-requirements.txt'
    & uv pip install --python $python --requirement $requirements --extra-index-url https://download.pytorch.org/whl/cpu
    if ($LASTEXITCODE -ne 0) { throw 'Python dependency installation failed.' }
    & uv pip install --python $python --no-deps "https://github.com/Contrastive-LM/CLM/archive/$ClmCommit.zip"
    if ($LASTEXITCODE -ne 0) { throw 'CLM installation failed.' }
    Write-Host "Installed in $Root. Run: scripts/clm-stack.ps1 start"
}

function Start-Stack {
    foreach ($path in @((Join-Path $runtimeDir 'llama-server.exe'), $encoder, $head, $python)) {
        if (-not (Test-Path $path)) { throw "Missing $path. Run: scripts/clm-stack.ps1 install" }
    }
    $pids = @{}
    if (-not (Test-Port $EncoderPort)) {
        $gpu = if ($Cpu) { '0' } else { '99' }
        $p = Start-Process -FilePath (Join-Path $runtimeDir 'llama-server.exe') -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $Root 'encoder.out.log') -RedirectStandardError (Join-Path $Root 'encoder.err.log') `
            -ArgumentList '-m', $encoder, '--embedding', '--pooling', 'last', '-c', '2048', '-b', '2048', '-ub', '2048',
                '--host', '127.0.0.1', '--port', $EncoderPort, '-ngl', $gpu, '--alias', 'qwen3-8b'
        $pids.encoder = $p.Id
        if (-not (Wait-Url "http://127.0.0.1:$EncoderPort/health" 180)) { throw "The encoder did not become ready; see $Root\encoder.err.log" }
    }
    if (-not (Test-Port $ClmPort)) {
        $p = Start-Process -FilePath (Join-Path $Root 'venv\Scripts\clm-serve.exe') -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $Root 'clm.out.log') -RedirectStandardError (Join-Path $Root 'clm.err.log') `
            -ArgumentList '--host', '127.0.0.1', '--port', $ClmPort, '--emb-url', "http://127.0.0.1:$EncoderPort/v1/embeddings",
                '--emb-model', 'qwen3-8b', '--ckpt', $head, '--no-download', '--no-ui'
        $pids.clm = $p.Id
        if (-not (Wait-Url "http://127.0.0.1:$ClmPort/health" 60)) { throw "clm-serve did not become ready; see $Root\clm.err.log" }
    }
    if ($pids.Count -gt 0) { $pids | ConvertTo-Json | Set-Content -LiteralPath $pidFile }
    Show-Status
}

function Stop-Stack {
    if (Test-Path $pidFile) {
        $pids = Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json
        foreach ($id in @($pids.clm, $pids.encoder)) {
            # clm-serve is a launcher around python.exe: end the whole tree, not just the launcher.
            if ($id) { & taskkill.exe /PID $id /T /F 2>&1 | Out-Null }
        }
        Remove-Item -LiteralPath $pidFile -Force
    }
    Show-Status
}

function Show-Status {
    $health = $null
    try { $health = Invoke-RestMethod -Uri "http://127.0.0.1:$ClmPort/health" -TimeoutSec 2 } catch { }
    [pscustomobject]@{
        Encoder = if (Test-Port $EncoderPort) { "listening on 127.0.0.1:$EncoderPort" } else { 'stopped' }
        CLM     = if ($health) { "ready on 127.0.0.1:$ClmPort (encoder reachable: $($health.embedder))" } else { 'stopped' }
        Root    = $Root
    } | Format-List
}

switch ($Action) {
    'install' { Install-Stack }
    'start'   { Start-Stack }
    'stop'    { Stop-Stack }
    'status'  { Show-Status }
}
