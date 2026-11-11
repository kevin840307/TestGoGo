# 原版移動協定與新版多人同步核對（2026-11-11）

目標：保留原始「Server 決定權威狀態、Client 依目的地及速度逐步呈現、位置校正具獨立因果」的原理；不恢復原版 800×600 的視野限制。此文件區分反編譯事實、相容性設計及尚未驗證的假設。

## 一、原始反編譯中已確認

來源：`Old/服務端/Core.dll.lst`、`Old/ClientData/Paradise.exe.lst`、`Old/服務端/plugins/SGOMapService.dll.lst`，定位細節見 [multiplayer-legacy-movement-audit.md](multiplayer-legacy-movement-audit.md)。

| 原版項目 | 確認內容 | 新版對應 |
|---|---|---|
| Core.dll `CDServer::InitThread`，0x0101630C、0x01016335、0x0101635E、0x01016383 | `FPS_BASE` 預設25，`FPS_CURRENT` 預設10，`FPS_CLIENT` 預設50；`1000 / FPS_CURRENT` → 預設100ms Server Update Tick。INI 可覆寫 | 玩家 `SyncPosition` 廣播節流基準100ms；怪物共享 AI 250ms，**這部分尚未完全與原版相同** |
| `CDServer::run`，0x01024B43～0x01024B80 | 超過 deadline 執行 Update，空閒 `Sleep(1)` | 正式 Server 使用 `PeriodicTimer` 防止處理時間疊加為固定延遲 |
| `Paradise.exe.lst`，`CSGOMap::MessageMap` 0x004CD52C | A106 玩家目的地、A107 其他角色目的地、A108 怪物移動、A10D 其他角色位置校正 | 新版 `MoveEntity` 帶目的地和速度；停止／位置校正用 `SpeedTime=0` |
| `CSGOChar::MoveStep`、`CSGOEnem::movelogic` | 接收目的地後，Client 依當前速度在本地連續步進 | `RemotePlayerMotion.advance`、`MonsterEntity.advance_network_runtime` 每 physics tick 步進，不隨渲染 FPS、AFK 或失焦停用 |
| `SGOMapService.dll.lst` `SendSyncDestPac`、`SendSyncPosition` | 目的地與權威座標校正由不同產包路徑發出 | **新版封包型別仍合一**，但透過 `SpeedTime=0` 明確區分權威校正，並保護 Queue FIFO 邊界 |
| 原版地圖像素與視窗 | 原版曾用 800×600 畫面 | 新版讀取實際 Canvas2D 四角、鏡頭/縮放和角色位置來估計 AOI，**不固定視窗大小** |

重要：上述 10Hz 是原版預設 Server **Update Tick**，不是原版每種類型封包必定每100ms發送的證據。既有 .lst 沒有真實封包收發 timestamp；不能聲稱原服怪物 A108 一定100ms/包。

## 二、本輪真正修復的原版相容性問題

### A. 玩家停止／位置校正必須是不可合併的事件

先前 `ServerBroadcastHub` 玩家高速通道對每個同一 EntityID 的所有 `MoveEntity` 合併。碰到「走路 → 停止 `SpeedTime=0` → 再走路」在同一回 WS 排隊時，停止座標可能被後一段移動覆蓋，違反原版獨立位置校正概念。

現改為有明確 `SpeedTime<=0.01` 的 player correction 是 FIFO boundary；前後普通移動各自可合併，攻擊、死亡、Spawn/Despawn 等邊界維持舊規則。未傳 `SpeedTime` 的舊相容/測試包仍可 coalesce。加入 `SGO.Server.BroadcastPriorityProbe` 三步停止→恢復測試，驗證原始物件和順序。

### B. Client 收到零時長位置校正不可再推導移動速度

原本 `RemotePlayerMotion.receive` 在 `SpeedTime=0` 時按0.05秒分母估算新 velocity，可能在真正 `SyncState(idle)` 從 WS drain 前產生假性650px/s追點。現在直接更新只讀/遊戲權威 target，將預測速度清零；視覺位置依既有 bounded correction 平滑靠近，不一影格跳躍。加入 `remote_multiplayer_motion_regression.gd` 校正測試。

### C. 4K／超寬／Zoomed-out 可見外圍範圍

既有 `ViewportAoiRadius.estimate` 已按鏡頭 canvas 四角＋headroom 算世界半徑，但 Client、Server player AOI、server world AOI 都在5000硬截斷：例如 3840×2160 畫面 zoom=0.4 的世界角落距離人物可超過5500單位，實際可見的角色和怪物卻不被 Server 訂閱，等 AOI 改變才出現，容易誤判為瞬移。

