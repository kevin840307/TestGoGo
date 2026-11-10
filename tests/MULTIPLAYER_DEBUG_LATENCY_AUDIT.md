# 多人 20 人／312 怪 Debug 延遲檢查與修正（2026-11-10）

## 問題來源：實際 Server Log

測試當下約 `ActiveSessions=21`、`Monsters=312`、`Assigned=312`，而非純粹 Godot 畫面造成的延遲：

| 實測項目 | 問題數字 |
|---|---:|
| `MAP_AI_SLOW_TICK` | 3264.9 ms |
| `SessionLockWaitMs` | 2130.1 ms |
| `PlayerSnapshotMs` | 410.5 ms |
| `TickAiMs` | 723.4 ms |
| `AttackEntity` | 最高 1768.3 ms |
| `UseSkill` | 最高 990.2 ms |

**定位：** 在 `SharedMapAiTicker.TickOnce()` 的玩家快照階段，原本於 `lock(session)` 內執行 `GetCharacterHp` SQLite 查詢；此外 20 個合成 Bot 在同一 350ms 迴圈同步發送 `SyncState`、`AttackEntity`、`UseSkill`，會產生不符合真人遊玩的尖峰與同步動畫。不能只靠調 FPS 或減少掉落物顯示解決。

## 已修改

1. `server/Application/World/SharedMapAiTicker.cs`：Session Lock 僅負責複製角色 ID、地圖、位置與寵物位置；HP DB 查詢及 `AccountSessionRegistry.IsCurrent` 移至鎖外。進入 TickAi 前仍檢查帳號／角色／地圖有效性；**不變更每個怪物 AI 的邏輯或 250ms 世界 Tick**。
2. `tests/SGO.Server.WireLoadProbe/InteractiveBots.cs`：每個 Bot 使用獨立非同步循環，起點亂數錯開，間隔在 `--interval-ms` 上下約 25% 隨機變動；不再用 `Task.WhenAll` 同步每輪攻擊。**不改技能、攻擊、移動或怪物傷害的 Server 判定**。
3. `client/scripts/network_entity_manager.gd`：仍保留既有最多 3500µs 的視覺建立預算，但遇到排隊中的其他玩家時可一次建立至多三位，不必每幀只能建一位；怪物、掉落物仍保留原來的生命週期／視覺優先級。
4. `client/scripts/network_manager.gd`：世界內封包數量上限由 48 提升為 128，**每 Physics Tick 4ms 處理預算完全不變**。Debug 模式下若已達預算且還有大量封包，新增最多每 10 秒一筆 `[NETWORK_RUNTIME_BACKLOG]`；正常閒置時不印。遊戲邏輯封包無刪減、網路處理不與 FPS 綁定。

## 用同一個 Server 與資料庫做 A/B 驗證

先保留原有 Debug 的 `Database:Provider`／`Database:Sqlite:Path` 設定，備份現有 DB。此工具**會在現有 DB 增加測試帳號**，請勿在正式營運環境直接壓測。

### Server（沿用原本啟動方式）

如使用 PowerShell Debug Server：

```powershell
git pull origin feature/local-server-net8
$env:Game__GmCommandsEnabled = "true"
$env:Game__TestBotCommandsEnabled = "true"
dotnet run -c Debug --project .\server\SGO.Server.csproj
```

如果用 VS2022 F5，將相同旗標放入 Server 的 Debug 啟動設定；不要以為設定另一個 PowerShell 視窗會自動傳到 VS。

### Client 與負載測試

打開 Godot Debug Client，先正常登入 Map13，測 1 人／20 人同區域。另一個終端機：

```powershell
dotnet run -c Debug --project .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj -- --bots --players 20 --map 13 --seconds 120 --monsters 0 --interval-ms 350 --output bots-result.json
```

測試應看到 `[BOTS] independent action loops started`；如仍看到全部角色每 350ms 整齊換動作，先確認新程式已 pull／編譯，或是否真的仍是 Server 過載所致。

比較三組數據：

- **Server**：`[MAP_AI_PROFILE]` 的 `SessionLockWaitMs`、`PlayerSnapshotMs`、`TickAiMs` 及 `[SLOW_PACKET_DETAIL]` 的 `HandlerMs`、`SessionLockWaitMs`。注意 Log 發生在多人高負載時，不是用空地圖數字比較。
- **Client**：`[NETWORK_RUNTIME_BACKLOG]` 中 `queued_before / drained / remaining / drain_ms`。若此行反覆出現且 `remaining` 增長，代表 Client 收包處理跟不上。若沒有但仍畫面頓，要進一步量測渲染場景建立、SpriteFrames、動畫更新。
- **Bot**：`attack`、`confirmedHits`、`visibleTargets`、`rejected`、`corrections`。不能以攻擊封包數當成成功傷害數。

## 回歸驗證

```powershell
dotnet build -c Debug .\server\SGO.Server.csproj
dotnet build -c Debug .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj
dotnet run -c Debug --project .\tests\SGO.Server.SmokeTests\SGO.Server.SmokeTests.csproj -- --suite concurrency
godot --headless --path client --script res://tests/ground_item_aoi_regression.gd
godot --headless --path client --script res://tests/aoi_pre_spawn_move_regression.gd
```

人工檢查兩人近距離互動時的 SpawnEntity、MoveEntity、SyncState、戰鬥、掉落、拾取、跨 AOI 離開／返回、重新連線；核對沒有遺漏／重複物品、沒有幽靈玩家。

**限制與下一步：** 以上是針對已定位的風險修正，尚未實際在 20 人／312 怪場景跑出新的對照數據。若 Session Lock 等待下降、但 `TickAiMs` 仍超過 250ms，才需要繼續拆解 CombatService 的怪物 A*／資料庫熱路徑；不可任意減慢玩家網路、戰鬥或移動更新，或在 Tick 忙碌時跳過遊戲邏輯。
