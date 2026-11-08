# SGO: GPU / CPU / RAM 1–5–10 client baseline

## 2026-11-08 render / allocation audit

The latest supplied `godot(20261108-052750).log` shows that
the login visual gate now reaches `[LOGIN_VISUAL_READY]` after 18 good
foreground frames; two subsequent login readings are 60 FPS. The
heavier runtime workload is a different problem:

| Single-client metric in this run | Maximum |
| --- | ---: |
| Godot static allocation | 460.1 MiB |
| Godot video resource allocation | 346.6 MiB |
| Draw calls in one sampled frame | 1,784 |
| Canvas nodes in one sampled frame | 13,866 |
| Ground items in the scene | 605 |
| Active entities | 693 |
| Largest foreground frame gap | 1,602.54 ms |

These numbers show why **10-window tests must monitor the total host
CPU/RAM/WDDM VRAM**, not just idle FPS. Godot's video metric does not
equal Windows per-process dedicated GPU memory.

### Concrete redundant work found and changed

1. `GameHUD._update_minimap()` requested a new **SubViewport
   UPDATE_ONCE every 50ms**, even when the camera/player stood still.
   The minimap world cull mask is layer 1, while monster/player/ground
   dynamic entities occupy layer 2. Live NPC and other player markers
   are rendered in a **separate Control overlay**, so the map background
   itself can be reused while stationary. It now redraws immediately
   whenever the camera moves or a map/focus/UI change requests a redraw,
   and once per second during idle periods. Overlay marker updating
   continues at its original cadence. This eliminates up to **19 of 20
   static-map redraw requests per idle second**; GPU time saving is an
   expected effect, not yet measured.
2. `NetworkEntityManager` already deep-copied the server's Monster
   SpawnEntity packet on receipt. When dequeuing a monster it used
   to deep-copy the entire metadata tree **again**, even though its
   canonical template had already been updated by newer MoveEntity
   packets. It now reuses that canonical cached metadata. Only the
   compatibility path for an uncached direct spawn needs a new deep
   copy. This avoids duplicate CPU/temporary RAM allocation and
   preserves the latest authoritative monster coordinates.
3. `GameManager` previously cleared icon/animation/resource caches
   **on every focus-out**. Yet the supplied log still measured roughly
   446–448 MiB static allocation around repeated focus transitions.
   Scene-held textures remain referenced and trimming does not
   guarantee memory release; repeated Alt+Tab can force costly reloads.
   Cache trimming now occurs at most **once per 60 seconds**, and only
   when a world is loaded. Background rendering-disabled, audio mute,
   HUD pause and the existing 30 FPS engine cap are not changed.
   `[FOCUS_POWER_SAVE] caches_trimmed=true/false` now reflects the
   action actually taken.

Verify:

```powershell
godot --headless --path client --script res://tests/multiclient_render_dedup_regression.gd
godot --headless --path client --script res://tests/focus_performance_regression.gd
godot --headless --path client --script res://tests/render_resource_regression.gd
```

For live tests, compare cumulative `minimap_world_redraws` and
`minimap_world_skips` in `[RENDER_STATS]`. While standing still for
10 seconds, about 10 static map redraw requests should occur instead
of about 200. Verify that NPC/player markers still animate/update,
and that moving the player immediately updates the minimap.

Also validate 1 focused + 9 background: WebSocket, auto-combat,
monster movement packets, ground item existence/pickups and timers
continue. A log marked `render_stats_valid=false` must never be
interpreted as actual background GPU draw calls.

### Next candidates requiring measured profiling, not blind removal

- **Hundreds of drops and >13k Canvas nodes**: investigate
  view-dependent item labels / draw batching, but preserve all items'
  authoritative coordinates, click/pickup and return-to-map snapshots.
- **1.6-second world-running stall**: add targeted phase timing around
  affected hidden catalog/UI construction, resource decoding and
  resource cache refill. A frame-gap alone does not identify the
  responsible function.
- **Long-lived texture caches**: instrument counts and WDDM memory,
  then compare image atlas/material sharing before introducing
  LRU/eviction policies that could make focus-in worse.
- **10× visible world vs 1 foreground + 9 idle**: both should be
  measured separately. Don't claim 10× performance from a single
  client's 60 FPS.

## Goal and scope

Target **10 concurrent exported Windows clients** without breaking movement,
targeted skills, monster visibility, ground pickups, audio restore, map
switches, or WebSocket/server state. Two distinct tests are required:

1. **1 focused + 9 unfocused**: user actively controls one client; the
   other nine stay logged in and continue receiving network/world state.
   They are allowed to stop rendering under the existing focus policy.
2. **10 visible and rendering**: all windows are visible on-screen, but
   Windows can focus only one at a time. This is **not** the same workload
   as 10 clients each drawing at 60 FPS. Use screenshots and focus flags
   to record what actually rendered; never infer FPS solely from idle
   windows.

