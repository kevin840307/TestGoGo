param(
    [string]$GodotBin = $env:GODOT_BIN,
    [string]$Maps = ""
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($GodotBin)) {
    $cmd = Get-Command godot -ErrorAction SilentlyContinue
    if ($null -eq $cmd) { $cmd = Get-Command godot4 -ErrorAction SilentlyContinue }
    if ($null -eq $cmd) { throw "找不到 Godot。請設定 GODOT_BIN 或用 -GodotBin 指定 Godot 4.7.2。" }
    $GodotBin = $cmd.Source
}
if (-not (Test-Path $GodotBin)) { throw "Godot 不存在: $GodotBin" }
$args = @("--headless", "--path", (Join-Path $root "client"), "--script", "res://tools/build_map_visual_aoi_base.gd")
if (-not [string]::IsNullOrWhiteSpace($Maps)) {
    $args += "--"
    $args += "--map=$Maps"
}
& $GodotBin @args
if ($LASTEXITCODE -ne 0) { throw "Map AOI base generation failed: $LASTEXITCODE" }
Write-Host "[MAP_AOI_BUILD] 完成；遊戲會優先使用 client/maps/streamed/MapXX_base.scn" -ForegroundColor Green
