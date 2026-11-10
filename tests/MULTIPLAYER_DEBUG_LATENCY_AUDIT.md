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


## 2026-11-10：120 秒 / 20 Bot 實測與第二輪修正

來源：`bots-result.json`（RunId `6d9a215ec3`）及 `godot(20261110-112807).log`，皆由使用者實測提供。20/20 Bot 連線、`AttackPackets=4944`、`ConfirmedDamageHits=4243`、`SingleTargetSkillPackets=196`、`Errors=[]`；`ObservedMonsterDeaths=47278` 為**各 Bot 接收到的死亡事件累計**，不能當作 47,278 隻不重複的實際死亡怪物。所有 Bot 共接收 117,877,168 bytes / 120 秒。

Godot 記錄 `NETWORK_RUNTIME_BACKLOG queued_before=325 drained=115 remaining=210 drain_ms=4.01`；`ENTITY_SPAWN_TIMING` 單次場景建立曾達 84.50ms；`MONSTER_CHASE_PACKET_GAP` 出現約 2.6–4.6 秒的視覺事件間隔，不能單獨證明 Server 漏包；`ground_known` 最多約 1,883，`ground_items=0` 因畫面設定為「不顯示」。背景的 20FPS 是節能設定，不等於前景實際 FPS。

### 本輪修改

1. **距離／阻擋**：`CombatService.Attack` 原本使用 `AttackRange+120px` 且沒有 LOS；現在保留 `+24px` 小幅網路誤差，並在有 `Blocking.dat` 的地圖執行 `MonsterNavigationService.IsDirectClear`。單體傷害技能的 `CastRange` 同樣改用 `+24px` 並檢查牆壁；無地圖阻擋資料的合成煙霧測試地圖維持距離驗證。
2. **弱化測試 Bot**：只對 `IsLoadTestBot` 的非範圍技能將傷害限制為 1。普攻原本最少就是 1，不能再降低；新增獨立 `--attack-ms 1000`（預設一秒一攻），與 `--interval-ms 350` 移動速度分離。Bot 自己要求在 92px 以內、無阻擋才發普攻或單體技能，避免嘗試從遠方傷害目標。
3. **Client 隱藏掉落物**：在 `ground_drop_display_mode=2` 時跳過昂貴的地上物視覺 AOI 全量掃描。收到的資料、所有權、壽命、拾取權限、伺服器封包完全保留，切回顯示由原有設定變更邏輯刷新。
4. **Client 擁擠畫面 AOI**：Balanced/Low + 自動怪物顯示時，已知怪物 >=96 才收窄視覺建立範圍；<=64 恢復。依照攝影機實際覆蓋距離保證畫面內怪物仍可見，且不更改 Server AOI／傳送／戰鬥；手動「一般」、畫質優先不套用。新增 `MONSTER_VISUAL_CROWD_AOI` Debug 記錄。
5. **怪物動作動畫**：不同 EntityID 的 idle/walk SpriteFrames 起始影格錯開，避免所有怪物同一影格同步循環。這**只是動畫顯示**，不是 Server 每隻怪物的獨立 AI Tick；真正的網路封包延遲仍需看 Server log。

### Debug 再測試（沿用原本 Server 與資料庫）

重新編譯並啟動同一個 Server、Godot Client（需要 Client 資源更新），測試指令：

```powershell
git pull origin feature/local-server-net8
dotnet build -c Debug .\server\SGO.Server.csproj
dotnet build -c Debug .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj
dotnet run -c Debug --project .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj -- --bots --players 20 --map 13 --seconds 120 --monsters 0 --interval-ms 350 --attack-ms 1000 --output bots-result.json
```

本輪修改已提交 GitHub，但尚未在此環境實際跑過 dotnet / Godot 編譯及壓力測試，**不能宣稱卡頓已經消除**。如果仍遲滯，優先蒐集新 Server `MAP_AI_PROFILE` 與 `SLOW_PACKET_DETAIL`，對照 Client `NETWORK_RUNTIME_BACKLOG`／`ENTITY_SPAWN_TIMING`／`MONSTER_VISUAL_CROWD_AOI`，分辨 Server lock、AI 計算或 Client 場景建立耗時。


## 2026-11-10：第三輪 — 遠端角色一格一格走、封包反壓與重複 AOI

### 確認的設計問題

- Server `PacketRouter` 每 **100ms 至多廣播一個**玩家位置，但舊版 `MoveEntity.SpeedTime` 固定 `0.10`，即使該玩家實際每 350ms 才移動一次。Client 的 `network_player.gd` 另外把測量間隔硬限 `0.25s`，且**每收到一筆快照就重啟插值**。結果長間隔時會「移動—停頓—再移動」，不是單純 FPS 問題。
- 多人測試 Bot 原本**每次 `SyncPosition` 固定只走 16px，約每 350ms 才一格**。這不是一般角色的流暢移動，而且不需要增加封包量即可改善。
- Server 過去對每筆 `SyncPosition` 都驗證地圖／刷新 AOI，即使座標沒有變；`SyncPresence` 又在每次封包和 250ms socket Tick 重複執行玩家／世界 AOI 兩種索引更新。
- WebSocket 優先佇列遇到網路反壓時會累積同角色很多**過時且未送出的** `MoveEntity`。如果全部依序補播，觀察者會慢慢走過早已過期的舊路徑。

