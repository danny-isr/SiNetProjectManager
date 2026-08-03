@echo off
setlocal
title SiNet - Export Acc Autodesk Token
cd /d "%~dp0"
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
endlocal & exit /b %ERR%
