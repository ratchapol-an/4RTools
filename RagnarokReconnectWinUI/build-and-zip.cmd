@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-and-zip.ps1"
set ERR=%ERRORLEVEL%
echo.
if not %ERR%==0 (
  echo Build failed with code %ERR%.
) else (
  echo You can share the zip under RagnarokReconnectWinUI\dist\
)
pause
exit /b %ERR%
