# SGO 多人移動：2004 原版封包/步進機制研究與新版優化規格

> 狀態：2026-11-10 第一版（證據與設計基準；待實測驗證）。先提交本文，再進行後續程式修改。  
> 分支：`feature/local-server-net8`。優先級：**多人玩家即時性 > 怪物移動 > 寵物跟隨 > 裝飾/掉落物呈現**。  
> 原則：保留 Server 權威、AOI、攻擊判定與固定更新速率；不能靠降低 FPS、減慢收包、暫停 AFK/背景網路與遊戲邏輯來取得流暢度。

## 1. 來源與證據等級

原始反編譯檔（本次由使用者提供，**不把整份大型反編譯檔提交 Git**）：

- `Paradise.exe.lst`：2004 原版 Client；`CSGOMap::MessageMap`（0x004CD52C）、`CSGOChar::MoveStep`（0x004BEA62）、`CSGOChar::SetMoveloop`（0x004BF0E9）、`CSGOEnem::movelogic`（0x004C9D69）、`CSGOEnem::SetMoveloop`（0x004C9FA5）、`CSGOMap::OnCharSyncDest`（0x004D1610）、`CSGOMap::OnCharSyncPosition`（0x004D2E1F）、`CSGOMap::OnEnemyMove`（0x004D1E1A）。
- `SGOMapService.dll.lst`：原版地圖服務；`CDMapService2D::OnPlayerMove`（0x10038104）、`CDMapService2D::OnNPCMove`（0x10046254）、`CDMapService2D::PlayerUpdate`（0x1002E180）、`CDMapService2D::NPCUpdate`（0x1002EA44）、`CDMapService2D::SendSyncDestPac`（0x1005E689）、`CDMapService2D::SendSyncPosition`（0x10069E2C）、`CDMove::OnMove`（0x100847E0）、`CDMapService2D::Update`（0x1002D58F）。
- `SGOPlayer.dll.lst`：原版玩家服務；`SGOPlayer::Update`（0x10018522）、`CDPlayerSys::Update`（0x10043FB1）、`CDPlayerSys::UpdateClient`（0x1003ED72）、`CDPlayerSys::UpdateOwnClient`（0x1003E4D9）。
- `CDGameShell.exe.lst`：原版 Shell/啟動與核心 DLL 容器，證明存在 DLL 載入/核心執行入口；**未憑此單檔推算實際遊戲收發週期**。

**已證實**：IDA 的命名函式、明確指令與新版程式碼設定。  
**推論**：原版 Client 更接近「收到路徑/目標後自行移動；位置同步用於校正」；須再用封包 trace 或更完整的函式逐一確認適用範圍。  
**未知**：原始正式服每種封包的**實際每秒發送次數**、網路延遲分布、是否有動態節流策略。不能用原版 Client 的畫面 FPS 冒充 Server 發包頻率。

## 2. 原版資料流（組語支持）

```text
原版地圖 Server
  CDMapService2D::OnPlayerMove / OnNPCMove
       -> CDMove、CDPlayerMove、CDNPCMove 的移動/路徑狀態
       -> SendSyncDestPac / SendSyncPosition ...（服務端封包）
           -> 原版 Client CSGOMap::MessageMap（根據命令分派）
           -> OnPlayerSyncDest / OnCharSyncDest / OnEnemyMove / OnCharSyncPosition
           -> CSGOChar::SetMoveloop / MoveStep、CSGOEnem::movelogic
           -> 每次遊戲更新推進本地座標與動畫
```

可直接核對的細節：

