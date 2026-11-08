@echo off
setlocal EnableExtensions DisableDelayedExpansion
pushd "%~dp0"
if errorlevel 1 (
    echo [ERROR] Cannot change to the SGO repository folder.
    exit /b 2
)

set "CLIENT=%CD%\client"
set "CONFIG_DIR=%LOCALAPPDATA%\SGO\GodotClientTests"
set "CONFIG_FILE=%CONFIG_DIR%\godot_path.txt"
set "DEFAULT_GODOT=C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe"
set "RUN_MODE=%~1"
set "EXIT_CODE=1"

if not exist "%CLIENT%\project.godot" (
    echo [ERROR] Project not found: "%CLIENT%\project.godot"
    echo Put this BAT in the root folder of the SGO Git project.
    goto :failed
)

if /I "%RUN_MODE%"=="configure" goto :configure
if not defined RUN_MODE goto :menu
if /I "%RUN_MODE%"=="all" goto :run
if /I "%RUN_MODE%"=="render" goto :run
if /I "%RUN_MODE%"=="focus" goto :run
if /I "%RUN_MODE%"=="resources" goto :run
if /I "%RUN_MODE%"=="import" goto :run
if /I "%RUN_MODE%"=="login" goto :run
if /I "%RUN_MODE%"=="perf-single" goto :perf_single
if /I "%RUN_MODE%"=="perf-multi" goto :perf_multi
if /I "%RUN_MODE%"=="perf-report" goto :perf_report
echo [ERROR] Unknown mode: %RUN_MODE%
echo Usage: run_godot_client_tests.bat [all^|render^|focus^|resources^|login^|import^|configure^|perf-single^|perf-multi^|perf-report]
goto :failed

:menu
cls
echo ================================================================
echo SGO - Godot CLIENT regression tests (separate from server tests)
echo ================================================================
echo  [1] Import + ALL available scene-mode client regression tests
echo  [2] Import + Multi-client rendering / AOI cache regression
echo  [3] Import + Background/focus performance regression
echo  [4] Import + Sprite/material/resource reuse regression
echo  [5] Import project only (refresh UID/class cache)
echo  [6] Login startup regression (no 18-frame hang)
echo  [7] REAL GAME: one-client CPU RAM GPU VRAM benchmark
echo  [8] REAL GAME: multi-client 1/5/10 benchmark
echo  [9] Generate HTML charts and compare previous runs
echo  [P] Set/change Godot executable path (saved for next time)
echo  [Q] Exit
echo.
choice /C 123456789PQ /N /M "Select [1-9,P,Q]: "
if errorlevel 11 goto :done
if errorlevel 10 goto :configure
if errorlevel 9 goto :perf_report
if errorlevel 8 goto :perf_multi
if errorlevel 7 goto :perf_single
if errorlevel 6 set "RUN_MODE=login" & goto :run
if errorlevel 5 set "RUN_MODE=import" & goto :run
if errorlevel 4 set "RUN_MODE=resources" & goto :run
if errorlevel 3 set "RUN_MODE=focus" & goto :run
if errorlevel 2 set "RUN_MODE=render" & goto :run
set "RUN_MODE=all"
goto :run

:configure
set "GODOT_EXE="
call :prompt_godot
if errorlevel 1 goto :failed
if "%~1"=="" goto :menu
set "EXIT_CODE=0"
goto :done

:ensure_godot
set "GODOT_EXE="
if exist "%CONFIG_FILE%" (
    set /p "GODOT_EXE=" < "%CONFIG_FILE%"
)
if defined GODOT_EXE if exist "%GODOT_EXE%" goto :select_console
if defined GODOT_EXE echo [WARN] Saved executable not found: "%GODOT_EXE%"
call :prompt_godot
if errorlevel 1 exit /b 1

:select_console
set "RUN_EXE=%GODOT_EXE%"
set "RUN_IS_GUI=1"
if /I "%RUN_EXE:~-12%"=="_console.exe" set "RUN_IS_GUI="
for %%F in ("%GODOT_EXE%") do set "CONSOLE_EXE=%%~dpnF_console.exe"
if exist "%CONSOLE_EXE%" (
    set "RUN_EXE=%CONSOLE_EXE%"
    set "RUN_IS_GUI="
)
echo [GODOT] "%RUN_EXE%"
exit /b 0

