@echo off
rem Double-click to install or update ScreenQuill from its latest GitHub release (no admin rights needed).
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/Fleekshot/ScreenQuill/main/installer/install.ps1 | iex"
if errorlevel 1 (
  echo.
  echo Installation failed.
)
pause
