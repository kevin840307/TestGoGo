# 2026-11-11 外圍怪物 AOI 反覆卸載／重建修復

## 從現有程式確認的問題

`NetworkEntityManager._update_monster_aoi` 原先僅用 `monster.global_position`（正在平滑追趕的 Client Body）判斷是否超過 Exit Radius，並在超過緩衝時間後 `queue_free()`。可是 `_monster_spawn_templates` 持續被最新的 Server `MoveEntity` 更新。當 Map13 負載、Server 250ms tick 超時、封包合併導致 Client 跟不上時，**顯示位置超過出口半徑、最新權威位置卻已回到 AOI 內**，舊程式仍可能卸載怪物；下個 AOI pass 又因 Server 座標在 Enter Radius 內而排隊重建，怪物瞬間出現在最新位置。

此機制可能解釋遠景怪物「重現即順移」，不是已經證實的唯一延遲來源；需要實機配合封包／AI profiling 驗證。

## 小範圍修正

- **只改 Client Scene lifetime**：回收條件改為「正在顯示的 Body 座標與最近的 Server-authoritative template 座標都超出 Exit Radius」；任一仍在 AOI 內則不啟動卸載倒數。
- 維持原本的 viewport 可見保護、Exit linger 1 秒、真正的 Server `DespawnEntity` 立即刪除、AFK data-only、Spawn queue 優先順序。
- 不改 Client FPS、Physics Tick、WebSocket poll、封包頻率、Server AOI、怪物戰鬥、路徑／Blocking 或權威位置；沒有新增每幀全場掃描。
- 以 `MonsterWaypointSpeed.is_fully_outside_aoi` 獨立算法覆蓋「視覺外權威內／視覺內權威外／兩者外」的無資產 CI regression。

## 驗證範圍與限制

GitHub `client-multiplayer-motion.yml` 可執行 `monster_waypoint_math_regression.gd`；真實 Map13 20 人／大量怪物在 1920×1080 與寬螢幕仍需實機觀察。建議同時觀察 `[MONSTER_AUTHORITATIVE_SNAP]`、`[MULTIPLAYER_PACKET_AUDIT]`、`[MAP_AI_PROFILE]`：若仍見瞬移且沒有 Scene 重建，接著應追查 250ms AI 超時、零時長修正和網路排隊，不要繼續盲目放大插值。
