# Windows PowerShell 5.1+ / PowerShell 7
# Non-destructive protocol contract, .NET smoke and AES/WebSocket checks.
# Run from any folder: powershell -ExecutionPolicy Bypass -File tests/run_protocol_checks.ps1
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$logRoot = Join-Path $env:TEMP ("sgo-protocol-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
Set-Location $root
$server = $null

function Invoke-Checked {
    param([string]$Step, [string]$Program, [string[]]$Arguments)
    Write-Host "[CHECK] $Step" -ForegroundColor Cyan
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE"
    }
}

try {
    Invoke-Checked "Packet Type/Action unit tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_packet_contract_audit.py", "-v"
    )
    Invoke-Checked "Client -> Server schema unit tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_packet_schema_audit.py", "-v"
    )
    Invoke-Checked "Server -> Client response unit tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_packet_response_audit.py", "-v"
    )
    Invoke-Checked "Sensitive packet log privacy tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_packet_log_privacy.py", "-v"
    )
    Invoke-Checked "Godot client network contract tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_client_network_contract.py", "-v"
    )
    Invoke-Checked "Godot client repair contract tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_client_repair_contract.py", "-v"
    )
    Invoke-Checked "GM security regression tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_gm_security.py", "-v"
    )
    Invoke-Checked "Mall external security regression tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_mall_external_security.py", "-v"
    )
    Invoke-Checked "Quest host audit unit tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_quest_host_audit.py", "-v"
    )
    Invoke-Checked "Smoke suite structure tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_smoke_suite_structure.py", "-v"
    )
    Invoke-Checked "Load probe structure tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_load_probe_structure.py", "-v"
    )
    Invoke-Checked "Wire load probe structure tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_wire_load_probe_structure.py", "-v"
    )
    Invoke-Checked "Feature gap tracking regression" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_feature_gap_tracker.py", "-v"
    )
    Invoke-Checked "Packet Type/Action coverage" "python" @(
        "tests/packet_contract_audit.py", "--strict", "--output",
        (Join-Path $logRoot "packet-contract.json")
    )
    Invoke-Checked "Request schema coverage" "python" @(
        "tests/packet_schema_audit.py", "--strict", "--output",
        (Join-Path $logRoot "packet-schema.json")
    )
    Invoke-Checked "Response field coverage" "python" @(
        "tests/packet_response_audit.py", "--strict", "--output",
        (Join-Path $logRoot "packet-response.json")
    )
    Invoke-Checked "Legacy quest host coverage" "python" @(
        "tests/quest_host_audit.py", "--strict", "--output",
        (Join-Path $logRoot "quest-host.json")
    )

    Invoke-Checked ".NET Server build" "dotnet" @(
        "build", "server/SGO.Server.csproj", "-c", "Release"
    )
    Invoke-Checked "Scripted item coverage report" "dotnet" @(
        "run", "--project",
        "tests/SGO.ItemCoverageReport/SGO.ItemCoverageReport.csproj",
        "-c", "Release", "--", "--strict", "--output",
        (Join-Path $logRoot "item-use-coverage.json")
    )
    Invoke-Checked "Historical mapping coverage report" "dotnet" @(
        "run", "--project",
        "tests/SGO.MappingCoverageReport/SGO.MappingCoverageReport.csproj",
        "-c", "Release", "--", "--output",
        (Join-Path $logRoot "mapping-coverage.json")
    )
    Invoke-Checked "Server 10/50/100 capacity probe" "dotnet" @(
        "run", "--project",
        "tests/SGO.Server.LoadProbe/SGO.Server.LoadProbe.csproj",
        "-c", "Release", "--",
        "--sizes", "10,50,100",
        "--fanout-iterations", "200",
        "--db-ops", "2",
        "--output", (Join-Path $logRoot "load-probe.json")
    )
    Invoke-Checked ".NET game smoke test build" "dotnet" @(
        "build", "tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj",
        "-c", "Release"
    )
    foreach ($suite in @("protocol", "announcements", "world", "concurrency")) {
        Invoke-Checked ("Fast " + $suite + " smoke tests") "dotnet" @(
            "run", "--no-build", "--project",
            "tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj",
            "-c", "Release", "--", "--suite", $suite
        )
    }
    Invoke-Checked ".NET full game integration smoke" "dotnet" @(
        "run", "--no-build", "--project",
        "tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj",
        "-c", "Release"
    )
    Invoke-Checked "AES WebSocket smoke test build" "dotnet" @(
        "build", "tests/SGO.Server.WireSmokeTests/SGO.Server.WireSmokeTests.csproj",
        "-c", "Release"
    )
    Invoke-Checked "AES WebSocket load probe build" "dotnet" @(
        "build", "tests/SGO.Server.WireLoadProbe/SGO.Server.WireLoadProbe.csproj",
        "-c", "Release"
    )

    $godotCommand = $null
    if ($env:GODOT_BIN -and (Test-Path $env:GODOT_BIN)) {
        $godotCommand = $env:GODOT_BIN
    }
    else {
        $resolvedGodot = Get-Command "godot" -ErrorAction SilentlyContinue
        if ($null -eq $resolvedGodot) {
            $resolvedGodot = Get-Command "godot4" -ErrorAction SilentlyContinue
        }
        if ($null -ne $resolvedGodot) {
            $godotCommand = $resolvedGodot.Source
        }
    }

    if ($null -ne $godotCommand) {
        Invoke-Checked "Godot headless client regressions: monster chase" $godotCommand @(
            "--headless", "--path", "client",
            "--script", "res://tests/monster_chase_regression.gd"
        )
        Invoke-Checked "Godot headless client regressions: performance hot paths" $godotCommand @(
            "--headless", "--path", "client",
            "--script", "res://tests/perf_regression_smoke.gd"
        )
        Invoke-Checked "Godot headless client regressions: ground-item visual AOI" $godotCommand @(
            "--headless", "--path", "client",
            "--script", "res://tests/ground_item_aoi_regression.gd"
        )
        Invoke-Checked "Godot headless client regressions: multiclient render dedup" $godotCommand @(
            "--headless", "--path", "client",
            "--script", "res://tests/multiclient_render_dedup_regression.gd"
        )
        Invoke-Checked "Godot headless client regressions: item choice UI" $godotCommand @(
            "--headless", "--path", "client",
            "--script", "res://tests/item_choice_regression.gd"
        )
        Invoke-Checked "Godot headless client regressions: network response contracts" $godotCommand @(
            "--headless", "--path", "client",
            "--script", "res://tests/network_response_contract_regression.gd"
        )
    }
    else {
        Write-Host "[SKIP] Godot headless client regressions: set GODOT_BIN or install godot/godot4." -ForegroundColor Yellow
    }

    $serverDll = Join-Path $root "server/bin/Release/net8.0/SGO.Server.dll"
    if (-not (Test-Path $serverDll)) {
        throw "Server DLL was not created: $serverDll"
    }
    try {
        $existing = Invoke-WebRequest -Uri "http://127.0.0.1:5678/health" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        if ($existing.StatusCode -eq 200) {
            throw "Port 5678 already serves a healthy server; stop it before running this test."
        }
    }
    catch {
        if ($_.Exception.Message -like "Port 5678 already*") {
            throw
        }
    }

    Write-Host "[CHECK] Starting isolated local server" -ForegroundColor Cyan
    $previousGmSetting = $env:Game__GmCommandsEnabled
    $env:Game__GmCommandsEnabled = "true"
    try {
        $server = Start-Process -FilePath "dotnet" -ArgumentList @(
            ('"' + $serverDll + '"')
        ) -WorkingDirectory (Join-Path $root "server") -RedirectStandardOutput (
            Join-Path $logRoot "server.log"
        ) -RedirectStandardError (
            Join-Path $logRoot "server-errors.log"
        ) -PassThru
    }
    finally {
        if ($null -eq $previousGmSetting) {
            Remove-Item Env:Game__GmCommandsEnabled -ErrorAction SilentlyContinue
        }
        else {
            $env:Game__GmCommandsEnabled = $previousGmSetting
        }
    }

    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        if ($server.HasExited) {
            throw "Local server exited before becoming healthy. See $logRoot"
        }
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:5678/health" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
            if ($response.StatusCode -eq 200) {
                $ready = $true
                break
            }
        }
        catch {
            # Startup not complete yet.
        }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) {
        throw "Local server health check timed out. See $logRoot"
    }
    Invoke-Checked "Encrypted WebSocket Request/Response E2E" "dotnet" @(
        "run", "--no-build", "--project",
        "tests/SGO.Server.WireSmokeTests/SGO.Server.WireSmokeTests.csproj",
        "-c", "Release", "--", "ws://127.0.0.1:5678/"
    )
    Invoke-Checked "Encrypted WebSocket 10/50/100 load probe" "dotnet" @(
        "run", "--no-build", "--project",
        "tests/SGO.Server.WireLoadProbe/SGO.Server.WireLoadProbe.csproj",
        "-c", "Release", "--",
        "--url", "ws://127.0.0.1:5678/",
        "--sizes", "10,50,100",
        "--rounds", "2",
        "--setup-concurrency", "10",
        "--mixed-seconds", "5",
        "--timeout-seconds", "20",
        "--output", (Join-Path $logRoot "wire-load-probe.json")
    )
    if ($null -ne $godotCommand) {
        $previousTestServerUrl = $env:SGO_TEST_SERVER_URL
        $env:SGO_TEST_SERVER_URL = "ws://127.0.0.1:5678/"
        try {
            Invoke-Checked "Godot real-server NetworkManager E2E" $godotCommand @(
                "--headless", "--path", "client",
                "--script", "res://tests/real_server_network_e2e.gd"
            )
        }
        finally {
            if ($null -eq $previousTestServerUrl) {
                Remove-Item Env:SGO_TEST_SERVER_URL -ErrorAction SilentlyContinue
            }
            else {
                $env:SGO_TEST_SERVER_URL = $previousTestServerUrl
            }
        }
    }
    else {
        Write-Host "[SKIP] Godot real-server NetworkManager E2E: set GODOT_BIN or install godot/godot4." -ForegroundColor Yellow
    }
    Write-Host "[PASS] All local protocol checks passed." -ForegroundColor Green
}
finally {
    if ($null -ne $server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        Write-Host "[CLEANUP] Local test server stopped."
    }
    Write-Host "Reports and server logs: $logRoot"
}
