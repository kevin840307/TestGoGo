# Compare external snapshots captured from the SAME Godot PID.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Login,
    [Parameter(Mandatory=$true)][string]$Game,
    [Parameter(Mandatory=$true)][string]$AFK,
    [Parameter(Mandatory=$true)][string]$Logout
)
$ErrorActionPreference = 'Stop'
$labels = @('Login', 'Game', 'AFK', 'Logout')
$paths = @($Login, $Game, $AFK, $Logout)
$records = @()
for ($i=0; $i -lt 4; $i++) {
    if (-not (Test-Path -LiteralPath $paths[$i])) {
        throw "Missing $($labels[$i]) snapshot: $($paths[$i])"
    }
    $obj = Get-Content -LiteralPath $paths[$i] -Raw | ConvertFrom-Json
    if ($obj.Stage -ne $labels[$i]) {
        throw "Expected $($labels[$i]) but JSON says $($obj.Stage)"
    }
    $records += $obj
}
$pidList = @($records | ForEach-Object { $_.ProcessId } | Sort-Object -Unique)
if ($pidList.Count -ne 1) {
    throw 'Snapshots have different PIDs; cannot compare separate processes'
}
$summary = for ($i=0; $i -lt 4; $i++) {
    [pscustomobject]@{
        Stage = $records[$i].Stage
        WorkingSetMB = [double]$records[$i].WorkingSetMB
        PrivateBytesMB = [double]$records[$i].PrivateBytesMB
        PrivateCommitMB = [double]$records[$i].PrivateCommittedMB
        MappedCommitMB = [double]$records[$i].MappedCommittedMB
        ImageCommitMB = [double]$records[$i].ImageCommittedMB
    }
}
Write-Host "Same-process memory snapshots (PID=$($pidList[0]), MB)"
$summary | Format-Table -AutoSize | Out-String | Write-Host
$start = $summary[0]
$end = $summary[3]
$deltas = @(
    ('WorkingSet={0:+0.00;-0.00;0.00} MB' -f ($end.WorkingSetMB-$start.WorkingSetMB)),
    ('PrivateBytes={0:+0.00;-0.00;0.00} MB' -f ($end.PrivateBytesMB-$start.PrivateBytesMB)),
    ('MEM_PRIVATE committed={0:+0.00;-0.00;0.00} MB' -f ($end.PrivateCommitMB-$start.PrivateCommitMB)),
    ('MEM_MAPPED committed={0:+0.00;-0.00;0.00} MB' -f ($end.MappedCommitMB-$start.MappedCommitMB)),
    ('MEM_IMAGE committed={0:+0.00;-0.00;0.00} MB' -f ($end.ImageCommitMB-$start.ImageCommitMB))
)
Write-Host ('Logout minus initial login: ' + ($deltas -join ', '))
# Allocation-base addresses are only clues; neither stacks nor ownership
# of specific suballocations can be inferred from these snapshots.
$baseline = @{}
foreach ($allocation in $records[0].TopPrivateAllocations) {
    $baseline[[string]$allocation.AllocationBase] = [double]$allocation.CommittedMB
}
Write-Host 'Largest private committed groups on return:'
$records[3].TopPrivateAllocations |
    Select-Object -First 12 |
    ForEach-Object {
        [pscustomobject]@{
            AllocationBase = $_.AllocationBase
            LogoutCommitMB = $_.CommittedMB
            InitialLoginCommitMB = $baseline[[string]$_.AllocationBase]
        }
    } | Format-Table -AutoSize | Out-String | Write-Host
Write-Host 'VirtualQueryEx categories are committed virtual memory, not physical RAM or GPU VRAM.'
