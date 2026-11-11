# 2026-11-11 多人外圍順移：怪物巡邏封包速度與 Session 鎖診斷

## 原版基準
`Old/ClientData/Paradise.exe.lst` 的 `CSGOEnem::SetMoveloop/movelogic` 依目的地與實體移動速度逐步更新。原版 A106 / A107 / A108 表示目的地／怪物移動，A10D 為位置校正；並沒有時間戳 trace 可證明固定送包頻率。

## 本輪確認的速度缺陷
新 Server 的 35單位追擊步進使用 0.25秒，約140單位/秒；但怪物巡邏單段可移動95～175單位，卻仍傳固定0.25秒的 `SpeedTime`，使新 Client 必須以380～700單位/秒動畫追趕，從遠距離看尤其像順移。

## 修改
- `CombatService.WanderVisualSeconds` 按距離除以標準速度計算巡邏呈現時間：95單位約0.68秒、175單位1.25秒；其他追擊、攻擊、碰撞及目的地選取不動。
- `ServerBroadcastHub` 改為允許1.5秒內的怪物巡邏封包在同一 actor 的未送佇列內合併，保存真實 `SnapshotSpanSeconds` 與平均 `MoveSpeed`，攻擊／死亡／Spawn／Despawn 仍為 FIFO 邊界。
- 增加 .NET BroadcastPriorityProbe + Godot 純算法回歸。

**限制**：Server 的權威怪物座標仍在巡邏觸發時一次更新到已核准目的地，Client 以新長度平滑顯示；此次沒有拆改 Server 巡邏實際世界步進。若要進一步模擬整段 Server 逐格移動，需要單獨驗證追擊、Aggro、Blocking 與戰鬥，不能在沒有測試的情況下直接更動。

## 已確認另一瓶頸
20 Bot/312隻怪物 AI Tick P95 約2.2秒。新增分段診斷發現 `ResourceRecovery.Tick` 在 Session 鎖內偶爾花1～2秒，`SyncPosition` 僅處理0.1～0.2ms卻等待該鎖高達約1.9秒。已加角色查詢／完整資產讀取／回復寫入的細部分段日誌，待回歸結果決定下一輪優化；不能用 Client 插值掩蓋秒級 Server 停更。
