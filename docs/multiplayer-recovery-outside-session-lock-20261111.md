# 2026-11-11 多人延遲：資源恢復 SQLite writer 移出 Session 鎖

## 實測根因
Map13 20人＋312怪物的 `SLOW_RESOURCE_RECOVERY_PHASE` 證實 `CharacterReadMs` 約 0.4～1.7ms、`AssetsReadMs` 約 0.1～0.3ms，但 `WriteMs` 約 1000～3000ms（SQLite WAL單寫入者競爭與 busy wait）。原本 `WebSocketEndpoint` 在 `lock(state)` 內執行 `PacketRouter.Tick` → `ResourceRecoveryService.Tick`，使 `SyncPosition` SessionLockWait 500～1900ms、共享怪物 AI 卡頓和外圍視覺跳躍。

## 本次修改
1. `ResourceRecoveryService.CaptureDueTick` 必須於 session lock 內呼叫，只複製 AccountId、CharacterId、MapId、IsResting、上次恢復時間，並預留原 5秒或設定週期。無 SQLite 操作。
2. **正式 WebSocket** `PacketRouter.Tick(..., includeResourceRecovery:false)` 維持所有其他 AI、資源、封包、Physics 的 250ms 時鐘。當恢復到期且沒有舊恢復任務時，開啟*每個連線最多一筆*背景 recovery Task，對獨立 `ClientSession` 快照執行原來完全相同的回血公式與 SQL 寫入，不持有 live Session monitor。
3. 送回 PlayerStatus 前後檢查原帳號、角色、地圖是否仍為現行連線；換圖、角色切換或登出不傳舊角色狀態。連線關閉取消傳送並等待已開始的 DB 任務結束再釋放 sendGate。
4. **Standalone / smoke** `PacketRouter.Tick` 保留預設 `includeResourceRecovery:true` 的舊執行路徑，契約不變。
5. 保留原恢復間隔、休息倍率、HP/MP計算、SQL原子更新、角色活著才回復、Server權威戰鬥規則。這不是降低封包或 Fixed Tick；Recovery SQLite若壅塞會在背景序列化，不再阻塞同一個 Client 的即時移動／技能封包。

## 風險／驗收
- 多任務共享 SQLite WAL 單 writer 仍可能拖慢其他 DB 寫入；本修正首先隔離 Session/AI monitor，不能宣稱 DB競爭本身完全消失。
- 需 GitHub Actions 20 Bot＋312怪物重測 P95及 `SLOW_PACKET_DETAIL` SessionLockWait，並跑 Windows smoke / AOI priority / Godot motion。需要避免回血 status 與技能事件競爭導致畫面暫時顯示較舊 HP；身份檢查避免跨角色顯示。
- 原版反編譯顯示目的地與 Client 持續移動，不表示原版一定每20ms發包；本修改不更動玩家100ms或怪物250ms頻率。
