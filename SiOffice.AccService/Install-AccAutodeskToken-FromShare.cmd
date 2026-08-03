@echo off
setlocal
cd /d "%~dp0"
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Requesting Administrator elevation...
  powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
echo Install Autodesk token from Server drop:
echo   %CD%
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-AccAutodeskToken-FromShare.ps1" %*
set ERR=%ERRORLEVEL%
echo.
echo Exit code: %ERR%
pause
exit /b %ERR%
