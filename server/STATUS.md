# SGO Local Server — Current Status

> Branch: `feature/local-server-net8`
>
> Scope: recovered/current Godot client interoperability using a local .NET 8 server.
> The current recovered client GameData is authoritative at runtime. Old server/client data is reference material for recovering names, relationships, scripts, map layout, shop membership, and gameplay rules.

## 2026-11-07: Shared map AI and monster wall navigation

The code now uses **one 250 ms scheduler** (`SharedMapAiTicker`) for
monster AI and monster respawns across connected players, grouped by map.
Each monster is assigned once per map tick to its target owner's session
or the nearest eligible player/pet context. Personal HP/MP recovery, buffs,
pet follow and client snapshots still run in the per-connection tick.
Maps with no connected world-ready players do not advance monster AI;
monster positions are retained in `WorldRuntimeService`.

Monster chase and wandering use `MonsterNavigationService`, reading the
same `Blocking.dat` collision source as the client. Every movement
segment is checked strictly; chase performs bounded A* detours and does
not cut diagonally through wall corners. Melee attacks cannot hit through
walls. Blocked paths stop the run animation and retry no more than once
per 750 ms. Missing collision maps fail closed.

The smoke suite has new navigation/central-tick regression assertions.
**Automated build and runtime gameplay verification still need a working
CI runner and Godot client run; do not infer that a successful commit means
those checks have passed.**

## 1. Runtime data priority

The server must not assume that a legacy ID still means the same thing in the current recovered client.

Priority:

1. **Current recovered client GameData** — authoritative runtime ID, display data and current stats.
2. **Old ClientData** — confirms the old display name/category for a legacy ID.
3. **Old server data** — supplies old ShopID membership, NPC/monster placement, Lua quest logic and gameplay rules.
4. If a legacy record cannot be matched safely to current GameData, **skip it and log it**. Do not expose a guessed legacy ID to the current client.

Current client roots are discovered from:

- `Game:ClientRoot`
- `SGO_CLIENT_ROOT`
- common recovered-client locations such as `C:\SGO\Recovered`

Current item folders read by the server:

```text
Items/
  booster/
  equip/
  general/
  recovery/
  weapon/
  horse/
  horseequip/
  horsebattery/
```

Current skills are loaded from `Skills/*.tres`. Current pet configuration is loaded from the recovered client's pet data.

### Legacy -> current item mapping

The intended mapping flow is:

```text
Old Server ShopID / ItemID
        ↓
Old ClientData confirms old name/category
        ↓
Current recovered client GameData searches by name/category
        ↓
Current ItemID
        ↓
NPC shop / inventory / equipment / GM / combat
```

ID equality is not enough.

Example already covered by regression test:

```text
Old Shop9
LegacyItemID = 31262
Old name     = 戰鬥蝕日斧

Current ItemID 31262
Current name   = 雙銃雷霆砲+16
→ legacy/current ID collision

Search current GameData by old name:
戰鬥蝕日斧 → current ItemID 49962 (test fixture)

Shop9 must send 49962, never legacy 31262.
```

Startup mapping logs include:

- `[LEGACY_ID_COLLISION]`
- `[NAME_REMAP]`
- `[SAFE_SAME_ID]`
- `[SAFE_SAME_ID_NO_NAME]`
- `[SKIPPED][CURRENT_ITEM_NOT_FOUND]`
- `[SKIPPED][OLD_NAME_MISSING]`

If old ClientData lacks a reliable name, cross-ID guessing is forbidden. An identical current ID may only be used when the category is compatible.

## 2. Protocol and connection

Implemented:

- Godot WebSocket protocol compatibility.
- AES-256-CBC + PKCS#7 + Base64 JSON matching the recovered client.
- Listener defaults to `127.0.0.1:5678`.
- 2 MiB inbound packet limit.
- WebSocket logout/close flow.
- Periodic server-side world tick; gameplay is no longer driven by player movement packets.

Important diagnostic logs:

- `[UNHANDLED_PACKET]` — packet Type has no server handler.
- `[NO_RESPONSE]` — a packet expected to do work produced no response.
- `[PACKET_HANDLER_FAILED]` — handler threw; Type and truncated payload are logged.

