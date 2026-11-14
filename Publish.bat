@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish.ps1"
set "publishExitCode=%ERRORLEVEL%"
echo.
if not "%publishExitCode%"=="0" echo XtraSearch publication failed.
pause
exit /b %publishExitCode%
