@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 (
  echo ERROR: Cannot access %~dp0
  echo CMD cannot use UNC as a working directory without pushd.
  pause
  exit /b 1
)
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Requesting Administrator elevation...
  powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  popd
  exit /b
)
echo Install Autodesk token from Server drop:
echo   %CD%
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-AccAutodeskToken-FromShare.ps1" %*
set ERR=%ERRORLEVEL%
echo.
echo Exit code: %ERR%
pause
popd
exit /b %ERR%
