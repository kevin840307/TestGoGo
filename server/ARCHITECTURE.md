# SGO Server Architecture

The server is organized around the same concepts the recovered client exposes:
**Account, Character, Inventory, Equipment, Pet, Mecha, Skill, Bank, Shop, Combat, World, NPC/Quest**.

The primary design goals are:

1. Client/server naming should match wherever possible.
2. Gameplay modules should be independently removable.
3. Database providers should be replaceable without changing gameplay code.
4. Static game resources should be loaded once and shared.
5. Multiplayer runtime state should be shared by map/channel, not duplicated per connection.

## Implemented shared map AI (2026-11-07)

- `WorldRuntimeService` owns authoritative monster objects shared by all
  players on a map; `ClientSession.Monsters` is a compatibility mirror of
  references to those objects, not an independent HP/position copy.
- `SharedMapAiTicker` owns the **single 250 ms map AI scheduler**.
  It snapshots world-ready players per map, runs respawn once, and assigns
  each monster to exactly one player context (current target owner or
  nearest available player/pet). Per-client `PacketRouter.Tick` does not
  execute monster AI for registered WebSocket sessions.
- `MonsterNavigationService` strictly checks each monster movement segment
  against `Blocking.dat` and uses a bounded A* path search when direct
  chase movement is blocked. Melee line of sight is blocked by walls.
- The map simulation is still in one process and uses map locks. Player
  and world entity AOI are **already implemented** as separate 1024-unit
  spatial grids with known sets under `Application/Messaging/`. Per-map
  actor queues and multi-instance channels remain future scaling work.
  No 24-hour or real-client throughput guarantee follows from smoke tests.

## 2026-11-08 實際模組邊界與多人效能

| 位置 | 目前責任 | 不應承擔 |
| --- | --- | --- |
| `Transport/WebSockets` | WebSocket lifecycle、AES transport、接收/送出 queue | 決定怪物誰能看見、改金錢 |
| `Protocol` | 固定 wire Type/Action/欄位格式驗證 | NPC、道具與戰鬥規則 |
| `Application/Messaging` | Player AOI、World AOI、player companion snapshot、通知投遞 | 資產增減及 DB 最終真相 |
| `Application/World` | 單圖 250ms AI 排程、怪物 owner 分派、世界 runtime 存取 | 每人複製一套怪物 |
| `Application/{Combat,Trade,Shops,Bank,Crafting}` | 各種具體遊戲規則及狀態轉移 | 自己拼接 AES 或持有任意 SQL |
| `Domain` | 狀態模型與遊戲規則需要的實體 | 網路、持久化 provider |
| `Infrastructure/Persistence/Sqlite` | 真正持久化的 money/item 交易、ground row、schema | 使用 AOI 作為 DB 權限或刪資料依據 |
| `client/scripts/network_entity_manager.gd` | Client 視覺物件、地面物 visual AOI、產生/去重 | 決定物品擁有權、最終死亡或掉寶 |

**現有網路 AOI 和 AI owner 查找是兩件事。** Player AOI
(enter 1800 / exit 2200) 與 World AOI (enter 2600 / exit 3000)
只控制「要送給誰」；怪物 AI 仍必須為每隻怪找一位執行者。
`SharedMapAiTicker` 在每次 map tick 使用
`MapAiOwnerSpatialIndex` 快照玩家與寵物位置，索引不限制 AOI
半徑，保留原先的精確最近 owner 結果與 CharacterId tie-break；
已有 target 的怪物則直接查 dictionary。這不應改變怪物會追誰，
只減少原本 O(monsters × players/pets) 的重複掃描。

目前仍需優先量測：每 250ms `GetCharacter` 的讀取數、
`TickAi` / pathfinding 熱點、SQL write contention、WebSocket
queue backlog、Godot Client 的首次載入/重繪/內存。
不能因 spatial index 的理論成本改善就宣稱真實 FPS、CPU 或
100 人容量已經過壓測。

### 小步重構原則

1. **先有驗收再拆檔**：`SqliteGameStore.cs` 現在仍是大型綜合
   provider。可在已建立對應 transaction / protocol contract 測試後，
   以 `partial` 或小 store 類別逐功能抽出
   Economy/Inventory/Trade/World；不能同時更動 SQL 與封包語意。
2. **分離過長 Smoke Program**：獨立案例維持
   `tests/SGO.Server.SmokeTests/{World,Integration,Protocol,Fixtures}`；
   以 `--suite world`、`--suite concurrency`、full 加入，避免
   再把測試塞進大檔案。金錢測資透過 `ExactMoneyFixture`
   設定**精確餘額**，而非在新角色預設 10,000 上直接加錢。