1. `Paradise.exe` 在 0x004017F6 把 `0x32 (=50)` 傳給 `CELGameManager::SystemSetFrame`：原版 Client 有 **50 frame/s 設定**，這**不是發包頻率證據**。
2. `CSGOChar::MoveStep` 在 0x004BEA6C～0x004BEAD2 對內部 X/Y 步進量加法，之後 `sar eax, 8` 將固定點值回寫畫面座標；`CSGOEnem::movelogic` 在 0x004C9D73～0x004C9DF1 有對應的累加/移位。**不是等下一張位置封包才逐格瞬移**。
3. `CSGOMap::MessageMap`（0x004CD52C）按命令分派：`0xA106` 對應 `OnPlayerSyncDest`、`0xA107` 對應 `OnCharSyncDest`、`0xA108` 對應 `OnEnemyMove`、`0xA10D` 對應 `OnCharSyncPosition`（參考 case 41222/41223/41224、0x004CDC16）。
4. `SGOMapService.dll` 的 `SendSyncDestPac`（0x1005EAEB）將 `0xA106` 設為 cmd，`SendSyncPosition`（0x1006A27A）將 `0xA10D` 設為 cmd，證實 **目的地指令與位置校正是分離的封包類別**。部分角色的目的地/怪物命令可能由其他路徑發出，不能當作全部都用 A106。
5. 原版服務使用 `GetCurrentFPS()` 計算時間基準：如 `SGOPlayer::Update`（0x10018C1F）有 `1000 / FPS` 計算；`CDMapService2D::Update`（0x1002D9A6）、`CDPlayerSys::Update`（0x100443D8）亦依 FPS 計數/呼叫。**只可確認 frame-based 更新架構，不能直接據此宣稱網路廣播間隔等於 frame。**

### 2.1 原版與新版不是相同的 wire protocol

原版是 `CelSockPacket` 數值命令與二進位欄位；新版為 WebSocket + JSON/AES 封包 `MoveEntity`、`SyncState`、`SpawnEntity`、`DespawnEntity` 等。因此應移植**行為與時序語意**，不是直接硬套舊封包格式或 ID。

## 3. 新版現況（2026-11-10）

| 子系統 | 實際配置/函式 | 多人風險 |
| --- | --- | --- |
| 玩家移動 | `server/Application/PacketRouter.cs` 的 `PositionBroadcastInterval = 100ms`，`MoveEntity.SpeedTime` 為**實際 Server 快照間隔** | 發包被節流但接收可能批量抵達；不可用 arrival gap 直接取代 Server duration |
| 怪物 AI | `server/Application/World/SharedMapAiTicker.cs` 共用 `PeriodicTimer(250ms)` | Tick 超時、擁塞或排隊可讓所有怪物同步卡頓 |
| WebSocket | `client/scripts/network_manager.gd` 每 physics tick poll；in-world 預設上限 128 封包與 4ms 工作預算 | 大量視覺工作與排隊可使收到的狀態變舊；但不應為追求畫面流暢降低收包/固定邏輯頻率 |
| 其他玩家 | `client/scripts/remote_player_motion.gd` / `client/network_player.gd` 以權威位置做視覺預測 | 收包間隔抖動、idle / burst / teleport 交錯造成視覺回跳 |
| 怪物 | `client/scripts/monster_entity.gd` 追逐最新位置；`network_entity_manager.gd` 負責派送 | 原先 `MAX_SPEED = 205px/s` 在遠距 250ms waypoint 會追不上，超時後停住再追 |
| AOI | `network_entity_manager.gd` 儲存已授權快照，管理 spawn、deferred、offscreen | 延遲 Spawn/Move/Despawn 順序、遠近切換不可用舊封包讓實體復活 |

**已在本文之前完成的第一輪修正（不等於已完整解決）**：

- `remote_player_motion.gd` 以 Server `SpeedTime` 優先，避免 WebSocket arrival jitter 直接造成速度推估錯誤（commit `bd468194`）。
- `monster_entity.gd` 提高原先 205px/s 的追點上限，依目標距離及 duration 計算（`66a27a42`）。
- `network_entity_manager.gd` 避免怪物狀態包重複 `sync_animation()`（`f9f93342`）。
- 基本 regression：`remote_multiplayer_motion_regression.gd`、`monster_chase_regression.gd`（`a6d9d5d6`、`98342d1f`）。
- **以上需持續測試，不能視為已通過 20 人遊玩驗收。**

