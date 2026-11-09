# One command for the four real client stages. Never alters the target.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateRange(1,2147483647)][int]$GamePid,
    [string]$OutputDirectory = '',
    [switch]$Automatic
)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This audit requires Windows x64'
}
$project = Join-Path $PSScriptRoot 'SGO.WindowsMemoryAudit.csproj'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path (Get-Location).Path ('results/windows-memory-audit-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Windows memory audit build failed' }

$stages = @(
    @{ Name='Login'; Instruction='Start Godot, stay on initial login. Once settled press Enter' },
    @{ Name='Game'; Instruction='Enter Map13 and wait until map and monsters are drawn, then press Enter' },
    @{ Name='AFK'; Instruction='Enable the ORIGINAL Godot AFK mode, wait for its transition, then press Enter' },
    @{ Name='Logout'; Instruction='Exit AFK and logout to login screen, then press Enter' }
)
foreach ($item in $stages) {
    $name = [string]$item.Name
    if (-not $Automatic) {
        [void](Read-Host ("[" + $name + "] " + $item.Instruction))
    }
    $game = Get-Process -Id $GamePid -ErrorAction SilentlyContinue
    if ($null -eq $game) {
        throw ("Godot process " + $GamePid + " is no longer alive; do not compare different runs")
    }
    if ($name -eq 'Logout' -and -not $Automatic) {
        Write-Host 'Waiting 10 seconds for deferred scene frees and allocations to settle...'
        Start-Sleep -Seconds 10
    }
    $destination = Join-Path $OutputDirectory ("vm-" + $name.ToLowerInvariant() + ".json")
    dotnet run --project $project --no-build -c Release -- --pid $GamePid --stage $name --output $destination
    if ($LASTEXITCODE -ne 0) { throw "Audit snapshot failed for $name" }
}
& (Join-Path $PSScriptRoot 'Compare-WindowsMemoryStages.ps1') -Login (Join-Path $OutputDirectory 'vm-login.json') -Game (Join-Path $OutputDirectory 'vm-game.json') -AFK (Join-Path $OutputDirectory 'vm-afk.json') -Logout (Join-Path $OutputDirectory 'vm-logout.json')
Write-Host ("The four read-only memory snapshots are in " + $OutputDirectory)