This is intentional so client-side actions that currently do nothing can be identified from server console logs and implemented next.

## 3. Accounts and characters

Implemented:

- Register/login.
- PBKDF2-SHA256 account password storage.
- Stable three-character slot response.
- Create/delete character.
- Character password change.
- Select character / `EnterWorldSuccess`.
- Character state, map and position persistence.
- Logout.
- Revive/reborn flow.
- Persistent level/stat/money/status fields.

The old client expects placeholder character slots and character-password-compatible fields; these compatibility behaviors are preserved.

## 4. World, maps and NPCs

Implemented:

- Old `Map/MapXX/NPC.dat` parser.
- Big5 NPC names.
- Runtime NPC `SpawnEntity(EntityType=2)`.
- Portals and normal NPC interaction.
- Map transition persistence.
- Map15 treated as the main city.
- Map15 combat enemy definitions are not spawned as normal combat monsters.
- Destination map entities are server-driven; the player does **not** need to move before NPCs appear.
- World snapshots are retried after map load so an early spawn packet lost while the client scene is loading is resent safely.

Map15 currently parses 52 NPC records in the smoke suite.

Merchant NPCs use a known visible/clickable recovered sprite fallback when an old NPC visual cannot yet be mapped to the current client.

## 5. Lua quest/NPC scripts

MoonSharp executes the old quest scripts.

Implemented host functions include:

- `Say`
- `Ask`
- `GetChoice`
- `GetData`
- `MovePlayer`
- `QuestEnd`
- `OpenShop`
- bank/repair/rank-related compatibility calls already exercised by tests

Old presentation prefixes such as `n`, `n1`, `p7`, and `s` are normalized before text reaches the current client, so strings such as `n要進入...` are not shown literally.

## 6. NPC shops and item transactions

Implemented:

- Old server `Dat/Shop/Shop.csv` supplies legacy shop membership.
- Legacy shop entries are mapped to **current client ItemIDs** using the mapping rules above.
- Shop dialog -> `OpenShop` -> `NpcShopViewResponse`.
- Buy:
  - validates current GameData item/category;
  - deducts money;
  - writes into the correct asset table;
  - emits the appropriate sync packet.
- Sell:
  - resolves the asset by the current client tab/category;
  - removes the correct record;
  - adds money;
  - emits money/asset sync.

Current asset category handling:

| Current category | Persistence target |
|---|---|
| booster/general/recovery | inventory |
| weapon/equip | equipment |
| horse | mecha/horse |
| horseequip | mecha equipment |
| horsebattery | inventory battery item |

This fixes the previous bug where `TabType=3` could be treated as horse equipment instead of a mecha/horse asset.

Smoke coverage includes:

- Map15 merchant interaction.
- Populated NPC shop.
- Buy/deduct money.
- Mapped current equipment purchase.
- Sell/removal/add money.
- Shop9 legacy ID collision remap.

## 7. Equipment and effective character stats

Implemented:

- Inventory and equipment persistence.
- Equip/unequip.
- `InventorySync` / `EquipSync`.
- Effective combat stats are calculated from the **current client item .tres data**, not legacy fixed slot bonuses.
- Current item fields used include attack/defense/magic stats/hit/flee/luck/attack speed/move speed/critical values/attack range where available.
- Equipped mecha parts also contribute via current GameData.

Combat uses the same effective-stat calculation as status sync, so the UI/state path and actual damage path do not intentionally use two separate stat models.

## 8. Recovery items

Implemented using current client recovery item data:

- `UseItemRequest`.
- HP/MP/SP recovery fields.
- Cap at MaxHP/MaxMP/MaxSP.
- Consume item count.
- Refresh status.

Legacy recovery IDs without a confirmed safe current mapping are skipped rather than guessed.

## 9. Mecha / horse

Implemented:

