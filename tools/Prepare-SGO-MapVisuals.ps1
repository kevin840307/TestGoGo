# One-time, offline optimized static map bases from locally recovered MapXX.scn.
# Source .scn and large textures are excluded from Git and cannot be made in CI.
[CmdletBinding()]
param([string]$GodotPath = '', [int[]]$MapIds = @(13, 15))
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$clientRoot = Join-Path $repoRoot 'client'
if (-not $GodotPath) {
    foreach ($candidate in @('godot','godot4','Godot_v4.7.2-stable_win64_console','Godot_v4.7.2-stable_win64')) {
        $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($cmd) { $GodotPath = $cmd.Source; break }
    }
}
if (-not $GodotPath) { throw 'Godot CLI not found; pass -GodotPath with the Godot executable path.' }
$mapIds = @($MapIds | Sort-Object -Unique)
foreach ($mapId in $mapIds) {
    if ($mapId -le 0) { throw 'MapIds must be positive' }
    $source = Join-Path $clientRoot ('maps/Map{0:D2}.scn' -f $mapId)
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Missing local map source $source. Recover or generate the map first."
    }
}
$mapArg = '--map=' + ($mapIds -join ',')
Write-Host "[MAP_AOI_PREPARE] maps=$($mapIds -join ',') godot=$GodotPath"
& $GodotPath --headless --path $clientRoot --script 'res://tools/build_map_visual_aoi_base.gd' -- $mapArg
if ($LASTEXITCODE -ne 0) { throw "Godot builder failed: exit $LASTEXITCODE" }
$manifestPath = Join-Path $clientRoot 'maps/streamed/manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Missing map AOI manifest: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($mapId in $mapIds) {
    $record = @($manifest.maps | Where-Object { [int]$_.map -eq $mapId } | Select-Object -First 1)
    $output = Join-Path $clientRoot ('maps/streamed/Map{0:D2}_base.scn' -f $mapId)
    if ($record.Count -eq 0 -or -not $record[0].ok -or -not (Test-Path -LiteralPath $output)) {
        throw "AOI base map $mapId failed. The original scene remains unchanged."
    }
    $original = Join-Path $clientRoot ('maps/Map{0:D2}.scn' -f $mapId)
    $sourceBytes = (Get-Item -LiteralPath $original).Length
    $baseBytes = (Get-Item -LiteralPath $output).Length
    Write-Host ("[MAP_AOI_PREPARE] map={0} detached_sprite_references={1} festival_preserved={2} source_bytes={3} base_bytes={4}" -f $mapId, $record[0].stripped, $record[0].festival_kept, $sourceBytes, $baseBytes)
}
Write-Host 'GameManager automatically selects generated bases; verify [MAP_STREAM_SOURCE] source=aoi_base.'
Write-Host 'Binary file sizes do not demonstrate reduced Windows RAM. Measure Private Bytes separately.'
