@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "Tools\Build.ps1"
pause
