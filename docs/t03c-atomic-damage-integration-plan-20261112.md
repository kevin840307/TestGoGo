# T03.c：多怪物傷害原子結算與 Server/Client 事件交接（2026-11-12）

目標：多玩家+多怪物在 Map13 連續打怪時，降低 `SharedMapAiTicker.TickOnce()` 被 `CombatService.TickAi()` 內的 SQLite/Session/Map lock 阻塞的長尾；**絕不能改掉 Server 權威、每擊傷害與死亡一次、攻擊間隔、Buff/寵物、封包因果順序**。

## 已完成與尚未完成

- PR #20：`/health.sqliteHotWrites` 量測單次傷害 gate wait/held、Open、UPDATE、Read；20 Bots 實測中 `DamageUpdate P99` 約 5ms，單次尖峰 >1s；背景 Position/Recovery 的 WAL 爭用仍存在。
- PR #21：只有 *可取代的背景位置寫入* 可延後並恢復；不得把這個機制套到 HP/MP、怪物擊中、掉落、交易或死亡事件。
- PR #23：修復 `LegacyPlayerGrowthCatalog` singleton 普通 Dictionary 的 GM level 多線程冷啟動競爭。
- PR #22：新增選用 `IAtomicCharacterDamageStore.ApplyDamageBatch(account, character, orderedDamageList)`，同一 SQLite write transaction 用舊 `MAX(0, hp - damage)`/death state SQL 一次更新，回傳 **完整 final Character**、**每一擊之後的 HP 序列**、**FirstDeathHitIndex**。具帳號隔離、多擊/越傷/死後不重複死亡/8 個並行致死者與原單擊 parity 回歸。
- **PR #22 尚未接到實際怪物 AI**。除非同一批工作已將全部下列條件驗證，勿在 todo.txt 將 T03.c 標記為完成；目前不得宣稱戰鬥卡頓已修復。

## 現有控制流程（先讀原始碼）

- `server/Application/World/SharedMapAiTicker.cs` 在每 250ms 按擁有者指派怪物，持有 `lock(session)` 呼叫 `CombatService.TickAi`。
- `server/Application/Combat/CombatService.cs` 在 `lock(runtime.SyncRoot)` 迭代怪物（包含寵物），同時處理追擊、格擋、每個 monster cooldown、狀態與 Packet List。玩家每一擊會呼叫 `_store.ApplyDamage`，回傳當下完整 `Character`、算出 Buff 後的 MaxHP，依序產生 `MoveEntity`／`SyncState(attack)`／`EntityDamage`／`PlayerStatus`／首次 `EntityDeath`。
- 多怪「有候選攻擊」**不等於**「可以直接加總傷害」：在同一 Tick 內可能混有 Pet hit、玩家技能、擊中後死亡、已死亡玩家之後的 monster 追擊行為；有些增益／職業數值依目前角色而計算。
- `ApplyDamageBatch` 要求同一個 account/character、依事件順序傳入 **Server 計算後**每擊傷害；結果 `HpAfterEachHit[i]` 對應第 i 位攻擊者。若首次致死索引為 i，只能為該擊建立一次死亡/擊殺事件。超過死亡點的怪物不能照原樣繼續命中。
- `FirstDeathHitIndex` 從零計數，`-1` 表示這組攻擊沒有首次致死事件（包含進來前已死）。所有正常 Hit 都保留原攻擊者 EntityID、方向、動畫、封包順序。

## 安全接入建議

1. 新增 `TickAi` 所屬角色的 *候選物理命中* 小型 ordered staging（不是跨玩家、跨 Tick、跨 Map 大型 batch）。保持怪物追擊/Blocking/時間邏輯，先以測試比對舊逐擊順序；切圖、身份變動、玩家死亡需直接清除候選隊列。
2. 需要保留**虛擬 HP** 與每次擬生效的攻擊快照，讓後續怪物在相同 Tick 看見先前命中效果。所有 Buff/防禦的計算仍是 Server 正式公式。
3. 使用 `ApplyDamageBatch` 結果確認每個中途 HP/第一致死者；在同一個 owner 決策上下文內同步生成 **原順序** 的完整 Server 封包，Client 不需推測或補算 HP。若 batch 交易失敗，整批不可發出部分傷害/死亡封包。
4. 若某一個 owner 的 monster 包含 interleaved pet hits、狀態轉換或其他必須逐擊生效事件，先 **回退舊逐擊路徑**，不要強行合併所有怪物。必要時用 feature flag 做 A/B，預設舊流程直到所有契約通過。
5. 減少 Session/Map lock 的 I/O 若需兩階段提案/提交，第二階段要驗證 actor ownership / map identity / target visibility / 怪物不重複傷害；不得在無 transaction guard 下先放鎖再讀寫同一 mutable MonsterRuntime。

## 必過回歸／壓測

- `.NET 8` 編譯；AOI Priority/SQLite 庫存與傷害契約；Windows Full Smoke（若已有無關失敗，留下完整失敗 ID）；Map13 20 Bots 312 怪**持續**移動/普通攻擊/單體技 + 測量 AI Tick P50/P95/P99/最大值、LockWait、DamageWrite、Socket queue age。
- 同一角色被多怪同 Tick 攻擊：HP 每擊遞減、順序與原本一致、首位致死者唯一、其他攻擊不觸發幽靈死亡；滿血/低血/負傷/過量傷害/吸血/回血 Buff/寵物坦怪/重連/切圖。
- 不降低 Client physics tick、network poll、Server 固定更新頻率或視覺 FPS 來換效能。不新增每擊逐包 IO/log。
- 原本「別人普通攻擊傷害數字」「受擊動畫」的 Client 變更是同 Repo 其他 Session 所做，**不得覆蓋他們提交**。

## 接手指示

先讀根目錄 `todo.txt`、本檔與 PR #22/#23。檢查最新 `feature/local-server-net8` HEAD 及開啟的 PR；若另一 Session 已在同檔實作，使用獨立工作分支，不覆寫。每項測試必須附 GitHub Actions Run URL；無法執行的標為未驗證。
