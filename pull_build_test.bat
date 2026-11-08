@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo ============================================================
echo SGO - Pull / Build / Full Smoke Test
echo ============================================================
echo.

echo [1/5] Pull latest branch...
git pull --ff-only
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :fail

echo.
echo [2/5] Build server...
dotnet build ".\server\SGO.Server.csproj"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :fail

echo.
echo [3/5] Build smoke tests...
dotnet build ".\tests\SGO.Server.SmokeTests\SGO.Server.SmokeTests.csproj"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :fail

echo.
echo [4/5] Run full smoke suite...
dotnet run --no-build --project ".\tests\SGO.Server.SmokeTests\SGO.Server.SmokeTests.csproj" -- --suite all
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :fail

echo.
echo [5/5] Run Python regression guards...
if exist ".\tests\test_admin_ui_structure.py" (
    python ".\tests\test_admin_ui_structure.py"
    set "RC=%ERRORLEVEL%"
    if not "%RC%"=="0" goto :fail
)
if exist ".\tests\test_global_rate_contract.py" (
    python ".\tests\test_global_rate_contract.py"
    set "RC=%ERRORLEVEL%"
    if not "%RC%"=="0" goto :fail
)

echo.
echo ============================================================
echo ALL BUILD AND TEST STEPS PASSED
echo ============================================================
pause
exit /b 0

:fail
if "%RC%"=="" set "RC=1"
if "%RC%"=="0" set "RC=1"
echo.
echo ============================================================
echo FAILED - exit code %RC%
echo Check the error output above.
echo ============================================================
pause
exit /b %RC%
