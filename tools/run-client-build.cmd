@echo off
rem Runs client-build.ps1 - log: tools\logs\client-build.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0client-build.ps1"
