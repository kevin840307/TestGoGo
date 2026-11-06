# SGO Local Server (.NET 8 + SQLite)

A **localhost-only prototype** built to match the recovered Godot client's WebSocket protocol. It is not yet a playable offline MMO.

## Start

```powershell
cd server
dotnet restore
dotnet run
```

Server: `ws://127.0.0.1:5678`; health endpoint: `http://127.0.0.1:5678/health`. SQLite automatically creates `Data/game.db` next to the server binary.

## Client change (approved)

In the recovered project's `scripts/network_manager.gd`, set `USE_DDNS` from `true` to `false`; see `client-patch/scripts/network_manager.gd.patch`. The existing localhost URL will then be used. DDNS code is left intact for future use. The original shipped EXE is not modified.

## Implemented

- WebSocket + AES-256-CBC Base64-encoded JSON compatible with existing client wire format.
- Account registration/login, role create/list/delete/password change, preliminary role selection.
- SQLite persistence and PBKDF2 account password hashing.\n- Character bootstrap fields required by `PlayerData.load_from_json()`.\n- Local movement/state persistence, local chat echo, revive status sync.\n- Logout handshake closes WebSocket to match the recovered client's return-to-login flow.
- Three-character limit, authenticated session checks, 2 MiB packet bound.

## Known gaps

- `EnterWorldSuccess` now contains complete safe defaults plus empty inventory/equipment/pet/horse/skill/buff/quest sync; **still unverified** against live Godot runtime.
- No monster AI, NPC logic, quests, combat, movement or save-position handling yet.
- The recovered client's static AES key/IV provide **no authenticated transport security**. Listener is deliberately loopback only.
- Client character password verification expects a password field in character list; prototype stores that specific character password as plaintext for protocol compatibility. Do not reuse real passwords.
- The recovered entry manager expects `character_list[0].ACCID` even for new accounts; an empty placeholder slot is sent to support first character creation.
- Actual map scenes must still be present in Godot project; recovery tools do not ensure buildability.

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

Map15 is explicitly treated as the main city: NPC/portal spawns are loaded, but combat Enemy.dat records are not spawned there. A portal script can issue `MovePlayer(x,y,mapId)`; the destination is persisted to SQLite and destination entities are spawned only after the first `SyncPosition` from the newly loaded client map.

Implemented Lua compatibility calls: `Say`, `Ask`, `GetChoice`, `GetData`, `MovePlayer`, `QuestEnd`. `OpenShop` is currently a compatibility stub.


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
