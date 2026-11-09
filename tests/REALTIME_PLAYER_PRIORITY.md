# 多人環境：玩家優先、怪物／掉落物獨立更新

## 變更範圍

Server：
- `WebSocketEndpoint` 的世界模擬仍維持 250ms 固定步進，**不變更** Server AI、戰鬥計算、傷害、技能 CD、AOI 可見集合、掉落生命周期。
- 原本每個 WebSocket 連線只在 250ms world-tick 末端從同一 `ConcurrentQueue` 集中送出所有 AOI 廣播；在玩家＋怪物＋掉落物大量更新時，整批會等待同一個 tick。
- 新增**獨立、事件喚醒的 AOI 廣播發送迴圈**：ServerBroadcastHub 用容量為 1 的通知 Channel 合併連續入隊通知，僅有新封包才喚醒連線的發送工作，不會讓 1000 條 WebSocket 每隔 25ms 空轉；每批最多取 256 筆已排隊封包。先發同批玩家的 Spawn/Move/Despawn/SyncState 及玩家傷害／治療，再發其他實體事件。佇列中所有封包都會送出，不能丟掉掉落物、戰鬥、移動訊息；`sendGate` 保護同一條 WebSocket 上的並行送出。
- 同一個優先級內維持原始入隊順序，避免同一玩家 Spawn/Move/Despawn 被倒序；對未知類型及非玩家實體，保留 FIFO 順序。這是 bounded priority，不是跨網路封包的全域重新排序。
- 既有 `[BROADCAST_BACKLOG]` 診斷仍保留；在非空佇列時最多每兩秒抽樣一次 Count。

Client：
- `NetworkManager` 固定 physics tick 解密／解析全部遊戲封包的流程不變，**沒有降低傳輸、封包處理、尋路或自動打怪頻率**。
- `NetworkEntityManager` 的權威 AOI 快取與待建立場景分離。重排**視覺建立**的順序：玩家 > 已揭露的戰鬥怪物 > 寵物 > 附近普通怪物 > NPC > 掉落物。掉落物仍持續接收、儲存掉落權限與剩餘時間，沒有移除或延長掉落。
- 玩家及怪物保留每一個實體自己的網路座標與視覺插值；低優先掉落物建立不阻塞每個已存在人物的 `network_move_to` 更新。

## 檢查方法

1. 用新的多人 bot 工具（見 `tests/SGO.Server.WireLoadProbe/INTERACTIVE_BOTS.md`）在隔離 Server 建 20／50／100 人，讓正常 Godot Client 同時進入。
2. 觀察實際玩家在大量怪物死亡掉落時的移動、普攻、技能、AOI Spawn/Despawn 是否持續。
3. 跑 Godot 不需 Server 的掉落／玩家優先順序回歸：

```powershell
godot --headless --path client --script res://tests/ground_item_aoi_regression.gd
godot --headless --path client --script res://tests/aoi_pre_spawn_move_regression.gd
```

4. 檢查 Server `[BROADCAST_BACKLOG]`、`[SLOW_PACKET]`，並對照 Client `network_drain_frame` 的 `remaining_packets`／`budget_hit`。
5. 若觀察結果未改善，下一步應從實際分段計時（Server 排隊時間、AES 加密／SendAsync、Client 收包延遲、Client Spawn 圖資載入）定位，不要直接降低玩家/怪物操作速率。

## 限制

事件通知可以讓廣播在有新封包時開始發送，**不代表保證在固定毫秒數內抵達**。高負載、網路反壓、Client 單幀解密負擔，都可能讓實際延遲更高。Server 250ms 的共享怪物 AI Tick 仍為固定節奏；本次不將 Server 怪物 AI 切成各自執行緒，以避免 1000 人環境出現寫入競爭、怪物瞬移或傷害多算。

本次程式碼已提交 GitHub，但執行環境沒有 .NET 8 與 Godot CLI，尚未完成完整建置與多人端對端測試，不能宣稱量化延遲已下降。
