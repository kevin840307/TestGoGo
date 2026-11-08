param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$sourceRoot = Join-Path $RepositoryRoot "Old\服務端\Dat\Map"
$targetRoot = Join-Path $RepositoryRoot "client\legacy_map"

if (-not (Test-Path $sourceRoot)) {
    throw "Blocking source root not found: $sourceRoot"
}

New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null

$copied = 0
Get-ChildItem -Path $sourceRoot -Directory -Filter "Map*" | ForEach-Object {
    $source = Join-Path $_.FullName "Blocking.dat"
    if (-not (Test-Path $source)) {
        return
    }

    $targetDir = Join-Path $targetRoot $_.Name
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

    $target = Join-Path $targetDir "Blocking.dat"
    Copy-Item -Force -Path $source -Destination $target
    $copied++
}

Write-Host "Synced $copied Blocking.dat files from the single legacy source."
