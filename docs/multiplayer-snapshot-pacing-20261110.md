# 多人移動快照：稀疏封包與塞包合併的相容性修正（2026-11-10）

## 背景

原版反編譯 `CSGOMap::OnCharSyncDest` / `CSGOMap::OnCharSyncPosition`、`CSGOChar::MoveStep` 可見「目標/位置校正」與「Client 自主逐幀步進」分工；**不能由此證明原版每 20ms、100ms 或 250ms 發一次封包**。詳見 `docs/multiplayer-legacy-movement-audit.md`。新版是 WebSocket+JSON，不複製原版二進位協定。

## 此 PR 的明確問題

- 現有 `RemotePlayerMotion` 每包預測上限 0.28 秒、40px，實際短於 350ms 的 bots 更新間隔；較快角色 40px 很快耗盡，接下來會等下一包，遠處看起來像「一格一格走」。
- `ServerBroadcastHub` 合併同角色的未送 `MoveEntity`，雖保留最新座標，但超過 750ms 的總時間被截斷後，接收端會從大位移推得過高速度。

## 修改

1. **Client**：依已知 Server `SpeedTime`（普通快照最多 750ms）取得每筆預測 horizon；100–350ms 移動延伸到下一個 waypoint 前後的小容忍時間，限制最大 420ms / 140px，仍在 physics tick 中更新；超過 750ms 的合併包僅允許 120ms 短時預測。
2. **Server**：不改舊有 `SpeedTime` 相容欄位（0.05～0.75s）；合併移動快照另送 `SnapshotSpanSeconds`（0.05～8s），不改 AOI、Session、傷害/死亡/傳送事件、廣播頻率或加密。
3. **Client dispatcher**：僅對其他玩家傳遞 `SnapshotSpanSeconds`，怪物／寵物維持原方式。舊包沒有欄位時走原邏輯。
4. **測試**：加入 350ms 連續步進、2s 合併距離/速度換算、長合併不得外推 2s 的演算法測試；擴充 Server priority probe 驗證新舊欄位並存。

## 重要限制與驗收

- **所有預測僅影響視覺**：`authoritative_position` 不更改，攻擊、拾取、Blocking、技能、AOI 不依賴預測位置；不降低 network poll / physics / FPS，不更動 AFK UI。
- `SnapshotSpanSeconds` 上限 8 秒是防禦界線，不代表超過 8 秒的塞包會自動流暢；此時仍要查 WebSocket backlog / SendAsync / SQLite / Client queue。
- 需要在 Map13 用 20 bots + 真實 Godot Client 重測 100/350ms、轉彎、停止、攻擊、失焦、換圖；**未實測前不宣稱已消除畫面 lag**。
- 預測上限 40px 擴大至 140px：若最後一個停止封包遺失，可能增加短暫畫面超前。Debug 需一起看 `MULTIPLAYER_PACKET_AUDIT` 和 `MULTIPLAYER_MOTION_AUDIT`，必要時再縮小數值。
