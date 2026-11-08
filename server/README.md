# SGO Local Server (.NET 8 + SQLite)

A **localhost-only prototype** built to match the recovered Godot client's WebSocket protocol. It is not yet a playable offline MMO.

## 2026-11-08 功能與驗證狀態

- [未完成／需驗證的功能清單](FEATURE_GAPS.md)：P0／P1／P2、依賴與驗收條件。
- [封包契約狀態](../tests/PACKET_CONTRACT.md)：Client/Server Type／Action、欄位 Schema 與靜態檢查範圍。
- [可維護的測試架構](../tests/SMOKE_TESTS.md)：領域分檔、共用 fixture、端到端測試與本機指令。

**請勿以 Handler 存在或測試程式已提交，推論所有功能或 Godot E2E 已通過。**

## Start

```powershell
cd server
dotnet restore
dotnet run
```

Server: `ws://127.0.0.1:5678`; health endpoint: `http://127.0.0.1:5678/health`. SQLite automatically creates `Data/game.db` next to the server binary.

## Client change (approved)

In the recovered project's `scripts/network_manager.gd`, set `USE_DDNS` from `true` to `false`; see `client-patch/scripts/network_manager.gd.patch`. The existing localhost URL will then be used. DDNS code is left intact for future use. The original shipped EXE is not modified.

## Current status

For the detailed implementation baseline, data-alignment rules, known gaps, regression coverage, and recommended next work, see [STATUS.md](STATUS.md).

Implemented at a high level:

- WebSocket + AES-256-CBC/Base64 JSON protocol compatibility.
- Account/character lifecycle, persistence, movement/state/revive.
- Server-driven world tick: NPCs, monsters and pet AI no longer depend on player movement packets.
- Old map/NPC/enemy/Lua data compatibility.
- NPC interaction, portals, bank/repair/rank/shop flows.
- Current-client-first GameData alignment for items, skills, pets and mecha.
- Legacy shop ID/name remapping to current client ItemIDs.
- NPC shop buy/sell with the correct current asset type.
- Inventory/equipment/current stat calculation.
- Recovery items.
- Mecha riding/equipment/battery.
- Pets: summon/recall/follow/equipment/command attack/auto attack.
- Monster wandering/aggro/chase/attack/death/respawn.
- Persistent skills and server-side GM commands.
- Unknown/no-response/failed packet diagnostics in server logs.
- SQLite behind `IGameStore` so a MySQL provider can be added later.

Important current gaps:

- Full `UseSkill` casting/damage/effect/cooldown implementation.
- Complete legacy-name -> current-ID coverage for every item/shop.
- Complete original drops/EXP/level-up/stat-point formulas.
- Exact current NPC visual mapping for every NPC.
- Remaining Lua host functions and full quest persistence.
- True shared multi-client world state.
- MySQL provider implementation.
- Automated Godot-client E2E tests.

## Build and publish

```powershell
dotnet build server/SGO.Server.csproj -c Release
dotnet publish server/SGO.Server.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

CI builds on Windows via GitHub Actions. No end-to-end Godot gameplay tests have been run.


## Game world data

The server now reads the uploaded game data under `Old/服務端/Dat`:

- `Map/MapXX/NPC.dat`: NPC / portal id, coordinates, Lua quest script and Big5 display name.
- `Map/MapXX/Enemy.dat`: binary enemy spawn records (variable-length enemy-id string, X/Y, fixed source payload).
- `Enemy/Enemy_Data.csv`: enemy name, level, sprite and base HP lookup.
- `Quest/*.txt`: Lua scripts executed through MoonSharp.

Map15 is explicitly treated as the main city: NPC/portal spawns are loaded, but combat Enemy.dat records are not spawned there. A portal script can issue `MovePlayer(x,y,mapId)`; the destination is persisted to SQLite. Destination entities are now spawned by the server world tick and retried after map load, so the player does not need to move first.

Implemented Lua compatibility includes `Say`, `Ask`, `GetChoice`, `GetData`, `MovePlayer`, `QuestEnd`, `OpenShop`, and the bank/repair/rank flows currently covered by smoke tests.


## Code layout

See `ARCHITECTURE.md`. The server is now split into:

- `Application/`: packet/game flow and persistence abstractions.
- `Domain/`: session/domain state.
- `Protocol/`: recovered-client AES/Base64/JSON compatibility.
- `Transport/WebSockets/`: WebSocket framing and connection lifecycle.
- `Infrastructure/GameData/, Infrastructure/Quest/, Infrastructure/World/`: old NPC/Enemy/Lua compatibility, split into Data/Quest/World.
- `Infrastructure/Persistence/`: database providers.

`PacketRouter` depends on `IGameStore`, not SQLite. Database selection is configuration-driven:

```json
"Database": {
  "Provider": "Sqlite",
  "Sqlite": {
    "Path": ""
  }
}
```

To add MySQL later, implement `MySqlGameStore : IGameStore` under `Infrastructure/Persistence/MySql/` and register the provider. No game-flow, WebSocket, map or Lua code should need changes.

Dialog presentation prefixes such as `n`, `n1`, `p7`, and `s` are stripped before sending dialog text to the recovered client.


## Current-client GameData alignment

Runtime data is current-client first:

- Current items: `client/Items/*/*.tres` or `Game:ClientRoot` / `SGO_CLIENT_ROOT`.
- Current skills: `client/Skills/*.tres`.
- Current pets: `client/Assets/Pets/Config.json`.
- Old server `Dat/Shop/Shop.csv` only supplies legacy shop membership.
- Legacy Item IDs are never trusted by ID alone. The server resolves a confirmed legacy display name to the current client ItemID. ID collisions with different names are rejected.
- Legacy entries without a reliable name are skipped and logged instead of being exposed as the wrong current item.

Useful log markers:

- `[LEGACY_ID_COLLISION]`
- `[NAME_REMAP]`
- `[SAFE_SAME_ID]`
- `[SKIPPED][CURRENT_ITEM_NOT_FOUND]`
- `[SKIPPED][OLD_NAME_MISSING]`
- `[UNHANDLED_PACKET]`
- `[NO_RESPONSE]`
- `[PACKET_HANDLER_FAILED]`

## GM chat commands

GM commands are server-side chat commands and require no client changes:

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

Asset/skill/pet GM commands validate against the current recovered client GameData rather than accepting legacy IDs blindly.
