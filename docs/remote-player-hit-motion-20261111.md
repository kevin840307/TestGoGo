# 2026-11-11 遠端玩家忽快忽慢與多人受擊動畫修正

## 現場證據

使用者 2026-11-11 14:10 左右的 Server Log：Map13，20~21 sessions／312 monsters。
- `SyncState` 慢封包 `TotalMs=271.6/445.7ms`，其中 `SessionLockWaitMs=0`，`HandlerMs=271.6/445.7ms`。源碼 `PacketRouter.SyncState` 對每個 walk/idle 直接呼叫 `SqliteGameStore.UpdateState` 寫一筆 SQLite，屬於不必要的慢速持鎖路徑。
- `MAP_AI_PROFILE` 達271~472ms，`TickAiMs` 為248~347ms，偶有 `SessionLockWaitMs=34.9~123.9ms`；部分尖峰與 DB/使用者封包 Session lock 競爭相互影響。
- Godot 實測 `godot(20261111-141017).log`：`REMOTE_PLAYER_PACKET_GAP` 角色359/362/363/364等約500~817ms；`target_delta` 約48.5~49.5座標單位；Server `SpeedTime` 約0.49~0.75秒。背景／失焦時 Physics 仍維持約60Hz，與FPS限20不同。
- `MULTIPLAYER_PACKET_AUDIT` 每10秒約4~5千包，其中 `EntityDamage` 與 `SyncState` 合計佔很大部分；WorldReady 初次入場曾排隊271包（實際遊戲期間的既有節流後仍偶有尖峰）。`MULTIPLAYER_MOTION_AUDIT` 顯示玩家端的接收間隔不等於 Server位置發送間隔，不應將網路等待當成移動速度。

## 根因／修正

### 1. 新版 Client 遠端玩家的移動不是固定速度

`RemotePlayerMotion.advance` 舊邏輯每個新座標到達就以高係數 `lerp` 追逐 *已送達的歷史終點*，在約100ms快速追一大段，預測又於約420ms停止；遇到630~750ms Bot/玩家位置封包就變成「前面衝很快、中間停下等」。

新邏輯以 Server `SpeedTime` 與位置差反建 `segment_start=server_end-velocity*span`，每個 Godot `_physics_process` 更新以 `velocity*delta` 持續前進。延遲／併包只允許最多2倍預期速度的有限視覺追趕；超過區間後不會無限推算。 `SpeedTime=0`、Server Idle/Death、真正跨圖瞬移仍由Server權威位置決定。**不更改 Client↔Server 封包頻率、不調低遊戲Fixed Tick或FPS、不影響玩家本身移動、AFK、戰鬥、撿物。**

### 2. RemotePlayer 的受擊動畫被 walk / idle 打斷

`NetworkPlayer._handle_movement` 原本只防止 `attack_*` 被 walk 覆蓋，沒有完整保護 `hit_weapon_melee`／`hit_weapon_gun`。 `NetworkEntityManager` 另外每個 `SyncState` 都會強制 `is_action_locked=false` 且呼叫 `sync_animation`，使被打的人物閃爍或姿態被中斷。

`RemotePlayerStatePolicy` 針對已鎖定的 hit 動畫保留姿態到 `BaseEntity._on_body_animation_finished`，新的死亡仍立即接管狀態；收到 Idle 時仍停止權威視覺外推，但動畫只在受擊結束後才切換。角色在播放hit/attack時會固定面向翻轉 parity，不會因每走1像素就重新切換左右翻轉。受擊結束後依 `RemotePlayerMotion.is_moving()` 正確恢復走路。

### 3. 自己人物被連續打時 Hit 延遲遭到飢餓

`BaseEntity.update_hp` 舊版在每次傷害發生就遞增 `hp_update_seq`，再等待約300ms 後才更新 HP、觸發 Hit；若此時又遭受一筆傷害，前一筆異步動作全部失效。大量怪物連續打人可能讓 HP 與受擊姿態長時間不顯示、最後一起跳。

新版在收到已經權威結算的 `EntityDamage` 時立即更新 HP 模型；僅視覺Hit依原本延遲一次排程，連續傷害不會取消首次 Hit；恢復生命、MISS與重複HP資料不會虛構受擊動作。致命傷害維持原演出延遲，使用`_fatal_visual_pending`防止連續零HP封包取消死亡動畫；若期間新權威HP變正則不誤觸死亡。

### 4. Server SyncState 不得阻塞即時移動

`PacketRouter` 舊版對每次 `SyncState` 都在 Session lock 直接執行 SQLite `UPDATE characters state`；使用既存的 `PositionPersistenceService` 100ms 背景批次寫入 `StageState` 取代直寫，會在角色切換／登出時執行 `FlushQueued` 或 `FlushFinal`，依最新狀態持久化；其他用直接建構 Router 的 Smoke 可以保留舊同步契約。SQL另加 `hp>0 OR target state is death/die` 守衛，避免延遲的 Walk/Idle 覆蓋已死亡的DB角色。

## 自動化結果與限制

- [Godot remote motion](https://github.com/kevin840307/TestGoGo/actions/runs/43038528826)：PASS。含750ms時間窗每幀持續移動、前後250ms速度相近、連續封包不中斷、60Hz Hit狀態保護、50/60Hz移動對照，以及20人/60怪的Headless回歸。
- [Godot visual allocation contract](https://github.com/kevin840307/TestGoGo/actions/runs/43038529049)：PASS。真實原版美術的完整人物受擊動畫不能在沒有素材的CI中目視驗收。
- [Server AOI priority + state persistence probe](https://github.com/kevin840307/TestGoGo/actions/runs/43038682770)：PASS。測試1000次 `SyncState` O(1)快取、最新值寫入、重新選角／登出flush、致命傷害後晚到的Idle不會覆蓋death。
- [Server Windows Smoke](https://github.com/kevin840307/TestGoGo/actions/runs/43038682985)：PASS。
- [20人WebSocket wire](https://github.com/kevin840307/TestGoGo/actions/runs/43038682943)：PASS；Movement 1140/1140，P95約14.9ms，測試與真實大量怪物及人類動作不同，不可直接視為實機無卡頓。
- [Map13 20 Bot 312怪](https://github.com/kevin840307/TestGoGo/actions/runs/43038682839)：PASS；205有效樣本，AI Tick P95=66.6ms，最大100.5ms，超過250ms為0。注意此CI採取無主動打怪的特定測試設定，無法覆蓋使用者真實持續戰鬥場景。

## 下一次實機驗收

在Map13 20 Bot、FPS60、1400x900或更寬畫面中觀察三項：(1)其他玩家遠端走路是否仍一段快一段停；(2)站樁遭怪物連續攻擊時，Hit/HP是否每次正常反應且不閃爍；(3)根據`[SLOW_PACKET_DETAIL]`確認是否已消除`Type=SyncState` 200ms以上持鎖。若仍異常，帶新的`godot.log`與同一時段Server Log定位後續狀況。

**保留原版目的地＋本地連續步進、Server權威校正；只是改掉新Client追逐歷史位置的速度波動，並不聲稱原版加密協定逐位元相同。怪物既有正常路徑未修改。**
