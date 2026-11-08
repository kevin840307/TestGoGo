# Local Server

C# .NET 8 / SQLite local interoperability server for the recovered Godot client.

- Server setup and usage: [server/README.md](server/README.md)
- Current implementation status, data-alignment rules, known gaps and next priorities: [server/STATUS.md](server/STATUS.md)
- Server architecture and database-provider boundaries: [server/ARCHITECTURE.md](server/ARCHITECTURE.md)

## One-click Windows tests

Double-click [`pull_build_test.bat`](pull_build_test.bat) on the `feature/local-server-net8` branch to select safe Git pull, Release builds, full smoke tests and optional 100–1,000 player AOI/WebSocket load tests. It uses [`tools/sgo_test_runner.ps1`](tools/sgo_test_runner.ps1) and writes reports to ignored `test-results/`.

See [Windows test runner instructions](tests/SGO_WINDOWS_TEST_RUNNER.md) for mode selection, isolated DB and safety rules.

## Godot Client tests (Windows)

Double-click [`run_godot_client_tests.bat`](run_godot_client_tests.bat) to run **Client-only** regression tests separately from the Server runner. On first use it asks for the Godot EXE and remembers the full path under `%LOCALAPPDATA%\SGO\GodotClientTests\godot_path.txt`. The default example points to `C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe`. It imports the project first, then launches dedicated test scenes so AutoLoad globals resolve. See [Godot Client BAT documentation](client/tests/GODOT_CLIENT_BAT.md).
