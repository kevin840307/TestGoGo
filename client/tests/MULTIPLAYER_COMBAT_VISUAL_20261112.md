# 多人連線動畫與傷害數字修正（2026-11-12）

## 本輪確認與修正

| 問題 | 在程式中確認的原因／風險 | 修改 |
| --- | --- | --- |
| 旁觀者看不到部分傷害數字 | `FloatingTextManager.show_damage` 等待命中延遲、特效部分時長，目標若因死亡卸載則提前返回；AOI 高優先序傷害封包可能早於怪物視覺場景建立 | 先記錄受擊世界位置；目標刪除後仍使用受擊座標顯示數字；已有怪物 Spawn 快取、尚無視覺節點時，僅在畫面內補顯示傷害 |
| 其他玩家攻擊／受擊姿勢被中途截斷 | `NetworkPlayer.apply_network_state` 先把單次動作鎖住，`NetworkEntityManager._on_entity_state_changed` 卻隨即清除 `is_action_locked`，又重複 `sync_animation` | 將單次動作鎖交給有 `apply_network_state` 的實體自行管理，並刪除外層重複同步；晚到的 Move/Idle 不覆蓋動作 |
| 自己或他人連續受擊時反覆重播、機甲卡住 | 暴擊／怪群連續傷害可能重播姿勢；機甲 `get_combat_hit_state` 回傳 `idle`，原本仍鎖住卻沒有能完成的 hit 動畫 | 加入 500ms 命中姿勢節流；機甲沒有專用 hit 動畫時不把 idle 鎖成單次動作；精簡視覺或資源缺漏時有有界解鎖保護，且絕不覆蓋死亡動畫 |
| 移動速度使走路／機甲腳步動畫加快 | `PaperDollEngine.update_animation` 把 `MoveSpeed` 乘進 `walk` 的 `speed_scale` | 固定走路動畫 1x；移動座標仍依 Server／Client 正常速度更新；攻擊速度仍獨立使用 AttackSpeed |

## 新玩家剛出現時的順移

- Server `BuildPlayerSpawnPacket` 原本已使用 AOI 即時座標，不依賴 DB 舊座標作為第一個位置。
- Client `_on_entity_spawned` 及 `_get_current_queued_spawn_data` 已將先到的 MoveEntity 缓存並合併到玩家生成位置；`NetworkPlayer.initialize_network_player` 也重設了遠端插值狀態。
- 本輪補強首次 MoveEntity 插值測試，沒有提高封包頻率或刪除真正換圖、傳點的瞬移判定。若實機仍有第一幀跳躍，需留存帶 `SpawnEntity`、`MoveEntity`、`SnapshotSpanSeconds`、`PositionCorrection` 時間戳的觀測紀錄再定位，不宜把真正的傳送全部變成慢慢滑行。

## 相容性原則

- 不改 Server 攻擊範圍、傷害、AOI、碰撞或玩家位置驗證。
- 不降低 Server↔Client 傳輸、固定物理更新、戰鬥／移動／自動打怪／撿物反應。
- 只改 Client 視覺動畫與數字；傷害數字仍完全由 Server 的真實 `EntityDamage` 提供，不會在 Client 估算假傷害。
- `Old/ClientData/Paradise.exe.lst` 已存在於 Git 倉，但本次經 GitHub 連接器讀取指定行回傳空文字，無法據此宣稱已逐個核對原版動畫每幀時長。倉庫既有 legacy 50Hz Client／10Hz Server 動作測試仍保留。

## 測試紀錄及限制

- GitHub Actions 的 `Godot multiplayer movement regressions` 已通過（含新增遠端首次位置、受擊／攻擊 one-shot 與補傷害視覺所有權測試）。
- `Godot Client visual allocation contract` 已通過。
- CI 的原畫素材缺失時會略過真實場景整合測試；單元／協定測試 PASS **不等於** 20 人實機動畫已完全符合原版。
- 全域 `Server build` 流程仍因既有 Quest Host strict audit 的 unsupported functions 失敗，與此輪 Client 視覺修改無直接關係。

## Windows 手動驗收重點

1. 用 2 個真實 Client + WireLoadProbe Bot，在地圖 13 同時普通攻擊：自身／旁觀者的同一筆 `EntityDamage` 都應看到傷害數字，包括擊殺那一擊。
2. 自己與他人連續被怪打：HP 立即更新、hit 姿勢不無限重播；攻擊／受擊／移動動畫不會相互剪斷；死亡動畫不能被修復邏輯覆蓋。
3. 使用高速鞋或腳程 buff／機甲：移動位置仍應照真實速度前進，但雙腳、機甲 walk 動畫播放率維持 1x；攻擊動畫仍依 AttackSpeed 運作。
4. 新玩家上線／AOI 邊緣進入：第一位置不能短暫出現在錯誤的地圖原點；移動連續、真正的換圖／GM 傳送要保持即時。
5. 同樣人數下比較 30、60 FPS、畫面中心和外圍怪物／玩家，查 `[MULTIPLAYER_PACKET_AUDIT]`、`[REMOTE_PLAYER_PACKET_GAP]` 與 `[MOTION_AUDIT]`，不要只憑單次視覺判定性能 PASS。
