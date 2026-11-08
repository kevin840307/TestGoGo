# SGO Client Performance Regression Checks

## Idle AOI scan deduplication and transition diagnostics (2026-11-08)

Evidence: supplied `godot(20261108-075204).log` (Godot 4.7.2 / RTX 3050).
The run contained **32 frame-gap events**, including an initial HUD
`426.56 ms` gap, player add-child `302.77 ms`, and several
`~269–302 ms` stalls adjacent to **map transitions** rather than
steady-state `world_running` work. Peak canvas node count was **7,085**,
draw calls **1,164**, and known ground items **607**.

Existing client AOI already avoids instantiating distant monsters and ground
items, but the scheduler still scanned both complete known sets at fixed
intervals **even when the observer remained stationary**:

- Monster AOI: every **200 ms** (5 checks/sec). The server `MoveEntity`
  path already updates deferred monster templates and can promote them
  immediately; a stationary full scan is rarely needed.
- Ground-drop AOI: every **250 ms** (4 checks/sec). `SpawnEntity` already
  records every item, including deferred and out-of-range drops, and
  `DespawnEntity` removes them; drop physics, expiry and ownership remain
  server-authoritative.

New behavior: when the observer or camera/radius changes, preserve the
original 200/250ms scan cadence. When stationary, perform a complete scan
**at least once every 1 second** as a fallback. This can skip up to roughly
**80% of idle monster scans and 75% of idle ground scans**, but is **not**
a measured CPU-percentage or GPU improvement. Newly arriving/moving actors
still follow their existing immediate packet handlers; pickup-requesting
ground objects are not unloaded. Map cleanup and re-entry clear the cached
observer position to force a fresh scan.

`[RENDER_STATS]` now includes cumulative, per-map
`monster_aoi_scans`, `monster_aoi_skips`, `ground_aoi_scans`, and
`ground_aoi_skips`. Compare those counters while standing still versus
walking / using the big map / resizing the viewport. The numbers are
**counts of scans**, not CPU samples.

`[FRAME_GAP] stage=...` now identifies map loading, cleanup, scene
instantiation, add-child and player reparent phases separately. This
prevents a 280ms map transition from being attributed to normal world
rendering, without hiding/ignoring the stall.

Tests:

```powershell
godot --headless --path client --script res://tests/stationary_aoi_scan_regression.gd
godot --headless --path client --script res://tests/aoi_monster_snapshot_order_regression.gd
godot --headless --path client --script res://tests/aoi_pre_spawn_move_regression.gd
godot --headless --path client --script res://tests/render_optimization_regression.gd
python client/tests/resource_uid_reference_regression.py
```

Manual acceptance: remain still 30s as drops and monsters appear/disappear;
walk continuously and stop; teleport twice; change map 13→15→13; vary
camera zoom and client resolution; leave auto-combat active while Alt+Tab
or running multiple clients. Verify that all monsters, nearby drops and
NPC interactions continue and no item disappears or respawns at a stale
location. Measure FPS and Godot/Windows memory before claiming 10-client
improvement. **Runtime Godot tests have not been executed in this tool
environment.**


## Login input readiness and smoothness (2026-11-08)

The previous login flow started the looping OGV while
`LoginScreen.visible=false`, waited only **two process_frame signals**,
then revealed the account/password form and immediately grabbed keyboard
focus. Neither `VideoStreamPlayer.play()` nor two process frames guarantee
the first decoded video frame or its GPU upload has been drawn. In
`godot(20261108-045738).log`, the input form appeared while focus
power-saving was still active at **10 FPS**; a later focused first-frame
gap reached **149 ms**.

The new startup policy:

- Keep the existing **opaque loading overlay and progress bar** visible.
- Complete required item/skill/pet indexing, scene resource preloading,
  non-crafting game-info preparation, and login BGM.
- Make the actual login scene, including the background OGV and login
  panel, visible **behind the overlay**. Lock account/password LineEdits,
  Login and Register buttons: typing and login requests remain disabled.
