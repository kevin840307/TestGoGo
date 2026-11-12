# SGO 多人機器人測試 — 弱攻擊、高血量、隨機合法座標

**直接連線現有 SGO Server，沿用 Server 已經使用的資料庫，不另外建立 DB。** 這些機器人是透過正式加密 WebSocket 註冊、創角、登入的真實連線玩家，使用 AOI、Blocking.dat 與 A* 移動、怪物戰鬥。你可以用正常 Godot Client 同時登入觀察。

**注意資料持久化：** 每次執行會在現有資料庫留下 `wire-load-...` 測試帳號及 `WL...` 測試角色，並可能留下戰鬥獲得的獎勵／掉落。工具不會自動刪除帳號，也不會替你清理或覆蓋正常玩家資料。建議事先備份原本的資料庫。

## 1. 沿用現有 Server（不要更換 DB 設定）

**如果現有 Server 已運行，且程式已包含 `/gm testbot` 並啟用 `Game:TestBotCommandsEnabled=true`，就不用重啟或另開 Server，直接跳到第 2 步。**

如果看到 `未知 GM 指令：testbot`，請先更新**執行中的 Server** 來源並重新編譯，確認程序已停止再以原本的 Server 執行設定重新啟動。這個回覆可能是**舊版 Server 執行檔**，也可能是未啟用 `Game:TestBotCommandsEnabled`。新版本在設定未啟用時會更明確回覆 `testbot 未啟用`。

**已修正防護：** 工具現在先建立與驗證 **1 位**測試 Bot（包含 `/gm testbot`、傳送與技能初始化），確認成功後才開始其餘帳號。若 Server 不支援指令或關閉測試旗標，會印 `PRECHECK FAILED` 並停止，避免一次再建立 20 個帳號。第一個測試帳號仍可能留在原本資料庫，這是現有持久化流程的結果。

如果尚未啟用測試功能，請停止**現有的 Server** 後，依你原本使用的 VS2022 Debug / Release / cmd 啟動方式設定 `Game:TestBotCommandsEnabled=true` 並重新啟動**同一個 Server**。例如原本就在 PowerShell 中啟動，先設定：

```powershell
$env:Game__TestBotCommandsEnabled = "true"
# 接著執行你原本啟動 Server 的命令，維持原本 Debug/Release 和 DB 設定
```

**不要因為測試工具而把現有 Server 改成另一種 Build Configuration。** 如果 `Database:Sqlite:Path` 為空，Server 的預設 DB 位於其執行目錄 `Data/game.db`，因此 `bin/Debug/net8.0` 和 `bin/Release/net8.0` 可能各自是不同資料庫。保留你目前的 `Database:Provider`、`Database:Sqlite:Path` 與啟動方式。

**不需要新設** `Database__Sqlite__Path`，更不要指向 `bot-test.db`。如果你先前曾在同個 PowerShell 設過 `Database__Sqlite__Path=...bot-test.db`，先確認它是不是仍在生效；只移除該舊測試覆寫，不要移除你真正使用的 DB 配置。

本分支原本 `server/appsettings.json` 已有 `Game:GmCommandsEnabled=true`；如果你有用其他環境設定關閉 GM，還須依現有配置啟用它。`Game:TestBotCommandsEnabled` 是額外的**明確啟用旗標**，不會自動對一般帳號開放測試能力值：Server 會查證該帳號確實是工具建立的 `wire-load-*` 帳號、`WL*` 角色、`@example.invalid` 測試電子郵件後才允許 `/gm testbot`。

## 2. PowerShell B：20 人測試（無需提供座標）

```powershell
dotnet run -c Release --project .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj -- --bots --players 20 --map 13 --monsters 0 --interval-ms 350 --seconds 0 --output bots-result.json
```

