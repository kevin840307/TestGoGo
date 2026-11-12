# SGO 大量多人／怪物／掉落物 Review（2026-11-12）

範圍：`feature/local-server-net8`；重點是 20 人、Map13 約 312 怪、數百至數千地上物、普通攻擊／受擊正確、30/60 FPS 下視覺連貫。所有 Client 優化只碰呈現，不得停掉 Server 封包、物理 tick、撿物或攻擊邏輯。

## 已完成（本輪）

- [x] `MonsterEntity.advance_network_runtime()`：Idle 且沒有未完成移動／戰鬥動作就結束 60Hz runtime 更新。以後收到 `MoveEntity`／`SyncState` 再啟用。
- [x] `MonsterEntity.update_hp()`：`EntityDamage.CurrentHP` 立即更新資料與血條，受擊動畫延時且節流；不在攻擊／死亡姿勢中強制重播受擊。
- [x] `NetworkEntityManager._remove_queued_spawn()`：沒有排入視覺佇列的實體直接 O(1) 返回，不再每次刪除掉落物都線性掃描佇列。
- [x] `_enqueue_spawn_by_priority()`：順序已合法的同類低優先級掉落物可尾端附加，維持玩家／近怪優先與同優先級 FIFO。
- [x] `FloatingTextManager`：不為畫面外（含 200px 寬容邊界以外）的傷害建立不可見 Label；100 個 Label 池全滿時回收最早使用的 Label，不會因上限完全漏掉新傷害數字。池不無限成長，登入畫面仍有資源釋放。
- [x] 新增獨立 Godot 4.7.2 測試：4096 個掉落物、鏡頭 AOI 篩選、相同位置重查、掉落物生成／移除與換圖清理。新增 Python source contract，均已接入 `client-multiplayer-motion.yml`。

回歸：`Godot multiplayer movement regressions` GitHub Actions RUN `43221902652` PASS；先前 RUN `43221443790` 已顯示 `[MASS_DROPS_TEST] ALL PASS`。注意純索引測試 ≠ 4096 個完整 Scene／碰撞／Sprite 都能維持 60 FPS。完整場景測試缺部分原始素材，CI 有 `[SKIP]`。

## 最大瓶頸並不在 Client 插值：Server 戰鬥 Tick

GitHub Actions `Map13 20-bot sustained combat AI baseline`，RUN `43221092769`，資料是「每次已完成的真實 Tick」，不是重複取樣最近一筆：

| 指標 | 結果 |
| --- | ---: |
| 並行 Bot／怪物 | 20／312 |
| 有效 Tick 樣本 | 344 |
| AI Tick P50／P95／P99 | 30.00／1009.30／4291.65 ms |
| 最長 Tick | 15054.08 ms |
| AI 内部 Tick P95 | 120.77 ms |
| 玩家 Session Lock Wait P99 | 4000.42 ms |
| Monster AI 250ms 超時次數 | 21 |

同分支 `Map13 20-bot real AI tick profile` RUN `43221092641`，偏移動（`confirmedHits=0`）情境，AI Tick P50 21.48ms／P95 50.19ms；但戰鬥情境 P95 ~1 秒，顯示 **Combat / Session / DB 寫入競爭** 為高優先分析對象，不只是怪數增長。

日誌實例：`SLOW_TICK_AI_PHASE DamageWriteMs=2002.2`、`MAP_AI_PROFILE PlayerSessionLockWaitMs=955.6`、`SLOW_PACKET_DETAIL SyncPosition SessionLockWaitMs=1797.8`。在 `SharedMapAiTicker.cs` 對 `session` 上鎖時執行 `CombatService.TickAi()`；而該流程在 `runtime.SyncRoot` 鎖內逐次呼叫 `_store.ApplyDamage()`，因此同步 DB 等候可能放大鎖持有時間。不可直接刪除鎖或改非原子 HP 更新，否則會傷及正常戰鬥。

