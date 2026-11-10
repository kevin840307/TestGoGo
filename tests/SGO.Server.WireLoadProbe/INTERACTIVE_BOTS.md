# SGO 多人機器人測試 — 弱攻擊、高血量、隨機合法座標

**直接連線現有 SGO Server，沿用 Server 已經使用的資料庫，不另外建立 DB。** 這些機器人是透過正式加密 WebSocket 註冊、創角、登入的真實連線玩家，使用 AOI、Blocking.dat 與 A* 移動、怪物戰鬥。你可以用正常 Godot Client 同時登入觀察。

**注意資料持久化：** 每次執行會在現有資料庫留下 `wire-load-...` 測試帳號及 `WL...` 測試角色，並可能留下戰鬥獲得的獎勵／掉落。工具不會自動刪除帳號，也不會替你清理或覆蓋正常玩家資料。建議事先備份原本的資料庫。

## 1. 沿用現有 Server（不要更換 DB 設定）

**如果現有 Server 已運行，且 `Game:TestBotCommandsEnabled=true`，就不用重啟或另開 Server，直接跳到第 2 步。**

如果尚未啟用測試功能，請在原本啟動 Server 的 PowerShell 視窗中加上：

```powershell
$env:Game__TestBotCommandsEnabled = "true"
dotnet run -c Release --project .\server\SGO.Server.csproj
```

上面只是**沿用原 Server 啟動命令**的示例。請在現有 Server 停止後，以原本的方式重新啟動一次；保留你現有的 `Database:Provider`、`Database:Sqlite:Path` 等配置，**不需要設定** `Database__Sqlite__Path`，更不要指向 `bot-test.db`。

本分支原本 `server/appsettings.json` 已有 `Game:GmCommandsEnabled=true`；如果你有用其他環境設定關閉 GM，還須依現有配置啟用它。`Game:TestBotCommandsEnabled` 是額外的**明確啟用旗標**，不會自動對一般帳號開放測試能力值：Server 會查證該帳號確實是工具建立的 `wire-load-*` 帳號、`WL*` 角色、`@example.invalid` 測試電子郵件後才允許 `/gm testbot`。

## 2. PowerShell B：20 人測試（無需提供座標）

```powershell
dotnet run -c Release --project .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj -- --bots --players 20 --map 13 --monsters 0 --interval-ms 350 --seconds 0 --output bots-result.json
```

啟動時，程式讀取 Map13 真實 `Enemy.dat` 與 `Blocking.dat`，**每個機器人獨立抽取地圖任一合法怪物出生區**，再在區域內隨機挑選可通行的起點。玩家不再全部集中於預設中心的 280px 內。怪物也會依機器人隨機分散召喚（使用該地圖的有效怪物種類）。不需要 `--x`、`--y`；若要強制集中於固定觀察區域，仍可手動輸入。機器人活動範圍涵蓋各練功區，尋怪不再有人為 600px 上限；**真正攻擊仍依正常 Server 攻擊距離及技能判定，絕不隔空打怪**。

預設 `--radius 280` 僅作為各角色隨機巡邏目的地的範圍（可改到 700），不限制全地圖練功區的出生位置。**預設 `--monsters 0`：直接攻擊現有 Server 地圖上的正常怪物，不額外召喚任何怪物。** 如需要增加怪物數才手動指定 `--monsters 20`，它會對現有 Server 產生真實 GM 怪物。

## 3. 長時間戰鬥行為

- 每個測試角色 **Lv1、ATK=1、MATK=1、DEF=1000、HP=20000**（透過獨立測試 GM 指令建立），另有足夠 MP/SP。定期補血，方便觀察長時間多人攻擊。
- 主要使用正常 `AttackEntity` 普通攻擊；並用同樣的 `SyncState` 讓觀察者看到機器人的攻擊動畫。偶爾使用單體技能 `20101` 赤色光彈、`23102` 烈火爆劍（Lv1）。
- **完全不施放範圍技能**，不使用 `23103` 赤星爆炸，也不使用 `99001` 星界震盪。
- 怪物存活狀態由 AOI `SpawnEntity`／`EntityDamage`／`EntityDeath`／`DespawnEntity` 更新；如果距離太遠會沿 Server 現有 A* 尋路靠近，沒怪時隨機走動。
- 怪物死亡後由 Server 原有重生流程管理；**不會透過工具重建假的怪物**。
- 普攻 Server 目前會造成至少 1 點傷害，沒有另外強制製造 Miss 的模擬；這個設定用低傷害換取更多次真實打擊。

## 4. 看見玩家、怪物和掉落物

開啟 Godot Client，連到**同一個測試 Server**。機器人終端機會列出該次隨機中心座標，可使用測試 GM 傳送到該位置觀察。注意 Client 只接收自己 AOI 內的角色，並不是所有模擬玩家都一定在畫面內。

`bots-result.json` 包含：
`MovementPackets`、`AttackPackets`、`SingleTargetSkillPackets`、`TestRecoveryPackets`、`ConfirmedDamageHits`、`ConfirmedSkillCasts`、`ObservedMonsterDeaths`、`PositionCorrections` 與 `Errors`。發送封包數不等於成功傷害，必須看後兩個確認數字。

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
- `rejected`：Server 回傳距離太遠、目標不存在、技能尚未學會、冷卻等訊息累計次數。
- `NO CONFIRMED HITS`：首次機器人座標、最近怪物距離、最近一條 Server Flash，方便直接判斷。

只有在**原本 Server 尚未啟用** `Game:TestBotCommandsEnabled` 時，才需要停止並重新啟動**同一個 Server**，使設定生效；既有 DB 路徑保持不變。僅看到 `[BOTS] run=...` 不代表玩家已成功登入，直到 `[BOTS] online=...` 才代表進入主循環。
