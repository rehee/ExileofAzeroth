@echo off
rem Runs update-dqq.ps1 - log: tools\logs\update-dqq.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-dqq.ps1"
