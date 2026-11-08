@echo off
setlocal
cd /d "%~dp0"

set "RUNNER=%~dp0tools\sgo_test_runner.ps1"
if not exist "%RUNNER%" (
  echo [ERROR] Missing tools\sgo_test_runner.ps1
  pause
  exit /b 2
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%RUNNER%" %*
set "RC=%ERRORLEVEL%"

echo.
if not "%RC%"=="0" (
  echo FAILED - exit code %RC%
) else (
  echo SUCCESS
)

pause
exit /b %RC%
