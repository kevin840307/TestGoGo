@echo off
setlocal
cd /d "%~dp0"
set "ROOT=%~dp0.."

if defined GODOT (
  set "GODOT_CMD=%GODOT%"
) else (
  where godot4 >nul 2>nul
  if not errorlevel 1 set "GODOT_CMD=godot4"
  if not defined GODOT_CMD (
    where godot >nul 2>nul
    if not errorlevel 1 set "GODOT_CMD=godot"
  )
)

if not defined GODOT_CMD (
  echo [ERROR] 找不到 Godot。請把 godot.exe 加到 PATH，或設定 GODOT=C:\path\to\Godot_v4.x.exe
  exit /b 1
)

echo Exporting real MapXX.scn previews...
echo Output: %ROOT%\game-editor\.generated\map-previews
"%GODOT_CMD%" --path "%ROOT%\client" --rendering-method gl_compatibility --audio-driver Dummy --script res://tools/export_map_previews.gd -- --output="%ROOT%\game-editor\.generated\map-previews" %*
if errorlevel 1 exit /b %errorlevel%

echo PASS: map previews exported.
endlocal
