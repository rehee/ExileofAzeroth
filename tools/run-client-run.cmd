@echo off
rem Runs client-run.ps1 - log: tools\logs\client-run.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0client-run.ps1"
