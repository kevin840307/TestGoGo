# Server Architecture

The server is intentionally layered so gameplay and protocol code do not depend on SQLite.

```text
server/
├─ Program.cs
├─ appsettings.json
├─ Application/
│  ├─ PacketRouter.cs
│  └─ Abstractions/
│     └─ IGameStore.cs
├─ Domain/
│  └─ Sessions/
│     └─ ClientSession.cs
├─ Protocol/
│  └─ ClientProtocol.cs
└─ Infrastructure/
   ├─ Legacy/
   │  ├─ LegacyDialogFormatter.cs
   │  ├─ LegacyEnemyCatalog.cs
   │  ├─ LegacyEnemyDatReader.cs
   │  ├─ LegacyNpcDatReader.cs
   │  ├─ LegacyQuestRuntime.cs
   │  └─ LegacyWorldService.cs
   └─ Persistence/
      ├─ PersistenceServiceCollectionExtensions.cs
      └─ Sqlite/
         └─ SqliteGameStore.cs
```

## Dependency direction

- **Application** contains game request flow and depends on abstractions only.
- **Domain** contains in-memory game/session state.
- **Protocol** contains recovered-client wire compatibility (AES/Base64/JSON helpers).
- **Infrastructure.Legacy** adapts the original map/quest data into the new server.
- **Infrastructure.Persistence** contains database-specific implementations.

`PacketRouter` depends on `IGameStore`, not on SQLite.

## Database provider

Default configuration:

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

To add MySQL later:

1. Add a MySQL driver package.
2. Create `Infrastructure/Persistence/MySql/MySqlGameStore.cs`.
3. Implement `IGameStore`.
4. Register it in `PersistenceServiceCollectionExtensions` under `case "mysql"`.
5. Add `Database:MySql:ConnectionString` configuration.
6. Run the same application/smoke test contract against the MySQL implementation.

No WebSocket, packet routing, Legacy Lua, NPC, monster, or gameplay code should need to change.

## Rule for new code

Do not put SQL inside Application/PacketRouter or Legacy code. Database-specific SQL belongs only under `Infrastructure/Persistence/<Provider>/`.