- Start background video and wait until `VideoStreamPlayer` has
  actually advanced its stream position (not just `is_playing()`).
- Repair a launch-time Windows focus notification lag only during
  initial bootstrap, after 3 consistent `window_is_focused()` polls.
  Never release input while the window is unfocused or still running
  at the intentional 10 FPS background startup throttle.
- Require **18 consecutive drawn foreground frames** with frame
  intervals no longer than **42 ms**, resetting on any slower or
  non-rendered frame. Count `Engine.get_frames_drawn()`, not just
  `SceneTree.process_frame` ticks.
- If OGV decoding fails to advance after 3.5s of **foreground** warmup,
  or foreground render remains unstable after 6s, disable that video
  and use the original static background. Video cannot block login
  indefinitely by itself.
- Only after visual readiness: animate the loading bar to 100%, unlock
  the form, take account input focus, and finally hide the overlay.
  Remove the unnecessary one-second login fade which previously hid the
  editable form during its initial reveal.
- Keep the original ASCII-only text validation, account suggestions,
  remembered accounts, server login packets, map handling, and
  on-demand crafting unchanged.

Debug events:

- `[LOGIN_VISUAL_READY] elapsed_ms=... stable_frames=18 video_fallback=...`
  occurs **before** `checkpoint=login_screen_ready`.
- `[LOGIN_VIDEO_FALLBACK]` only if the OGV is too slow or consistently
  causes unstable foreground frames.
- `[LOGIN_STARTUP_FRAME_GAP]` remains active for the first six
  seconds after login reveal. It never prints credentials, their
  lengths, or typed characters.

```powershell
godot --headless --path client --script res://tests/login_visual_readiness_regression.gd
godot --headless --path client --script res://tests/login_input_startup_regression.gd
godot --headless --path client --script res://tests/startup_loading_regression.gd
```

**Manual required:** Cold-start with the window focused and in the
background; after launching, press keys immediately, verify no account
field accepts text while loading, and check responsive input only after
`[LOGIN_VISUAL_READY]`. Test Alt+Tab during the last 100% loading bar,
invalid login, registration, successful login, and 1/5/10 client launches.
The overlay intentionally remains visible until visual readiness; when
a client remains unfocused, it awaits foreground focus rather than
showing an input-enabled form at 10 FPS.

Runtime Godot 4.7.2 is unavailable in the tool environment: static
GitHub checks do NOT prove the real rendering/timing test passed.

## Craft first-open lazy loading (2026-11-08)

Previously, login startup instantiated CraftDismantleWindow and built
**1,453 Tree recipes** before the account/password form appeared. In the
`godot(20261108-045738).log` cold-run observation, that work took
**17,990.66 ms** in background startup mode (10 FPS). The same window was
also a mandatory part of GameHUD's before-world-reveal path, meaning
removing ONLY the login prewarm would shift the stall to world entry.

Now:
- `EntryManager` does **not** thread-load or instantiate the craft window
  during login; other essential resource indexing and loading stages remain.
- `GameHUD.prepare_lazy_windows_before_reveal` excludes `craft`.
  World creation/character movement no longer waits for 1,453 recipes.
- The first **actual click** on the craft menu or Ctrl+G opens the craft
  window and displays a content-area progress panel **before** building.
  It reuses the existing time-budgeted, frame-yielded recipe builder,
  showing recipe progress from 0–90%, then player inventory availability
  progress from 90–100%.
- The window retains its built Tree and cached recipes; subsequent
  openings do not rebuild static recipe rows. The existing on-demand
  dismantle tab remains independent. Repeated first-open clicks,
  inventory-sync refreshes and dismantle-tab changes are guarded against
  concurrent static Tree construction.
- The title/close button remain accessible while the progress indicator
  covers the unfinished content, and no gameplay networking/server logic
  is paused.
