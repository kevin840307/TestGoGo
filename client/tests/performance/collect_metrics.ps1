# SGO actual game hardware recorder. Requires Windows PowerShell 5.1.
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidateSet("single","multi")][string]$Scenario,
 [Parameter(Mandatory=$true)][string]$GodotExe,
 [Parameter(Mandatory=$true)][string]$ClientPath,
 [Parameter(Mandatory=$true)][string]$OutputRoot,
 [ValidateRange(10,3600)][int]$DurationSeconds=60,
 [ValidateRange(0,120)][int]$WarmupSeconds=10
)
$ErrorActionPreference="Stop"
Set-StrictMode -Version 2.0
$inv=[Globalization.CultureInfo]::InvariantCulture
if (-not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) { throw "Godot executable not found: $GodotExe" }
if (-not (Test-Path -LiteralPath (Join-Path $ClientPath "project.godot"))) { throw "Godot project not found: $ClientPath" }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$runId=(Get-Date -Format "yyyyMMdd-HHmmss")+"-"+[guid]::NewGuid().ToString("N").Substring(0,6)
$runDir=Join-Path $OutputRoot $runId
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$hardwareFile=Join-Path $runDir "hardware.csv"
$godotFile=Join-Path $runDir "godot_events.csv"
$metaFile=Join-Path $runDir "session.json"
$previous=@{}; $offsets=@{}; $modeByPid=@{}
$targets=New-Object System.Collections.ArrayList
$launched=New-Object System.Collections.ArrayList
$completedStages=New-Object System.Collections.ArrayList
$cores=[Math]::Max(1,[Environment]::ProcessorCount)
$startedUtc=[DateTime]::UtcNow
$script:gpu3dCounterAvailable=$false
$script:gpuMemoryCounterAvailable=$false

