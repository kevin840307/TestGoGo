# Server AOI 與 Client 資源優化（2026-11-08）

> 分支：`feature/local-server-net8`
>
> 目標：不改變玩家可感知的正常遊戲行為，讓 Server 不再把遠距離、玩家根本看不到也不需要知道的動態封包送給整張地圖。AOI 只裁切「可由距離安全決定」的世界事件；資產、交易、Party/Club、全服訊息等仍遵守原業務規則。

## 1. Player AOI

`Application/Messaging/PlayerSpatialAoi.cs`

- Spatial Grid cell：1024 遊戲座標。
- 進入半徑：1800。
- 離開半徑：2200。
- 使用不同 enter/exit 半徑避免玩家在邊界來回 Spawn/Despawn。
- 只有完成 `WorldReady` 的角色才加入 AOI；選角完成但 Client 地圖尚未 ready 時不會提前暴露給其他玩家。
- 玩家 A 與 B 距離很遠：
  - A 不接收 B 每一步 `MoveEntity`。
  - B 進入 A AOI 時，A 收到完整 `SpawnEntity`。
  - B 離開 A AOI 時，A 收到 `DespawnEntity`。
  - 只有 AOI 內才持續收到移動、附近聊天、人物狀態、外觀與玩家來源戰鬥視覺。
- 切圖、瞬移、角色切換、Logout、transport disconnect 都會移除舊 known-set；新圖 `WorldReady` 再重建。
- 已走 Player AOI 的高頻/可見性事件包括：
  - 玩家 Spawn / Despawn / Move。
  - nearby chat。
  - 玩家 `SkillCast` / 玩家來源攻擊視覺。
  - `SyncStatus`。
  - 外觀/稱號更新。
  - Party name color。
  - PlayerShop 招牌。
  - Party-return 視覺。
  - 寵物/機甲等 owner companion 的 Spawn/Move/IdleSpeech/攻擊 `SyncState`。

## 2. Monster / Ground World AOI

`Application/Messaging/WorldEntitySpatialAoi.cs`

Player AOI 之外另有一個動態世界 entity spatial index，避免把 1000 人問題從 bandwidth 變成「每個玩家移動都掃全圖怪物」的 CPU 問題。

### Spatial 結構

- Observer grid：玩家位置。
- Entity grid：怪物與地面物。
- Cell：1024。
- World entity enter radius：2600。
- World entity exit radius：3000。
- 每個 observer 有 visible entity known-set。
- 每個 entity 反向保存 observers。
- 玩家移動只檢查附近 entity cells。
- 怪物移動只檢查附近 observer cells。
- 不需要 O(players × monsters) 全掃描。

### 怪物

AOI 已套到：

- Monster `SpawnEntity`。
- `MoveEntity`。
- `SyncState`。
- `MonsterTarget`。
- 怪物相關 `EntityDamage` / `EntityHeal`。
- 怪物 `SkillCast`。
- `EntityDeath`。
- Respawn。

重要順序不變量：

1. 玩家第一次需要知道某怪物時，一定先 Spawn。
2. Spawn 之後才可收到 Move/State/Damage 等增量封包。
3. 離開 AOI 一定 Despawn。
4. Death 先送給已知 observers，再從 AOI known-set 移除。
5. Respawn 會重新以新的 Spawn 進入 AOI。

SharedMapAI 的 driver socket 也套相同 visibility check，不會因為某個連線剛好是該 map 的 AI driver，就收到整張地圖所有遠方怪物 Move/State。

### 動態怪物 EntityID 安全

World AOI 使用 EntityID 作 process-wide key，因此動態怪物 ID 也必須跨地圖唯一。

- 一般地圖怪物：`1_000_000 + mapId * 10_000 + index`。
- supplemental runtime 怪：`8_000_000 + mapId * 10_000 + index`。
- GM / dynamic spawn：已改成 `9_000_000 + mapId * 10_000 + sequence`。

因此不同地圖的 dynamic spawn 不會因相同 9M EntityID 在 AOI index 互相覆蓋，避免「突然少怪」。

### 地面物品

以下來源都走同一套 persisted ground AOI：

- 玩家 `DropItemRequest`。
- 怪物死亡掉落。
- WorldReady / map-return DB snapshot。
- `PickUpItemRequest`。
- expire cleanup。

行為：

- SQLite `ground_items` 是 authoritative truth。
- 遠處玩家完全不需要收到該地面物 Spawn。
- 玩家靠近時補 `SpawnEntity`。
- 玩家離開時可 Despawn Client 視覺，但不刪 DB row。
- Pickup/Expire 只通知曾知道該 entity 的 observers。
- late entrant 的 `RemainingTime` / `ProtectTimeSeconds` 由最新時間重新計算，不會因 AOI 再 Spawn 而重置倒數。
- `BuildGroundItemSnapshotPackets` 會用最新 DB rows 對 AOI group 做 authoritative reconciliation；若地圖沒人時 DB 物品已過期，之後玩家回圖不會被舊 AOI cache 復活成「幽靈物品」。

這和 Client `network_entity_manager.gd` 的 visual AOI 是兩層：

- Server AOI：不傳不需要的遠方封包，降低網路/queue/serialize 負擔。
- Client visual AOI：已知但暫時不需畫面的地面物不建立 Sprite/Label/Collision，降低 CPU/RAM/render 負擔。

## 3. PlayerShop AOI

