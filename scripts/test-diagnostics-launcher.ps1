[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ("NeuroTune & friend's launcher test " + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$launcher = Join-Path $root 'Raccogli-log-NeuroTune.bat'
$collector = Join-Path $root 'Collect-NeuroTune-Diagnostics.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Raccogli-log-NeuroTune.bat') -Destination $launcher

function Invoke-Launcher {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $env:SystemRoot 'System32\cmd.exe'
    $start.Arguments = '/d /s /c ""' + $launcher + '" --elevated <nul"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'Launcher did not finish.' }
        return @{ ExitCode = $process.ExitCode; Output = $output.GetAwaiter().GetResult() + $errors.GetAwaiter().GetResult() }
    }
    finally { $process.Dispose() }
}

try {
    # Exercise the post-UAC path with a fixture, never real journals or an elevation prompt.
    "param([string]`$InstallDirectory); Write-Output ('FIXTURE_OK|' + `$InstallDirectory)" | Set-Content -LiteralPath $collector
    $result = Invoke-Launcher
    if ($result.ExitCode -ne 0 -or $result.Output -notmatch 'FIXTURE_OK' -or $result.Output -notmatch 'sul Desktop') { throw 'Installer launcher failed.' }

    [IO.File]::WriteAllText((Join-Path $root 'NeuroTune.exe'), 'fixture-not-executable')
    $result = Invoke-Launcher
    if ($result.ExitCode -ne 0 -or $result.Output -notmatch ([regex]::Escape($root))) { throw 'Portable launcher path was not forwarded.' }

    "throw 'fixture collector failure'" | Set-Content -LiteralPath $collector
    $result = Invoke-Launcher
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'Raccolta non completata') { throw 'Collector failure was hidden.' }

    Remove-Item -LiteralPath $collector
    $result = Invoke-Launcher
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'estrai TUTTO lo ZIP') { throw 'Missing companion file was not explained.' }
    Write-Host 'Launcher tests passed: installer, portable, quoted paths, collector failure and incomplete extraction. UAC prompt not automated.'
}
finally { Remove-Item -LiteralPath $root -Recurse -Force }