- Current mecha-card definitions.
- Activate/use a mecha card to create a persistent rideable mecha.
- `DriveMechaRequest`.
- Persist `SummonedHorseUID`.
- `HorseSync`.
- Mecha equipment install/remove.
- Mecha battery/energy charge.
- Current GameData values for max battery, battery charge and mecha equipment stats.
- Current mecha appearance/type information is sent through existing client-compatible packets.

## 10. Pets

Implemented:

- Persistent pet records.
- Current-client pet appearance/config usage.
- Summon/Recall.
- Rename.
- Pet equipment attach/remove.
- `PetSync`.
- Runtime `SpawnEntity(EntityType=4)` / `DespawnEntity`.
- Persist summoned state.
- Respawn summoned pet after map transition.
- Server-side follow movement.
- Pet runtime position survives world snapshot retries without client/server desync.
- `CommandAttack`.
- Automatic nearby-monster acquisition/attack.
- Pet combat tick runs before monster AI so the pet can act consistently on the same server tick.

## 11. Monsters and combat

Implemented:

- Old `Enemy.dat` map spawn parsing.
- Old enemy catalog lookup.
- Per-session monster runtime entities.
- Server-authoritative HP/death/respawn.
- Player -> monster attacks.
- Pet -> monster attacks.
- Monster -> player chase/attack/damage/death.
- Attack/idle/walk state packets.
- Aggro and lose-target behavior.
- Respawn.
- GM monster spawn.
- Independent idle wandering.

Important world-tick behavior:

- Monster AI runs even when the player sends no movement packet.
- Different monsters use staggered deterministic pseudo-random wander timers rather than moving synchronously.
- Idle movement stays near the current wander center.
- Losing aggro/player death re-anchors the wander center at the monster's current position instead of teleporting it back to the original spawn point.
- A monster does not move on the same tick that it drops aggro because the player died; wandering resumes later.

## 12. Skills

Implemented foundation:

- Persistent character skill table.
- `SkillSync` at character bootstrap.
- GM skill grant/list/clear.
- Current client `Skills/*.tres` is the runtime source.
- Old skill IDs may only be translated when an old-client display name can be matched to a current skill name.

Still incomplete:

- Full `UseSkill` gameplay behavior.
- MP/cooldown/range/target validation.
- Skill-specific damage/effects/buffs.
- Legacy skill formula/rule reconstruction.

## 13. GM commands

GM commands are implemented completely server-side through the existing chat packet; no client modification is required.

```text
/gm help
/gm pos
/gm map <MapID> [X] [Y]
/gm heal
/gm level <1-255>
/gm money <amount>
/gm item <current ItemID> [count]
/gm equip <current ItemID>
/gm mecha <current Horse ItemID>
/gm pet <current PetID> [name]
/gm skill <current SkillID|all> [level]
/gm skills
/gm clearskills
/gm spawn <EnemyID> [X] [Y]
/gm killall
```

Asset/skill commands validate against current recovered client GameData. Old IDs must be mapped by confirmed name before they are accepted as current runtime IDs.

GM availability can be controlled server-side; do not rely on the client to provide authorization.

## 14. Persistence architecture

Gameplay code depends on `IGameStore`, not directly on SQLite.

Current provider:

- SQLite.

Provider-specific SQL remains under `Infrastructure/Persistence/Sqlite` and schema files under `Database/Sqlite`.

Future MySQL migration should implement a new `MySqlGameStore : IGameStore` and provider registration without rewriting protocol/world/gameplay flow.

## 15. Current automated verification

The Windows GitHub Actions workflow runs:

```powershell
dotnet build server/SGO.Server.csproj -c Release
dotnet run --project tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj -c Release
```

The current smoke suite covers, among other cases:

- protocol AES round-trip;
- stable multi-character slots;
- character persistence;
- NPC/portal parsing;
- Lua dialog/portal/bank/repair/shop flows;
- current/legacy item mapping;
- the Shop9 `31262 戰鬥蝕日斧` collision/remap case;
- NPC shop buy/sell;
- equipment and effective attack;
- recovery item use;
- mecha creation/riding/equipment/battery;
- pet summon/follow/equipment/command attack/auto attack;
- world spawn without player movement;
- map transitions and pet respawn;
- independent monster wandering;
- aggro/chase/attack/player death;
- monster death/respawn;
- GM level/item/map/skill/spawn flows.

