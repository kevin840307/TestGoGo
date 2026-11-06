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
- SQLite persistence and PBKDF2 account password hashing.
- Three-character limit, authenticated session checks, 2 MiB packet bound.

## Known gaps

- Preliminary `EnterWorldSuccess` payload and empty inventory/equipment/pet/horse/skill/quest sync; **unverified** against live Godot runtime.
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
