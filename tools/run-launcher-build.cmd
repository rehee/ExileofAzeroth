@echo off
rem Runs launcher-build.ps1 - log: tools\logs\launcher-build.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0launcher-build.ps1"
