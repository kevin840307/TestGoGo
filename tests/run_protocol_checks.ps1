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
    Invoke-Checked "Smoke suite structure tests" "python" @(
        "-m", "unittest", "discover", "-s", "tests",
        "-p", "test_smoke_suite_structure.py", "-v"
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

    Invoke-Checked ".NET Server build" "dotnet" @(
        "build", "server/SGO.Server.csproj", "-c", "Release"
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
    $server = Start-Process -FilePath "dotnet" -ArgumentList @(
        ('"' + $serverDll + '"')
    ) -WorkingDirectory (Join-Path $root "server") -RedirectStandardOutput (
        Join-Path $logRoot "server.log"
    ) -RedirectStandardError (
        Join-Path $logRoot "server-errors.log"
    ) -PassThru

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
    Write-Host "[PASS] All local protocol checks passed." -ForegroundColor Green
}
finally {
    if ($null -ne $server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        Write-Host "[CLEANUP] Local test server stopped."
    }
    Write-Host "Reports and server logs: $logRoot"
}