- Debug log `[CRAFT_FIRST_OPEN] recipes=1453 total_ms=...` identifies
  the lazy-load cost. The historical `[CRAFT_STATIC_PREWARM]` name can
  still appear on the *first use* (method name retained for compatibility);
  it must no longer appear **before** `login_screen_ready`.

Test without a server:

```powershell
godot --headless --path client --script res://tests/craft_first_open_regression.gd
godot --headless --path client --script res://tests/npc_shop_close_and_crafting_regression.gd
godot --headless --path client --script res://tests/startup_loading_regression.gd
```

Manual check: cold-start -> account input (no recipe progress/18-second
wait), select character -> initial world load (no craft creation), first
click crafting (visible progress), close/reopen crafting (instant reuse),
switch to dismantle and back, change inventory, verify available recipe
quantities and normal packets. Also validate 5/10 client startup; no
background client should allocate its entire recipe UI until specifically
opened. **No Godot runtime is available in this tool environment, so
tests require execution on the target system.**

## Initial loading screen (independent of optional crafting)

The entry scene still shows its LoadingOverlay, progress bar and status
label from the initial scene state. Item/skill/pet indexing starts only
after two process-frame signals so the initial loading surface can render
before the synchronous indexing work.

**Change from the previous implementation:** the login stage no longer
constructs the full crafting recipe Tree. The old 60–85% startup
"預先建立合成配方" steps are retired; craft progress is now
shown only inside the crafting window on first use. Startup continues to
prepare essential resources, game-info window, login video and BGM.

```powershell
godot --headless --path client --script res://tests/startup_loading_regression.gd
godot --headless --path client --script res://tests/smooth_loading_bar_regression.gd
godot --headless --path client --script res://tests/craft_first_open_regression.gd
```

On a cold start, the account/password screen must appear without any
`[CRAFT_STATIC_PREWARM]` / `[LOGIN_HUD_PREWARM] kind=craft`
before `login_screen_ready`. The startup overlay stays visible through
essential initialization. If Windows initially treats the app as
unfocused, the bootstrap loading surface can render at 10 FPS; after
login reveal, the original 30 FPS background cap, render-loop-off,
muted policy resumes. This exception does not affect world networking.

## Foreground/background performance diagnostics (focus-aware)

Godot's focus power-save behavior is **intentional**: on focus loss the client
sets `Engine.max_fps` to the configured background cap (currently 30), disables the RenderingServer loop, mutes the Master
audio bus, and trims selected transient caches. On refocus it restores the
previous FPS setting, rendering, and the previous mute state. This change adds
**debug-only telemetry**, not new game/application throttling.

- `[FOCUS_POWER_SAVE]`: emitted at startup, focus transitions and when
  the initial loading-overlay exception begins/ends.
  `focus_query_disagrees=true` means the synchronous
  `DisplayServer.window_is_focused()` query disagreed with the delivered
  focus event at that instant. This can be an OS notification/query ordering
  race; verify subsequent periodic `focus_actual` before diagnosing failure.
- `[MONSTER_CHASE_PACKET_GAP]`: appends
  `focus_changed_in_gap=true/false` and current focus/render status.
  A long interval that crosses Alt+Tab is **not** proof of missing server
  movement packets: inactive/idle monsters may legitimately have long gaps.
  Correlate with server movement and foreground-only traces.
  Reports `event`, `focus`, `focus_actual`, `power_save`, `fps_cap`,
  `render_loop`, `audio_muted`, `focus_age_ms`, `focus_changes`, and
  whether caches were trimmed.
- `[PERF_ANALYSIS]`: adds the same focus context to every checkpoint/periodic
  sample, plus `cpu_metric=frame_budget_ratio`. **cpu_frame_pct is NOT Windows
  process CPU utilization**: it is a game-thread frame-budget estimate.
- `[RENDER_STATS]`: adds the same focus context and
  `render_stats_valid=false` when the render loop is disabled. Draw calls
  recorded during that period may be stale; do **not** compare them to
  foreground rendering.
