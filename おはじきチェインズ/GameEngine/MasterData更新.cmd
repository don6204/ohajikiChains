@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "Tools\RefreshMaster.ps1"
pause
