# SGO Local Server

C# .NET 8 / SQLite local interoperability server for the recovered Godot client.

- Server setup and usage: [server/README.md](server/README.md)
- Current implementation status, data-alignment rules, known gaps and next priorities: [server/STATUS.md](server/STATUS.md)
- Server architecture and database-provider boundaries: [server/ARCHITECTURE.md](server/ARCHITECTURE.md)

## One-click Windows tests

Double-click [`pull_build_test.bat`](pull_build_test.bat) on the `feature/local-server-net8` branch to select safe Git pull, Release builds, full smoke tests and optional 100–1,000 player AOI/WebSocket load tests. It uses [`tools/sgo_test_runner.ps1`](tools/sgo_test_runner.ps1) and writes reports to ignored `test-results/`.

See [Windows test runner instructions](tests/SGO_WINDOWS_TEST_RUNNER.md) for mode selection, isolated DB and safety rules.
