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

## 2026-11-11 後續提交與實測更新

- `35ccb4e`：Server AOI Priority probe 增加 Detached Recovery 週期測試，確保快照僅複製帳號、角色、地圖、休息狀態、上次恢復時間，不立即訪問 SQLite；第二次250ms Tick 不能重排回血，5秒到期可恢復。CI PASS。
- `60467f8`：`SqliteGameStore.ApplyDamage` 的單一原子 `UPDATE` 不再先開始額外明示 transaction；SQL 條件、HP、death state 和 GetCharacter return 不變。
- `f594e3e`：`CombatService.TickAi` 累計 `DamageWriteMs`、`PetDamageWriteMs`、`ChaseNavMs`、`WanderMs`，只在異常慢速 Tick 以節流方式記錄。
- `e44d8c7`：`ResourceRecoveryService.TickSerializedAsync` 使用 Singleton SemaphoreSlim 將多名玩家同時到期的回血寫入序列化；其等待不占 Session 鎖或消耗 ThreadPool 阻塞執行緒。WebSocket 每連線仍至多一筆未完成 Task；保留回血規則與頻率。

### Map13 壓測比較（各輪 CI 獨立樣本，不能視為控制變因實驗）

| 版本/Actions | 有效樣本 | AI Tick P50 | AI Tick P95 | AI最大值 | 結論 |
|---|---:|---:|---:|---:|---|
| 初始 [43009112637](https://github.com/kevin840307/TestGoGo/actions/runs/43009112637) | 190 | 55.7ms | 2444.6ms | 5006ms | 嚴重異常 |
| 移出 Session lock [43017109931](https://github.com/kevin840307/TestGoGo/actions/runs/43017109931) | 197 | 38.6ms | 1019.9ms | 4581.9ms | 有改善，仍超出250ms |
| 傷害寫入與查因 [43018051050](https://github.com/kevin840307/TestGoGo/actions/runs/43018051050) | 200 | 42.2ms | 1090.2ms | 3048.4ms | DamageWriteMs 可達2011ms |
| 回血寫入序列化 [43018323952](https://github.com/kevin840307/TestGoGo/actions/runs/43018323952) | 195 | 49.5ms | 1463.1ms | 3097.7ms | P95 波動且**仍未達標**；最高一次 DamageWriteMs 約3825ms |

最後一輪 **20個 Bot/312隻怪物、正常連線成功**，但有17/195個有效 Tick 超過250ms，怪物 AI 的剩餘慢速路徑被證實主要是 `SqliteGameStore.ApplyDamage`，`ChaseNavMs`／`WanderMs` 接近零。單純以新的 Client 插值掩蓋這個封包空窗無法真正消除順移。

目前驗證：Server AOI Priority + .NET8 build [43018323901](https://github.com/kevin840307/TestGoGo/actions/runs/43018323901) PASS、Windows Smoke [43018324167](https://github.com/kevin840307/TestGoGo/actions/runs/43018324167) PASS、20人 wire [43018323955](https://github.com/kevin840307/TestGoGo/actions/runs/43018323955) PASS（Movement 1140/1140；其P95=406.5ms仍不可當成低延遲達標）、Godot isolated motion [43015548138](https://github.com/kevin840307/TestGoGo/actions/runs/43015548138) PASS，原始素材不足時完整 Godot Scene SKIP。預設的 Server build 包含既有 Quest host 未綁定函式嚴格稽核，會 FAIL，與 .NET8 compile 的結果不同。

### 仍未解決的核心風險

**不要宣稱順移已解決**：AI 傷害更新在 SQLite 單一 writer 爭用時仍可占住 Map AI/Session lock 一秒以上。長期需要將每名玩家一輪多個怪物傷害整批結算成一次原子寫入、或使用具明確併發控制的 runtime HP state＋持久化隊列；兩者都需要驗證死亡只結算一次、傷害順序、技能/吸血、防禦/暫時 Buff、其他玩家同步、寵物、地圖轉換與斷線恢復，禁止直接用非同步寫入改變戰鬥正確性。

建議下一次提供實機 Map13（20 Bot＋可見外圍怪物）`godot.log` 與 Server Log 的相同時間段，對照 `MULTIPLAYER_PACKET_AUDIT`、`MAP_AI_PROFILE`、`SLOW_TICK_AI_PHASE` 以及 `MONSTER_AUTHORITATIVE_SNAP`，才能確定剩餘的外圍順移有多少是 Server 封包空窗、多少是 Client 畫面重建或零時長權威校正。
