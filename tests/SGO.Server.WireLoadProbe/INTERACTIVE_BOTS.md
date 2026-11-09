# SGO 多人機器人測試 — 弱攻擊、高血量、隨機合法座標

**僅供隔離的測試 Server／資料庫使用。** 此工具會建立多組新帳號，使用 Server 正式加密 WebSocket、AOI、Blocking.dat 與 A* 移動、怪物戰鬥，並讓正常 Godot 玩家同時登入觀察。

## 1. PowerShell A：測試專用 Server

在 TestGoGo 專案根目錄：

```powershell
$env:Database__Sqlite__Path = (Join-Path (Get-Location) "server/Data/bot-test.db")
$env:Game__GmCommandsEnabled = "true"
$env:Game__TestBotCommandsEnabled = "true"
dotnet run -c Release --project .\server\SGO.Server.csproj
```

`Game__TestBotCommandsEnabled` 是**另外明確啟用的**測試旗標，預設為 false。它只允許測試 Server 使用 `/gm testbot`，不修改正常角色成長、攻擊或碰撞計算。兩個 GM 旗標都必須設為 true；請勿在正式 Server 啟用。

## 2. PowerShell B：20 人測試（無需提供座標）

```powershell
dotnet run -c Release --project .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj -- --bots --players 20 --map 13 --interval-ms 350 --seconds 0 --output bots-result.json
```

啟動時，程式讀取 Map13 真實 `Enemy.dat` 與 `Blocking.dat`，**每個機器人獨立抽取地圖任一合法怪物出生區**，再在區域內隨機挑選可通行的起點。玩家不再全部集中於預設中心的 280px 內。怪物也會依機器人隨機分散召喚（使用該地圖的有效怪物種類）。不需要 `--x`、`--y`；若要強制集中於固定觀察區域，仍可手動輸入。機器人活動範圍涵蓋各練功區，尋怪不再有人為 600px 上限；**真正攻擊仍依正常 Server 攻擊距離及技能判定，絕不隔空打怪**。

預設 `--radius 280`，可自行設定 `--radius 500`（最多 700）；預設召喚怪物數隨玩家人數增加、最多 100 隻，亦可手動指定 `--monsters 20`。

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

啟動工具前須**重啟**測試 Server 讓兩個環境變數生效。僅看到 `[BOTS] run=...` 不代表玩家已成功登入，直到 `[BOTS] online=...` 才代表進入主循環。