function Fmt($v) {
 if ($null -eq $v) { return $null }
 return ([double]$v).ToString("F2",$inv)
}
function CsvRow([string]$file,[object]$record) {
 if (Test-Path -LiteralPath $file) { $record | Export-Csv -LiteralPath $file -NoTypeInformation -Encoding UTF8 -Append }
 else { $record | Export-Csv -LiteralPath $file -NoTypeInformation -Encoding UTF8 }
}
function Fields([string]$line) {
 $kv=@{}
 foreach ($m in [regex]::Matches($line,'([A-Za-z_]+)=([^\s]+)')) { $kv[$m.Groups[1].Value]=$m.Groups[2].Value }
 return $kv
}
function GpuSnapshot {
 $threeD=@{}; $all=@{}; $dedicated=@{}
 try {
  $rows=@(Get-CimInstance -Namespace root/cimv2 -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction Stop)
  $script:gpu3dCounterAvailable=$true
  foreach ($row in $rows) {
   $name=[string]$row.Name
   $match=[regex]::Match($name,'pid_(\d+)')
   if (-not $match.Success) { continue }
   $key=$match.Groups[1].Value
   if (-not $all.ContainsKey($key)) { $all[$key]=0.0 }
   $all[$key]+=[double]$row.UtilizationPercentage
   if ($name -match 'engtype_3D') {
    if (-not $threeD.ContainsKey($key)) { $threeD[$key]=0.0 }
    $threeD[$key]+=[double]$row.UtilizationPercentage
   }
  }
 } catch { $script:gpu3dCounterAvailable=$false }
 try {
  $rows=@(Get-CimInstance -Namespace root/cimv2 -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory -ErrorAction Stop)
  $script:gpuMemoryCounterAvailable=$true
  foreach ($row in $rows) {
   $match=[regex]::Match([string]$row.Name,'pid_(\d+)')
   if (-not $match.Success) { continue }
   $key=$match.Groups[1].Value
   if (-not $dedicated.ContainsKey($key)) { $dedicated[$key]=0.0 }
   $dedicated[$key]+=[double]$row.DedicatedUsage / 1MB
  }
 } catch { $script:gpuMemoryCounterAvailable=$false }
 return @{threeD=$threeD;all=$all;dedicated=$dedicated}
}
function ReadGodot([object]$target,[string]$stage,[bool]$discardEvents=$false) {
 $path=[string]$target.log_path
 if ($path -eq "" -or -not (Test-Path -LiteralPath $path)) { return }
 try { $lines=[IO.File]::ReadAllLines($path) } catch { return }
 $offset=0
 if ($offsets.ContainsKey($path)) { $offset=[int]$offsets[$path] }
 if ($offset -gt $lines.Length) { $offset=0 }
 $key=[string]$target.pid
 if (-not $modeByPid.ContainsKey($key)) { $modeByPid[$key]=@{monster_simplify=""} }
 for ($i=$offset; $i -lt $lines.Length; $i++) {
  $line=$lines[$i]
  if ($line.StartsWith("[PERFORMANCE_PROFILE]")) {
   $modeByPid[$key]=Fields $line
   continue
  }
  $kind=""
  if ($line.StartsWith("[RENDER_STATS]")) { $kind="render" }
  if ($line.StartsWith("[PERF_ANALYSIS]")) { $kind="process" }
  if ($kind -eq "" -or $discardEvents) { continue }
  $v=Fields $line
  $event=[pscustomobject][ordered]@{
   timestamp_utc=[DateTime]::UtcNow.ToString("o");run_id=$runId
   scenario=$Scenario;stage=$stage;target_count=$targets.Count;pid=[int]$target.pid
   kind=$kind;mode=[string]$v["mode"];combat=[string]$v["combat"]
   monster_simplify=[string]$modeByPid[$key]["monster_simplify"]
   focus=[string]$v["focus"];render_stats_valid=[string]$v["render_stats_valid"]
   fps=[string]$v["fps"];draw_calls=[string]$v["draw_calls"]
   canvas_nodes=[string]$v["canvas_nodes"];visible_monsters=[string]$v["visible_monsters"]
   simplified_monsters=[string]$v["simplified_monsters"]
   process_ms=[string]$v["process_ms"];physics_ms=[string]$v["physics_ms"]
   godot_static_mem_mb=[string]$v["static_mem_mb"]
   godot_video_mem_mb=[string]$v["video_mem_mb"]
   godot_texture_mem_mb=[string]$v["texture_mem_mb"]
  }
  CsvRow $godotFile $event
 }
 $offsets[$path]=$lines.Length
}

