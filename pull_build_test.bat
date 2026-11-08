@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
title SGO - Pull / Build / Tests / AOI Load
set "RUNNER=%~dp0tools\sgo_test_runner.ps1"

if not exist "%RUNNER%" (
    echo [ERROR] Missing tools\sgo_test_runner.ps1
    echo Please use the latest feature/local-server-net8 branch with BOTH files.
    pause
    exit /b 2
)

where powershell.exe >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Windows PowerShell not found.
    pause
    exit /b 2
)

set "MODE="
if /I "%~1"=="full"      set "MODE=Full"
if /I "%~1"=="aoi"       set "MODE=Aoi"
if /I "%~1"=="wire100"   set "MODE=Wire100"
if /I "%~1"=="wireall"   set "MODE=WireAll"
if /I "%~1"=="build"     set "MODE=BuildOnly"
if /I "%~1"=="buildonly" set "MODE=BuildOnly"

if not "%~1"=="" (
    if not defined MODE (
        echo [ERROR] Unknown mode: %~1
        echo Usage: pull_build_test.bat [full^|aoi^|wire100^|wireall^|build]
        exit /b 2
    )
    goto :run
)

:menu
cls
echo ================================================================
echo SGO - Pull / Build / Tests / AOI Performance
echo Branch: feature/local-server-net8
echo ================================================================
echo.
echo  [1] Pull + Release Build + FULL Smoke Tests + Python checks
echo  [2] Option 1 + Moving AOI benchmark (100, 250, 500, 1000)
echo  [3] Option 2 + REAL WebSocket 100 players on isolated test DB
echo  [4] Option 2 + REAL WebSocket 100/250/500/1000 (HEAVY)
echo  [5] Pull + Release Build only
echo  [Q] Exit
echo.
echo Safety: never force-pulls, never resets your changes,
echo         never uses the existing game.db in load mode.
echo Reports: test-results\YYYYMMDD-HHMMSS-xxxxxx\
echo.
choice /C 12345Q /N /M "Select [1-5,Q]: "
if errorlevel 6 exit /b 0
if errorlevel 5 set "MODE=BuildOnly"
if errorlevel 4 if not defined MODE set "MODE=WireAll"
if errorlevel 3 if not defined MODE set "MODE=Wire100"
if errorlevel 2 if not defined MODE set "MODE=Aoi"
if errorlevel 1 if not defined MODE set "MODE=Full"

:run
echo.
echo [START] Mode=%MODE%
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%RUNNER%" -Mode "%MODE%"
set "RESULT=%ERRORLEVEL%"
echo.
if "%RESULT%"=="0" (
    echo ================================================================
    echo SUCCESS. Open the latest directory under test-results for logs.
    echo ================================================================
) else (
    echo ================================================================
    echo FAILED with exit code %RESULT%.
    echo See the latest test-results\...\run.log for the failed step.
    echo ================================================================
)
echo.
if "%~1"=="" pause
exit /b %RESULT%
