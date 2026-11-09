# 多人機器人互動測試（真實 WebSocket）

在 **TestGoGo / feature/local-server-net8** 的專案根目錄執行。這是可與正常 Godot Client 同時連進同一台**本機測試 Server** 的長時間測試，不是原本跑完即退出的 WireLoadProbe。

## 1. 在 PowerShell 視窗 A 啟動獨立 Server

請使用測試資料庫，**不可指向平常遊玩的 SQLite**，因為會新增許多帳號、角色及怪物。

```powershell
$env:Database__Sqlite__Path = (Join-Path (Get-Location) "server/Data/bot-test.db")
$env:Game__GmCommandsEnabled = "true"
dotnet run -c Release --project .\server\SGO.Server.csproj
```

## 2. 在 PowerShell 視窗 B 啟動機器人

```powershell
dotnet run -c Release --project .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj -- --bots --players 20 --map 13 --x 4331 --y 5346 --interval-ms 350 --seconds 0 --monsters 5 --output bots-result.json
```

- `--players 20`：同時登入 20 個**獨立註冊的真實帳號／角色**，可改 10、50、100，最大允許 1000。
- `--map 13 --x 4331 --y 5346`：把機器人聚集在尼西亞台地的觀測位置（靠 GM 傳送，請依該地圖可走位置調整）。
- `--interval-ms 350`：每個機器人約每 350 毫秒發送一次位置更新；沿 X 方向以 4px 步長往返最多 40px。
- `--monsters 5`：建立 5 隻敵人 ID=245；機器人交錯傳送真實 `AttackEntity`。
- `--seconds 0`：不限制時間；按 Ctrl+C 停止並寫出摘要。也可以用 `--seconds 300` 跑五分鐘。
- `--no-combat`：只走路，不生成怪物／攻擊；這時也可用 `--monsters 0`。
- `--setup-concurrency 5`：同時進行的帳號建立數量，避免瞬間過載。
- `--output bots-result.json`：保存送出位置／攻擊封包數、接收 bytes、PositionCorrection 及錯誤統計。

機器人使用與原測試相同的 `ClientProtocol` AES/WebSocket 加密、註冊、登入、選角與 `WorldReady`。不啟動 Godot 視窗；所有人都由同一個 .NET 測試程序維持連線。

## 3. 在正常遊戲 Client 觀察

保留以上兩個終端機，正常啟動 Godot 遊戲，連到**同一個測試 Server**，登入並傳送到 Map 13 的相近座標，觀察附近的機器人是否出現、持續移動、攻擊及離開 AOI。可先 10 人，再 50、100 人。按 Ctrl+C 停止測試後，觀察其他玩家是否正常從畫面消失。

注意：這是**封包級模擬角色**，不是完整 Godot 自動尋路或自動打怪 AI。移動路線是附近的 4px 小步往返；如果前方是阻擋格，Server 會回 `PositionCorrection`，摘要會計數。攻擊的是啟動時生成的敵人；敵人死亡後不會由此工具自動重新生成，需手動重啟一輪測試。也不會模擬技能、撿物、裝備或完整的 Client 畫面效能。

## 4. 測試環境與狀態

本模式僅允許 `ws://127.0.0.1`、本機 loopback 或 `.internal` 測試主機，以降低誤連正式環境的風險。需要 Server 啟用測試用 GM 指令。執行結束時自動關閉模擬 WebSocket 連線。

程式碼已提交到 TestGoGo 測試分支；**尚未在有 .NET 8 SDK、可啟動完整 SGO Server 的環境實際編譯和做端對端驗證**，因此不宣稱已通過壓力測試。請確認 .NET 8 SDK 已安裝，若有 build 或執行錯誤，附上終端機輸出。
