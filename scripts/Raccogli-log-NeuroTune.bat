@echo off
setlocal
set "NEUROTUNE_DIAGNOSTICS_LAUNCHER=%~f0"
set "NEUROTUNE_DIAGNOSTICS_SCRIPT=%~dp0Collect-NeuroTune-Diagnostics.ps1"

if not exist "%NEUROTUNE_DIAGNOSTICS_SCRIPT%" (
    echo Prima estrai TUTTO lo ZIP. Lascia il BAT e il PS1 nella stessa cartella.
    pause
    exit /b 1
)

if /i "%~1"=="--elevated" goto collect

echo Richiesta del permesso amministratore per leggere i log di NeuroTune...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command "try { $q=[char]34; $arguments='/d /s /c '+$q+$q+$env:NEUROTUNE_DIAGNOSTICS_LAUNCHER+$q+' --elevated'+$q; Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\cmd.exe') -ArgumentList $arguments -Verb RunAs -ErrorAction Stop | Out-Null } catch { Write-Host 'Avvio annullato o non consentito. Nessuna impostazione e stata modificata.'; exit 1 }"
if errorlevel 1 (
    pause
    exit /b 1
)
exit /b 0

:collect
echo Raccolta diagnostica. Nessuna analisi AI e nessuna modifica a Windows.
if exist "%~dp0NeuroTune.exe" (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%NEUROTUNE_DIAGNOSTICS_SCRIPT%" -InstallDirectory "%~dp0."
) else (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%NEUROTUNE_DIAGNOSTICS_SCRIPT%"
)
set "COLLECTOR_EXIT=%errorlevel%"
echo.
if "%COLLECTOR_EXIT%"=="0" (
    echo Trovi il file NeuroTune-diagnostics-*.json sul Desktop.
    echo Controllalo prima di inviarlo, insieme allo screenshot dell'errore.
) else (
    echo Raccolta non completata. Invia uno screenshot di questa finestra, senza chiavi API.
)
echo.
pause
exit /b %COLLECTOR_EXIT%
