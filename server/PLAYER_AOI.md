# Server 玩家 AOI 與 Client 資源優化（2026-11-08）

## 已完成：Server 玩家 AOI

- `Application/Messaging/PlayerSpatialAoi.cs`：以地圖＋1024 遊戲座標單位網格定位玩家；新目標進入半徑 **1800**，既有目標離開半徑 **2200** 才 Despawn。
- 每個已完成 `WorldReady` 的連線維護 `Visible` 玩家集合。其他玩家不會在登入選角、客戶端地圖仍在載入時提前收到 Spawn。
- 角色靠近：兩側各得到對方完整 `SpawnEntity`；包含 Server 最近的即時位置而非 DB 最後儲存的舊座標。超過出口半徑：Server 各送 `DespawnEntity`。同位置無變化的 250ms Session Tick 為 O(1)。
- 切圖、瞬移、角色切換、斷線：移除舊可見關係；新圖 `WorldReady` 再重建。AOI 操作以 `lock` 保護可見集合和索引；封包入隊前再次比對實際可見狀態及角色身分。
- `ServerBroadcastHub.PublishToPlayerObservers` 依已知觀察者傳送，取代玩家位置、動作、外觀、部分人物狀態、特效、附近聊天、玩家 SkillCast 與玩家來源攻擊效果的同圖全體廣播。
- **不按 AOI 過濾**：玩家自身請求的回應、全服／隊伍／公會訊息、定向交易與資料庫交易。這些仍由原本業務規則管理。
- 怪物戰鬥、世界掉落物和其 Spawn／Despawn／同步目前仍保留 Server 同圖事件模型。**這次沒有把它們錯誤地套用玩家 AOI 篩選**，避免地圖換回後怪物、掉落物消失；要讓怪物／掉落物也由 Server 做 AOI，必須先完成每個觀察者對這些物件的已知集合、入場快照、移動接近補 Spawn、離開補 Despawn 和競態測試。此項仍待完成。

## 已完成：Client 地面物品視覺 AOI

`client/scripts/network_entity_manager.gd` 把 Server 的地面物品資料保存在字典，只有接近目前相機畫面才建立 Sprite、Label、碰撞區域；離開視覺 AOI 再回收節點，但**不刪掉 Server 已知的掉落物記錄**。再次靠近時使用最後的快照重建，並扣除原先經過的保護時間與存在時間，避免重置倒數。拾取進行中的物品不得卸載。重複的 `WorldReady` 快照不應讓遠處物品重複產生節點。現有 `DespawnEntity` 會連同快取一起清理。

進入／離開預設為 2000／2400；依 Camera2D 的可見尺寸及縮放自動加大，避免高解析度視窗邊緣物品被裁掉。每 250ms 檢查一次；Queue 採穩定的二分插入，減少大量回圖封包時的重複排序比較。

Debug `[RENDER_STATS]`：
- `ground_items` = 已建立並可渲染的物品實體數。
- `ground_known` = Client 持有的 Server 地面物品快照數。
- `ground_deferred` = 尚未建立視覺節點的遠處地面物品數。
- `minimap_world_redraws` / `minimap_world_skips` = 小地圖靜態底圖更新／跳過次數。

## 驗收

```powershell
dotnet run --project tests/SGO.Server.SmokeTests -- --suite world
dotnet run --project tests/SGO.Server.SmokeTests -- --suite all
dotnet run --project tests/SGO.Server.WireLoadProbe -- --help
godot --headless --path client --script res://tests/ground_item_aoi_regression.gd
godot --headless --path client --script res://tests/multiclient_render_dedup_regression.gd
```

`PlayerSpatialAoiSmokeTests` 測試遠方無邊、進出閾值、雙向 Spawn／Despawn、重複移動不重送、負座標、瞬移、切圖、離線、1000 人稀疏索引與並行移動，以及 Hub 僅把近距離移動封包送入指定 Queue。WireLoadProbe 的同圖測試已改依各玩家真正的 AOI 距離計算預期的 MoveEntity／附近聊天投遞次數，而不是假設每人全圖收到。

實際上線前還需做 **2／10／50／100 位真 WebSocket Player** 的 AOI 進出範圍測試、Server CPU／記憶體／封包速率量測、持續混合打怪和交易測試，並檢查 1 前景＋9 背景 Client 的拾取／視窗切換。1000 人是 Spatial Grid 的資料結構測試，**不是已測過 1000 人真實連線穩定性**。

此執行環境沒有 .NET SDK 與 Godot 4.7.2；目前只能完成原始碼靜態核對，不能宣稱回歸程式或 FPS／GPU 前後測試已實際 PASS。
