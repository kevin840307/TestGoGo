# Non-invasive Windows process sampler. Never trims working sets or triggers GC.
# Example:
# pwsh -File tools/SGO.AfkProbe/Measure-WindowsMemory.ps1 -ProcessIds 1234 -Stage AFK -Samples 30 -OutputCsv results/memory.csv
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][int[]]$ProcessIds,
    [ValidateSet('Login','Game','AFK','Logout','Standalone','Other')]
    [string]$Stage = 'Other',
    [ValidateRange(1, 3600)][int]$Samples = 30,
    [ValidateRange(1, 60)][int]$IntervalSeconds = 1,
    [string]$OutputCsv = ''
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and $env:OS -ne 'Windows_NT') {
    throw 'Windows required: Private Working Set requires Windows performance counters'
}

$initialized = $false
if ($OutputCsv) {
    $OutputCsv = [System.IO.Path]::GetFullPath($OutputCsv)
    $dir = Split-Path -Parent $OutputCsv
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $initialized = Test-Path -LiteralPath $OutputCsv
}

for ($i = 0; $i -lt $Samples; $i++) {
    # Look up the process by exact ID, not by process name. Multiple Godot
    # windows otherwise collapse into the wrong Performance Counter instance.
    $performance = @{}
    try {
        Get-CimInstance Win32_PerfRawData_PerfProc_Process -ErrorAction Stop |
            ForEach-Object {
                if ($_.IDProcess -in $ProcessIds) {
                    $performance[[int]$_.IDProcess] = $_
                }
            }
    } catch {
        Write-Warning 'Private Working Set counter unavailable; leaving that column blank.'
    }

    foreach ($targetId in $ProcessIds) {
        $process = Get-Process -Id $targetId -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            Write-Warning "Process $targetId has exited."
            continue
        }
        $process.Refresh()
        $counter = $performance[$targetId]
        $privateWs = $null
        if ($null -ne $counter -and
            $null -ne $counter.PSObject.Properties['WorkingSetPrivate']) {
            $privateWs = [math]::Round([double]$counter.WorkingSetPrivate / 1MB, 2)
        }
        $entry = [pscustomobject]@{
            Timestamp = (Get-Date).ToString('o')
            Stage = $Stage
            PID = $targetId
            ProcessName = $process.ProcessName
            WorkingSetMB = [math]::Round($process.WorkingSet64 / 1MB, 2)
            PrivateWorkingSetMB = $privateWs
            PrivateBytesMB = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
            PeakWorkingSetMB = [math]::Round($process.PeakWorkingSet64 / 1MB, 2)
            Threads = $process.Threads.Count
            Handles = $process.HandleCount
        }
        $entry | Format-Table -AutoSize | Out-String | Write-Host
        if ($OutputCsv) {
            if ($initialized) {
                $entry | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Append
            } else {
                $entry | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation
                $initialized = $true
            }
        }
    }
    if ($i + 1 -lt $Samples) {
        Start-Sleep -Seconds $IntervalSeconds
    }
}
