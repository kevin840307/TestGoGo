# SGO renderer-free AFK experiment (.NET 8)

**Experiment, NOT a production AFK switch.** Never attach it to an account that
is still logged in through Godot. The server allows one active connection per
account; a new login supersedes the old connection. There is no atomic session
handoff, preservation of all client-side automation or seamless restore yet.

## What it does

- Minimal independent .NET 8 executable. The csproj links only
  `server/Protocol/ClientProtocol.cs` as source; it does not load Godot or
  reference the full Server assembly.
- Real AES/Base64 WebSocket login, character selection, WorldReady, Ping/Pong,
  ChangeMap acknowledgement, passive authoritative packet reception.
- In-memory bounded dictionaries for monster and ground-item snapshots;
  no Godot sprites, map textures, HUD, rendering or local A*.
- Server-authoritative AutoSaint can be explicitly enabled only when the
  character has learned the two required skills. No client-side auto-routing,
  movement, item pickup or general auto-battle is emulated.
- Measures the **probe process** Working Set and Private Bytes, samples every
  second, writes JSON with the measured values.
- Does not manipulate process working sets or force GC.

## Build and run

From the repository root:

```powershell
dotnet build tools/SGO.AfkProbe/SGO.AfkProbe.csproj -c Release
dotnet run --project tools/SGO.AfkProbe -c Release -- --self-test
dotnet run --project tools/SGO.AfkProbe -c Release -- --synthetic --seconds 15 --output afk-synthetic.json
```

Real encrypted WebSocket using a **disposable, uniquely named account** on a
local test server. This inserts a throwaway account/character into that
test server's database; do not use a production DB.

```powershell
dotnet run --project tools/SGO.AfkProbe -c Release -- --url ws://127.0.0.1:5678/ --smoke-account --seconds 30 --output afk-wire.json

# Optional local-only Map13 monster AOI test (uses GM command on test account):
dotnet run --project tools/SGO.AfkProbe -c Release -- --url ws://127.0.0.1:5678/ --smoke-account --test-map 13 --seconds 30 --output afk-map13.json
```

For a *dedicated* existing test account only, use an environment variable
instead of placing the password on the command line:

```powershell
$env:SGO_AFK_TEST_PASSWORD = '<enter a test-account password>'
dotnet run --project tools/SGO.AfkProbe -c Release -- --url ws://127.0.0.1:5678/ --user '<test-account>' --password-env SGO_AFK_TEST_PASSWORD --character-id 123 --allow-session-replacement --seconds 30
```

Optional `--enable-auto-saint --mp-threshold 30 --sp-threshold 30` sends
the **existing server command**; the server must confirm it is enabled.
The program **does not implement or claim equivalent movement/combat/pickup**
for arbitrary AFK bots.

## Compare real Windows memory (same machine/build/time window)

Use the PID of the original Godot process printed by `[BACKGROUND_MEMORY]`
and of the standalone process from the JSON report. Do not mix the Godot
`static_mem_mb` with Windows process Working Set:

```powershell
pwsh -File tools/SGO.AfkProbe/Measure-WindowsMemory.ps1 -ProcessIds 12345 -Stage Game -Samples 30 -OutputCsv results/memory.csv
pwsh -File tools/SGO.AfkProbe/Measure-WindowsMemory.ps1 -ProcessIds 12345 -Stage AFK -Samples 30 -OutputCsv results/memory.csv
pwsh -File tools/SGO.AfkProbe/Measure-WindowsMemory.ps1 -ProcessIds 12345 -Stage Logout -Samples 30 -OutputCsv results/memory.csv
```

The script reads Windows `WorkingSetMB`, `PrivateWorkingSetMB` (when
the PerfProc counter is accessible) and `PrivateBytesMB` by exact PID.
It never trims pages. The default Task Manager Memory column may use a
different metric depending on OS version, so compare named columns.

**Decision:** Only consider an isolated/external AFK runtime if a real
server-connected session consumes meaningfully less **Windows Private Bytes**
and its reconnect/handoff correctness can be established. Synthetic
`--synthetic` measures just the data model, not real gameplay, networking
or continuity. No 100MB saving is promised.

## Remaining gaps / explicit stop conditions

1. Actual reconnect/atomic handoff is **not implemented**.
2. The standalone program only supports server-driven AutoSaint, not every
   client-originated combat, movement, pathfinding, loot or pet action.
3. CI's Map13 probe covers monster AOI snapshots but does not enable AutoSaint, move, or pick up drops.\n4. Character auto-battle, item economy and map transition equivalence need
   tests before integrating with the original AFK UI.
5. CI Windows probe results are **a different machine** from the user's
   Windows 10 + RTX 3050; user-machine acceptance remains necessary.
6. If Windows Private Bytes are not meaningfully lower, do not integrate this
   architecture. Stop and report the measurement.