- `[FRAME_GAP]`: expected background intervals (currently ~33 ms at 30 FPS) are
  **not** emitted as stalls. Unexpectedly long background gaps remain logged
  as `kind=background_stall`; focused stalls are
  `kind=foreground_stall`. Focus transitions reset the frame-gap timestamp
  so intentional background sleep is not blamed on the foreground.
- Memory can remain high after focus loss because shared textures/resources
  or allocator reservations are retained. Compare memory only at the same
  phase, map, population, and focus state, not based on whether FPS fell.

Run:
```powershell
run_godot_client_tests.bat focus (Windows; imports project and runs scene)
```

Manually verify: in-game foreground → Alt+Tab away for 10 seconds → return,
and repeat during a map transition. Expect one `focus_out` and one
`focus_in` event per transition, no repetitive 200 ms `FRAME_GAP` spam,
a configured FPS cap (currently 30)/render-loop-off/muted report while unfocused, and restored
foreground settings and sound on return. Confirm skill, monster, dropped item,
map transition and network behavior are unchanged. Never compare focused
and unfocused samples as if they represented identical rendering workloads.

## Automated, server-free smoke test

After Godot has imported the project, run from the repository root:

```powershell
godot --headless --path client --script res://tests/perf_regression_smoke.gd
```

Expect exit code 0 and `[PERF_SMOKE] ALL PASS`. This tests: PaperDoll and monster SpriteFrames cache reuse, no-op ACT timing identity, spawn queue priorities (ground item > NPC > monster), AOI monster coordinates and nested ExtraData preservation.

To test monster chase interpolation and authoritative attack-position corrections:

```powershell
godot --headless --path client --script res://tests/monster_chase_regression.gd
```

Expect exit code 0 and `[MONSTER_MOVE_TEST] ALL PASS`. The test now includes
viewport-edge interpolation and render-only smoothing of a 70px
`SpeedTime=0` server correction; the underlying collision body must snap to
the authoritative position immediately. Large corrections (over 96px) and
death should not retain a visual offset.

## Player navigation and old-map rollback regression

Run this independent, server-free regression after importing the project:

```powershell
godot --headless --path client --script res://tests/player_map_movement_regression.gd
```

Expect `[PLAYER_MOVE_TEST] ALL PASS`. This covers switching from Map15
`(1268,317)` to Map13 `(3104,2918)`, invalidating the old cached walkable
position immediately, and resetting `data_model.map_id`, movement targets,
and the per-map position anchor for the new scene. A failed pathfinding query
must no longer issue `SyncPosition` using the old anchor.

**Manual acceptance:** enter Map13 and click a nearby walkable tile; the
player must move smoothly without `[MOVE_BLOCKED] ... To=(1268,317)`.
Then cross Map13 → Map15 → Map13 repeatedly and test the first click after
each transition. Also test an unreachable/blocking tile to confirm the
player stops without an out-of-map coordinate rollback.

Check Client `[PLAYER_MAP_NAV_RESET]`, `[PLAYER_MAP_ID_REPAIR]`, and
`[PLAYER_SYNC_SKIPPED]` (Debug builds), along with Server `[MOVE_BLOCKED]`.
The server's collision and anti-wall validation must remain unchanged.

## Mecha dismount rollback: same-map repro

The user reported the `Map13 From=(3104,2918) To=(1268,317)`
`local_route_failed` errors specifically **after dismounting**. That delta
is much too large to be a normal short click. The server-side validation
should NOT be weakened.

Reproduce with Debug client and Debug server:

1. Load Map13 and stand on regular walkable ground; mount a mecha.
2. Move a short distance, stop, dismount, then click a nearby location.
3. Verify `[MECHA_DISMOUNT_SERVER]` and
   `[MECHA_DISMOUNT_REBASE]` report the same map and server location;
   observe the displayed `diff` and confirm it is not silently reused.
