@echo off
setlocal
cd /d "%~dp0"
py -3 -m unittest discover -s tests -v
if errorlevel 1 python -m unittest discover -s tests -v
endlocal