3. **不把 AOI 誤用在經濟功能**：Party/Trade/Club/Global 仍按業務
   送出，地面物存在與裝備交易由 SQLite transaction 最終決定。
4. **以可觀察結果做效能迭代**：每次更動都需比較
   WorldReady→Spawn、離開→Despawn、near/far leak、
   player item conservation、10/50/100 client latency、
   bytes/sec、CPU/RAM/tick P95/P99 與 Godot regression。

## 2026-11-08 再次核對目錄責任：AOI／資料守恆／Client 回歸

現有目錄的責任大致合理，不建議只為改名稱就搬移大量檔案。
這次新增的功能依使用者操作流程分類，不把「渲染」、「資產」與
「傳輸」混在同一個類別：

| 程式／測試 | 目前位置 | 是否合理／後續原則 |
| --- | --- | --- |
| `PlayerSpatialAoi`、`WorldEntitySpatialAoi`、`ServerBroadcastHub` | `server/Application/Messaging/` | 合理：只維護可見集合與封包投遞；不修改 DB。新增 `SnapshotScope` 是 AOI 記憶體生命週期，仍屬 Messaging；不應放在 SQLite 類別。 |
| `PacketRouter` 的 WorldReady / PickUpItemRequest 相容路由 | `server/Application/PacketRouter.cs` | 現階段避免橫跨整個路由重寫。穩定後再將**單一功能**逐步搬至 `Application/World/GroundItemPacketHandler.cs`，前提是保持原封包格式、DI、AOI、交易測試全部 PASS。 |
| `GroundItemRuntime` | `server/Domain/World/` | 合理：資料模型不應決定 WebSocket 或 Godot 顯示。 |
| `DropAssetToGround`／`PickUpGroundItem` | `server/Infrastructure/Persistence/Sqlite/` | SQL transaction 是資產真相；不因 AOI 渲染離開而刪除持久化 row。下次拆存取層必須先有 PickUp/Drop/Trade/Shop/Craft 保全測試。 |
| `network_entity_manager.gd` | `client/scripts/` | 接收既有 `NetworkManager` 事件、維護 Scene 與 AOI 視覺物件；不處理 Server 資產權限。 |
| `network_entity_pending_moves.gd` | `client/scripts/` | 合理：不依賴 Scene 的純狀態輔助器（TTL、上限、Despawn 間隔防護），避免把封包排序細節複製到各類 Node。 |
| `World/*Aoi*SmokeTests.cs` | `tests/SGO.Server.SmokeTests/World/` | 不依賴 DB，測 AOI 邊界、版本與交錯順序。 |
| `Integration/Ground*SmokeTests.cs` | `tests/SGO.Server.SmokeTests/Integration/` | 真 SQLite/交易守恆與多人競態，跟純 AOI 測試分離。 |
| `client/tests/aoi_*regression.gd` | `client/tests/` | 真 GDScript 驅動物件管理，涵蓋 queued/delayed/duplicate 封包與換圖。 |

### 維護成本與多人效能驗收準則

- **同一實體不應有三份真相**：SQLite 決定持久化物品，WorldRuntime
  決定怪物生命值及座標，AOI 只決定對誰送哪個可見邊界事件，
  Godot 的 Scene cache 只決定畫面渲染與過場效果。
- **只對已知近距離者送 delta**：世界中相隔 9,000 單位的玩家
  不需要彼此逐步 Move；進入 1,800 單位才 Spawn，超過
  2,200 單位才 Despawn。Monster/ground 分別用 2,600/3,000
  半徑（延續既有設定）；金錢、Party/Trade/Club、跨圖私訊
  不應錯套 AOI 過濾。
- **封包交錯下可恢復**：即使最初 Move 比 Spawn 早，
  Client 也只暫存最新位置、不生成不存在的物件；Despawn
  後舊 Move 不會復活，WorldReady 後由 authoritative 快照收斂。
  這是容錯，不等於 Server 已證明所有多執行緒封包先後正確。
- **不能讓壓力檢測拖慢正常遊戲**：未生成 Move cache 僅
  O(256) 上限，過期掃描有時間節流，不做逐 frame 全表清理；
  需真正測量 100/500/1,000 人的 bytes/sec、queue lag、GC、
  CPU/RAM、P95/P99 和 EntityID ghost/leak。
- **測試結構不回到單檔**：純協定放 `Protocol/`，AOI/座標放
  `World/`，SQLite 跨功能併發放 `Integration/`，
  可共享建角/金錢測資放 `Fixtures/`，Godot 實體事件放
  `client/tests/`，由主 Runner 只做註冊。

