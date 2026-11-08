# SGO Windows test orchestrator. Start via pull_build_test.bat.
# PowerShell 5.1+; designed to stop on any failed build/smoke/probe.
[CmdletBinding()]
param(
    [Parameter(Position=0)]
    [ValidateSet('Menu','BuildOnly','Full','Aoi','Wire100','WireAll')]
    [string]$Mode = 'Menu'
)

$ErrorActionPreference = 'Stop'

# The BAT is intentionally a tiny launcher. Keeping the menu here avoids
# races when another contributor edits the repository's .bat entry point.
if ($Mode -eq 'Menu') {
    Write-Host ''
    Write-Host '================================================================'
    Write-Host 'SGO - Pull / Release Build / Smoke Tests / AOI Load'
    Write-Host '================================================================'
    Write-Host '[1] Pull + Build + FULL Smoke + Python regression tests'
    Write-Host '[2] Option 1 + moving AOI test (100,250,500,1000)'
    Write-Host '[3] Option 2 + isolated REAL WebSocket 100-player load'
    Write-Host '[4] Option 2 + isolated REAL WebSocket 100/250/500/1000'
    Write-Host '[5] Pull + Release Build only'
    Write-Host '[Q] Exit without changes'
    Write-Host ''
    Write-Host 'Safety: no force-pull, no reset, no existing game.db.'
    Write-Host 'Outputs: test-results/<timestamp>/'
    Write-Host ''
    $selection = (Read-Host 'Choose [1-5,Q]').Trim().ToUpperInvariant()
    switch ($selection) {
        '1' { $Mode = 'Full' }
        '2' { $Mode = 'Aoi' }
        '3' { $Mode = 'Wire100' }
        '4' { $Mode = 'WireAll' }
        '5' { $Mode = 'BuildOnly' }
        default {
            Write-Host 'Canceled. No repository update or test started.'
            exit 0
        }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$branch = 'feature/local-server-net8'
$reportsRoot = Join-Path $repoRoot 'test-results'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $reportsRoot ($stamp + '-' + ([guid]::NewGuid().ToString('N').Substring(0,6)))
New-Item -ItemType Directory -Path $runDir -Force | Out-Null
$transcriptOn = $false
$exitCode = 1

function Say([string]$message) {
    Write-Host ''
    Write-Host ('=' * 68) -ForegroundColor Cyan
    Write-Host $message -ForegroundColor Cyan
    Write-Host ('=' * 68) -ForegroundColor Cyan
}

function Require-Tool([string]$name) {
    $tool = Get-Command $name -ErrorAction SilentlyContinue
    if ($null -eq $tool) {
        throw "Required tool '$name' not found in PATH. Install Git for Windows and .NET 8 SDK."
    }
    return $tool.Source
}

function Run-External([string]$label, [string]$exe, [string[]]$arguments) {
    Say $label
    & $exe @arguments
    $rc = $LASTEXITCODE
    if ($rc -ne 0) {
        throw "$label failed (exit $rc). See $runDir"
    }
}

function Check-TcpPortFree([int]$port) {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback, $port)
    try {
        $listener.Start()
    } catch {
        throw "Port $port is already occupied. Close the other local test server first; no existing process will be killed."
    } finally {
        try { $listener.Stop() } catch {}
    }
}

function Run-WireProbe([int]$players, [string]$dotnetPath) {
    $port = 5688
    $wireDir = Join-Path $runDir ("wire-" + $players)
    New-Item -ItemType Directory -Path $wireDir -Force | Out-Null
    $dbPath = Join-Path $wireDir 'game.db'
    $stdoutPath = Join-Path $wireDir 'server.stdout.log'
    $stderrPath = Join-Path $wireDir 'server.stderr.log'
    $consolePath = Join-Path $wireDir 'wire.console.txt'
    $probeStderr = Join-Path $wireDir 'wire.stderr.txt'
    $jsonPath = Join-Path $wireDir ("wire-aoi-" + $players + '.json')
    $serverDll = Join-Path $repoRoot 'server\bin\Release\net8.0\SGO.Server.dll'
    if (-not (Test-Path -LiteralPath $serverDll)) {
        throw "Missing built server DLL: $serverDll"
    }

    Say "REAL WEBSOCKET: $players virtual accounts / players, separate DB + port $port"
    Check-TcpPortFree $port

    # Save process env: only this newly created Server will inherit a GM-enabled
    # config. Existing SGO servers and existing game.db stay untouched.
    $envKeys = @(
        'Database__Sqlite__Path',
        'Game__ListenUrl',
        'Game__GmCommandsEnabled',
        'Game__ClientRoot'
    )
    $oldEnv = @{}
    foreach ($key in $envKeys) {
        $oldEnv[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    }

    $server = $null
    try {
        $env:Database__Sqlite__Path = $dbPath
        $env:Game__ListenUrl = "http://127.0.0.1:$port"
        $env:Game__GmCommandsEnabled = 'true'
        $env:Game__ClientRoot = Join-Path $repoRoot 'client'

        $server = Start-Process -FilePath $dotnetPath -ArgumentList ('"{0}"' -f $serverDll) -WorkingDirectory $repoRoot -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru -WindowStyle Hidden
        # Remove GM/database overrides before launching the load-generator.
        # The spawned server already inherited them.
        foreach ($key in $envKeys) {
            [Environment]::SetEnvironmentVariable($key, $oldEnv[$key], 'Process')
        }

        $ready = $false
        $healthUrl = "http://127.0.0.1:$port/health"
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-Date) -lt $deadline) {
            $server.Refresh()
            if ($server.HasExited) { break }
            try {
                $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2
                if ($response.StatusCode -eq 200) {
                    $ready = $true
                    break
                }
            } catch { }
            Start-Sleep -Milliseconds 700
        }
        if (-not $ready) {
            Write-Host 'Server startup stdout (last 30):' -ForegroundColor Yellow
            if (Test-Path $stdoutPath) { Get-Content $stdoutPath -Tail 30 | Out-Host }
            Write-Host 'Server startup stderr (last 30):' -ForegroundColor Yellow
            if (Test-Path $stderrPath) { Get-Content $stderrPath -Tail 30 | Out-Host }
            throw "Isolated Server did not become healthy on $healthUrl."
        }

        Write-Host "Server healthy. Probe output: $jsonPath"
        $probeArgs = @(
            'run', '--no-build', '-c', 'Release',
            '--project', (Join-Path $repoRoot 'tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj'),
            '--',
            '--url', "ws://127.0.0.1:$port/",
            '--sizes', [string]$players,
            '--rounds', '10',
            '--move-pixels', '4',
            '--setup-concurrency', '10',
            '--mixed-seconds', '30',
            '--timeout-seconds', '60',
            '--output', $jsonPath
        )

        # The tool emits large JSON to stdout and phase progress to
        # stderr. Run it as a separate process so PowerShell 5.1's
        # ErrorActionPreference=Stop cannot turn native stderr progress
        # into a NativeCommandError during the load test.
        $quoteArguments = foreach ($arg in $probeArgs) {
            if ($arg -match '[\s"]') {
                '"' + $arg.Replace('"', '\"') + '"'
            } else { $arg }
        }
        $probe = Start-Process -FilePath $dotnetPath -ArgumentList ($quoteArguments -join ' ') -WorkingDirectory $repoRoot -RedirectStandardOutput $consolePath -RedirectStandardError $probeStderr -PassThru -Wait -WindowStyle Hidden
        $probeExit = $probe.ExitCode
        if ($probeExit -ne 0) {
            Write-Host "Probe progress/errors (last 35 lines): $probeStderr" -ForegroundColor Yellow
            if (Test-Path $probeStderr) { Get-Content $probeStderr -Tail 35 | Out-Host }
            Write-Host "Probe output (last 35 lines): $consolePath" -ForegroundColor Yellow
            if (Test-Path $consolePath) { Get-Content $consolePath -Tail 35 | Out-Host }
            throw "Wire probe failed for $players players (exit $probeExit)."
        }
        if (-not (Test-Path -LiteralPath $jsonPath)) {
            throw "Wire probe returned exit 0 but did not write $jsonPath"
        }

        $report = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
        if (@($report.Results).Count -ne 1) {
            throw "Expected one wire result for $players players; see $jsonPath"
        }
        foreach ($result in @($report.Results)) {
            if (-not $result.Success -or $result.Players -ne $players -or
                $result.SetupSuccesses -ne $players -or
                $result.SetupFailures -ne 0 -or
                $result.Movement.ExpectedDeliveries -ne $result.Movement.ActualDeliveries -or
                $result.WorldAoi.FarSpawnLeaks -ne 0 -or
                -not $result.WorldAoi.ReentrySpawnReceived) {
                throw "Wire report failed correctness checks for $players players; see $jsonPath"
            }
            Write-Host ("PASS: players={0}; received moves={1}; P95={2} ms; far leaks={3}" -f $result.Players,$result.Movement.ActualDeliveries,$result.Movement.RoundLatencyMs.P95,$result.WorldAoi.FarSpawnLeaks) -ForegroundColor Green
        }
    } finally {
        if ($null -ne $server) {
            try {
                $server.Refresh()
                if (-not $server.HasExited) {
                    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
                    [void]$server.WaitForExit(5000)
                }
            } catch {
                Write-Warning ("Server cleanup: " + $_.Exception.Message)
            }
        }
        foreach ($key in $envKeys) {
            [Environment]::SetEnvironmentVariable($key, $oldEnv[$key], 'Process')
        }
        Write-Host "Test Server stopped; isolated DB retained for debugging: $dbPath"
    }
}

