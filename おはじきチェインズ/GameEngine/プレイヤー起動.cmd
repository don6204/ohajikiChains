@echo off
cd /d "%~dp0"
if not exist "bin\OhajikiEngine.exe" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "Tools\Build.ps1"
  if errorlevel 1 (pause & exit /b 1)
)
start "" "bin\OhajikiEngine.exe" --player