建議**先確保 CI Runner 能實際產生 steps 與 build/test PASS**，
再逐個收斂 `PacketRouter.cs` 和 `SqliteGameStore.cs` 的大型
領域方法；在缺少可用環境時直接全專案重新分類會產生很高的
regression 風險，並不能保證效能更好。

## Naming policy

Use client-facing names in new application/domain code:

| Client concept | Server name |
| --- | --- |
| Character | Character |
| Inventory | Inventory |
| Equip | Equipment |
| Pet | Pet |
| Mecha | Mecha |
| Skill | Skill |
| Bank | Bank |
| NPC | NPC |
| Monster/Enemy | Monster for runtime entity, Enemy for static GameData |
| Map | Map |
| Shop | Shop |

The original protocol/database contains legacy `Horse*` fields. These remain only at compatibility boundaries
(packet field names, legacy data files and existing database schema). New gameplay/application code should use
**Mecha** terminology. Do not introduce new `HorseService` or `HorseManager` classes.

## Current layout

```text
server/
├─ Program.cs                         # composition root only
├─ Application/
│  ├─ GameFeatureServiceCollectionExtensions.cs
│  ├─ PacketRouter.cs                 # core protocol dispatch only
│  ├─ Packets/
│  │  └─ IClientPacketHandler.cs      # pluggable packet module contract
│  ├─ Bank/
│  │  ├─ BankService.cs
│  │  └─ BankPacketHandler.cs
│  ├─ Combat/
│  ├─ Gm/
│  ├─ Shops/
│  └─ Abstractions/
│     ├─ IGameStore.cs                # compatibility aggregate only
│     └─ GameStorePorts.cs            # small DB-neutral feature ports
├─ Domain/
│  ├─ Sessions/
│  ├─ Assets/
│  ├─ Shops/
│  └─ World/
├─ Protocol/                          # client wire compatibility
├─ Transport/WebSockets/              # connection/framing only
└─ Infrastructure/
   ├─ GameData/
   │  ├─ GameDataServiceCollectionExtensions.cs
   │  └─ ...catalogs
   ├─ Quest/
   ├─ World/
   │  └─ WorldDataService.cs
   └─ Persistence/
      ├─ PersistenceServiceCollectionExtensions.cs
      └─ Sqlite/
```

## Feature modules

A feature owns its application behavior and packet handlers.

Example:

```text
Application/Bank/
├─ BankService.cs
└─ BankPacketHandler.cs
```

`BankPacketHandler` declares:

```text
PacketType = "BankTransfer"
```

`PacketRouter` resolves registered `IClientPacketHandler` modules before its compatibility switch.

This means a feature can be removed by not registering its handler. New features should follow this pattern.
The legacy switch in `PacketRouter` is a migration area, not the desired final architecture.

Configuration example:

```json
"Features": {
  "Bank": true
}
```

As Pet, Mecha, Skill, Shop and other systems are migrated, they should receive the same feature toggle and packet handler boundary.

## Persistence / database replacement

Application code should depend on the smallest required persistence port:

```text
IAccountStore
ICharacterStore
IInventoryStore
IEquipmentStore
IPetStore
IMechaStore
ISkillStore
IShortcutStore
IBankStore
IShopStore
ICharacterAssetReader
```

`IGameStore` is kept only as a compatibility aggregate while old services are migrated.

SQLite currently implements every port with one class:

```text
SqliteGameStore
  ├─ IAccountStore
  ├─ ICharacterStore
  ├─ IInventoryStore
  ├─ ...
  └─ IShopStore
```

A PostgreSQL/MySQL provider may use either one class or several classes. Gameplay code does not need to know.

Desired provider layout:

```text
Infrastructure/Persistence/
├─ Sqlite/
│  ├─ SqliteGameStore.cs
│  └─ SqliteSchemaMigrator.cs
├─ PostgreSql/
│  ├─ PostgreSqlCharacterStore.cs
│  ├─ PostgreSqlInventoryStore.cs
│  └─ ...
└─ MySql/
   └─ ...
```

Do not put SQL in Application, Domain, Protocol or Transport.

## Resource loading

There are two distinct resource categories.

### 1. Static GameData

Examples:

- current client `Items/*.tres`
- current client `Skills/*.tres`
- pet config
- old server `Enemy_Data.csv`
- `NPC.dat`
- `Enemy.dat`
- shop mapping
- legacy NPC visual mapping
- Lua quest source

These are read-only reference data and must be shared across all players.

They are registered as singleton services through:

```text
AddGameData()
```

`CurrentClientDataCatalog` loads current item/skill/pet/mecha definitions once at startup.

`WorldDataService` uses a thread-safe map cache for NPC/enemy static definitions:

```text
MapId -> immutable NPC definitions
MapId -> immutable Enemy spawn definitions
```

A player entering a map must not reread the same `.dat/.csv/.tres` files from disk.

