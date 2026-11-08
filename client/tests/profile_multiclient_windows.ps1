# Windows PowerShell 5.1+: profile already-running SGO clients.
# powershell -ExecutionPolicy Bypass -File client/tests/profile_multiclient_windows.ps1 -ProcessName SGO -Samples 36 -IntervalSeconds 5 -OutputCsv .\sgo_10_clients.csv
# No process is started, stopped, or logged in by this script.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProcessName,
    [ValidateRange(1,10000)][int]$Samples = 36,
    [ValidateRange(1,3600)][int]$IntervalSeconds = 5,
    [string]$OutputCsv = ".\sgo_multiclient.csv"
)

$ErrorActionPreference = "Stop"
$name = $ProcessName -replace "\.exe$", ""
$cores = [Environment]::ProcessorCount
$lastCpu = @{}
$lastSampleTime = $null
$firstWrite = $true
$gpuEngineSupported = $true
$gpuMemorySupported = $true
$reportedGpuWarning = $false

function Get-GpuCountersByPid {
    $engineSum = @{}
    $dedicated = @{}
    $shared = @{}
    if ($script:gpuEngineSupported) {
        try {
            $samples = @(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction Stop)
            foreach ($entry in $samples) {
                if ([string]$entry.Name -match "pid_(\d+)") {
                    $clientPid = [int]$Matches[1]
                    $engineSum[$clientPid] = [double]$engineSum[$clientPid] + [double]$entry.UtilizationPercentage
                }
            }
        } catch { $script:gpuEngineSupported = $false }
    }
    if ($script:gpuMemorySupported) {
        try {
            $samples = @(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory -ErrorAction Stop)
            foreach ($entry in $samples) {
                if ([string]$entry.Name -match "pid_(\d+)") {
                    $clientPid = [int]$Matches[1]
                    $dedicated[$clientPid] = [double]$dedicated[$clientPid] + [double]$entry.DedicatedUsage / 1MB
                    $shared[$clientPid] = [double]$shared[$clientPid] + [double]$entry.SharedUsage / 1MB
                }
            }
        } catch { $script:gpuMemorySupported = $false }
    }
    return @{ Engine = $engineSum; Dedicated = $dedicated; Shared = $shared }
}

if (Test-Path -LiteralPath $OutputCsv) {
    throw "Output CSV already exists: $OutputCsv. Use a new filename."
}
Write-Host "SGO profiler: process=$name samples=$Samples interval=$IntervalSeconds seconds"
Write-Host "GPU engine SUM is not whole-GPU utilization. Use the Windows GPU graph for overall busy percentage."
for ($sampleId = 1; $sampleId -le $Samples; $sampleId++) {
    $now = [DateTime]::UtcNow
    $elapsed = if ($lastSampleTime -ne $null) { ($now - $lastSampleTime).TotalSeconds } else { 0.0 }
    $gpu = Get-GpuCountersByPid
    if (-not $reportedGpuWarning -and (-not $gpuEngineSupported -or -not $gpuMemorySupported)) {
        Write-Warning "Some Windows GPU/WDDM counters are unavailable; their CSV cells will be blank."
        $reportedGpuWarning = $true
    }
    $procs = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    $rows = @()
    foreach ($proc in $procs) {
        if ($null -eq $proc) { continue }
        $procId = [int]$proc.Id
        $usedCpu = [double]$proc.TotalProcessorTime.TotalSeconds
        $cpuPct = $null
        if ($elapsed -gt 0 -and $lastCpu.ContainsKey($procId)) {
            $deltaCpu = [Math]::Max(0.0, $usedCpu - [double]$lastCpu[$procId])
            $cpuPct = [Math]::Round(100 * $deltaCpu / $elapsed / $cores, 2)
        }
        $lastCpu[$procId] = $usedCpu
        $gpuEngineSum = if ($gpu.Engine.ContainsKey($procId)) { [Math]::Round([double]$gpu.Engine[$procId], 2) } else { $null }
        $gpuDedicatedMB = if ($gpu.Dedicated.ContainsKey($procId)) { [Math]::Round([double]$gpu.Dedicated[$procId], 1) } else { $null }
        $gpuSharedMB = if ($gpu.Shared.ContainsKey($procId)) { [Math]::Round([double]$gpu.Shared[$procId], 1) } else { $null }
        $rows += [PSCustomObject]@{
            UtcTime = $now.ToString("o")
            Sample = $sampleId
            Pid = $procId
            CpuPctWholeHost = $cpuPct
            WorkingSetMB = [Math]::Round($proc.WorkingSet64 / 1MB, 1)
            PrivateBytesMB = [Math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
            Threads = $proc.Threads.Count
            Handles = $proc.HandleCount
            WddmGpuEngineActivitySum = $gpuEngineSum
            WddmDedicatedGpuMB = $gpuDedicatedMB
            WddmSharedGpuMB = $gpuSharedMB
        }
    }
    if ($rows.Count -gt 0) {
        if ($firstWrite) {
            $rows | Export-Csv -Path $OutputCsv -NoTypeInformation -Encoding UTF8
            $firstWrite = $false
        } else {
            $rows | Export-Csv -Path $OutputCsv -NoTypeInformation -Encoding UTF8 -Append
        }
    }
    $totalWorkingSetMB = 0.0
    foreach ($row in $rows) { $totalWorkingSetMB += $row.WorkingSetMB }
    Write-Host ("{0}/{1} clients={2} summed_working_set={3:N1}MB" -f $sampleId, $Samples, $rows.Count, $totalWorkingSetMB)
    $lastSampleTime = $now
    if ($sampleId -lt $Samples) { Start-Sleep -Seconds $IntervalSeconds }
}
if ($firstWrite) {
    Write-Warning "No matching running process. Pass the executable name without .exe."
} else {
    Write-Host "Saved results to $OutputCsv"
}