4. Confirm no repeated `[MOVE_BLOCKED]` jumps to `(1268,317)`.
5. Repeat after teleporting between maps; NPC, skills, character appearance
   and ordinary mounted movement must remain unchanged.
6. On a mecha-only special-terrain tile, dismount must be rejected with the
   explanatory message. Walk back to regular ground and retry.

The encrypted real-WebSocket suite also validates the authoritative map/X/Y
on successful `DriveMechaResponse`, then remounts to preserve the original
combat setup. Run it only against the dedicated isolated test server.
`dotnet run --project tests/SGO.Server.SmokeTests -- --suite world`
checks the special-terrain dismount invariant without requiring a live server.

It is **not** an end-to-end game test. In this editing environment Godot was unavailable; these tests have not been executed.

## Rare corner jitter while casting during movement

Run `player_map_movement_regression.gd` for walking/buff sync,
waypoint-step clamping, preserved skill locks and targeted-skill ownership.
Run `perf_regression_smoke.gd` for zero-allocation template position updates.

Manually test in matching Client + Server Debug sessions:

1. Hold left mouse to move along straight paths, cast a buff/self skill.
   Character movement/animations and `SyncPosition` should remain normal.
2. Follow a route around a tight wall corner while targeting a monster
   using a skill. No repeat ±5px corner oscillation.
3. Repeat after dismount and through Map13 → Map15 → Map13. Avoid any new
   collision bypass, blocked movement, or attack/cast-range regression.
4. Check ordinary attack, monster direction, NPC clicks, pickup, skill effects,
   UI and nearby entities for unchanged behavior.

`[PLAYER_POSITION_CORRECTION]` prints only in Debug builds and is limited
to once per second during repeated corrections. Compare client messages to
server `[MOVE_BLOCKED]` for the same session. A client-only file cannot
fully identify why the server rejected a position.

## Critical regression: invisible monsters after returning to a map

The Godot loading-overlay completion sends `WorldReady` after each map
transition. The server previously responded with **only ground items**, even
though the client can discard early NPC/monster packets while tearing down
the previous scene. Server now responds with the authoritative NPC, monster,
ground-item, player and active-pet snapshot, and cancels remaining periodic
full-map retries. This is required to ensure a visible, clickable target exists.

Client also temporarily prioritizes cached monster creation if the server
reports its aggro, attack, skill cast or combat damage while the monster is
AOI-deferred. This avoids invisible attackers without disabling distant-monster
AOI culling, weakening walls or changing combat range.

Run:
```powershell
godot --headless --path client --script res://tests/perf_regression_smoke.gd
dotnet run --project tests/SGO.Server.SmokeTests
```

The server integration suite includes `MapReadySnapshotSmokeTests.cs` and
must confirm the NPC and monster snapshot plus cancellation of redundant
full-map retries.

**Manual acceptance (both Client and Server Debug):**

1. From Map13, switch to Map31 and return to Map13. Repeat with
   Map13 → Map15 → Map13 at least five times.
2. Let a monster target and damage the player; it must be rendered and
   selectable. Test normal attack, targeted skill, non-targeted AoE, death
   and respawn, as well as NPCs, pets, and ground items.
3. Confirm `[MONSTER_COMBAT_REVEAL]` appears for any previously deferred
   attacker and never continuously requeues the same monster.
4. Compare `[RENDER_STATS]` active/deferred/visible monsters with server
   map presence. Check stable FPS, no new missing-entity states or repeated
   snapshot packet storms.

Regression tests are added but **not claimed as executed** in an
environment without local Godot/.NET runtime.

## Follow-up: monster snaps and restored ground items after map return

The server ground-item snapshot now sets `IsSnapshot=true`. Only a
newly spawned drop can play the monster-death bounce animation. This avoids
the full active-monster scan and temporary hidden sprite for every restored
drop; no change to the 60-second server-side drop lifetime.

AOI unload must keep the last authoritative MoveEntity coordinates rather than
the interpolating MonsterEntity render position. Server correction
SpeedTime=0 still updates the collision/targeting position instantly; for
visible small/moderate corrections the sprite/UI now visually ease for 0.24s.
Actual teleports greater than 160 px remain immediate.

