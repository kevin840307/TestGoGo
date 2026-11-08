# SGO Client Performance Regression Checks

## Automated, server-free smoke test

After Godot has imported the project, run from the repository root:

```powershell
godot --headless --path client --script res://tests/perf_regression_smoke.gd
```

Expect exit code 0 and `[PERF_SMOKE] ALL PASS`. This tests: PaperDoll and monster SpriteFrames cache reuse, no-op ACT timing identity, spawn queue priorities (ground item > NPC > monster), AOI monster coordinates and nested ExtraData preservation.

It is **not** an end-to-end game test. In this editing environment Godot was unavailable; these tests have not been executed.

## Manual end-to-end acceptance

1. Startup prewarm → login form: mouse and login input are responsive; progress 0→100.
2. Login → character select: all character previews render and the progress bar is independent.
3. Character select → first world: player, HUD, nearby monsters, NPCs, pets and ground items appear before overlay disappears. No unseen combat during loading.
4. Monsters: walk smoothly after first map reveal, with correct direction, attack, damage and death animations.
5. NPC and drops: NPC interaction (server range 700), quests, drops, pickup, equipment inventory, skill effects/shortcuts all work.
6. Map A → B → A: collision and pathfinding remain correct; no missing NPC/monsters/drops or stale entities; each transition completes its loading overlay.
7. Repeated transitions (10x) and inventory (200+ equipment): compare steady-state FPS, static memory, texture/video memory, nodes and `[FRAME_GAP]` outliers.

Compare `[PERF_ANALYSIS]`, `[MONSTER_SPAWN_TIMING]`, `[ENTITY_SPAWN_TIMING]` and map-transition stage timings with the same map, monster count and player position before/after.

Rollback on any missing entity, invalid movement, altered character/monster appearance, broken NPC interaction, GDScript parse error or new sustained frame gap.

**Debug-only requirement:** diagnostic output must not run in Release exports.
