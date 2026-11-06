@echo off
rem Runs runtime-selftest.ps1 - log: tools\logs\runtime-selftest.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0runtime-selftest.ps1"