After Map13 -> Map15 -> Map13, and Map13 -> Map31 -> Map13:
- Check the same living monster never reappears at its spawn location and then
  visibly jumps 20-101px to the server combat position.
- Check items dropped less than 60 seconds ago reappear, including when a dead
  monster occupies the same position; expired items should not reappear.
- Confirm a restored ground item is clickable and pickup works.
- Check `[RENDER_STATS] ground_items=... ground_nearby_visible=...`
  to distinguish absence of server-sent items from culling or rendering.
- Re-run targeted skill, non-targeted skill, NPC click, pet/mecha,
  monster pursuit, offscreen re-entry and pickup while moving.

This regression is covered by client `perf_regression_smoke.gd` and server
`MapReadySnapshotSmokeTests.cs`; these automated tests have not been
executed in this environment without Godot/.NET.

## Smooth loading percentage regression

The initial startup, login/character selection and in-world map transitions
now share `scripts/smooth_loading_bar.gd`. The stage targets still represent
real completed work; only the visual percentage is eased. Regressions and
late packets cannot decrease the target. On success the bar eases to 100%
during the existing fade without extending load or network lifecycles.

Run `godot --headless --path client --script res://tests/smooth_loading_bar_regression.gd`.

Check: launch → login, login → character select, character select → Map13,
Map13 → Map15 → Map13, and a missing map path. Verify monotonic smooth fill,
no abrupt 90→100 jumps, the same overlay teardown and exactly one original
WorldReady handshake per transition.

## Daytime zero-opacity overlay culling

The day/night shader mathematically outputs alpha=0 when
`night_strength == 0`. The daylight phase no longer draws its fullscreen
ColorRect or recomputes the player screen UV each frame. Dawn/dusk/night
immediately re-enable the original shader as soon as night strength exceeds
zero; the original calendar time rules and visual uniforms are untouched.
This optimization does not disable the player's environment effects.

`render_resource_regression.gd` checks daylight / dusk / night toggles.
Manual visual test: start during daylight, then verify shader output at
dusk/night and that walking moves the lit radial region as before.
Compare Draw Calls and CPU with the same map/time before and after.

## Focused render-resource cleanup (no gameplay rule changes)

The previous design allocated an invisible TargetIndicator AnimatedSprite2D
for every monster during setup. Target indicators now create only after the
first server-confirmed target state and pause while offscreen. This retains
the existing icon, attack/click state and animation when visible.

Ground items retain their own Area2D, Sprite2D, CollisionShape2D, item
name and ownership UI. Identically sized icons now share one immutable
RectangleShape2D; identically tinted drops share one palette ShaderMaterial
(no per-instance shader uniforms are updated after initialization). Both
preserve the old padded click area and the displayed colors.

Run:
```powershell
run_godot_client_tests.bat resources (Windows; imports project and runs scene)
godot --headless --path client --script res://tests/perf_regression_smoke.gd
```

For gameplay acceptance, check Map13 with many monsters and ground drops:
select/deselect monsters, leave/reenter the viewport with active aggro,
test attack direction and skill effects, click/pick up items with distinct
icon sizes and colors, check countdown/protection, then switch
Map13 → Map15 → Map13. Record `[PERF_ANALYSIS]` and `[RENDER_STATS]` under
the same population/viewpoint; do not claim RAM/FPS savings without
comparative measurements.

## 2026-11-08 log: NPC shop close and recipe prewarm optimization

Baseline from `godot(20261108-034948).log`: mostly 60 FPS, 18
frame gaps >=100ms, 1453 recipe prewarm 990.06ms, map transition frame
gaps 287.58-376.88ms, HUD 134.56ms, paper doll 314.88ms,
up to 7413 canvas nodes, 308 ground items, 1549 draw calls,
338MB Godot static memory and 348.8MB video memory.

