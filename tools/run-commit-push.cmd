@echo off
rem Runs commit-push.ps1 - log: tools\logs\commit-push.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0commit-push.ps1"
