# SGO Windows Private Bytes root-cause audit

This is a **read-only Windows x64 external process**. It targets the Godot
client, not the separate experimental AFK program.

The latest comparison on the target PC showed post-logout Windows
Private Working Set about 129 MiB above initial login and Private Bytes
about 262 MiB higher. Internal Godot static-memory measurements do not
explain that gap.

We use VirtualQueryEx to group committed virtual memory by MEM_PRIVATE,
MEM_MAPPED and MEM_IMAGE. We also report OS Working Set and Private Bytes.

Important: the tool never invokes SetProcessWorkingSetSize,
EmptyWorkingSet or garbage collection in Godot, and never changes
rendering, networking, packet handling, fixed ticks, combat or AFK UI.
Values are a single momentary snapshot, so the existing
tools/SGO.AfkProbe/Measure-WindowsMemory.ps1 remains useful for
time-series of Private Working Set.

## Build and self-test

From the repository root on Windows:

    dotnet build tools/SGO.WindowsMemoryAudit/SGO.WindowsMemoryAudit.csproj -c Release
    dotnet run --project tools/SGO.WindowsMemoryAudit -c Release -- --self-test

Self-test reserves and touches 64 MiB inside the audit process, verifies
that private committed memory and Windows Private Bytes increase by
at least 48 MiB, and then frees this memory. It never touches Godot.

## Four stages on the same client process

Copy the Godot PID from its [BACKGROUND_MEMORY] log line. Example PID
11232 is from a previous run and MUST be replaced after restart.

    $gamePid = 11232
    dotnet run --project tools/SGO.WindowsMemoryAudit -c Release -- --pid $gamePid --stage Login --output results/vm-login.json
    dotnet run --project tools/SGO.WindowsMemoryAudit -c Release -- --pid $gamePid --stage Game --output results/vm-game.json
    dotnet run --project tools/SGO.WindowsMemoryAudit -c Release -- --pid $gamePid --stage AFK --output results/vm-afk.json
    dotnet run --project tools/SGO.WindowsMemoryAudit -c Release -- --pid $gamePid --stage Logout --output results/vm-logout.json

Take the snapshots at stable initial login, Map13, original AFK and
after returning to login and waiting 10 seconds. Then compare:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/SGO.WindowsMemoryAudit/Compare-WindowsMemoryStages.ps1 -Login results/vm-login.json -Game results/vm-game.json -AFK results/vm-afk.json -Logout results/vm-logout.json

Snapshots must all have the same PID, resolution, graphics settings and
map. If a working-set trimmer is installed, disable it for the
acceptance comparison; otherwise it can distort resident-memory figures.

## Interpretation

- PrivateBytesMB is the Windows OS-level private committed-memory metric.
- PrivateCommittedMB is VirtualQueryEx committed virtual memory with type
  MEM_PRIVATE. This is not identical to Private Bytes.
- MappedCommittedMB and ImageCommittedMB are committed virtual memory
  mapping categories, NOT physical RAM or GPU VRAM measurements.
- TopPrivateAllocations lists the largest committed private allocation
  bases, useful for detecting ranges that persist in the same process.

This diagnostic **does not reveal allocation call stacks or conclusively
attribute memory to the Godot allocator or graphics driver**. If the
post-logout delta is in private committed ranges, use a Windows
allocation trace (WPR or VMMap snapshots) to identify the owner
before removing further graphics features or clearing caches.
