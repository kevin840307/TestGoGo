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
