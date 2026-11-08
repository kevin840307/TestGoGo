# Windows process memory sampler; does not modify the game process.
# -ProcessIds supports one or more Godot client PIDs from [BACKGROUND_MEMORY].
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][int[]]$ProcessIds,
    [ValidateRange(1, 500)][int]$Samples = 36,
    [ValidateRange(1, 3600)][int]$IntervalSeconds = 5,
    [string]$OutputCsv = ""
)

$csvInitialized = $OutputCsv -and (Test-Path -LiteralPath $OutputCsv)
for ($sample = 1; $sample -le $Samples; $sample++) {
    foreach ($targetProcessId in $ProcessIds) {
        $process = Get-Process -Id $targetProcessId -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            Write-Warning "Process $targetProcessId is not running."
            continue
        }
        $entry = [pscustomobject]@{
            Timestamp = (Get-Date).ToString("o")
            PID = $targetProcessId
            Name = $process.ProcessName
            WorkingSetMB = [math]::Round($process.WorkingSet64 / 1MB, 2)
            PrivateMB = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
            VirtualMB = [math]::Round($process.VirtualMemorySize64 / 1MB, 2)
            HandleCount = $process.HandleCount
            ThreadCount = $process.Threads.Count
        }
        $entry | Format-Table -AutoSize | Out-String | Write-Host
        if ($OutputCsv) {
            if ($csvInitialized) {
                $entry | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Append
            } else {
                $entry | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation
                $csvInitialized = $true
            }
        }
    }
    if ($sample -lt $Samples) {
        Start-Sleep -Seconds $IntervalSeconds
    }
}
