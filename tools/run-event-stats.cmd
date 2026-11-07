@echo off
rem Runs event-stats.ps1 - log: tools\logs\event-stats.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0event-stats.ps1"
