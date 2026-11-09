# 多人 NPC 式測試機器人（真實 WebSocket 玩家）

此工具只增加在測試程式中，不修改 Server AOI、角色成長、戰鬥、技能計算或 Godot Client。**請使用獨立 SQLite 測試資料庫**；每次執行會建立新的帳號與角色。Server 要啟用 GM 指令。

## A. 開啟 Server

PowerShell 視窗 A，從專案根目錄執行：

```powershell
$env:Database__Sqlite__Path = (Join-Path (Get-Location) "server/Data/bot-test.db")
$env:Game__GmCommandsEnabled = "true"
dotnet run -c Release --project .\server\SGO.Server.csproj
```

> 這是一份獨立的測試 DB。測試後可直接刪除；切勿把這個設定指到正式資料庫。

## B. 開啟 20 人測試

PowerShell 視窗 B，從**同一個專案根目錄**執行：

```powershell
dotnet run -c Release --project .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj -- --bots --players 20 --map 13 --x 4331 --y 5346 --radius 280 --interval-ms 350 --seconds 0 --monsters 5 --level 150 --output bots-result.json
```

- `--players`：模擬人數 1～1000，各有獨立帳號／角色及加密 WebSocket。
- `--map --x --y`：觀察中心（地圖、像素座標）。若該位置被擋住，會在 `max(--radius, 256px)` 範圍內找出**真正距離最近**的可走格並印出修正座標；找不到則停止，不會跳過碰撞。已確認 Map13 `(4331,5346)` 的 Blocking flag=7、最近可走格為 `(4424,5528)`（約 204px），因此預設 `--radius 280` 可正常選擇可通行中心。
- `--radius 280`：每個玩家隨機落在**以中心為圓心**的可走位置。程式直接使用 Server 的 `Blocking.dat` 驗證，且必須可從觀察中心移動抵達；不會盲目傳送到阻擋格。
- `--level 150`：所有機器人先使用既有 GM level/maxstats/heal 提升 HP、防禦、能力值與補滿 HP/MP/SP，並定期補充資源，避免容易死掉。
- `--monsters 5`：用所選地圖 `Enemy.dat` 中的有效怪物 ID，在可通行位置召喚 5 隻；怪物死亡後由 Server 原有重生邏輯處理。
- `--interval-ms 350`：走路／AI 指令間隔。移動透過原本 `SyncPosition` 封包，路徑採用 Server 既有的 A* 服務，每一步驗證可走性。
- `--seconds 0`：一直跑到 Ctrl+C；`--seconds 300` 則五分鐘結束。
- `--no-combat`：只做亂走，停用攻擊、技能及召喚測試怪物。

## C. 戰鬥模式

機器人只會選擇**從 Server AOI SpawnEntity 收到的活怪**，收到 `EntityDamage` 更新血量、`EntityDeath/DespawnEntity` 就移除目標、`SpawnEntity` 重生後再重新選怪；沒有固定攻擊已死亡的怪物 ID。

尋到怪物後，若距離太遠會透過阻擋地圖 A* 接近；在攻擊距離內則輪流使用：

- 普通攻擊 `AttackEntity`：主要攻擊。
- `20101` 赤色光彈：簡單單體技能。
- `23102` 烈火爆劍：另一個單體技能。
- `23103` 赤星爆炸：偶爾使用的簡單小範圍技能（半徑 200）。

**不會使用 99001 星界震盪。** 使用正常 `UseSkill` 封包，因此技能冷卻、MP/SP 消耗、傷害和 AOI 廣播都經過 Server 原本的流程；不是假動畫。

## D. 開正常 Client 觀察

兩個終端機保持執行，正常登入 Godot Client，連到同一個測試 Server，前往 Map 13 的座標 (4331, 5346) 附近，觀察角色移動、靠近怪物、普攻、單體技能、小範圍技能、掉落與 AOI。測試結束按 Ctrl+C。

`bots-result.json` 包含：
- `MovementPackets`、`AttackPackets`、`SingleTargetSkillPackets`、`SmallAreaSkillPackets`：已發送封包量。
- `ConfirmedDamageHits`：真實收到自身 `EntityDamage` 的傷害事件數，不能用 AttackPackets 直接代替。
- `ConfirmedSkillCasts`：真實收到自身 `SkillCast` 的次數。
- `ObservedMonsterDeaths`：AOI 中觀測到的怪物死亡數。
- `PositionCorrections`：Server 拒絕走路並更正座標的次數。
- `Errors`：帳號創建、GM 命令、封包與移動發生的錯誤。

正常攻擊成功至少應觀測到 `ConfirmedDamageHits > 0` 且 `ConfirmedSkillCasts > 0`；如歸零，先檢查 `Errors`、遊戲內聊天的 GM 回覆、該地圖是否有敵人資料、`PositionCorrections`。

## E. 驗證狀態

已把實作更新於 GitHub 的 `feature/local-server-net8` 分支，**但本次環境沒有 .NET 8 SDK 執行環境，因此尚未完成實際編譯與連線驗證，不能宣稱功能 PASS**。可先執行：

```powershell
dotnet build -c Release .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj
```

如編譯錯誤，將錯誤輸出附上；如果編譯過關，請先用 `--players 10` 驗證再增加到 20、50、100。
