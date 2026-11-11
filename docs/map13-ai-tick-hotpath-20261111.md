# 2026-11-11 Map13 20 人＋312 怪物 AI 熱路徑效能修正

## 實測證據
- ActiveSessions=21, ActiveMaps=1, Monsters=312, Assigned=312。
- `MAP_AI_PROFILE`: TickAiMs=210.4–266.8ms, PlayerSessionLockWaitMs=0–47.4ms, SessionLockWaitMs=0–43.3ms，整輪 253.5–294.7ms；已超過固定 250ms Tick，會產生所有怪物同步延遲／視覺追趕。
- 技能 `USE_SKILL_PROFILE`: CombatMs 約 104–125ms，並可能同一 Session 競爭鎖。

## 原版對照
`Old/ClientData/Paradise.exe.lst` 的 `CSGOChar::MoveStep`、`CSGOEnem::movelogic` 用固定點連續移動；`Old/服務端/plugins/SGOMapService.dll.lst` 區分目的地 A106、位置 A10D。這不代表每個包必須每 20ms 送出，也不能以 Client 補間掩蓋 Server 250ms 超時。完整證據和限制在 `docs/multiplayer-legacy-movement-audit.md`。

## 本輪確認的熱路徑
`SharedMapAiTicker` 每輪已先用 `GetCharacterHpSnapshot` 將 Map13 20+ 人的 HP 一次查出，但之後對**每個有怪物分配的玩家**呼叫 `CombatService.TickAi`。舊 `TickAi` 又無條件執行：
- `GetCharacter`：完整角色欄位，包含 club/title/transform/timed stat 與經驗修復工作；
- `GetCharacterAssets`：背包、裝備、寵物、馬匹及相關完整資料。即使玩家離所有怪物很遠、所有怪物只是定期巡邏，一樣查全部。
- 這個重複 DB 工作位在 Session/Map AI 熱路徑，會放大總延遲。不能憑這份 Log 證明 200ms 全是 DB；尋路與 312 怪物需要另外量測。

## 修正
1. `SharedMapAiTicker` 將本輪已驗證的玩家存活快照傳給 `TickAi`，不再對同玩家額外讀角色；對其他直接呼叫 `TickAi` 的測試/入口保留 `GetCharacterHp` 相容 fallback。
2. 沒有召喚寵物 runtime 位置者不查寵物；有寵物的玩家只讀窄版 `GetActivePetFollowSnapshot`，保留 State/HP/Defense/UID 等 AI 需要的欄位。
3. `GetCharacter`、`GetCharacterAssets` 只在怪物真的準備傷害**玩家**時載入，並再確認實際 HP，防止 Map HP 快照與當下死亡競態。攻擊、技能、命中、防禦與其他 Server 權威規則不更動。
4. 不改 250ms AI、100ms 玩家快照、封包順序、AOI、Client FPS/physics、AFK/失焦、攻擊與掉落規則。

## 驗證
- .NET 8 專用 Server AOI/Build probe、20-client encrypted WS probe、Windows smoke；`BroadcastPriorityProbe` 補測 SQLite 窄版 pet 的 State/Defense（防止資料結構缺欄導致寵物攻擊崩潰）。
- 真實 20 bot/312 monsters 的 Before/After `MAP_AI_PROFILE` 必須再測，目標是 TickAi p95 **穩定低於 250ms**；這是待測目標，不是假稱已達成。Client 外圍順移也可能仍受 Server AOI / Godot scene backlog 影響。
