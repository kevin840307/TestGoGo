# SGO Client Performance Regression Checks

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