三處上限一致改為10000；**預設1700～2600級的普通使用情境不會自動擴大**，只有實際 canvas 要求超過時才多訂閱。新增 4K 0.4縮放、6200世界單位遠端玩家的雙向Spawn/Despawn、遠處怪物Spawn/Despawn 及恢復正常視野測試。上限10000是防止極端視窗要求無界全圖廣播，超過此上限仍可能裁切。

### D. 原版50Hz步進與新版60Hz呈現等效性

增加 `remote_multiplayer_motion_regression.gd` 的 deterministic 10Hz Server snapshot，分別在50Hz和60Hz呼叫可視步進的比較，檢查無倒退、無單幀突跳、最後顯示位置誤差有界。這是算法契約測試，不是對真實原版遊戲 FPS 的逐畫面錄影比對。

## 三、當前量測與尚有差異

- [Map13 20 Bot +312怪物基準](https://github.com/kevin840307/TestGoGo/actions/runs/43020820425)：AI Tick P50=43.34ms，P95=145.05ms，Max=224.32ms；188筆有效樣本，250ms以上0筆。這是本輪改動前的 CI 量測，作為不降低性能的比較基準。
- [20人 WebSocket 加密壓測](https://github.com/kevin840307/TestGoGo/actions/runs/43020820228)：Movement 1140/1140，P95=409.8ms，NearbyChat 1200/1200，P95=7.2ms。測試場景中仍有GM配置/SQLite尖峰，不能宣稱每個玩家移動更新都會在100ms內到達。
- 原版 Server預設10Hz與新版怪物AI每250ms仍有差距。新版靠「有意義的目的地／速度＋Client持續Physics Tick」而非調高312怪的整輪昂貴AI頻率；如果改變 Server tick，必須先測戰鬥、Blocking、死亡只一次、AOI以及 SQLite負載，不能單純改常數。
- `CombatService.TryWander` 的 Server權威位置會在巡邏起點一次變成目的地，Client依 `WanderVisualSeconds` 走完全段；真正的 Server runtime 路徑逐步更新仍與原版概念有差距，可能在巡邏中進入戰鬥時產生位置校正。這一點不能謊稱已完全還原。
- Godot headless 無需全圖資源的測試只能證明數學、封包語義及 AOI 對稱正確；使用者實際 Map13 的不同解析度、移動中的外圍怪物與 20個真 Godot client 畫面仍需要實機確認。

## 四、驗收方式

GitHub Actions `Godot multiplayer movement regressions` 驗證 10Hz/50Hz/60Hz、遠端移動與校正、viewport AOI、多人模擬；`Server AOI priority and build` 驗證.NET8編譯、player stop FIFO、world/player AOI；`Server smoke Windows` 確認 Server 功能；`Map13 20-bot real AI tick profile` 檢查實際20人＋312怪的更新尾延遲。

實機需要同時比對 `[MULTIPLAYER_PACKET_AUDIT]`、`[MONSTER_AUTHORITATIVE_SNAP]`、`[REMOTE_PLAYER_PACKET_GAP]`、Server `[MAP_AI_PROFILE]`、`[SLOW_PACKET_DETAIL]`，特別是鏡頭邊緣、Zoom/resize、停止再移動。網路資料、AI、Fixed Tick 及AFK完整功能不可為了降FPS而降頻。


## 五、2026-11-11 21:41 使用者現場 99001／21 Session log 與本輪處理

### 現場異常（真實負載、並非 CI 合成）

Map13 21 Session／312 Monsters：

- `MAP_AI_PROFILE` 第一、第二次 `TickAiMs=248.9/270.4ms`；另一筆 `PlayerSessionLockWaitMs=249.2ms, TickAiMs=43.8ms, TotalMs=293.6ms`；另一次 `PlayerSessionLockWaitMs=110.1ms, TickAiMs=216.7ms`。
- `[USE_SKILL_PROFILE] SkillID=99001 CombatMs=252.0ms` 與另一次 `CombatMs=200.5ms`，其中 `EnsureMapMs=0.0`；同一技能的 `SLOW_PACKET_DETAIL HandlerMs=252.3ms, SessionLockWaitMs=0`。
- 這些數據**證明技能 99001 結算占用玩家的 Session lock**，而共享 Map AI 等待同一把鎖時會短暫停更；並非只有 Client 插值、解析度或 WebSocket 解密問題。

### 本輪小範圍修正與保護

1. `CombatService.AddAreaKillRewards` 將同次 AOE 結算的 `GetTimedEffectValue(exp_multiplier_percent)` 從「每一隻擊殺查一次」改成「每次技能結算查一次」，仍以各怪物原始等級、原始倍率及 EXP 算式各自計算。排除技能結算期間個別查詢造成的 N 倍重複開銷。
2. `AddAreaBatchExtras` 只載入一次結算後的角色、完整資產與 Lucky，供各隻怪物獨立掉落 `RollDrops` 使用；每隻怪物仍分別遞增 `KillSequence`、抽獎、Blocking 落點判定、存檔、Quest/Mecha/Pet Exp，**不合併掉落機率，也不減少傷害或死亡事件數量**。普通單怪擊殺維持舊查詢契約。
3. `[SLOW_AREA_SKILL_PHASE]`（單次超過 100ms、全服務5秒節流）記錄 `TotalMs, SelectMs, DamageMs, RewardsMs, ExtrasMs, OtherMs, Targets, Kills, SkillID, Map, Character`。後續可直接判斷 99001 是否仍卡在獎勵、掉落與任務，避免僅憑總時間胡亂修改 Server Tick。
4. `characterSnapshot` 缺失時的 Lucky 使用 nullable fallback 以保留普通重新載入行為。新的快照只活於同一次同步 AOE，不緩存跨角色／跨 Tick 資料，也不改技能冷卻、權威怪物座標、戰鬥結果與網路送包次數。

本輪 Commit：[`9addd7f`](https://github.com/kevin840307/TestGoGo/commit/9addd7f75eb8f4e57ffbd8a16bd85c464782fd34)（查詢快照）、[`ba459a2`](https://github.com/kevin840307/TestGoGo/commit/ba459a2faae723459eb2935cb4060d8bd1cacb44)（分段診斷）。

### GitHub 自動化驗證

- [`43033102695` Server AOI priority and build](https://github.com/kevin840307/TestGoGo/actions/runs/43033102695)：PASS；驗證 .NET 8 編譯與位置校正／AOI 送包契約。
- [`43033102690` Server smoke Windows](https://github.com/kevin840307/TestGoGo/actions/runs/43033102690)：PASS；含既有 AOE、物品與怪物整合冒煙測試。
- [`43033102697` Map13 20 Bot/312 怪](https://github.com/kevin840307/TestGoGo/actions/runs/43033102697)：PASS；216 筆總樣本、196 筆有效，`AI Tick P50=29.4ms, P95=54.0ms, Max=224.8ms, over250=0`。另曾在**非有效區間／暖機**見到 `SLOW_TICK_AI_PHASE` 約 1520ms，其中 `DamageWriteMs` 約 1515ms；不能宣稱極端延遲已根除。
- 普通 [Server build](https://github.com/kevin840307/TestGoGo/actions/runs/43033102705) workflow 的 **Packet contract audit FAIL** 仍由舊版 Quest Lua host 的未綁定函式引起，不是這次 .NET 編譯失敗。這個 workflow 的結果不能標記成 PASS。
- 既有 [Godot movement regressions](https://github.com/kevin840307/TestGoGo/actions) 覆蓋 10Hz Server／50Hz 原版 Client／60Hz 新版 physics 步進、位置校正與動態視野；本輪只改 Server，不需改 Client FPS。缺少原圖資的實際場景畫面測試仍為 SKIP。

### 原版相似性：已確認與仍未一致之處

**確定的行為相似性**：原版目的地訊息和位置校正分離（A106/A107/A108/A10D）；新版透過 `SpeedTime>0` 與 `SpeedTime=0` 的 FIFO barrier 分離處理。原版步進採本地連續更新；新版 Godot remote/monster 使用不依賴渲染 FPS 的固定 physics 更新。原版預設 Server Update 10Hz、Client 50Hz，新版保留玩家位置100ms廣播基準，並已用50/60Hz回歸比對。新版 Dynamic Canvas AOI 以實際視窗與攝影機視野計算，**完全不恢復800×600限制**。

**尚不能宣稱與原版完全相同**：原版每種封包的真正發送間隔仍缺含 timestamp 的封包擷取；新版怪物共用 AI 250ms 與原版預設 Server 100ms Update Tick 仍不同。且新版怪物巡邏會先更新權威座標到遠端目的地、由 Client 自行走完 95～175px 路段；尚未完整恢復舊版 Server `CDNPCMove` 持續移動的權威時間線。調整此處需額外測試 Blocking/AOI/掉落/攻擊距離/兩人同步與死亡只結算一次；不能只把 Tick 改成100ms或用插值掩蓋根因。

**實機驗收尚缺**：同一台機器在 Map13 20 Bot＋真 Client 重測星界震盪 99001，收集 `[SLOW_AREA_SKILL_PHASE]`、`[MAP_AI_PROFILE]`、`[MULTIPLAYER_PACKET_AUDIT]`、`[MONSTER_AUTHORITATIVE_SNAP]`，並觀察 zoom／超寬視窗邊緣、玩家移動→停止→繼續、怪物追擊轉攻擊。只有自動化 PASS 不等於已親眼確認整個畫面沒有順移。
