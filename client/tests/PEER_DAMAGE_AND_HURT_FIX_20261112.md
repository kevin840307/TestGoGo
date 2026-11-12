# 旁觀者普通攻擊傷害數字與原版受擊動畫 Review（2026-11-12）

## 觀察與證據

使用者實機：自己看別人普通攻擊怪物，無傷害數字（顯示數字未關閉）；自身／他人受擊動畫不自然。

真正兩人加密 WebSocket（獨立 SQLite）已證明 Server 在基本 AOI 情境有正確廣播：A 與 B 先各自 WorldReady、B 收到 A 移動與聊天；Server GM 在同區域生成怪物，B 收到 SpawnEntity，A AttackEntity 後 A/B 都收到 **相同 TargetID、TargetType、AttackerID、Damage、CurrentHP/MaxHP**。獨立測試 GitHub Actions RUN `43225461015` PASS。增加 TargetX/TargetY 後 RUN `43227115211` PASS，確認這兩個值也有送達旁觀者。專門回歸：`tests/SGO.Server.WireSmokeTests/Program.cs --peer-damage-only` + `.github/workflows/server-peer-damage-wire.yml`。

**真實競態**：`ServerBroadcastHub.IsPlayerPriorityPacket` 將玩家發出的 `EntityDamage` 送往高優先佇列，`SpawnEntity` 仍是世界一般佇列。多人／大量怪物時，觀眾有機會先收到 Damage，怪物的視覺 Scene/模板尚未建立；舊 Client 當場丟掉顯示數字。即使 Scene 存在，原 `FloatingTextManager.show_damage` 還會等待攻擊命中時間及 FX 播放進度，與快速 AOI 卸載、死亡相撞。

## 本次修正

- **Server** `CombatService.Attack()`：正常 Player→Monster `EntityDamage` 新增 `TargetX`、`TargetY`，來源為 Server 即時怪物座標，純加法欄位；HP/傷害/命中/攻擊距離不變。
- **Client** `NetworkEntityManager._on_entity_damage_received()`：依 `PeerDamageVisualPolicy` 判斷 *其他玩家的非技能普通攻擊*（`TargetType=1`, `SkillID=0`, `Damage>0`、攻擊者 ID 為其他玩家）。若已建怪物場景，跳過原有等待動畫／FX 才出數字的路徑，改直接顯示 Server 的確切傷害值，另非阻塞播放命中特效；不虛構傷害、不重複顯示。
- 當怪物場景 **尚未建立** 時：優先用新 `TargetX/Y` 定位，舊版封包仍可回退到 AOI 模板座標，進入共用 Label 池呈現。
- **浮動文字**：給已存在的遠端普通攻擊目標專用顯示入口，避免不精確的攝影機裁切把數字錯殺；不提高 Label 池上限，效果仍與文字可見設定分離。
- **受擊動畫**：原版 40MB `Old/ClientData/Paradise.exe.lst` 透過 GitHub Actions 真正讀到的 `CBattleSys::CheckSetHurt`、`CSGOChar::HurtNow`、`CSGOCharDrawLogic::SetHurtNow`、`SetHurtEffEvent`、`StopBattleHurt` 均確認有 **動作狀態判斷** 與 **受擊效果／受擊姿勢分流**。其中 `SetHurtNow` 會依 `IsRaceActionType`／`IsGunActionType` 選受擊動作。新 `BaseEntity.get_combat_hit_state()` 優先依目前攻擊／持槍／近戰狀態選 `hit_weapon_gun` 或 `hit_weapon_melee`，而非用混合 SpriteFrames 中是否存在 GA 動作推測。機甲沒有對應 AH/GH 動作時不強行鎖住 idle。這不是宣稱全部原始函式已一比一移植。
- 修正舊 WireSmoke：C# 區域函式不能同名多載、SyncPosition 原座標不廣播、WorldReady 可能與 NPC Spawn 交錯、160 封包上限不足以排空 300+ 世界場景；專用入口避免商城／技能 Buff 舊測試干擾兩人傷害驗證。

## 測試

- 真正 2 Client `[PEER_DAMAGE_WIRE] ALL PASS`：RUN `43225461015`、新增座標後 RUN `43227115211`。
- 獨立 Godot 4.7.2 `[PEER_DAMAGE_VISUAL_TEST] ALL PASS`：其他人普攻走直接數字，自己普攻／技能／MISS／怪物傷害保留既有路徑。
- Source regression `tests/test_mass_entity_visual_contract.py` 已加入立即顯示路徑與原版受擊動作選擇的護欄，原有 20 actor／4096 drop 測試仍保留。
- **限制**：CI 缺少部分原版 UI/角色圖資，不能透過 GitHub 測到最終真實紙娃娃動畫及攝影機 Label Layer。使用者實機仍必須驗證。

## Windows 驗收

1. 最新 Branch 的 Server 與 Client 一起更新（因 Server 新增位置欄位，舊 Client 不會用）。
2. 兩人同圖（Map13）、一起打怪：旁觀者看 A 普攻時，每筆正傷害出現一次數字；同一筆 HP 變化相等，擊殺也顯示。比較短劍、雙槍、機甲與普通拳。
3. 角色近戰／槍／機甲被怪打：動畫不會被自己或別人的 MoveEntity 打斷；不反覆重播，不鎖死，不影響固定物理 Tick。
4. 大量怪物／掉落物或鏡頭外圍，檢查 `[MULTIPLAYER_PACKET_AUDIT]` 的 `EntityDamage`，並與視覺數字相對照；CI 的世界/載入測試不是完整實機保證。

**不要**把數字問題推回 Server AI Tick：已證明正常同圖普通傷害封包可到旁觀者；極端 Server 延遲是另一項效能議題，需分別驗證。