## 3.1 2026-11-10 實際多人壓測證據（重要）

GitHub Actions：[20 人 wire latency 測試 #42813635362](https://github.com/kevin840307/TestGoGo/actions/runs/42813635362)，測試完成且 `Success=True`，**但不是延遲達標**：

- 移動：`1140/1140` 個 delivery；`RoundLatencyMs P50=929.2ms`、`P95=4008.4ms`、`P99=4008.4ms`。這是**該測試的 round latency 統計**，不能直接冒充玩家端畫面每一包的 one-way latency。
- 附近聊天：`1200/1200`，`P50=5.5ms`、`P95=12.6ms`。
- 共用 AI tick：`P95=14.3128ms`；然而 log 也出現 `[SLOW_PACKET_DETAIL] Type=SyncPosition HandlerMs=3000.8`、`AttackEntity HandlerMs=1002.3`、`[SLOW_WORLD_TICK] LockWaitMs=768.0`。

**結論：多人慢不是單純 Client 渲染；Server 的移動請求偶爾會阻塞上千毫秒。** 只用平滑動畫不能填補秒級過期位置。故 P0 需同步收集 `[SLOW_MOVEMENT_HANDLER]`（Collision/AOI/Persist/Broadcast 分段）、`[SLOW_PACKET_DETAIL]`、`[MAP_AI_PROFILE]`，判定 SQLite 寫入、AOI 列舉或其他請求占鎖的責任；**不可先猜測是某一個鎖、直接刪交易/驗證或大幅改寫 Server**。

新提交的純 Client 診斷及測試（在本文建立後進行）：

- `client/scripts/multiplayer_motion_audit.gd`：Debug 模式每 10 秒聚合每類 actor 的收包間隔、Server SpeedTime、距離最新 waypoint 的追點距離，有限 actor 記憶體，沒有逐封包印 Log。
- `client/scripts/network_manager.gd`：Debug 模式 `[MULTIPLAYER_PACKET_AUDIT]` 聚合 Socket 收包量、分類、佇列峰值、每個 physics tick 處理預算耗盡次數；不變更收包上限、排序或 FPS。
- `client/tests/multiplayer_motion_audit_regression.gd`：保證 player/monster 統計隔離、Server 時間與收包時間分開、換圖/消失清理。
- `.github/workflows/client-multiplayer-motion.yml`：獨立安裝已確認存在的 Godot 4.7.2 stable，匯入資源並執行 remote player、monster、AOI、audit 的 headless regression。此 workflow 與原有 Quest strict audit 失敗互不阻塞。

## 4. 預計實作順序（先文件、再程式、再量測）

### P0：證據與時間線（先做）

1. Client 只在 Debug 記錄每類封包：`server_interval_ms` (`SpeedTime`)、`receive_gap_ms`、每 physics tick 處理數、最久封包處理時間、queue backlog、每類 actor 的 visual-vs-authoritative 差距 p50/p95/max。輸出**聚合**統計，不要 per-frame 大量 print。
2. Server 取 `MAP_AI_TICK_GAP`、`MAP_AI_SLOW_TICK`、`MAP_AI_PROFILE` 和 20 人負載 `Movement p50/p95/p99`；若 Server 250ms tick 本身延遲，僅靠 Client 插值無法修復。
3. 釐清 `MoveEntity` 的 `SpeedTime` 是「前次快照間隔」而不是「距離目的地需要時間」，尤其是 bots 的 350ms 更新。收到多個 packet 同一 frame 時不能逐一把舊資料當新目標。

### P1：玩家視覺運動

- 保留 `authoritative_position` 供遊戲邏輯；新增獨立視覺軌跡，以**Server 快照的時間/位移**形成目標速度與有限預測，不用每包直接重啟 Tween。
- 研究短時間 snapshot buffer（建議初始約 100–150ms 可調）、採樣插值／限時外推；**實際參數依 p95 jitter 和使用者視覺驗收決定，不在未實測前宣稱最佳**。
- Idle/攻擊/死亡/傳送/地圖切換收到時即套用權威事件；避免回拉和穿牆視覺假象；面向與步行動畫避免每包重播。
- 本地角色仍是立即輸入／本地運動，不因他人視覺平滑而增加自己的控制延遲。

### P2：怪物與寵物視覺運動

- 區分持續移動 waypoint 與位置校正 `SpeedTime <= 0.01`；持續移動使用固定 update 連續步進，避免「每 250ms 到點站一下」。
- 怪物進入 attack/hit/death 時依權威狀態立刻反應，不能為平滑而延後技能/碰撞/死亡；遠距追擊與回頭角度要自然。
- 同批怪物不應因每包同步重置動畫 phase；offscreen 只停止不必要的視覺製作，保留位移與網路處理。
- 無移動時不更新動態視覺；不額外拉低渲染 FPS、不增加 Server 網路頻率作為唯一解。

### P3：多人壓力與回歸

- 完整 Godot headless（player、monster、AOI、packet contract、AFK、20 玩家視覺採樣）及 .NET smoke/20 wire probe。
- 測試案例：連續每 100ms/250ms 包、350ms bot、抖動 100→350→50ms、同 frame burst、長時間丟包、角落轉向、攻擊切換、死亡、Spawn→Move 亂序、換圖清理、20 名玩家與大量怪物/掉落物同時出現。
- 驗證 **遊戲邏輯、封包收取與固定 physics 更新永不因失焦/AFK/縮小/低資源模式被降速**。

## 5. 驗收指標（建議初始門檻，非實測結果）

- 20 人 + 怪物 Map13：其他玩家走路**沒有每 100–350ms 明顯停頓/瞬移**；怪物不會全體一起抽動。採用影片對照原版 + 新版。
- 一般路徑下連續移動時 `visual_vs_authoritative` p95 建議小於 **40px**，重大校正需單獨記錄而非隱藏。
- Server 20 人 Movement delivery 完整；p95/p99 延遲需與修改前比較，不能惡化。FPS 前台原設定 30/60（或使用者自訂）不下降；AFK/失焦只影響視覺、音效。
- 攻擊距離、命中、死亡、撿物及 AOI 權威座標與事件順序不受影響。
- 每項測試保留可重跑指令、環境、測試結果和失敗原因；無 Godot Runner 則不得假稱 PASS。

## 6. 仍需資料（有則精準定位，沒有也可先進行 P0/P1）

1. **最好提供**原版 Server 的 `CDServer` / 主引擎核心 `*.dll.lst`（含 `GetCurrentFPS`、服務呼叫排程與 CelSocket 送包實作），才有機會推導真實廣播節奏、聚合/節流條件。現有 `CDGameShell` 比較像外殼，不足以定論。
2. 有條件才補：原版 Server/Client 10–30 秒封包擷取或具時間戳 trace（玩家長走、怪物追擊、多人）；可驗證頻率、命令順序及角落/攻擊時的額外同步包。
3. 新版同條件的 `godot.log` + `bots-result.json` + Server log／短錄影；能快速分辨「收包已延遲」與「收到了但描畫不順」。

## 7. 不做的事

- 不改舊版二進位協定為新遊戲協定；不把反組譯資料全量入庫。
- 不靠把 60fps 改成 20fps、不把網路/遊戲物理降頻、不為視覺犧牲遊戲邏輯。
- 不在缺實測時宣稱舊版送包「一定每 20ms」、或所有多人 lag「確定只是 Client」。
- 不影響已實作的 AFK 外觀 UI、不破壞角色與怪物原本的 AOI、戰鬥/互動契約。


## 8. 2026-11-10 實作與實測結果（本版更新）

### 8.1 `Old/` 新發現的資料

Git `Old/` 內已存在 `Old/ClientData/Paradise.exe.lst`、`Old/服務端/Core.dll.lst`、
`Old/服務端/CDGameShell.exe.lst`、`Old/服務端/plugins/SGOMapService.dll.lst`、
`Old/服務端/plugins/SGOPlayer.dll.lst`，以及 `CDRDPortSys.dll.lst`、
`SGOCareerSys.dll.lst` 等。無需再次上傳已存在的反編譯檔。
本次已用已附上的完整 `SGOMapService.dll.lst` 和 `Paradise.exe.lst` 直接確認：

- `CDMapService2D::SendSyncDestPac`：`0x1005EAEB` 將 `0xA106` 作為命令。
- `CDMapService2D::Update`：`0x1002D9A6` 透過 `CDServer::GetCurrentFPS` 計數。
- `CSGOChar::MoveStep`：角色固定點步進，並非每封包直接瞬移。
- 以上只能證明 **frame update 和目的地/位置同步的分工**，不能反推原版實際每秒送多少包。
- `Core.dll.lst` 可進一步追核心計時器，但 GitHub 大型檔的 Contents API 無法回傳完整正文；本階段不因這點卡住實作。

### 8.2 修改範圍

| 類型 | 更新 | 保護 |
|---|---|---|
| Client | `remote_player_motion.gd` 以 Server `SpeedTime` 推估速度，不因 WS 收包抖動誤減速 | 仍限制預測距離/時間，權威位置獨立 |
| Client | `monster_entity.gd` 拔除不合理 205px/s 追點上限，`monster_waypoint_speed.gd` 分離算式 | 0 時間立即校正、attack/hit/death 優先 |
| Client | `network_entity_manager.gd` 怪物位置移動改由固定 Physics Tick 驅動 | FPS/AFK/失焦只改視覺，不改封包與戰鬥 |
| Client | `network_manager.gd` 及 `multiplayer_motion_audit.gd` Debug 每 10 秒聚合封包量、排隊與追點差距 | 不逐包大量 print、不篡改封包 |
| Server | `PositionPersistenceService` 100ms 背景檢查、每角色合併最新待存座標；`SqliteGameStore.UpdatePositionsBatch` 單交易 | 3 秒儲存節奏不變，登出同步儲存，換圖/復活/傳送 fence，舊地圖 SQL map_id guard |
| Server | ChatMessage 正常聊天改用 EnterWorld 已驗證的角色名稱/種族，不再每則重新載入角色；密語改查線上 Presence | GM/社團特殊指令、權限與其它頻道沿用原處理 |

### 8.3 20 人加密 WebSocket 實測（不可誤讀為畫面 FPS）

| 測試／版本 | Movement Round P95 | Nearby Chat Round P95 | Map AI Tick P95 | 其他 |
|---|---:|---:|---:|---|
| [#42816108025](https://github.com/kevin840307/TestGoGo/actions/runs/42816108025)，同步 SQLite 時 | **3006.8ms** | 8.0ms | 3.6ms | `PersistMs` 慢速事件 P95 6151.4ms，MAX 7151.4ms |
| [#42817804141](https://github.com/kevin840307/TestGoGo/actions/runs/42817804141)，位置批次化 | **10.1ms** | 13.4ms | 1022.8ms | 同輪 AI 仍被聊天/其它 Session 持鎖阻塞 |
| [#42818788968](https://github.com/kevin840307/TestGoGo/actions/runs/42818788968)，Chat 快速路徑 | **15.6ms** | **8.3ms** | **16.6ms** | Movement 1140/1140、Chat 1200/1200，全送達 |

**解釋**：測試列出的是 WireLoadProbe 的 `RoundLatencyMs` 和 `AiTickElapsedMs`，不是 Godot 看到的每幀動作延遲。最後一輪仍有少量 `ChatMessage` Handler 1–5 秒與 Session lock 例外峰值，**不可以因 P95 已改善就宣稱所有卡頓消失**；要再用 Debug log 確認最慢路徑並補視覺測試。

### 8.4 已執行的測試

- [Godot 4.7.2 #42815431514](https://github.com/kevin840307/TestGoGo/actions/runs/42815431514)：`REMOTE_MOTION_TEST`、`MONSTER_WAYPOINT_TEST`、`MOTION_AUDIT` **ALL PASS**。
- [Position persistence + 20-player wire #42818788968](https://github.com/kevin840307/TestGoGo/actions/runs/42818788968)：`POSITION_PERSIST_TEST ALL PASS`（批次、舊 map 防護、最新座標合併、最終 logout、換圖 fence），20 人 `Success=True`。
- **未驗證**：完整 Godot Scene/動畫/AOI/headless 測試，因 Checkout 不含原始 UI PNG（`BG_ENEMY_HP.png` 等）；CI 明確 `SKIP`。完整素材和 20 人實機畫面體驗仍須驗證。
- 原本 `Server build` 包含 Quest host strict audit，仍可能因 57 個舊腳本未綁定函式而失敗；此問題不等於多人移動修改失敗。

### 8.5 下一個可驗收的實機步驟

1. 用 20 bot + 真實 Client 進 Map13，Debug LOG 搜尋 `MULTIPLAYER_PACKET_AUDIT`、`MULTIPLAYER_MOTION_AUDIT`、`NETWORK_RUNTIME_BACKLOG`、`SLOW_PACKET_DETAIL`、`MAP_AI_PROFILE`。
2. 比對玩家和怪物的封包接收間隔、累積 backlog、移動 waypoint 距離以及實際視覺卡頓，錄 15–30 秒影片。
3. 只在觀察後判定要不要加入 100–150ms snapshot interpolation buffer / adaptive interpolation；不要憑空加固定延遲。
4. 特別檢驗：AFK/失焦/縮小、不降低 Physics/封包處理；攻擊距離與生命值同步、怪物追擊與返回 Map13、物品 AOI、不影響既有正常功能。


## 9. 第二輪修正與驗證（2026-11-10）

### 9.1 Server：保護同地圖傳送與辨認初始化卡頓

- `GmCommandService.Teleport` 在傳送前 `PositionPersistenceService.FenceAndDiscard`，即使 map_id 沒變，舊的待寫入座標也不會在新座標後寫回。
- 同時將 GM 傳送的角色完整資料載入改為 `GetCharacterLocation` 的窄查詢；保留角色不存在與無座標參數時的行為。
- `PacketRouter` 添加 `SLOW_GM_COMMAND`（只有指令類型、耗時、角色 ID，沒有輸入參數），與一般聊天 `SLOW_CHAT_PHASE` 分開。
- `PositionPersistenceProbe` 新增「**同地圖 GM warp 後，舊的 staged position 不得覆蓋座標**」案例。
- [20 人 wire #42820899336](https://github.com/kevin840307/TestGoGo/actions/runs/42820899336)：1140/1140 movement，移動 RoundLatency P95 **18.4ms**、聊天 **8.3ms**、AI Tick **4.7ms**、`SLOW_MOVEMENT_HANDLER=0`、`POSITION_BATCH_FAILED=0`、`SLOW_CHAT_PHASE=0`。
- 同一輪還發現慢速測試 GM 指令：`level` 19 筆，最大 9155.5ms；`map` 27 筆，最大 8153.1ms（此為超過門檻的節流診斷樣本，不代表全部 GM 請求）。它們是初始化/測試管理工作，**不應誤記成一般玩家 ChatMessage 很慢**。
- 這些數字是單次 CI 測試；各輪曾見 P95 15.6、464.6、950.9、18.4ms 等波動，仍須把 Server 尖峰與真實 Client 視覺卡頓關聯起來；不可只引用最好的一次。
- [Windows smoke #42820851730](https://github.com/kevin840307/TestGoGo/actions/runs/42820851730)：成功；含跨地圖密語 `T09772` 的回歸修復。原 `/name` 使用資料庫名稱查找的行為已恢復，僅一般聊天使用登入快取身分。

### 9.2 Client：避免缺包時倒退、避免過大單幀補幀

`remote_player_motion.gd` 新增兩個有界視覺策略：

1. 0.28 秒／40px 的預測上限到達後，**停在有限的視覺預測位置**，不會因收包延遲 1～2 秒就自行往舊座標退回。
2. 限制收到大位移快照後每次 physics 更新的視覺追趕：`MAX_VISUAL_CATCHUP_SPEED=900px/s`（60Hz 時最多 15px/tick）。這只限制**視覺**，不改 Server 座標、網路、碰撞、攻擊或傳送；大於傳送門檻的真正傳送仍立即重設位置。

`client/tests/remote_multiplayer_motion_regression.gd` 已新增 2 秒缺包、idle 停止、真正傳送及突發補包的測試。

`client/tests/multiplayer_20_actors_motion_regression.gd` 新增無資源的 20 名遠端玩家 + 60 隻怪物模擬：

- 玩家一半每 100ms 收點、一半每 350ms 收點，怪物 250ms waypoint，統一每 1/60 秒視覺步進，總模擬 12 秒。
- 驗收不倒退、單幀位移有界、連續 waypoint 中間怪物不停止，且不影響任何 authoritative position。
- [Godot 4.7.2 #42821642172](https://github.com/kevin840307/TestGoGo/actions/runs/42821642172)：`REMOTE_MOTION_TEST`、`MONSTER_WAYPOINT_TEST`、`MOTION_AUDIT`、`MULTI20_MOTION_TEST` 均 **ALL PASS**；模擬最大每 physics tick 位移 **13.74px**。這是演算法模擬，**不是 20 個完整 Godot 視窗的 FPS/畫面驗收**。

### 9.3 仍未完成或需要在完整 Client 上驗證

- Git checkout 缺 `client/ui/BG_ENEMY_HP.png`、`client/ui/cursor/C_NORMAL.spr.0000.png` 等遊戲圖片；CI 不能完成真實 SpriteFrames 的 20 玩家 + 60 怪物 GPU/繪製延遲測試，已明確 `SKIP`，而非假稱通過。
- 若實機仍感受到怪物「一起走停」，要確認是 `MAP_AI_TICK_GAP`、AOI spawn queue、`MULTIPLAYER_PACKET_AUDIT` 中 packet burst，還是動畫 frame 反覆重設，並比對 `MULTIPLAYER_MOTION_AUDIT`。
- 攻擊距離/死亡/拾取、返回 Map13 的 AOI 恢復與失焦/AFK 20Hz 渲染（但邏輯/收包維持正常）仍是實機驗收必檢項目。
- `Server build` 的舊 Quest strict host audit 尚有數十個 `UNBOUND`，不是本次移動分支的編譯錯誤；位置持久化/20 人 wire/Godot 純演算法/Windows Smoke 分別以專用 workflow 驗證成功。

### 9.4 可重跑指令

```powershell
# 完整本機 Client（需原始圖片材質）
godot --headless --path client --script res://tests/remote_multiplayer_motion_regression.gd
godot --headless --path client --script res://tests/monster_chase_regression.gd
godot --headless --path client --script res://tests/multiplayer_20_actors_motion_regression.gd

# 可獨立檢查座標持久化契約
dotnet run -c Release --project tests/SGO.Server.PositionPersistenceProbe/SGO.Server.PositionPersistenceProbe.csproj

# 20 人加密 WS 測試（本機執行中的 Server，測試帳號只用於測試）
dotnet run -c Release --project tests/SGO.Server.WireLoadProbe/SGO.Server.WireLoadProbe.csproj -- --url ws://127.0.0.1:5678/ --sizes 20 --rounds 3 --move-pixels 4 --mixed-seconds 8 --output wire20-result.json
```
