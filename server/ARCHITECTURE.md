# SGO Server Architecture

The server is organized around the same concepts the recovered client exposes:
**Account, Character, Inventory, Equipment, Pet, Mecha, Skill, Bank, Shop, Combat, World, NPC/Quest**.

The primary design goals are:

1. Client/server naming should match wherever possible.
2. Gameplay modules should be independently removable.
3. Database providers should be replaceable without changing gameplay code.
4. Static game resources should be loaded once and shared.
5. Multiplayer runtime state should be shared by map/channel, not duplicated per connection.

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
