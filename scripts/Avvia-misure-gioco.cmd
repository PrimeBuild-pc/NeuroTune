@echo off
title NeuroTune - Misure di gioco
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0physical-gpu-measurement.ps1"
if errorlevel 1 echo La raccolta non e stata completata. Leggi il messaggio sopra. Avvia questo file come amministratore.
pause