:prompt_godot
set "SUGGESTED_GODOT="
if exist "%DEFAULT_GODOT%" set "SUGGESTED_GODOT=%DEFAULT_GODOT%"
echo.
echo [CONFIG] Please enter the FULL path to Godot EXE.
echo          You may drag Godot_v4.7.2-stable_win64.exe into this window.
if defined SUGGESTED_GODOT echo          Press Enter to accept: "%SUGGESTED_GODOT%"
:ask_godot
set "ENTERED_GODOT="
set /p "ENTERED_GODOT=Godot EXE path: "
if not defined ENTERED_GODOT if defined SUGGESTED_GODOT set "ENTERED_GODOT=%SUGGESTED_GODOT%"
if not defined ENTERED_GODOT (
    echo [ERROR] No EXE path was entered.
    goto :ask_godot
)
set "ENTERED_GODOT=%ENTERED_GODOT:"=%"
if /I not "%ENTERED_GODOT:~-4%"==".exe" (
    echo [ERROR] Path must end with .exe
    goto :ask_godot
)
if not exist "%ENTERED_GODOT%" (
    echo [ERROR] File not found: "%ENTERED_GODOT%"
    goto :ask_godot
)
set "GODOT_EXE=%ENTERED_GODOT%"
if not exist "%CONFIG_DIR%" mkdir "%CONFIG_DIR%"
if errorlevel 1 exit /b 1
> "%CONFIG_FILE%" echo(%GODOT_EXE%
if errorlevel 1 exit /b 1
echo [SAVED] "%CONFIG_FILE%"
exit /b 0

:perf_single
set "PERF_SCENARIO=single"
goto :perf_collect

:perf_multi
set "PERF_SCENARIO=multi"
goto :perf_collect

:perf_collect
call :ensure_godot
if errorlevel 1 goto :failed
set "PERF_ROOT=%CD%\test-results\performance"
echo.
echo [PERFORMANCE] Real game sampling: %PERF_SCENARIO%
echo [INFO] 10-sec warmup + 60-sec measurement per stage.
echo [INFO] Close ONLY game windows started by the recorder if prompted.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0client\tests\performance\collect_metrics.ps1" -Scenario "%PERF_SCENARIO%" -GodotExe "%GODOT_EXE%" -ClientPath "%CLIENT%" -OutputRoot "%PERF_ROOT%"
if errorlevel 1 goto :failed
call :generate_perf_report
if errorlevel 1 goto :failed
goto :perf_success

:perf_report
set "PERF_ROOT=%CD%\test-results\performance"
call :generate_perf_report
if errorlevel 1 goto :failed
goto :perf_success

:generate_perf_report
if not exist "%PERF_ROOT%" (
    echo [ERROR] No performance sessions yet. Run mode 7 or 8 first.
    exit /b 2
)
py -3 --version >nul 2>nul
if not errorlevel 1 goto :generate_perf_with_py
python --version >nul 2>nul
if not errorlevel 1 goto :generate_perf_with_python
echo [ERROR] Python 3 is required to generate offline charts. Raw CSV is retained.
exit /b 2

:generate_perf_with_py
py -3 "%~dp0client\tests\performance\generate_report.py" --root "%PERF_ROOT%"
exit /b %errorlevel%

:generate_perf_with_python
python "%~dp0client\tests\performance\generate_report.py" --root "%PERF_ROOT%"
exit /b %errorlevel%

:perf_success
if not exist "%PERF_ROOT%\report.html" goto :failed
echo.
echo ================================================================
echo [SUCCESS] SGO Performance HTML and CSV generated
echo [REPORT] "%PERF_ROOT%\report.html"
echo [SUMMARY] "%PERF_ROOT%\summary.csv"
echo ================================================================
start "" "%PERF_ROOT%\report.html"
set "EXIT_CODE=0"
goto :done

:run
call :ensure_godot
if errorlevel 1 goto :failed
set "TIMESTAMP="
for /f %%T in ('powershell.exe -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set "TIMESTAMP=%%T"
if not defined TIMESTAMP set "TIMESTAMP=manual"
set "LOG_DIR=%CD%\test-results\godot-client-%TIMESTAMP%-%RANDOM%"
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"
if errorlevel 1 goto :failed
echo.
echo [RUN] Mode: %RUN_MODE%
echo [LOG] "%LOG_DIR%"
echo [INFO] This checks Godot client tests only. It does not start Server.
echo.
echo [1/2] Importing project resources and class/UID cache...
call :execute --headless --path "%CLIENT%" --editor --import --log-file "%LOG_DIR%\import.log"
if errorlevel 1 (
    echo [FAILED] Godot project import failed. See import.log.
    goto :failed
)
if /I "%RUN_MODE%"=="import" goto :success

echo.
echo [2/2] Running tests using scene mode (autoload enabled)...
if /I "%RUN_MODE%"=="all" (
    call :run_test login_visual_readiness_regression LOGIN_VISUAL_READY_REGRESSION
    if errorlevel 1 goto :failed
    call :run_test world_loading_render_regression WORLD_RENDER_REGRESSION
    if errorlevel 1 goto :failed
    call :run_test multiclient_render_dedup_regression RENDER_DEDUP_REGRESSION
    if errorlevel 1 goto :failed
    call :run_test focus_performance_regression FOCUS_PERF_REGRESSION
    if errorlevel 1 goto :failed
    call :run_test render_resource_regression RENDER_RESOURCE_REGRESSION
    if errorlevel 1 goto :failed
)
if /I "%RUN_MODE%"=="render" (
    call :run_test multiclient_render_dedup_regression RENDER_DEDUP_REGRESSION
    if errorlevel 1 goto :failed
)
if /I "%RUN_MODE%"=="focus" (
    call :run_test focus_performance_regression FOCUS_PERF_REGRESSION
    if errorlevel 1 goto :failed
    call :run_test world_loading_render_regression WORLD_RENDER_REGRESSION
    if errorlevel 1 goto :failed
)
if /I "%RUN_MODE%"=="resources" (
    call :run_test render_resource_regression RENDER_RESOURCE_REGRESSION
    if errorlevel 1 goto :failed
)
if /I "%RUN_MODE%"=="login" (
    call :run_test login_visual_readiness_regression LOGIN_VISUAL_READY_REGRESSION
    if errorlevel 1 goto :failed
)
goto :success

:run_test
set "TEST_NAME=%~1"
set "PASS_TAG=%~2"
echo.
echo [TEST] %TEST_NAME%
call :execute --headless --path "%CLIENT%" --scene "res://tests/%TEST_NAME%.tscn" --quit-after 1200 --log-file "%LOG_DIR%\%TEST_NAME%.log"
if errorlevel 1 (
    echo [FAILED] Godot returned an error for %TEST_NAME%.
    echo See "%LOG_DIR%\%TEST_NAME%.log"
    exit /b 1
)
if not exist "%LOG_DIR%\%TEST_NAME%.log" (
    echo [FAILED] No test log was created: %TEST_NAME%
    exit /b 1
)
findstr /L /C:"[%PASS_TAG%] ALL PASS" "%LOG_DIR%\%TEST_NAME%.log" >nul
if errorlevel 1 (
    echo [FAILED] Test did not report ALL PASS: %TEST_NAME%
    echo Recent log output:
    powershell.exe -NoProfile -Command "Get-Content -LiteralPath '%LOG_DIR%\%TEST_NAME%.log' -Tail 30"
    exit /b 1
)
findstr /L /C:"SCRIPT ERROR:" /C:"ERROR: Failed to load script" "%LOG_DIR%\%TEST_NAME%.log" >nul
if not errorlevel 1 (
    echo [FAILED] Godot script compilation errors were found: %TEST_NAME%
    exit /b 1
)
echo [PASS] %TEST_NAME%
exit /b 0

:execute
if defined RUN_IS_GUI (
    start "" /wait "%RUN_EXE%" %*
) else (
    "%RUN_EXE%" %*
)
exit /b %errorlevel%

:success
echo.
echo ================================================================
echo [SUCCESS] Requested Godot client tests passed: %RUN_MODE%
echo [LOGS] "%LOG_DIR%"
echo ================================================================
set "EXIT_CODE=0"
goto :done

:failed
echo.
echo ================================================================
echo [FAILED] Client test could not finish successfully.
if defined LOG_DIR echo [LOGS] "%LOG_DIR%"
echo ================================================================
set "EXIT_CODE=1"

:done
if "%~1"=="" pause
popd
exit /b %EXIT_CODE%
