@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Collect-NeuroTune-HardwareReport.ps1"
if errorlevel 1 (
  echo.
  echo Collection failed. No system changes were made.
)
echo.
pause
exit /b %errorlevel%