**NPC merchant:** Left X worked, but right-hand SharedInventoryPanel's
`closed` signal was not connected. Both now use `_on_close_pressed`;
buy/sell packets and price logic are unchanged. NPC product-row palette
materials are cached by color and effect within the window.

**Craft:** `CraftingWindow.prepare_static_tree_async` no longer yields a
whole frame for every 24 inexpensive recipes. It yields after at most
64 recipes or 2.5ms of processing, whichever happens first. This keeps
the existing 1453-recipe data, sorting, icons, search and crafting
behavior, while removing unnecessary per-batch idle frames.

Tests:
```powershell
godot --headless --path client --script res://tests/npc_shop_close_and_crafting_regression.gd
godot --headless --path client --script res://tests/perf_regression_smoke.gd
```

Manual test: open NPC merchant, use left/right X separately, reopen, buy/
sell item variants and check coloring. Check personal shop and player
trade right-side close/cancel remain unchanged. Open craft list, search,
filter craftable recipes, use skill/equipment, and confirm all recipes
remain available. Repeat Map13→Map15→Map13. Compare log milestones and
frame gaps with the original log under the same conditions.

Logged warnings not yet addressed: stale UID in Skills/*.tres,
PaperDollEngine mecha ACT timeline, missing Hit_Effect08 and B-Fire01_AB
ACT frame. No measured CPU/RAM improvement is claimed until real tests.

## Manual end-to-end acceptance

1. Startup prewarm → login form: mouse and login input are responsive; progress 0→100.
2. Login → character select: all character previews render and the progress bar is independent.
3. Character select → first world: player, HUD, nearby monsters, NPCs, pets and ground items appear before overlay disappears. No unseen combat during loading.
4. Monsters: walk smoothly after first map reveal, with correct direction, attack, damage and death animations.
5. NPC and drops: NPC interaction (server range 700), quests, drops, pickup, equipment inventory, skill effects/shortcuts all work.
6. Map A → B → A: collision and pathfinding remain correct; no missing NPC/monsters/drops or stale entities; each transition completes its loading overlay.
7. Repeated transitions (10x) and inventory (200+ equipment): compare steady-state FPS, static memory, texture/video memory, nodes and `[FRAME_GAP]` outliers.

Compare `[PERF_ANALYSIS]`, `[MONSTER_SPAWN_TIMING]`, `[ENTITY_SPAWN_TIMING]` and map-transition stage timings with the same map, monster count and player position before/after.

For start-of-chase stutter, use matching Client **and Server Debug logs**:
`[MONSTER_AGGRO_FIRST_MOVE]` measures aggro-to-first-waypoint latency;
`[MONSTER_CHASE_PACKET_GAP]` captures gaps between subsequent aggro
waypoints; `[MONSTER_CHASE_CLIENT_IDLE]` and
`[MONSTER_CHASE_SERVER_IDLE]` separate local idle transitions from
server-requested idle. Server `[MAP_AI_TICK_GAP]` and
`[MONSTER_AI_PATH_BLOCK]` distinguish scheduler delays and 750ms
blocked-path retries. A client-only log cannot prove the underlying cause
of a long AI movement gap. `[MONSTER_AUTHORITATIVE_SNAP]` should now be a
render-smoothed attack-entry correction for 8–96px differences; it does
not mean the collision body was visually delayed.

Rollback on any missing entity, invalid movement, altered character/monster appearance, broken NPC interaction, GDScript parse error or new sustained frame gap.

**Debug-only requirement:** diagnostic output must not run in Release exports.

## 2026-11-08 07:52 Godot log: drop visuals, monster shapes, skill initialization

In `godot(20261108-075204).log`, login reached
`[LOGIN_VISUAL_READY] elapsed_ms=470.3 stable_frames=18` with no video
fallback. **The remaining stalls were mostly world initialization and
map transitions**, not continuous foreground FPS: `[HUD_INIT_TIMING]
synchronous_ms=399.12`, `[HUD_SKILL_INIT] setup_ms=60.65`, player
PaperDoll initial build 645.02ms, a 426.56ms HUD frame gap and repeated
~280–303ms map-transition frame gaps. Some first monster setups cost
~10–24ms each. No Godot SCRIPT ERROR was logged.

The log also shows 18 stale Skill Script UID warnings. The committed
`skill_data.gd.uid` and `skill_level_data.gd.uid` did not match
the UIDs referenced by the preexisting `Skills/*.tres`. The two script
UID files now match the legacy resources, without altering game skill
statistics, effects, icon indexes, levels or client-server packets. A
Python regression walks every skill `.tres` and flags mismatches.

**Validated source problems and the new changes:**

1. **Authoritative drop positions:** The old new-drop bounce in
   `network_entity_manager.gd` randomly changed the entire item's
   `Area2D.position` by up to 45px horizontally and 25px vertically,
   *after* the server had validated the safe drop coordinate. That could
   visibly put an item into a wall and desynchronize click location and
   AOI range. The new bounce moves only the `Sprite2D` visual child
   vertically and returns to its original offset. The ground item root,
   collision shape, global coordinates, authority, lifetime and pickup
   requests are not repositioned.
2. **Monster collision allocation:** `MonsterEntity` previously made a
   new `RectangleShape2D` for every identical sprite on each spawn.
   It now caches immutable rectangle resources per texture size. The
   existing per-monster `CollisionShape2D.position` is still independent,
   so target/click positions and blocking behavior remain unchanged.
   This saves repeated object allocation; no quantified CPU/RAM gain is
   claimed without a Godot run.
3. **Hidden skill window:** `SkillWindow.setup()` used to call
   `update_ui()` during synchronous HUD init even though the window
   was hidden; this loaded `.tres` resources and created all skill card
   nodes before the player could see the skill window. Now it only sets
   `target_player`. `GameHUD._toggle_window()` still calls `update_ui()`
   before showing the window, and `SkillSync` still refreshes when
   visible. This moves the initial ~60ms skill work to first use and
   avoids that resource allocation entirely in clients that never open
   the skill book.

**Regressions to run after pulling:**

```powershell
python client/tests/resource_uid_reference_regression.py
godot --headless --path client --script res://tests/render_optimization_regression.gd
run_godot_client_tests.bat resources (Windows; imports project and runs scene)
```

Manual verification: kill a monster at the wall and observe a short
visual drop bounce with the *same* final server/click position; click it
during/after animation, and then transition maps/relog to confirm no
lost/stale items. Spawn many monsters of the same and different sprites
and check hit/target alignment. After logging in, open the skill window
for the first time and verify all learned skills, categories, icons,
shortcuts and SkillSync behavior; close/reopen and check the displayed
levels still match. Verify no stale Script UID warning remains.

**Remaining opportunities requiring a measured follow-up:**

- Map-change repeated ~300ms main-thread stalls: profile
  `map_resource.instantiate`, `root.add_child`, player reparenting,
  static map Canvas registrations and GPU texture uploads individually.
  Avoid keeping every map loaded per client: this would raise RAM/VRAM
  substantially during 10-open sessions.
- Ground items: hundreds of known items and ~7k Canvas nodes; next
  profile visible label/text rendering and protection timer overhead.
  Keep all item authoritative state and click/pickup behavior; never
  drop far objects from server state merely to improve FPS.
- First unique-monster SpriteFrames construction: isolate per-sprite
  atlas decode vs animated frame creation; consider bounded during-map
  warmup only if CPU/VRAM measurements support it.
- Initial PaperDoll 645ms: profile layer/resource setup and ACT timing
  paths before deleting any layers; fix repeated warnings separately.

No local Godot binary is available here: source-level audit and
automated test additions have **not** proved in-game FPS, CPU, GPU,
map-change time or 10-open savings. Compare the same maps/positions and
item/monster counts in a follow-up Debug Godot log after testing.

