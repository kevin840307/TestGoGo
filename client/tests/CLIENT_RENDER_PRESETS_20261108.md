# SGO Client-only render presets / independent FPS (2026-11-08)

The changes touch ONLY the Godot Client. No Server code, packet schema, server-side AOI, item ownership, combat rules or timers changed. Server packets are still handled by Client; offscreen scenes are deferred while authoritative snapshots remain cached.

## Controls

- 效能模式: **畫質優先** enables old world enhancement; **一般** disables enhancement but retains normal player/monster artwork; **低負載** simplifies ordinary monsters/players and reduces effect updates; **多開** further prioritizes low-resource render paths.
- The independent **畫質增強** switch has been removed; old setting is ignored, visual preset indices and per-account values remain compatible.
- **畫面 FPS** uses an editable **SpinBox with up/down arrows** (no long dropdown; Range.step=1 and custom_arrow_step=5 preserve manual 144): 不限制 = 0, 30..120 in 5-FPS increments, 144. The arrows skip directly 0↔30 and 120↔144. Numeric entry is normalized; default 60, saved per account. Changing graphic preset NEVER changes FPS. Low means smoother rendering via less work, *not lower FPS*.

| Visual mode | Base effect budget | Monster visual AOI (enter/exit) | Ground item visual AOI (enter/exit) | World enhancement |
|---|---:|---:|---:|---|
| 畫質優先 | 100% | 2200 / 2600 | 2400 / 2800 | On |
| 一般 | 75% | 1800 / 2200 | 2000 / 2400 | Off |
| 低負載 | 35% | 1250 / 1650 | 1450 / 1850 | Off |
| 多開 | 15% | 950 / 1350 | 1100 / 1500 | Off |

Radii are world units and expand to cover the entire camera view (including zoom/offset and a safety margin). They affect **visual scene instantiation only**. Local combat targets, aggro/reveal monsters and pickup-requesting drops are kept. For deferred drops, server packet timestamps, ownership and TTL are retained. Background rendering suspension continues but networking and auto-combat remain live.

## Logs for comparisons

- `[PERFORMANCE_PROFILE]`: mode, combat, effect budget, chosen foreground FPS, quality enhancement, visual AOI base radii.
- `[RENDER_STATS]`: foreground FPS, actual draw calls, nodes, active/deferred monster and ground counts, effective AOI radii.
- `[PERF_ANALYSIS]`: `process_ms`, `physics_ms`, `cpu_process_time_ms`, `static_mem_mb`, texture/video resource allocations, process PID. **`cpu_frame_pct` is frame-time budget ratio, not real operating-system CPU%.** Godot video resources are not actual WDDM VRAM.
- On Windows, existing PowerShell script measures per-process real CPU (% of whole host), working-set/private RAM, GPU Engine activity and dedicated/shared GPU VRAM. Newly added `ModeLabel` distinguishes comparable runs:

```powershell
powershell -ExecutionPolicy Bypass -File client/tests/profile_multiclient_windows.ps1 -ProcessName SGO -ModeLabel "Low-60FPS" -Samples 30 -IntervalSeconds 5 -OutputCsv .\sgo_low_60.csv
```

Repeat for each mode with the SAME selected FPS, camera/resolution, map, player position, monster/drop load and focus state. Match `pid=` in Godot log to CSV `Pid`. Compare OS CPU%, working/private RAM, WDDM GPU, FPS, P95 frame time, draw calls and active nodes. Missing GPU counters remain blank rather than misleading zero.

## Regression scripts

```powershell
godot --headless --path client --script res://tests/performance_ui_settings_regression.gd # includes Engine.max_fps assertions
godot --headless --path client --script res://tests/performance_visual_aoi_regression.gd
godot --headless --path client --script res://tests/stationary_aoi_scan_regression.gd
godot --headless --path client --script res://tests/ground_item_aoi_regression.gd
godot --headless --path client --script res://tests/aoi_monster_snapshot_order_regression.gd
```

Manually test a full combat session in all modes: autofight, loot, moving/zooming, changing map, swapping presets and background focus. No Godot executable or live user client was available to this execution environment, so these scripts are **added but not claimed executed**. A measurable gain is **not yet proven**; verify with collected per-process Windows metrics.

## 2026-11-08: unlimited FPS and Mass combat FX-off

- 0 FPS means **unlimited**. `Engine.max_fps=0` plus disabling VSync when the chosen cap exceeds the monitor refresh or is unlimited; prior VSync policy is restored at supported lower caps. `[FPS_VSYNC]` logs requested cap, refresh Hz and the effective VSync mode. On drivers with additional FPS throttles (e.g. power/driver settings), 0 cannot force hardware beyond those limits. Unlimited can increase GPU usage and cause tearing.
- Mass combat enforces `effect_budget_percent=0`, `combat_effects_enabled=false`. Skill cast/hit/extra FX, projectiles, weapon FX sprites, ordinary hit/miss textures and item FX are skipped client-side. Damage/MISS numbers, character/monster body state, buffs, equipment transformations, hit points, pickup and server packets are untouched.
- FPS does **not** set `Engine.time_scale` nor `Engine.physics_ticks_per_second`. Local movement/attack remains in fixed `_physics_process`; the WebSocket poll, packet budget and per-second item/horse/pet prediction are also moved to that fixed tick, so lowering the *render cap* does not lower network poll cadence. This cannot guarantee identical visual latency under CPU overload: measure both state and frame latency.
- Regression: `performance_ui_settings_regression.gd` asserts 0 FPS and VSync policy; `performance_profile_regression.gd` checks Mass profile. Tests require real Godot runtime to be considered PASS.