Latest verified code before this documentation update had both build and the complete smoke suite passing.

## 16. Known gaps / deliberately unfinished areas

The next systems are not considered complete yet:

1. **UseSkill / combat skills**
   - highest-value missing gameplay path.
   - server currently has skill persistence/bootstrap, but not the full skill engine.

2. **Complete item/name remapping**
   - some old ClientData item-name files are empty/incomplete.
   - continue mapping by confirmed names where available.
   - never force a legacy ID whose current meaning differs.

3. **NPC visual mapping**
   - some NPCs currently use a safe recovered fallback sprite.
   - map old NPC visual/type IDs to exact current client NPC resources.

4. **More Lua host functions / quest systems**
   - progressively implement only calls actually found in old scripts.
   - quests/rewards/conditions should become persistent where required.

5. **Drops / EXP / leveling formulas**
   - monster death does not yet reconstruct the complete original reward/drop/formula system.

6. **Full skill/effect/buff state**
   - buffs/debuffs, duration, stacking and visual effects require protocol and legacy-rule work.

7. **Multi-client shared world**
   - monster runtime is currently per session.
   - fine for local single-player interoperability, but not a true shared MMO world.

8. **MySQL provider**
   - architecture is ready for a second provider, but only SQLite is implemented today.

9. **Godot end-to-end automation**
   - server-side smoke tests are strong, but actual client UI/gameplay still needs manual validation.

## 17. Recommended next development order

### P0 — Implement real skill casting

This should be next.

Reason:

- the client already sends `UseSkill`;
- skill persistence/SkillSync already exists;
- combat, targeting, HP, MP, monster runtime and status sync already exist;
- this unlocks the largest missing player-facing system.

First slice:

```text
UseSkill
→ validate learned skill
→ validate MP
→ validate target/map/range
→ consume MP
→ emit SkillCast
→ apply damage/heal
→ emit EntityDamage / EntityHeal
→ PlayerStatusSync
→ cooldown
```

Start with a small set of skills that can be confidently identified in both old data and current `Skills/*.tres`. Unknown skills should log and return a clear server message.

### P1 — Complete current item/shop mapping

After basic skills work:

- build a generated mapping report:
  `Old ShopID + LegacyID + confirmed old name + current ID + current name + status`;
- review all Map15 merchants;
- fix every `[LEGACY_ID_COLLISION]` with a current name match;
- leave unmatched records skipped rather than guessed.

This will make normal progression/items trustworthy without GM commands.

### P2 — Rewards, drops and leveling

Add:

- EXP reward;
- money reward;
- drop tables;
- loot entities/pickup;
- level-up thresholds;
- stat-point award;
- current level/status sync.

This is the step that turns combat into a progression loop.

### P3 — Exact NPC visuals and remaining NPC services

Replace fallback NPC sprites with exact current client resources and finish remaining Map15 services/shops/bank/repair/rank/quest scripts.

### P4 — Full quest persistence and more Lua API coverage

Implement missing host functions only as discovered in scripts and add regression tests per function.

### P5 — Shared-world/multi-client architecture, if needed

Only do this after single-player/local interoperability is stable. It is a much larger architectural step and is not required to finish the recovered client experience.

## 18. Practical next manual test

Before implementing more systems, run the current client and verify these exact cases once:

1. Login and stay completely still: Map15 NPCs should appear without moving.
2. Use a portal and stay still: destination NPCs/monsters should appear.
3. On a monster map, stand still: monsters should wander independently.
4. Open the Map15 merchant and buy/sell an item.
5. Confirm a known remapped item shows the **current** name/icon.
6. Summon a pet; move, change map, and let it auto-attack.
7. Ride/charge/equip the mecha.
8. Trigger any unsupported client action and confirm the server prints `[UNHANDLED_PACKET]`, `[NO_RESPONSE]`, or `[PACKET_HANDLER_FAILED]`.

If these pass manually, proceed directly to P0 skill casting.
