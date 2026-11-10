# 多人移動第二階段：合併封包的長距離視覺平滑（2026-11-11）

目標：`feature/local-server-net8`。上一輪已納入 PR #5 視野邊界保留、PR #7 Server 視窗 AOI、PR #8 AOI 空間掃描改善；**這些只能處理邊緣訂閱，不代表多人順移已完全根治**。

## 原版參考

`Old/ClientData/Paradise.exe.lst`：`0xA106`/`0xA107` 目的地、`0xA108` 怪物移動、`0xA10D` 位置校正，`CSGOChar::MoveStep` 與 `CSGOEnem::movelogic` 逐幀推進位置；不能據此宣稱原版確切每秒送包次數。詳見 `docs/multiplayer-legacy-movement-audit.md`。

## 具體缺口

1. `ServerBroadcastHub.TryDequeueOrdinary` 可在 WebSocket queue 壅塞時合併同怪物未傳送的多個 `MoveEntity`，將歷史 2s 移動表示成 `SpeedTime=0.75`、`SnapshotSpanSeconds=2.0`，但 Client `NetworkEntityManager` **未將該 span 傳遞給 `MonsterEntity`**。造成遠處怪物視覺把長時間累積位移在短時窗內衝完。
2. `RemotePlayerMotion.receive` 遇到視覺位置距離新座標 >=1200px 就 `reset`，即使 Server 明確告知這筆是合理的 3s 合併走路，仍然立刻順移。
3. 合併封包缺少筆數診斷：無法區分 2s 長間隔是幾筆歷史快照。

## 本輪修正

- Monster：只對 `SnapshotSpanSeconds` 是 (0.75, 8] 秒的合併包調整**純視覺**追趕時長，採取 `max(SpeedTime, min(1.5, sqrt(SnapshotSpanSeconds*SpeedTime)))`；一般 250ms waypoint、0ms 戰鬥強制校正保持原行為。效果是避免過度衝刺，仍不花數秒重播舊歷史。
- Remote player：如果 Server 提供有效合併跨度，而且位移符合跨度 × 限制速度，跨越 1200px 的連續走路不再直接 reset；真正無標記／過快的傳送仍立即套用。
- Server：已存在的雙優先佇列、AOI、送包合併與保序不改；合併封包增加 `CoalescedMoves` 純診斷欄位（玩家及怪物），供實機查明壅塞程度；舊 Client 可忽略欄位。
- 不修改 Server/Client 封包 poll、physics tick、FPS、AFK/失焦、戰鬥、尋路、碰撞、撿物、原本 UI。

## 驗證限制與下一步

獨立 Godot 測試涵蓋 100/250ms 普通移動、2s 怪物合併、3s 玩家位移、無標記傳送、超出合理速度的硬校正；完整場景含原始素材，仍須實機 Map13/20 bots 1920×1080 或更大視窗重測。特別蒐集 `MULTIPLAYER_PACKET_AUDIT`、`MULTIPLAYER_MOTION_AUDIT`、`BROADCAST_BACKLOG`、`MAP_AI_TICK_GAP` 與 Server `SLOW_SEND_BATCH`，核對是否還有 *AOI Despawn→Spawn* 或 0ms attack correction 跳躍。視覺平滑不等於掩蓋 1–5s 的真正網路／Server 延遲。