Write-Host "SGO CLIENT HARDWARE BENCHMARK - actual game (not headless)"
Write-Host "Output: $runDir"
Write-Host "Windows CPU percent is normalized across $cores logical processors."
Write-Host "WDDM GPU/VRAM unsupported counters will be EMPTY, never guessed."
$count=1
if ($Scenario -eq "multi") {
 $choice=Read-Host "How many clients [1, 5, 10; default 5]?"
 if ([string]::IsNullOrWhiteSpace($choice)) { $choice="5" }
 if ($choice -notin @("1","5","10")) { throw "Only 1, 5 or 10 are supported." }
 $count=[int]$choice
 Write-Host "Use separate login accounts if your server permits only one session per account."
 Write-Host "10 clients may exceed available VRAM; try 1 then 5 first."
}
$launch=Read-Host "Launch NEW game windows (L) or attach EXISTING windows (A)? [L]"
if ([string]::IsNullOrWhiteSpace($launch)) { $launch="L" }
$launch=$launch.ToUpperInvariant()
if ($launch -notin @("L","A")) { throw "Choose L or A." }
if ($launch -eq "A") {
 $exePath=[IO.Path]::GetFullPath($GodotExe)
 $found=@(Get-CimInstance Win32_Process | Where-Object {
  $_.ExecutablePath -and [string]::Equals([IO.Path]::GetFullPath($_.ExecutablePath),$exePath,[StringComparison]::OrdinalIgnoreCase) -and
  $_.CommandLine -notmatch '(?i)--editor'
 } | Sort-Object ProcessId)
 if ($found.Count -lt $count) { throw "Only $($found.Count) matching game processes exist; need $count." }
 foreach ($p in ($found | Select-Object -First $count)) {
  [void]$targets.Add([pscustomobject]@{pid=[int]$p.ProcessId;log_path="";launched=$false})
 }
 Write-Host "Attached $count processes. Godot FPS/DrawCalls unavailable in attach mode; hardware is still recorded."
} else {
 if ($count -gt 1) {
  $confirmation=Read-Host "Type LAUNCH-$count to open $count new game windows"
  if ($confirmation -cne ("LAUNCH-"+$count)) { throw "Launch cancelled." }
 }
 for ($i=1;$i -le $count;$i++) {
  $logPath=Join-Path $runDir ("godot-client-{0:D2}.log" -f $i)
  $p=Start-Process -FilePath $GodotExe -ArgumentList @(
   "--path",('"'+$ClientPath+'"'),"--log-file",('"'+$logPath+'"')
  ) -PassThru -ErrorAction Stop
  [void]$targets.Add([pscustomobject]@{pid=[int]$p.Id;log_path=$logPath;launched=$true})
  [void]$launched.Add([int]$p.Id)
  Write-Host "Game $i/$count PID=$($p.Id)"
  if ($i -lt $count) { Start-Sleep -Seconds 2 }
 }
}
$multiLabel="multibox-on"
if ($Scenario -eq "multi") {
 $multiLabel=Read-Host "Label for ALL clients [balanced-on/low-on/multibox-on/balanced-off; default multibox-on]"
 if ([string]::IsNullOrWhiteSpace($multiLabel)) { $multiLabel="multibox-on" }
 if ($multiLabel -notmatch '^[a-zA-Z0-9_-]{1,40}$') { throw "Mode label may use only letters, numbers, - or _." }
}
$stages=if ($Scenario -eq "single") { @("balanced-on","low-on","multibox-on","balanced-off") }
else { @("multi-$count-$multiLabel") }
foreach ($stage in $stages) {
 Write-Host ""
 Write-Host ("STAGE: "+$stage)
 if ($stage -eq "balanced-on") { Write-Host "Choose Balanced / PvP / monster simplify ON." }
 elseif ($stage -eq "low-on") { Write-Host "Choose Low / PvP / monster simplify ON." }
 elseif ($stage -eq "multibox-on") { Write-Host "Choose Multibox / PvP / monster simplify ON." }
 elseif ($stage -eq "balanced-off") { Write-Host "Choose Balanced / PvP / monster simplify OFF." }
 else { Write-Host "Set ALL clients to same map and mode; test them in the same condition." }
 $ready=Read-Host "Enter after settings and map are ready; type Q to finish"
 if ($ready -match '^(?i:q|quit)$') { break }
 Write-Host "Return focus to Godot NOW. The BAT/CMD window is foreground after pressing Enter."
 Write-Host "Warmup: $WarmupSeconds sec; use it to refocus your game window."
 if ($WarmupSeconds -gt 0) { Start-Sleep -Seconds $WarmupSeconds }
 # Consume historic login/warmup/performance-profile lines without
 # attributing them to the next 60-second measurement stage.
 foreach ($target in $targets) { ReadGodot $target $stage $true }
$previous.Clear()
 [void]$completedStages.Add($stage)
 $end=[DateTime]::UtcNow.AddSeconds($DurationSeconds)
 $nextSampleUtc=[DateTime]::UtcNow
 $idx=0
 Write-Host "Recording $DurationSeconds sec. No map/mode/focus changes during the stage."
 while ([DateTime]::UtcNow -lt $end) {
  $idx++
  $gpu=GpuSnapshot
  $now=[DateTime]::UtcNow
  foreach ($target in $targets) {
   $pidValue=[int]$target.pid
   try { $proc=Get-Process -Id $pidValue -ErrorAction Stop } catch { continue }
   $key=[string]$pidValue
   $cpuPct=$null
   if ($previous.ContainsKey($key)) {
    $prev=$previous[$key]
    $delta=($now-$prev.utc).TotalSeconds
    if ($delta -gt 0.05) {
     $cpuPct=[Math]::Min(100.0,[Math]::Max(0.0,([double]$proc.CPU-$prev.cpu))/$delta*100.0/$cores)
    }
   }
   $previous[$key]=@{utc=$now;cpu=[double]$proc.CPU}
   $gpu3d=$null;$gpuAll=$null;$vram=$null
   if ($script:gpu3dCounterAvailable) {
    if ($gpu.threeD.ContainsKey($key)) { $gpu3d=[Math]::Min(100.0,[double]$gpu.threeD[$key]) }
    if ($gpu.all.ContainsKey($key)) { $gpuAll=[Math]::Min(100.0,[double]$gpu.all[$key]) }
   }
   if ($script:gpuMemoryCounterAvailable -and $gpu.dedicated.ContainsKey($key)) {
    $vram=[double]$gpu.dedicated[$key]
   }
   $sample=[pscustomobject][ordered]@{
    timestamp_utc=$now.ToString("o");run_id=$runId;scenario=$Scenario;stage=$stage
    target_count=$count;sample_index=$idx;pid=$pidValue
    cpu_percent=Fmt $cpuPct
    ram_working_mb=Fmt ([double]$proc.WorkingSet64/1MB)
    ram_private_mb=Fmt ([double]$proc.PrivateMemorySize64/1MB)
    gpu_3d_percent=Fmt $gpu3d
    gpu_engine_percent=Fmt $gpuAll
    gpu_vram_dedicated_mb=Fmt $vram
   }
   CsvRow $hardwareFile $sample
   ReadGodot $target $stage
  }
  if ($idx % 10 -eq 0) {
   Write-Host ("  "+$stage+" samples="+$idx+" remaining_s="+[Math]::Max(0,[Math]::Ceiling(($end-[DateTime]::UtcNow).TotalSeconds)))
  }
  $nextSampleUtc=$nextSampleUtc.AddSeconds(1)
  $sleepMs=[Math]::Floor(($nextSampleUtc-[DateTime]::UtcNow).TotalMilliseconds)
  if ($sleepMs -gt 0) { Start-Sleep -Milliseconds $sleepMs }
  else { $nextSampleUtc=[DateTime]::UtcNow }
 }
}
if (-not (Test-Path -LiteralPath $hardwareFile)) {
 throw "No samples were captured. Ensure the Godot game process stays running and retry."
}
$metadata=[ordered]@{
 schema_version=1;run_id=$runId;scenario=$Scenario;target_count=$count
 started_utc=$startedUtc.ToString("o");finished_utc=[DateTime]::UtcNow.ToString("o")
 duration_seconds_per_stage=$DurationSeconds;warmup_seconds_per_stage=$WarmupSeconds
 logical_processor_count=$cores
 gpu_engine_counter_available=$script:gpu3dCounterAvailable
 gpu_memory_counter_available=$script:gpuMemoryCounterAvailable
 stages=@($completedStages.ToArray());clients=@($targets.ToArray())
 note="Stage labels are chosen by user; validate actual mode against Godot profile events. Attach mode has no Godot logs."
}
$metadata | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $metaFile -Encoding UTF8
Write-Host ""
Write-Host "Complete. CSV+meta in: $runDir"
Write-Host "Menu 9 generates the comparison HTML."
if ($launched.Count -gt 0) {
 $answer=Read-Host "Close ONLY the $($launched.Count) clients started by this tool? [y/N]"
 if ($answer -match '^(?i:y|yes)$') {
  foreach ($id in $launched) { Stop-Process -Id $id -ErrorAction SilentlyContinue }
 }
}
exit 0
