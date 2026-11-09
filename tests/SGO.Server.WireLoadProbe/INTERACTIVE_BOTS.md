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

啟動時，程式讀取 Map13 真實 `Enemy.dat`，從怪物出生位置中**隨機選擇可走的練功區中心**，再於其附近 `Blocking.dat` 可通行位置隨機分配角色和測試怪物。每次執行的中心位置都可能不同。不需要再提供 `--x`、`--y`；若希望限定觀察區域，仍可選擇手動輸入兩個座標。

預設 `--radius 280`，可自行設定 `--radius 500`（最多 700）；預設召喚怪物數隨玩家人數增加、最多 100 隻，亦可手動指定 `--monsters 20`。

## 3. 長時間戰鬥行為

- 每個測試角色 **Lv1、ATK=1、MATK=1、DEF=1000、HP=20000**（透過獨立測試 GM 指令建立），另有足夠 MP/SP。定期補血，方便觀察長時間多人攻擊。
- 主要使用正常 `AttackEntity` 普通攻擊；偶爾使用單體技能 `20101` 赤色光彈、`23102` 烈火爆劍（Lv1）。
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
