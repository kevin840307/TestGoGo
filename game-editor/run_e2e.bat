@echo off
setlocal
cd /d "%~dp0"

where npm >nul 2>nul
if errorlevel 1 (
  echo [ERROR] 找不到 npm / Node.js。
  exit /b 1
)

echo [1/3] Installing pinned Playwright dependency...
call npm install --no-audit --no-fund
if errorlevel 1 exit /b 1

echo [2/3] Ensuring Chromium is installed...
call npx playwright install chromium
if errorlevel 1 exit /b 1

echo [3/3] Running real browser E2E...
call npm run test:e2e
if errorlevel 1 exit /b 1

echo PASS: browser E2E
endlocal