No code path stops WebSocket, character synchronization or game rules to
achieve the target. The user's background policy remains **30 FPS cap with
render loop disabled**, except the existing startup-loading exception.

## Current reference: godot(20261108-042909).log

Godot 4.7.2 / NVIDIA RTX 3050 / OpenGL Compatibility:

- Single-client foreground: commonly **60 FPS**.
- Highest observed Godot GPU resource allocation **326.8 MiB**;
  highest observed texture allocation **289.9 MiB**.
- Highest observed foreground draw calls: **1,322**.
- Highest observed static memory: **343.3 MiB**; peak static
  allocation observed in later log: **403.4 MiB**.
- Large map transitions produced **284–307ms** stalls.
- Background rendering-disabled samples are tagged
  `render_stats_valid=false`: **never** use these draw calls for
  comparisons. `RENDER_VIDEO_MEM_USED` measures engine allocations,
  **not actual GPU utilization nor OS-reported VRAM**.
- `process_ms` and `cpu_frame_pct` are not Windows per-process CPU usage.
  They cannot be multiplied by ten to predict total host CPU percentage.

A naïve 10× GPU-resource estimate from the largest observed value is
roughly 3.3 GiB, before driver, swap chains, allocations, other
programs and per-process overhead. This is planning only; the real 10
client measurement is mandatory.

## Changes to benchmark

- PaperDollEngine now shares **exactly equivalent** hue/effect
  ShaderMaterials across all doll parts and instances.
- MonsterEntity shares hue-only ShaderMaterials for identical monster
  ColorIndex, without altering animation frames or sprite resources.
- On focus-out, GameManager now actually invokes the existing visual
  pause path. Cursor scan, minimap refresh and the day/night overlay
  visual process pause, and resume on focus-in; combat/world logic and
  networking are not paused.
- Fixed Godot 4.7.2 static type inference errors in
  `legacy_media_resolver.gd` (otherwise GameHUD dependencies fail to
  compile in the supplied log).
- Every debug `[RENDER_STATS]` and `[PERF_ANALYSIS]` line includes
  `pid`, `window` width/height, two palette pool sizes, focus and
  `gpu_memory_metric=godot_resources`.

## Windows measurements

First build **exported** clients (do not use 10 Godot editor instances,
as the editor introduces large extra CPU/RAM/GPU overhead). Start with 1
client, then 5, then 10. Hold the same map, monster population, camera
position, resolution, effects, HUD and time-of-day. Repeat foreground
and background runs. Start each round with a new CSV name.

```powershell
powershell -ExecutionPolicy Bypass -File client/tests/profile_multiclient_windows.ps1 `
  -ProcessName "SGO" -Samples 36 -IntervalSeconds 5 -OutputCsv ".\sgo_1.csv"
```

For five/ten instances, use `sgo_5.csv` / `sgo_10.csv`, and verify
the script prints `clients=5` or `clients=10`. If exported .exe has a
different name, supply its ProcessName (without `.exe`).

The PowerShell script **does not launch or interact with clients**.
It writes one row per PID and sampling interval: whole-host-normalized
process CPU percentage, working set, private bytes, thread/handle
count, and optional WDDM per-process GPU dedicated/shared memory
and engine-activity counters. Windows may not expose the WDDM counters;
blank columns mean **not available**, not zero. The engine-activity sum
is **not** the NVIDIA GPU global busy percentage. Also record overall
GPU 3D utilization, VRAM, and dedicated process GPU memory in Windows
Task Manager > Performance / Details or a trusted GPU profiler.

## Acceptance and regressions

Compare at equal conditions, **not** foreground versus throttled background:

- Foreground: stable gameplay at 60 FPS target (report median and 1%
  low), no new delayed clicks, absent monsters, dropped-item loss, or
  long synchronized frame gaps.
- Background: nine clients remain connected; inventory, timers,
  monster state and incoming packets continue; after Alt+Tab all
  character animation, cursor, minimap, audio and skill targeting
  resume normally.
- Profile peak Windows process RAM **and** dedicated WDDM VRAM across
  all ten, not just Godot internal static/video memory.
- For 10× visible rendering with high effect counts, record whether
  VRAM, GPU busy%, or CPU is saturated before claiming a GPU fix.
- Repeat Map13 -> Map15 -> Map13, high-drop situations, simultaneous
  monsters/skills, NPC interaction, mecha and player cosmetics.
- Compare draw calls, texture resource memory, paper/monster
  ShaderMaterial pool counts **before vs after** at identical scenes.
  Lower allocations are expected, but exact GPU/FPS improvements must
  be measured on the target machine.

Focused client smoke test:

```powershell
godot --headless --path client --script res://tests/multiclient_gpu_regression.gd
godot --headless --path client --script res://tests/focus_performance_regression.gd
godot --headless --path client --script res://tests/render_resource_regression.gd
```

**The current environment cannot execute Godot or Windows PowerShell**.
Do not mark the runtime tests or 10-client target PASS until the
target system produces baseline and post-change measurements.