**測試品質提醒**：同一 sustained-combat log 在第 149 秒有 `attack=1180`、`confirmedHits=177`、`rejected=1016`；大部分攻擊被 Server 拒絕。因此「20 個 Bot」不等於 20 個一直有效造成傷害的真人玩家。需區分被拒攻擊原因（射程、冷卻、目標死亡、阻擋），再設計有效攻擊比率夠高的壓測案例。

## 接續工作（優先順序）

- [ ] **P0：精確量測 Server Tick 的鎖與 DB 等候**。按 `ApplyDamage`／`ApplyPetDamage`／`GetCharacter`／`GetCharacterAssets` 統計次數、P50/P95/P99；`session` 與 `runtime.SyncRoot` 分別量取得等待／持有期間，絕不可用 Debug 每包刷大量 log。
- [ ] **P0：改善戰鬥資料寫入**。設計每角色／每 Tick 的原子批次傷害提交：每次命中仍各有真實傷害、即時 HP、掉落／死亡與攻擊者歸屬；單 Tick 對角色寫 DB 次數應遠小於怪物攻擊次數。先寫多人同時擊殺、回血、復活、斷線、地圖切換、物品與技能交錯的 SmokeTests，再動 `CombatService` 或鎖範圍。
- [ ] **P1：替 Server 效能壓測增加通過門檻**。候選（不是已達成結果）：20 真實可命中 Bot + 312 怪，AI Tick P95 小於 250ms、P99 小於 500ms、個別 Session Lock Wait P99 小於 50ms；在測試資料與機器穩定後再設為 CI 硬門檻，避免目前已知失敗基準把整條 CI 鎖死。
- [ ] **P1：改進 Bot 的有效攻擊覆蓋率**。同時輸出 `AttackEntity` 拒絕原因類別及怪物 ID，確保有足夠真命中、真擊殺、多人重疊、拾取及大量掉落，否則壓力偏向封包拒絕處理。
- [ ] **P1：實機 2/10/20 人 + 312 怪 + 600/1000 掉落**。測 30/60 FPS、近身／遠端普通攻擊、雙槍、機甲、技能、受擊、死亡／重生、AOI 外圍、Alt+Tab、AFK 返回；比較 `[MULTIPLAYER_PACKET_AUDIT]`、`[ENTITY_SPAWN_TIMING]`、`[MONSTER_CHASE_PACKET_GAP]`、Server `[MAP_AI_PROFILE]`、`[BROADCAST_QUEUE_AGE]`；記錄平均與 P95 幀時間，不能只憑平均 FPS。
- [ ] **P2：密集掉落物實機渲染**。4096 資料索引已通過，但物件 Scene／碰撞、Label、貼圖材質和焦點切換仍需 GPU/主執行緒 Profiler 才能確定是否需要分層渲染、畫面可見 Label 或更細視覺 AOI。
- [ ] **P2：確認原版動作逐幀語義**。需可讀的 `Old/ClientData/Paradise.exe.lst` 命中事件對映或原版實錄，才能比較 hit/attack 優先序、命中時間、機甲動畫長度；目前不能宣稱一比一。

## 必守契約

1. Server 保持權威座標／碰撞／攻擊距離／命中／HP／死亡／掉落與拾取。
2. 不以降低 FPS、調慢封包處理、暫停低資源／AFK 遊戲邏輯換效能。
3. 高優先序玩家動作與 `SpawnEntity`／`MoveEntity`／`SyncState`／`EntityDamage`／`EntityDeath` 的相對順序不可被錯誤合併或重播。
4. 改 Server 鎖與 DB 前先用獨立測試資料保護持久化及重播冪等；不要修改真玩家 HP／帳號資料跑壓測。
5. `Server build` 的總工作流程目前被原有 Quest Host strict audit 的 unsupported functions 擋住；不能當作 Client 改動造成的新編譯錯誤，但也不能因此宣稱整套 Server CI 全綠。
