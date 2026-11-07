@echo off
rem Runs mapping-report.ps1 - log: tools\logs\mapping-report.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0mapping-report.ps1"