### 2. Runtime world state

Runtime state is different from static GameData.

Examples:

- monster current HP
- monster current X/Y
- aggro target
- respawn time
- dropped items
- players currently in a map
- temporary map effects

The long-term multiplayer model is:

```text
WorldRuntime (singleton)
└─ ChannelRuntime
   └─ MapRuntime
      ├─ Players
      ├─ Monsters
      ├─ Drops
      └─ Map AI tick
```

A `ClientSession` should contain only player/connection-local state:

```text
AccountId
CharacterId
CurrentMapId
LastX / LastY
skill cooldowns
player buffs
pet command state
connection state
```

It should **not** own a private copy of every monster on every visited map.

The current `ClientSession.Monsters / MonstersByMap` is therefore transitional and should be migrated to shared `MapRuntime`.

## Multiplayer performance model

Do not run one complete monster AI loop per connected player.

Target model:

```text
Current transitional model:
100 players on Map07
→ potentially 100 copies of Map07 monsters
→ potentially 100 monster AI loops

Target model:
100 players on Map07 / Channel 1
→ 1 MapRuntime
→ 1 set of monsters
→ 1 AI tick
→ broadcast deltas to interested players
```

### Tick ownership

Recommended responsibilities:

- `WorldRuntime`: owns map/channel lifetimes.
- `MapRuntime`: owns monsters, drops and spatial index.
- `CombatService`: evaluates combat commands against a `MapRuntime`.
- `ClientSession`: never schedules global monster AI.
- WebSocket connection only receives relevant world deltas.

A starting server tick of **100–250 ms** for monster AI is reasonable for this game style.
Movement broadcasts should be delta-based rather than sending complete snapshots continuously.

### Interest management

When player count increases, do not broadcast every entity change to every player.

Use an interest radius / spatial buckets:

```text
MapRuntime
└─ spatial cells (for example 512x512 world units)
   ├─ players
   ├─ monsters
   └─ drops
```

Only send spawn/move/damage events to sessions that can observe the entity.
This is more important than micro-optimizing JSON allocations.

### Map lifetime

Recommended policy:

- First player enters map/channel → create `MapRuntime`.
- Additional players reuse it.
- Last player leaves → keep warm for a short idle timeout.
- Idle timeout expires → persist required state and release runtime objects.
- Static GameData remains cached because it is small/read-only.

This prevents every map from permanently consuming full runtime memory while avoiding repeated disk parsing.

## Concurrency

Singleton read-only catalogs are safe to share.

Mutable world runtime must have one clear synchronization model. Prefer one of:

1. per-map single-threaded actor/event-loop, or
2. per-map lock protecting mutations.

For this server, a **per-map serialized tick/command queue** is preferable because combat ordering is easier to reason about.
Avoid many fine-grained locks around individual monsters.

Database writes should not be used as the real-time world synchronization mechanism.

## Packet flow target

```text
WebSocketEndpoint
    ↓
ClientProtocol
    ↓
PacketRouter
    ↓
IClientPacketHandler
    ↓
Feature Service
    ↓
Domain / WorldRuntime
    ↓
small persistence port(s)
```

Example:

```text
BankTransfer
→ BankPacketHandler
→ BankService
→ IBankStore
→ SQLite/PostgreSQL adapter
```

Combat target:

```text
UseSkill
→ SkillPacketHandler
→ SkillService / CombatService
→ MapRuntime
→ ISkillStore + ICharacterStore only when persistence is needed
```

## Migration order

Refactor incrementally; do not rewrite the server in one pass.

1. Split `IGameStore` into small persistence ports. **Started.**
2. Move packet cases to feature handlers. **Bank is the reference implementation.**
3. Rename runtime/application `Horse` concepts to `Mecha`; retain Horse only at compatibility boundaries.
4. Move Pet packet flow into `Application/Pet`.
5. Move Mecha packet flow into `Application/Mecha`.
6. Move Skill packet flow into `Application/Skill`.
7. Introduce shared `WorldRuntime / MapRuntime`.
8. Move monster state out of `ClientSession`.
9. Add spatial interest management.
10. Run multi-client load/soak tests before adding another database provider.

## Rules for new code

- Use client-facing feature names.
- One feature should not reach directly into another feature's database tables.
- New application services inject small ports, not `IGameStore`.
- Static files are parsed once or cached by key.
- Runtime world state is never duplicated just because another player connected.
- Protocol field compatibility stays at the edge.
- Legacy `Horse*` naming is allowed only where protocol/schema compatibility requires it.
- SQL belongs only under `Infrastructure/Persistence/<Provider>/`.
- New packets should be implemented through `IClientPacketHandler`, not by enlarging `PacketRouter`.
