# 2026-11-11 多人外圍順移續修：時間跨度與可視 AOI

## 原版比對
- `Old/ClientData/Paradise.exe.lst` 的 `CSGOMap::MessageMap` 使用
  `0xA106`（PlayerSyncDest）、`0xA107`（CharSyncDest）、
  `0xA108`（EnemyMove）、`0xA10D`（CharSyncPosition）。
- `CSGOChar::MoveStep`／`CSGOEnem::movelogic` 在收到方向/目的地後本地逐步移動；
  `SGOMapService.dll.lst` 的目的地與位置校正亦為不同命令。
- 原版 50 FPS 是畫面更新證據，不是網路每 20ms 發包的證據。
  尚缺帶時間戳的原版封包 trace，無法宣稱原版確切發包率。

## 已確認的新版風險
1. `PacketRouter.SyncPosition` 把超過 750ms 的**單筆** Server 移動廣播間隔
   直接截短，只有**進入發送佇列後**才合併的快照會附帶
   `SnapshotSpanSeconds`。Server 本身阻塞 2s 再發包時，觀察者可能
   將 2s 位移誤認為 750ms 的快速移動。
2. `ServerBroadcastHub` 在合併已有 `SnapshotSpanSeconds` 的延遲封包時，
   原本只使用 `SpeedTime`，遺失排隊前的時間。
3. 世界怪物路徑雖然已把完整時間附在合併封包，Client
   `NetworkEntityManager` → `MonsterEntity` 卻沒有傳遞此欄位，
   怪物可能加速追趕數個舊 waypoint。
4. Camera 視野半徑每秒更新一次；局部顯示 AOI 在邊界擺動時立即卸載
   Monster Scene，重建後使用最新權威快照，看起來像外圍順移。

## 修改
- Server：單筆超過 750ms 的玩家快照改送原有 `SpeedTime` +
  `SnapshotSpanSeconds`（最長 8s）；一般 100ms 封包大小不變。
  發送佇列合併時優先採用可信的完整 Server 時間。
- Client：Monster normal movement 維持 250ms；僅合併封包使用完整時間
  計算有界的視覺追點時間（最多 1.25s），避免把 2s 歷史路徑壓成
  750ms 突進，並保持 `SpeedTime=0` 戰鬥校正立即生效。
- 視野：量化的 AOI Radius 改每 250ms 檢查（沒有變化就不發封包），
  不變更固定網路 poll／Physics 更新。Client 只對距離離開半徑
  不超過 500 世界單位的實體提供最多 1s 的視覺卸載緩衝；
  真正遠離畫面的實體仍立即回收。進入真正的 Server Despawn
  仍立即移除，不受這個視覺緩衝影響。

## 測試與風險
- `tests/SGO.Server.BroadcastPriorityProbe`：原本 4×250ms 合併、
  新增 Server 排隊前 2s + 排隊中 250ms 的世界怪物與
  2s + 100ms 的玩家；確認 Server 快照時間沒被壓短。
- Godot 純演算法測試 `monster_waypoint_math_regression.gd`：
  Normal 250ms／合併 2s／戰鬥 0ms／AOI fringe 1s。
- 專用 Actions：`client-multiplayer-motion.yml`、
  `server-aoi-priority.yml`。CI 只驗證測試真正執行的路徑；
  缺原始素材時完整 Scene 測試必須標註 SKIP。
- 待實機：Map13 兩個 Client + 20 bots，在 1920×1080 和更高解析度
  測遠距玩家、怪物、縮放／拖移鏡頭、攻擊停止、切圖、
  AFK／Unfocused；對照 `MULTIPLAYER_PACKET_AUDIT`、
  `MULTIPLAYER_MOTION_AUDIT`、`MAP_AI_PROFILE`、
  `SLOW_MOVEMENT_HANDLER`。本改動不保證消除所有 CPU/DB 尖峰，
  也沒有測出原版的真實發包頻率。

## 協作與整合
- 變更採獨立 PR，目標 `feature/local-server-net8`；Merge 前核對 HEAD，
  不 rebase/force-push 其他協作者的提交。
- 合併後只清理本次 `codex/multiplayer-edge-motion-20261111` 分支。
