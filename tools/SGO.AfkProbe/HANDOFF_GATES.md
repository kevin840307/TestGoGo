# AFK process replacement: required acceptance gates

The standalone .NET probe deliberately does **not** replace the Godot
AFK mode yet. It shows the memory cost of a separate process; it does NOT
establish equivalence to normal gameplay.

## Measured evidence (GitHub Windows runner, 2026-11-09)

- Real SGO WebSocket, dedicated disposable local test account, Map13 with 199
  monster snapshots, 1,174 encrypted packets in a 10-second trial:
  peak Working Set 48.68 MiB, peak Private Bytes 18.58 MiB.
  Actions run: https://github.com/kevin840307/TestGoGo/actions/runs/42630210615
- Separate external PID sampler recorded Working Set ~48.28 MiB,
  Private Working Set ~14.87 MiB and Private Bytes ~17.75 MiB peak.
  Actions run: https://github.com/kevin840307/TestGoGo/actions/runs/42630466874
- These are **runner results**, not measurements on the user's PC. They
  exclude Godot renderer, sprites, textures, all gameplay UI, and the cost
  of a replacement AFK UI.
- `AutoSaintEnabled` in the initial trial was false. Server-side AutoSaint
  is separately tested by CI after granting required skills to a disposable
  test character. A successful enable is not proof of movement or pickup.

## Never merge a process handoff until all gates are satisfied

1. Server-owned handoff token with a short expiry and atomic ownership transfer
   between old Godot socket and standalone process. Login currently
   supersedes the old connection via AccountSessionRegistry; this alone is
   NOT a seamless handoff and must not be treated as one.
2. Persist or transfer session-only state (AutoSaint, summoned pet mode and
   targets, active buffs/cooldowns, party, map, ground-drop/quest state,
   position, pending operations) without duplicate effects.
3. Continue *all* incoming and outgoing game packets at original cadence.
   The standalone proof-of-concept currently only handles server-driven
   AutoSaint plus packet parsing; it is not an implementation of every
   client-side auto-attack/pathfinding/movement/pickup action.
4. Cross-process exit/restart must safely drain pending actions; connection
   failure or interrupted handoff must recover without losing HP/MP/items,
   duplication or broken AOI snapshots. Require 10-client concurrency
   and disconnect/resume/retry regression tests.
5. Preserve existing AFK experience. If Godot is terminated to actually
   release its ~300 MiB working set, its window/UI also disappears. A small
   native AFK shell would need to reproduce the same user-facing UI/mouse
   state, then relaunch Godot and restore via full transition loading.
   Keeping the original Godot process alive negates much of the memory gain.
6. Measure 30+ minutes **Private Working Set + Private Bytes** on target
   Windows machine, with identical map, monster density, combat mode, GUI
   configuration and client count. Exclude server memory or identify it
   separately. Do not compare Godot MEM_STATIC with Windows working set.
7. Stop the design if actual Windows memory advantage is small or any
   gameplay/latency/packet correctness regresses. No forced Windows
   working-set trim, FPS reduction of logic ticks, silent network throttle
   or GUI substitutions without explicit approval.

## Current implementation status

- Working: independent .NET executable, same AES/JSON wire framing, login,
  character enter, WorldReady, Ping/Pong, Map13 AOI and state dictionaries,
  real Windows memory meter and separate per-PID sampler.
- Pending: atomic handoff/recovery, complete gameplay action parity, native
  AFK shell matching original UI, reliable long soak and user-PC acceptance.
