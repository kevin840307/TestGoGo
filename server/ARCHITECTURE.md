# Server Architecture

Current implementation baseline and next priorities: see [STATUS.md](STATUS.md).

The server is layered so gameplay and protocol code do not depend on SQLite.

```text
server/
├─ Program.cs                         # composition root only
├─ appsettings.json
├─ Application/
│  ├─ PacketRouter.cs                 # request/game flow
│  ├─ Combat/                         # monster/pet/player combat services
│  ├─ Gm/                             # server-side GM chat commands
│  ├─ Shops/                          # NPC shop buy/sell application flow
│  └─ Abstractions/
│     └─ IGameStore.cs                # database-neutral contract
├─ Domain/
│  └─ Sessions/
│     └─ ClientSession.cs
├─ Protocol/
│  └─ ClientProtocol.cs               # AES/Base64/JSON client wire format
├─ Transport/
│  └─ WebSockets/
│     └─ WebSocketEndpoint.cs
└─ Infrastructure/
   ├─ GameData/                       # current-client + legacy reference catalogs
   ├─ source/
   │  ├─ Data/
   │  │  ├─ EnemyCatalog.cs
   │  │  ├─ EnemyDatReader.cs
   │  │  └─ NpcDatReader.cs
   │  ├─ Quest/
   │  │  ├─ DialogFormatter.cs
   │  │  └─ QuestRuntime.cs
   │  └─ World/
   │     └─ WorldDataService.cs
   └─ Persistence/
      ├─ PersistenceServiceCollectionExtensions.cs
      └─ Sqlite/
         └─ SqliteGameStore.cs
```

## Dependency direction

- **Transport** handles WebSocket framing only.
- **Protocol** handles recovered-client encryption and JSON helpers.
- **Application** contains request/game flow and depends on abstractions.
- **Domain** contains in-memory session/domain state.
- **Infrastructure.GameData / Infrastructure.Quest / Infrastructure.World** adapts original map, enemy and Lua quest data.
- **Infrastructure.Persistence** owns provider-specific SQL.

Application code must not reference `Microsoft.Data.Sqlite` or provider SQL.

## Database provider

Default:

```json
{
  "Database": {
    "Provider": "Sqlite",
    "Sqlite": {
      "Path": ""
    }
  }
}
```

An empty SQLite path means `Data/game.db` next to the server executable.

### Adding MySQL

1. Add the chosen MySQL .NET driver.
2. Create `Infrastructure/Persistence/MySql/MySqlGameStore.cs`.
3. Implement `Application/Abstractions/IGameStore.cs`.
4. Add the provider registration in `PersistenceServiceCollectionExtensions`.
5. Add `Database:MySql:ConnectionString`.
6. Run the same persistence/application smoke tests against MySQL.

The following should remain unchanged when changing DB providers:

- WebSocket endpoint
- packet encryption/protocol
- PacketRouter/game flow
- NPC/monster loaders
- Lua runtime
- map transitions

## Rules for new code

- SQL belongs only under `Infrastructure/Persistence/<Provider>/`.
- Client packet details belong under `Protocol/` or Transport, not persistence.
- Old server binary/data compatibility belongs under `Infrastructure/`.
- New gameplay systems should be added under Application/Domain rather than growing Program.cs.


## SQL file policy

Database schema and initialization SQL must live under `Database/<Provider>/`.

Examples:

```text
Database/
├─ Sqlite/
│  ├─ 001_schema.sql
│  ├─ 002_indexes.sql
│  └─ 003_character_columns.sql
└─ MySql/
   └─ 001_schema.sql
```

Provider startup code reads these files and executes them. Do not embed `CREATE TABLE`, `ALTER TABLE`, `CREATE INDEX`, or seed DDL in C#.

Parameterized runtime DML such as account login, item purchase, movement persistence, and inventory updates remains in the provider implementation because it is application data access rather than database initialization.
