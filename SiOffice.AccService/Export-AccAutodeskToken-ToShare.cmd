@echo off
setlocal
title SiNet - Export Acc Autodesk Token
pushd "%~dp0"
if errorlevel 1 (
  echo ERROR: Cannot access %~dp0
  echo CMD cannot use UNC as a working directory without pushd.
  pause
  exit /b 1
)
echo ================================================================
echo   Export Autodesk token (workstation)
echo ================================================================
echo Folder:
echo   %CD%
echo.
echo IMPORTANT: use this .cmd file (not the .ps1).
echo The window will stay open until you press a key.
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Export-AccAutodeskToken-ToShare.ps1" %*
set ERR=%ERRORLEVEL%
echo.
echo ================================================================
echo Exit code: %ERR%
echo ================================================================
echo.
pause
popd
endlocal & exit /b %ERR%
