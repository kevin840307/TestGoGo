# 多人外圍人物／怪物順移：相機可視範圍 AOI 修正

目標分支：`feature/local-server-net8`。本篇屬於 2026-11-10 的新增修正與驗收規格。

## 原版可確認的機制

`Old/ClientData/Paradise.exe.lst` 裡 `CSGOMap::MessageMap` 分派 `0xA106`（玩家目的地）、`0xA107`（角色目的地）、`0xA108`（怪物移動）、`0xA10D`（位置校正）。`CSGOChar::MoveStep`、`CSGOEnem::movelogic` 逐 frame 更新本地位置，並不依賴每包直接指定可見座標。原版設定 50 FPS 不是原版網路送包頻率證據。精確送包頻率仍需原版封包 trace／完整 server 排程證據；詳見 `docs/multiplayer-legacy-movement-audit.md`。

## 原因：Client 可視區大於 Server 固定 AOI

- 新 `client/project.godot` 虛擬解析度是 1400×900（`canvas_items`），使用者可以自行放大視窗與 zoom-out；不能硬套 800×600。
- Client `network_entity_manager.gd::_monster_aoi_radii` 原已根據視窗及 camera zoom 擴大可視怪物範圍；但 Server `PlayerSpatialAoi` 固定 1800/2200，`WorldEntitySpatialAoi` 固定 2600/3000。
- Server 可能已停止傳送 Client 仍看得見的外圍怪物或角色，導致重新 Spawn 時突然跳到正確位置。**這是具程式證據的 AOI 範圍落差，不代表所有卡頓只出於此；Server/Client 仍可能有 queue/backpressure/實體初始化尖峰**。
- 現有玩家 `SyncPosition` 廣播節流約 100ms；怪物 AI 共用 Tick 250ms。此修正不改兩者，不犧牲戰鬥、固定 tick、FPS／AFK／背景封包處理。

## 協定與資料流

```
Godot Camera2D -> viewport canvas transform affine_inverse
  -> 四個實際可見角落與角色的最遠世界距離 + 420px margin
  -> 128px 量化，上下限 1800/5000px
  -> WorldReady { MapID, ViewRadius }
  -> Server 在 WorldReady 首次註冊 AOI 前設置 observer 半徑
視窗尺寸／Zoom／Camera 位置偏移使需求半徑改變時：
  -> 每秒做一次低成本檢查（未改則不發送）
  -> ViewportAoi { Radius }
  -> Server 重算該玩家與世界物件 AOI（無位置／DB／戰鬥更新）
  -> 需要的新物件 Spawn；不再需要的物件 Despawn
```

- Player AOI 使用兩方需求半徑的較大值維護原本的**雙向** Spawn/Despawn；舊 Client 都是 1800/2200，既有 400px 遲滯。
- Monster/ground AOI 依個別觀察者半徑 `max(2600, ViewRadius)` 計算，仍保留 400px 遲滯；未送新欄位的舊 Client 保持 2600/3000。
- 最大 5000px 由 Server 再次 clamp，避免惡意或極端視窗無限擴大傳輸；未改變優先玩家封包 lane、合併策略、Gameplay 權威。
- 額外開銷只在需要較大視窗的 observer；除了視角變動，靜止人物不重新發 AOI 封包。

## 回歸與驗收

1. `.github/workflows/client-multiplayer-motion.yml` 新增隔離的 `viewport_aoi_radius_regression.gd`：1400×900、4K、縮放、偏心視角、極端視角封頂。
2. `PlayerSpatialAoiSmokeTests.cs`：人不移動，放大／縮小 viewport 時雙方 Spawn／Despawn 完整、沒有重複。
3. `WorldEntitySpatialAoiSmokeTests.cs`：怪物在擴大 AOI 的邊界持續移動，正確進出及遲滯；縮小視窗撤銷外圍流量。
4. .NET 8 build / server AOI probe / 20-player encrypted WS probe；確認 `Movement delivery` 不變、`P95/P99` 與修改前對照。
5. 實機在 Map13 用兩 Client＋20 Bots，針對 1400×900、2560×1440、3840×2160及縮放，測試遠處角色／怪物接近、邊界跨越、攻擊、死亡、回地圖；記錄 `[MULTIPLAYER_PACKET_AUDIT]`、`[MULTIPLAYER_MOTION_AUDIT]`、`[NETWORK_RUNTIME_BACKLOG]`、`[SLOW_MOVEMENT_HANDLER]`。
6. 若離開 camera 世界距離已超過 5000px，上述方案不保證完整 AOI；需再做與 camera 方位相關的方形/扇形 AOI。

以上測試若 GitHub Actions 未執行或 CI SKIP 完整圖資場景，**不得記錄為 PASS 或假稱實機流暢**。