try {
    Start-Transcript -Path (Join-Path $runDir 'run.log') -Force | Out-Null
    $transcriptOn = $true
    Say "SGO / $Mode / result directory: $runDir"

    $git = Require-Tool 'git'
    $dotnet = Require-Tool 'dotnet'

    $currentRoot = (& $git -C $repoRoot rev-parse --show-toplevel)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($currentRoot)) {
        throw "This BAT must be placed in the SGO Git repository root."
    }
    $currentBranch = (& $git -C $repoRoot branch --show-current).Trim()
    if ($currentBranch -ne $branch) {
        throw "Wrong branch: '$currentBranch'. Expected '$branch'. Switch branches manually; this script never overwrites working files."
    }
    $dirty = @(& $git -C $repoRoot status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0) { throw "git status failed." }
    if ($dirty.Count -gt 0) {
        $dirty | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
        throw "Tracked files contain local edits. Commit/stash them manually, then rerun. No auto-stash, reset or force pull."
    }

    Run-External "1. Git pull (fast-forward only, no force)" $git @('-C', $repoRoot, 'pull', '--ff-only', 'origin', $branch)
    $commit = (& $git -C $repoRoot rev-parse --short HEAD).Trim()
    Write-Host "Testing commit: $commit"
    [IO.File]::WriteAllText((Join-Path $runDir 'commit.txt'), ($commit + [Environment]::NewLine))

    Run-External "2. Build .NET Server / Release" $dotnet @(
        'build', (Join-Path $repoRoot 'server\SGO.Server.csproj'), '-c', 'Release', '--nologo')
    foreach ($project in @(
        'tests\SGO.Server.SmokeTests\SGO.Server.SmokeTests.csproj',
        'tests\SGO.Server.LoadProbe\SGO.Server.LoadProbe.csproj',
        'tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj'
    )) {
        Run-External ("3. Build " + $project + " / Release") $dotnet @(
            'build', (Join-Path $repoRoot $project), '-c', 'Release', '--nologo')
    }

    if ($Mode -ne 'BuildOnly') {
        Push-Location $repoRoot
        try {
            Run-External "4. Smoke tests / all" $dotnet @(
                'run', '--no-build', '-c', 'Release', '--project',
                (Join-Path $repoRoot 'tests\SGO.Server.SmokeTests\SGO.Server.SmokeTests.csproj'),
                '--', '--suite', 'all'
            )
            $pythonChecks = @('tests\test_admin_ui_structure.py','tests\test_global_rate_contract.py','tests\test_gm_security.py','tests\test_windows_runner_contract.py','tests\test_skill_target_and_popup_cursor_contract.py','tests\test_primitive_monster_simplification_contract.py','tests\test_builtin_entity_simplification_contract.py','client\tests\resource_uid_reference_regression.py')
            $existingChecks = @($pythonChecks | Where-Object { Test-Path -LiteralPath (Join-Path $repoRoot $_) })
            if ($existingChecks.Count -gt 0) {
                $pythonTool = Get-Command py -ErrorAction SilentlyContinue
                $pythonArgs = @('-3')
                if ($null -eq $pythonTool) {
                    $pythonTool = Get-Command python -ErrorAction SilentlyContinue
                    $pythonArgs = @()
                }
                if ($null -eq $pythonTool) {
                    throw "Python regression files exist but neither 'py -3' nor 'python' is installed. Tests were NOT skipped."
                }
                foreach ($test in $existingChecks) {
                    Run-External ("5. Python regression: " + $test) $pythonTool.Source ($pythonArgs + @(Join-Path $repoRoot $test))
                }
            }
        } finally { Pop-Location }
    }

    if ($Mode -in @('Aoi','Wire100','WireAll')) {
        $aoiJson = Join-Path $runDir 'aoi-moving.json'
        Run-External '6. Moving AOI benchmark: 100,250,500,1000 players (dense and spread)' $dotnet @(
            'run', '--no-build', '-c', 'Release',
            '--project', (Join-Path $repoRoot 'tests\SGO.Server.LoadProbe\SGO.Server.LoadProbe.csproj'),
            '--', '--sizes', '100,250,500,1000',
            '--aoi-only', '--aoi-steps', '50',
            '--output', $aoiJson
        )
        if (-not (Test-Path $aoiJson)) { throw "Missing AOI JSON: $aoiJson" }
        $aoi = Get-Content $aoiJson -Raw | ConvertFrom-Json
        $rows = @($aoi.AoiMovingPlayers)
        if ($rows.Count -ne 8 -or @($rows | Where-Object { -not $_.Success }).Count -gt 0) {
            throw "AOI moving results are incomplete or failed (expected eight rows)."
        }
        foreach ($row in $rows) {
            Write-Host ("AOI PASS: {0,4} players / {1,-12} / saved {2:N1}% / P99 {3:N4} ms" -f $row.Players,$row.Layout,$row.AvoidedDeliveryPercent,$row.UpdateLatencyMs.P99) -ForegroundColor Green
        }
    }

    if ($Mode -eq 'WireAll') {
        Write-Host ''
        Write-Host 'CAUTION: WireAll will create 100+250+500+1000 test accounts and run resource-intensive load tests.' -ForegroundColor Yellow
        Write-Host 'Separate SQLite databases and port 5688 will be used; no production DB touched.' -ForegroundColor Yellow
        $confirm = Read-Host "Type RUN-1000 to continue"
        if ($confirm -cne 'RUN-1000') {
            throw 'WireAll canceled; nothing beyond offline AOI benchmark was started.'
        }
    }

    if ($Mode -eq 'Wire100') {
        Run-WireProbe 100 $dotnet
    } elseif ($Mode -eq 'WireAll') {
        foreach ($size in @(100,250,500,1000)) {
            Run-WireProbe $size $dotnet
        }
    }

    Say "SUCCESS: $Mode (commit $commit)"
    Write-Host "Reports: $runDir" -ForegroundColor Green
    Write-Host 'Note: these server tests do NOT measure Godot rendering or 10-client GPU/RAM.'
    $exitCode = 0
} catch {
    Write-Host ''
    Write-Host ("FAILED: " + $_.Exception.Message) -ForegroundColor Red
    Write-Host "Logs and partial reports: $runDir" -ForegroundColor Yellow
    $exitCode = 1
} finally {
    if ($transcriptOn) {
        try { Stop-Transcript | Out-Null } catch {}
    }
}
exit $exitCode