啟動時，程式讀取 Map13 真實 `Enemy.dat` 與 `Blocking.dat`，**每個機器人獨立抽取地圖任一合法怪物出生區**，再在區域內隨機挑選可通行的起點。玩家不再全部集中於預設中心的 280px 內。怪物也會依機器人隨機分散召喚（使用該地圖的有效怪物種類）。不需要 `--x`、`--y`；若要強制集中於固定觀察區域，仍可手動輸入。機器人活動範圍涵蓋各練功區，尋怪不再有人為 600px 上限；**真正攻擊仍依正常 Server 攻擊距離及技能判定，絕不隔空打怪**。

預設 `--radius 280` 用於明確指定中心時的初始散布範圍（可改到 700），不限制全地圖練功區的出生位置。走路目的地另採用真人較遠距離的地板點擊（優先 220px 以上、最遠約 700px），沿途依 Blocking.dat／A* 合法逐步送出 SyncPosition；狹窄地形則退回合法的短目的地。**預設 `--monsters 0`：直接攻擊現有 Server 地圖上的正常怪物，不額外召喚任何怪物。** 如需要增加怪物數才手動指定 `--monsters 20`，它會對現有 Server 產生真實 GM 怪物。

**新增常走路角色**：預設約 25% Bot 是「經常走動型」（20 人約 5 位），角色在這次執行期間固定，不會每個 tick 隨機換角色。這些 Bot 約 80% 決策會點較遠地板，通常每 4～7.5 秒決定下一個目的地；約 20% 決策仍會追怪，實際出手後僅打約 2～4.2 秒就再做選擇。其餘 Bot 保持約 25% 點地板，多數打怪 3.5～8.5 秒、少部分會持續打 11～19 秒（模擬耐心打完較強的怪物）。所有 Bot 仍依 Blocking.dat／A* 合法逐步移動，不會瞬移。用 `--roamers 8` 可指定走動型人數；`--roamers 0` 關閉走動型分組（一般 Bot 原本的走路仍保留）。終端機可看 `frequent roamers`、`roamerMoves` 和 `roamerGroundClicks`，輸出 JSON 可查角色索引與各項計數。

## 3. 長時間戰鬥行為

- 每個測試角色 **Lv1、ATK=60、MATK=45、DEF=1000、HP=20000**（透過獨立測試 GM 指令建立），另有足夠 MP/SP。定期補血，方便觀察長時間多人攻擊。
- 主要使用正常 `AttackEntity` 普通攻擊（約 60 傷害）；單體技能對測試 Bot 的傷害上限為 90，避免固定高技能傷害秒殺怪物。並用同樣的 `SyncState` 讓觀察者看到機器人的攻擊動畫。偶爾使用單體技能 `20101` 赤色光彈、`23102` 烈火爆劍（Lv1）。
- **完全不施放範圍技能**，不使用 `23103` 赤星爆炸，也不使用 `99001` 星界震盪。
- **避免測試自己製造卡頓**：每隻 Bot 登入時一定初始化 Lv1、ATK=1、DEF=1000、HP=20000。單純走路（`--no-combat`）不再每 20 秒重設一次已經沒變的數值。戰鬥 Bot 在首次登入後約 55～102 秒才視需要重設，之後每 90 秒更新；20 人刷新時間錯開，避免密集 GM SQLite 寫入與 Session 鎖同時阻塞正常移動。
- 怪物存活狀態由 AOI `SpawnEntity`／`EntityDamage`／`EntityDeath`／`DespawnEntity` 更新；如果距離太遠會沿 Server 現有 A* 尋路靠近，沒怪時隨機走動。
- 模擬真人點擊：從 AOI 內選擇怪物（不固定最近，也避免一直選同隻）；點怪後追擊最多約 16 秒，**實際開始出手後多數連續攻擊 3.5～8.5 秒，約四分之一非走動型攻擊回合持續 11～19 秒**；遇到剩餘血量低於約 45% 的怪物，部分 Bot 會再多打 2.5～5.5 秒嘗試收尾。選怪時也會偶爾優先攻擊受傷怪物，之後換怪或改點較遠地板移動。每隻 Bot 有自己的動作與攻擊節奏，並在終端機輸出 `monsterClicks`／`groundClicks`／`switches`。
- 怪物死亡後由 Server 原有重生流程管理；**不會透過工具重建假的怪物**。目標死亡／離開視野後，Bot 下次出手前會有約 0.45～1.1 秒的人類反應延遲。
- 普攻 Server 沿用原本有效攻擊力公式（本次 Bot 為 ATK=60），不注入假擊殺或假 Miss；必須靠真正 `AttackEntity`／`UseSkill` 打死怪物。使用測試專用權限隔離，不改一般玩家傷害。