PlayerShop 現在不是「同地圖就可以遠端買」。

`ViewShop` / `BuyItem` 必須：

- Seller 在線。
- 同地圖。
- Buyer 的 Player AOI 已看得到 Seller。

所以手工送封包也不能從地圖另一端查看/購買沒出現在 Client 世界中的玩家商店。

商店 Open/Close/SoldOut/Disconnect 的 `SyncShopBoard` 也只送給 Seller 的 Player AOI observers。

資產交易本身仍由 SQLite transaction 保證 money/item consistency；AOI 只是 visibility/authorization gate，不替代 DB 原子性。

## 4. 刻意不套距離 AOI 的封包

以下保留原規則，這是刻意設計，不是漏做：

- 玩家自己的 request/response。
- Party 定向訊息。
- Trade 定向訊息。
- Club 定向/群組訊息。
- 全服公告、world/global channel。
- 資產/DB transaction。
- NPC 靜態 map snapshot。

NPC 是低頻、進圖一次性的靜態內容，而且可能供 Quest/minimap/portal 等 Client 邏輯使用。把它改成距離 AOI 能省的 bandwidth 遠小於怪物移動/玩家同步，卻有較高 UX 相容風險，所以目前保留完整 snapshot。

## 5. 「不突然少怪／少物品」安全規則

AOI 不是刪除資料，只決定某 connection 此刻是否需要收到世界 entity。

必須一直成立：

- DB ground row 不因離開 AOI 被刪除。
- WorldReady 會重新用 authoritative DB/runtime state reconcile。
- AOI enter 必須補 Spawn。
- AOI exit 必須補 Despawn。
- 增量封包不能早於 Spawn。
- Death/Pickup/Expire 後 known-set 必須清掉。
- 切圖/斷線後舊 observer relation 必須清掉。
- entity ID 不得跨 map collision。
- stale ground AOI cache 必須由 DB snapshot 清理。
- PlayerShop 遠距離拒絕不得改 money/item。

## 6. 測試

### Server modular smoke

- `World/PlayerSpatialAoiSmokeTests.cs`
  - far suppression。
  - enter/exit hysteresis。
  - 雙向 player Spawn/Despawn。
  - map/disconnect。
  - 1000 sparse players。
  - concurrent movement。

- `World/WorldEntitySpatialAoiSmokeTests.cs`
  - monster/ground far suppression。
  - observer movement與 entity movement。
  - enter/exit hysteresis。
  - removal known-set。
  - late observer 使用最新 spawn factory。
  - authoritative ground reconciliation。
  - 1000 entities。
  - 1000 observers。
  - concurrent monster movement。

- `Integration/PlayerShopAoiSmokeTests.cs`
  - far View/Buy 拒絕。
  - 拒絕不能改 money/item。
  - 靠近後可 View/Buy。
  - 離開後再次拒絕。

- `test_smoke_suite_structure.py`
  - AOI 模組與 wiring 不可被移除。
  - dynamic 9M monster ID 必須含 mapId。
  - PlayerShop 不可退回 `PublishToMap`。
  - client AOI regressions 必須在 normal runner。

### Client regressions

`tests/run_protocol_checks.ps1` 有 Godot 時會執行：

- `ground_item_aoi_regression.gd`
- `multiclient_render_dedup_regression.gd`
- 原有 network/perf regressions。

### 真 AES/WebSocket 10/50/100

`SGO.Server.WireLoadProbe` 新增 `WorldAoi` phase：

1. 真實 clients 分近/遠兩群。
2. 在 near group 旁 spawn monster。
3. near group 應收到 Spawn。
4. far group 必須 0 Spawn leak。
5. 把一位 far client 移回近區。
6. 該 client 必須補收到同一怪物 Spawn。
7. 再恢復成 compact group，繼續 MixedSoak。

原本 movement/chat 的 expected delivery 也依 PlayerSpatialAoi 實際距離計算，不再假設同地圖 N² fanout。

## 7. Health / 壓測診斷

`/health` 新增：

- `aoi.playerReady`
- `aoi.worldObservers`
- `aoi.worldEntities`

MixedSoak 會和：

- working set
- GC heap
- CPU total
- SharedMapAI tick P50/P95/P99/max
- active sessions/maps/monsters

一起採樣。

這讓 10/50/100 人測試可以直接核對「socket 都在線，但動態 entity delivery 已按附近玩家裁切」。

## 8. 驗收指令

```powershell
dotnet run --project tests/SGO.Server.SmokeTests -- --suite world
dotnet run --project tests/SGO.Server.SmokeTests -- --suite concurrency
dotnet run --project tests/SGO.Server.SmokeTests -- --suite all
powershell -ExecutionPolicy Bypass -File tests/run_protocol_checks.ps1
```

長時間多人驗收可把 WireLoadProbe 的 `--mixed-seconds` 提高到 600、3600 或更久。

## 9. 目前驗證狀態

程式、測試與壓測 harness 已提交，但此工作環境沒有 .NET SDK / Godot，GitHub Actions 目前也持續在取得 Runner 前結束（job `steps=null`）。

所以目前狀態仍是 **IMPLEMENTED_UNVERIFIED**，不能宣稱 10/50/100 真實負載或 Godot regression 已 PASS。

1000 人目前是 spatial-index 資料結構測試，不等於已完成 1000 個真 WebSocket client 的容量驗收。
