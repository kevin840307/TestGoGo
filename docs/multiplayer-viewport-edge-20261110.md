# 多人視野外圍順移：以實際 Canvas2D 視野判斷（2026-11-10）

## 原版協定核對

現有 [multiplayer-legacy-movement-audit.md](./multiplayer-legacy-movement-audit.md) 已依
`Old/ClientData/Paradise.exe.lst`、`Old/服務端/plugins/SGOMapService.dll.lst` 確認：

- `CSGOMap::MessageMap` 處理 `0xA106` PlayerSyncDest、`0xA107` CharSyncDest、`0xA108` EnemyMove、`0xA10D` CharSyncPosition。
- `CSGOChar::MoveStep` / `CSGOEnem::movelogic` 在 Client 本地連續步進；目的地更新與位置校正不是同一件事。
- `SGOMapService::SendSyncDestPac` 與 `SendSyncPosition` 用不同命令；以 Server FPS 推導「每幀送封包」是不成立的。
- 原版實際每 100ms/250ms/其他間隔送包，沒有帶時間戳的原版網路擷取仍無法確定。

新版 WebSocket/JSON/AES 的 `MoveEntity` 是世界座標快照，
`SyncState` 是動作狀態；不應把舊版二進位命令 ID 原樣套用。

## 問題與修正

重點是使用者已不再使用 800×600，外圍最容易發生順移。

此前 Client 怪物視覺 AOI 半徑以 `viewport.size.length()/2 / zoom`
加上 `camera.global_position` 與玩家的距離估算，沒有直接測量
Camera2D/CSS-style viewport stretch、camera offset、rotation 和非等比變換
後的「實際四角世界座標」；當範圍不足時，怪物可能被卸載，
稍後用最新 Server 快照重建，看起來像瞬移。

本 PR：

1. `MonsterWaypointSpeed.required_view_radius` 使用 viewport 矩形四角的
   `canvas_transform.affine_inverse()` 世界座標計算最大可見距離，
   保留原 360 世界單位緩衝；異常矩形或零行列式回傳 0 給既有估算法。
2. `NetworkEntityManager._monster_aoi_radii` 改用實際四角推算，
   AOI 仍只影響視覺場景建立/回收，不改 Server 封包或固定 Physics。
3. 鏡頭平移時依 128 世界單位格觸發視覺 AOI 重新評估，
   不需等玩家移動或 1 秒 stationary sweep。
4. 即使距離公式判定離開 AOI，實體只要仍在實際 viewport+64px 緩衝內，
   就不釋放 Scene，避免下一刻重建造成跳躍。
5. `monster_waypoint_math_regression.gd` 加上 1920×1080、3440×1440、
   Zoom 0.5、camera offset、無效 viewport regression。

## 仍須搭配 Server 驗收

- `PlayerSpatialAoi` 現有 Enter/Exit 1800/2200、`WorldEntitySpatialAoi`
  2600/3000 都是**以玩家為中心的固定世界半徑**。當 Camera 顯示世界
  距離超過 Server Enter 半徑，即使 Client 視覺 AOI 正確，仍可能沒有
  Spawn/Move 包；這個修正**沒有擅自擴大 Server 廣播半徑**，避免無測試
  便增加多人封包量。要從實測的 viewport/zoom、server AOI enter/exit
  事件定量決定後續如何調整 Server subscription。
- 協作者的 PR #4 獨立處理 Server 未送出的怪物 waypoint 壅塞合併；
  請勿重複修改 ServerBroadcastHub。
- `client/tests/monster_waypoint_math_regression.gd` 使用 CI Godot 無資產環境；
  完整 Map13 20 bots + Godot 實景需圖資，本 PR 的單元測試不能代替實機 FPS 驗收。

## 實機驗收

- 以 1920×1080 與目前更大的解析度、相同 Zoom 測 Map13 20 bots，
  錄製 20 秒人物附近與畫面四角移動。
- Debug 搜 `MULTIPLAYER_PACKET_AUDIT`、`MULTIPLAYER_MOTION_AUDIT`、
  `MONSTER_AUTHORITATIVE_SNAP`、`BROADCAST_BACKLOG`、`MAP_AI_PROFILE`。
- 比對 Server AOI 出入時的 Spawn/Despawn、SyncState 停止、攻擊事件，
  且切圖、失焦、掛機、掉落物與 30/60/自訂 FPS 不得退化。

## Server viewport subscription 補齊（PR #6）

PR #5 已修正 Client 自己的實際 Canvas2D 視野與 monster Scene 回收，但 Server 原先仍固定使用：

- 玩家 PlayerSpatialAoi：Enter/Exit = 1800/2200。
- monster / ground WorldEntitySpatialAoi：Enter/Exit = 2600/3000。

因此 Client 在 ultrawide、4K 或 zoom-out 時，即使畫面真的看得到外圍 actor，
Server 仍可能尚未建立該 observer 的 Spawn/Move subscription；等 actor 進入固定半徑後，
Client 直接拿到較新的權威座標，就會呈現「畫面外圈突然出現／順移」。

PR #6 改為：

1. Client WorldReady 帶 ViewRadius，由 viewport 半對角、zoom、camera/player offset
   加 480 world-unit margin 計算；遊戲中每 0.5 秒只檢查一次 resize/zoom，
   半徑變化小於 64 不送 ViewInterest。
2. 800×600 與一般 1080p 維持原 1800 player AOI 下限，不增加既有流量。
   3440×1440 約 2344，4K 約 2683；Client/Server 皆 clamp 最大 4200。
3. 玩家 AOI 保留既有 reciprocal visibility 與 400-unit hysteresis；
   pair 半徑採兩端實際需求較大者，candidate 仍由 1024 spatial cells 篩選，
   不退回全地圖 O(N²) 廣播。
4. world entity AOI 使用每個 observer 自己的 enter/exit radius；
   entity 移動時最多搜尋受限 4600 的 observer cells，再以個別半徑精確判定。
5. 這只改封包 delivery interest；Blocking、戰鬥距離、命中、Server 權威位置、
   Physics tick、AFK/失焦封包處理均不變。

### 封包節奏結論

原版反編譯只能證明 MoveStep / movelogic 在 Client 本地持續步進，
且 destination movement 與 position correction 是不同命令；原版 50 FPS
不能當成 wire packet Hz。沒有帶時間戳的原版封包擷取前，不宣稱精確原服 Hz。

目前新版明確的節奏是：

- 玩家權威位置 snapshot：Server 最快約每 100ms 廣播一次（約 10 Hz），
  SpeedTime 帶實際 snapshot 間隔，Client 在固定 Physics tick 連續平滑。
- 共享 monster AI waypoint：目前約每 250ms 一輪（約 4 Hz），Client 不中斷地
  插值到 waypoint；PR #4 只合併 socket 尚未送出的同 actor 舊 waypoint，
  combat/state/authoritative correction 保持 FIFO 邊界。
- 這次不靠提高發包頻率暴力換順暢，而是避免 stale backlog、維持 Client 連續運動，
  並讓 Server subscription 真正覆蓋新版可見 viewport。
