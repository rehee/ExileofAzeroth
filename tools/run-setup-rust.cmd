@echo off
rem Runs setup-rust.ps1 - log: tools\logs\setup-rust.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-rust.ps1"
