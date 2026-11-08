@echo off
setlocal
cd /d "%~dp0"
py -3 editor.py --project-root "%~dp0.." %*
if errorlevel 1 python editor.py --project-root "%~dp0.." %*
endlocal