## 4. 看見玩家、怪物和掉落物

開啟 Godot Client，連到**同一個測試 Server**。機器人終端機會列出該次隨機中心座標，可使用測試 GM 傳送到該位置觀察。注意 Client 只接收自己 AOI 內的角色，並不是所有模擬玩家都一定在畫面內。

`bots-result.json` 包含：
`MovementPackets`、`FrequentRoamerCount`、`FrequentRoamerBotIndices`、`FrequentRoamerMovementPackets`、`FrequentRoamerGroundClicks`、`FrequentRoamerAttackPackets`、`MonsterClickDecisions`、`GroundClickDecisions`、`TargetSwitchDecisions`、`AttackPackets`、`SingleTargetSkillPackets`、`TestRecoveryPackets`、`ConfirmedDamageHits`、`ConfirmedSkillCasts`、`ConfirmedMonsterKills`（Bot 自己的擊殺數）、`ObservedMonsterDeaths`（AOI 死亡通知總數，可能重複計數）、`PositionCorrections` 與 `Errors`。發送封包數不等於成功傷害，必須看後兩個確認數字。

Ctrl+C 正常停止並列印統計。建議 10 → 20 → 50 → 100 人循序觀察。

## 5. 編譯與驗證

```powershell
dotnet build -c Release .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj
```

目前修改已提交 GitHub，但尚未在此工作環境完成 .NET 8 編譯、Godot 渲染及實際負載驗證，不宣稱壓測 PASS。

## 6. 如果看起來沒有打怪

最新 CLI 會逐步輸出部分玩家的 `websocket-connected / register-account / login / create-character / enter-world / bootstrap / world-ready` 等初始化進度，並於每一位玩家成功時印出 `ready #NNN`。某個帳號失敗時會立即印出 `SETUP FAILED #NNN` 與原因，不需要一直等待 10 人完成才知道哪一步失敗。

正式進入戰鬥後，每約 10 秒印出：

- `visibleTargets`：所有機器人收到的 AOI 活怪數總和。若是 0，先查 WorldReady/SpawnEntity 和怪物位置。
- `attack`：已送出的實際 AttackEntity 封包數；若為 0，查尋路與目標距離。
- `confirmedHits`：收到含機器人 AttackerID 的真正 EntityDamage 事件數；如果 `attack > 0` 但仍是 0，查 Server 攻擊判定。
- `confirmedKills`：收到 `EntityDeath.KillerID` 等於 Bot 角色 ID 的擊殺通知數，避免把旁觀其他玩家擊殺當成自己擊殺。
- `rejected`：Server 回傳距離太遠、目標不存在、技能尚未學會、冷卻等訊息累計次數。
- `NO CONFIRMED HITS`：首次機器人座標、最近怪物距離、最近一條 Server Flash，方便直接判斷。

只有在**原本 Server 尚未啟用** `Game:TestBotCommandsEnabled` 時，才需要停止並重新啟動**同一個 Server**，使設定生效；既有 DB 路徑保持不變。僅看到 `[BOTS] run=...` 不代表玩家已成功登入，直到 `[BOTS] online=...` 才代表進入主循環。