### 已修改（不降低固定更新與遊戲邏輯）

1. **遠端角色視覺插值**：新增 `client/scripts/remote_player_motion.gd`，整合到 `client/network_player.gd`。`MoveEntity` 保留 Server 權威終點，物理更新每幀用連續位置＋小幅、有上限的速度預測更新畫面，避免每個封包重啟短 tween。推測最多 40px、0.28 秒；超過長時間沒封包會向最後權威位置平滑修正；收到 `SyncState idle/attack/hit/death` 停止預測。真正大幅傳送（>=1200px）仍直接同步，避免跨牆插值。此預測結果只用於**遠端渲染**，絕不進入 Server 戰鬥／拾取／座標權威。
2. **Server 不發冗餘位置**：`SyncPosition` 若 X/Y 未變，直接回傳，不做碰撞／SQL dirties／AOI／廣播；有更新時 `SpeedTime` 是當前與上次廣播之間的實際間隔（限制 0.05～0.75 秒），而非寫死 0.10。原本的 100ms 廣播節流與 Ping/Gameplay 事件不變。
3. **有背壓時合併過期玩家移動快照**：`ServerBroadcastHub` 僅對 WebSocket 玩家優先佇列的 **同 EntityID 尚未送出的 MoveEntity** 更新成最新位置，不增加 WebSocket 發送次數。遇到同角色 `SpawnEntity`、`DespawnEntity`、`SyncState`、`SkillCast`、`EntityDamage` 等事件必須截斷合併區間；其他封包（傷害、技能、掉落、怪物 AI／AOI）**不會被合併或丟棄**。隊列出站時維持原有同角色重要事件順序。
4. **減少熱路徑多餘 AOI**：`PacketRouter.SyncPresence` 只在角色／地圖／WorldReady／座標改變後才執行昂貴的玩家與世界空間索引 reconcile；`SyncPosition` 已執行的 reconcile 不再緊接著做第二次。其他怪物／掉落物由其自身 AOI upsert 即時處理，沒有降低封包處理速度。
5. **測試 Bot 真正連續的步幅**：`BotWalkPlanner` 允許依 `--interval-ms` 選擇最高 64px 的路段，350ms 約走 49px；仍以 Blocking.dat + A* 做整段驗證，遇到窄路則退回 16px 安全步長。仍每 ~350ms 才送 1 筆位置，並未提高封包頻率。
6. **Debug 診斷**：`[REMOTE_PLAYER_PACKET_GAP]` 在遠端角色走路時出現 500ms 以上位置封包間隔才印，且全域最多 12 筆，不造成大量 Debug log。

### 新增回歸測試

```powershell
godot --headless --path client --script res://tests/remote_player_motion_regression.gd
dotnet run -c Debug --project .\tests\SGO.Server.SmokeTests\SGO.Server.SmokeTests.csproj
```

- Godot 驗證 350ms 快照不單幀跨 16px、不在新位置封包立即瞬移、權威 idle 收斂、地圖傳送可立即定位與 burst 收包有界。
- `PlayerSpatialAoiSmokeTests` 驗證被合併的玩家移動快照只剩最新位置，但攻擊動作、Despawn 邊界之前與之後的 MoveEntity 仍保持順序。
- `SessionIdentityCleanupSmokeTests` 驗證 WorldReady 初始化、未移動 socket Tick 不需重新 reconcile、改變位置會刷新 AOI、離開 WorldReady 會移除觀察者；重複 `SyncPosition` 不會污損 DB 位置狀態。

### 真實多人 Debug 重測

**同一個既有 Server 與資料庫**；先在 Server 與 Godot Client 都更新並重啟，使用 F5 / `-c Debug`。Bot 命令不變：

```powershell
git pull origin feature/local-server-net8
dotnet run -c Debug --project .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj -- --bots --players 20 --map 13 --seconds 120 --monsters 0 --interval-ms 350 --attack-ms 1000 --output bots-result.json
```

比對：角色走路是否仍一格一格、`[REMOTE_PLAYER_PACKET_GAP]` 次數、`[NETWORK_RUNTIME_BACKLOG]` 剩餘封包、`[MAP_AI_PROFILE]` TickAi/SessionLockWait、`[ENTITY_SPAWN_TIMING]` 與 `bots-result.json` 每位平均接收 bytes。**如果 Client 平滑但 Server 仍長時間停止送怪物 MoveEntity，仍須另行拆解 CombatService/SharedMapAiTicker 的熱路徑**，不能藉由 Client 預測假造傷害或伺服器位置。

**測試限制**：此工作環境沒有 .NET 8 SDK 與 Godot CLI；GitHub 上已新增程式碼／測試，但尚未實際執行編譯及 20 人端對端驗證，所有效能效果以重測 Log 為準。
