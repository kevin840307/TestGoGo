@echo off
setlocal
cd /d "%~dp0"

echo [1/3] Python editor tests
py -3 -m unittest discover -s tests -v
if errorlevel 1 (
  python -m unittest discover -s tests -v
  if errorlevel 1 exit /b 1
)

where node >nul 2>nul
if errorlevel 1 (
  echo [WARN] Node.js not found. Browser JS syntax/math checks skipped locally.
  echo        CI will still run them.
  exit /b 0
)

echo [2/3] Browser JavaScript syntax
for %%F in (web\app-core.js web\app-health.js web\app-skills.js web\app-world.js web\app-map-math.js web\app-map-editor.js web\app-history.js web\app-tools.js) do (
  node --check "%%F"
  if errorlevel 1 exit /b 1
)

echo [3/3] Map coordinate math contract
node tests\test_map_math.js
if errorlevel 1 exit /b 1

echo PASS: game-editor test suite
endlocal
