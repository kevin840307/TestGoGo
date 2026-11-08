@echo off
setlocal EnableExtensions
pushd "%~dp0.."
if errorlevel 1 (
    echo [ERROR] Could not enter repository root.
    exit /b 1
)

set "MODE=%~1"
if "%MODE%"=="" set "MODE=all"
if /I "%MODE%"=="pull" goto :pullonly
if /I "%MODE%"=="pull-all" goto :pullall
if /I "%MODE%"=="all" goto :all
if /I "%MODE%"=="world" goto :world
if /I "%MODE%"=="concurrency" goto :concurrency
if /I "%MODE%"=="load" goto :load

echo SGO local validation - feature/local-server-net8
echo.
echo Usage:
echo   tests\run_local_verification.bat pull
echo   tests\run_local_verification.bat all
echo   tests\run_local_verification.bat pull-all
echo   tests\run_local_verification.bat world
echo   tests\run_local_verification.bat concurrency
echo   tests\run_local_verification.bat load 10,50,100 60
echo   tests\run_local_verification.bat load 100,500,1000 600
echo.
echo All runs Python audits, .NET build/smoke, AES WebSocket, and Godot
echo headless regressions when GODOT_BIN or godot/godot4 is available.
echo Load needs a separately started throwaway test server on port 5678.
echo Never run the load probe against a production database.
goto :done

:pullonly
call :update
if errorlevel 1 goto :failed
goto :done

:pullall
call :update
if errorlevel 1 goto :failed
goto :all

:all
where python >nul 2>&1
if errorlevel 1 (
    echo [ERROR] python not found on PATH.
    goto :failed
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet SDK 8 not found on PATH.
    goto :failed
)
echo [RUN] Complete SGO regression checks.
powershell -NoProfile -ExecutionPolicy Bypass -File "tests\run_protocol_checks.ps1"
if errorlevel 1 goto :failed
goto :done

:world
where dotnet >nul 2>&1
if errorlevel 1 goto :failed
dotnet run --project "tests\SGO.Server.SmokeTests\SGO.Server.SmokeTests.csproj" -c Release -- --suite world
if errorlevel 1 goto :failed
goto :done

:concurrency
where dotnet >nul 2>&1
if errorlevel 1 goto :failed
dotnet run --project "tests\SGO.Server.SmokeTests\SGO.Server.SmokeTests.csproj" -c Release -- --suite concurrency
if errorlevel 1 goto :failed
goto :done

:load
where dotnet >nul 2>&1
if errorlevel 1 goto :failed
set "SIZES=%~2"
if "%SIZES%"=="" set "SIZES=10,50,100"
set "SOAK=%~3"
if "%SOAK%"=="" set "SOAK=60"
set "REPORT=%TEMP%\sgo-wire-load-%RANDOM%-%RANDOM%.json"
echo [WARNING] Ensure port 5678 is an isolated SGO test server and DB.
echo [RUN] Real encrypted WebSockets: players=%SIZES%, mixed_seconds=%SOAK%, move_pixels=4
dotnet run --project "tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj" -c Release -- --url "ws://127.0.0.1:5678/" --sizes "%SIZES%" --rounds 2 --setup-concurrency 10 --mixed-seconds "%SOAK%" --move-pixels 4 --timeout-seconds 30 --output "%REPORT%"
if errorlevel 1 goto :failed
echo [PASS] Probe report: "%REPORT%"
goto :done

:update
for /f "delims=" %%B in ('git branch --show-current') do set "CURRENT_BRANCH=%%B"
if /I not "%CURRENT_BRANCH%"=="feature/local-server-net8" (
    echo [ERROR] Current branch is not feature/local-server-net8.
    echo Switch branches manually and ensure no uncommitted changes before pulling.
    exit /b 1
)
echo [RUN] Fetch and fast-forward ONLY (never reset or force).
git fetch origin feature/local-server-net8
if errorlevel 1 exit /b 1
git pull --ff-only origin feature/local-server-net8
exit /b %ERRORLEVEL%

:failed
echo [FAIL] Validation stopped at first failure.
popd
exit /b 1

:done
popd
exit /b 0
