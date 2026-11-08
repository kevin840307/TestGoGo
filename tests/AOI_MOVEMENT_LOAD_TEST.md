# SGO：100～1,000 玩家同地圖移動壓力測試

分支：`feature/local-server-net8`。測試請用 **獨立資料庫、獨立 Server，絕對不要指向正式環境**；WireLoadProbe 會建立測試帳號／角色並呼叫 GM 指令。

## 1. AOI 計算壓測：真正移動，無須啟動 Server

從 `C:\SGO\Recovered`（或專案根目錄）開 PowerShell：

```powershell
dotnet run -c Release --project .\tests\SGO.Server.LoadProbe\SGO.Server.LoadProbe.csproj -- --sizes 100,250,500,1000 --aoi-only --aoi-steps 50 --output .\aoi-moving.json
```

這個模式 **不會開啟 WebSocket、不建立帳號、不寫 SQLite**，僅測量真實 `PlayerSpatialAoi` 程式的進／離 AOI、Move 目標集合與記憶體配置。

每個 N 都測兩種 **同一 MapId=15** 的合成佈局：

- `dense`：除兩個邊界測試玩家外，其餘 N-2 名都在 1,800 範圍內；模擬主城／活動場密集人潮，通常沒有太多 AOI 可省。
- `groups-of-20`：每 20 人一群、不同群組相隔很遠；模擬大型地圖分散練功，AOI 應大幅減少遠方投遞。

普通玩家每步確實移動 8 單位；另有兩個特別玩家反覆跨越 AOI 進入 1,800／離開 2,200 邊界，強制驗證 `Spawn/Despawn` 對稱性。

查看 `aoi-moving.json` → `AoiMovingPlayers`：

- `Players`, `Layout`, `StepsPerPlayer`, `Updates`。
- `WouldDeliverWithoutAoi` 與 `AoiMoveDeliveries`、`AvoidedDeliveryPercent`。
- `EnterNotifications`／`ExitNotifications`（AOI 切換不能遺漏）。
- `UpdateLatencyMs.P95`、`P99`、`Max`、`CpuMs`、`AllocatedBytes`、`UpdatesPerSecond`。
- `Success` 應為 `true`；邊界不一致、玩家沒移動或進出通知缺漏會直接拋出例外。

**限制**：上述虛擬座標不依賴實際地圖阻擋格；它驗證 AOI 計算效能，**不代表**真實 1,000 個 WebSocket、SQLite 或 Client 也一定能承受。

## 2. 真實連線壓測：100～1,000 個 WebSocket，且座標實際改變

在**獨立測試資料庫**啟動 Server；第一個 PowerShell 視窗：

```powershell
cd C:\SGO\Recovered
# 一定使用新的測試 DB，不共用現有正式遊戲資料。
$env:Database__Sqlite__Path = (Join-Path (Get-Location) ("server\Data\aoi-load-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".db"))
$env:Game__GmCommandsEnabled = "true"
dotnet run -c Release --project .\server\SGO.Server.csproj
```

確認 `http://127.0.0.1:5678/health` 有正常回應。

第二個 PowerShell 視窗，先測 100：

```powershell
dotnet run -c Release --project .\tests\SGO.Server.WireLoadProbe\SGO.Server.WireLoadProbe.csproj -- --url ws://127.0.0.1:5678/ --sizes 100 --rounds 10 --move-pixels 4 --setup-concurrency 10 --mixed-seconds 30 --timeout-seconds 60 --output .\wire-aoi-100.json
```

100 PASS 後循序把 `--sizes 100` 改成 `250`、`500`、`1000`；每個規模使用新的輸出檔。

WireLoadProbe 會註冊／登入／創角／選角／WorldReady，測 `SyncPosition -> MoveEntity`、附近聊天、戰鬥競爭、世界近／遠 AOI 出現／重新進入，以及 30 秒以上的移動＋攻擊＋聊天混合負載。以 `--move-pixels 4` 送出正負 4 單位的座標變動；不只重送原本位置。`PositionCorrection` 代表移動被 Server 地圖驗證拒絕，工具會回報失敗，**不能把拒絕移動當 PASS**。

混合測試以約 250ms 每輪連續發送玩家位置（近 4 次/秒）。`--rounds` 階段會刻意隔開 120ms，接近 Server 每 100ms 的移動廣播限速。若想測更久，可將 `--mixed-seconds 30` 改成 `300` 或 `600`。

## 3. 結果判讀

打開 `wire-aoi-100.json`，檢查 `Results`：

| 欄位 | 期待 |
|---|---|
| `Success` | true |
| `SetupSuccesses` | 等於 Players |
| `SetupFailures` | 0 |
| `Movement.ExpectedDeliveries` | 等於 ActualDeliveries |
| `Movement.RoundLatencyMs.P95/P99` | 相較前一個測試規模是否暴增 |
| `WorldAoi.FarSpawnLeaks` | **0** |
| `WorldAoi.ReentrySpawnReceived` | true |
| `MixedSoak.Runtime.MaxAoiReadyPlayers` | 至少等於 Players |
| `MixedSoak.Runtime.CpuPercentOfMachine` | 按機器核心數正規化的 CPU 使用觀測值 |
| `MixedSoak.Runtime.MaxWorkingSetBytes` | 觀察 RAM 是否隨人數飆升 |
| `MixedSoak.Runtime.AiTickElapsedMs.P95/P99` | Server AI tick 是否出現長尾 |
| `Errors`、`MixedSoak.Errors` | 空陣列 |

記得另開工作管理員監測 Server 的 CPU、Private Bytes、RAM、網路流量。單一壓測機器也會消耗 CPU／網路：若 Server 和壓測工具同機，1000 人的瓶頸可能是壓測工具本身，可改分機量測。

## 4. 驗收範圍與注意事項

1. **現有 WireLoadProbe 所有玩家會在同一張圖 WorldReady**，但以小幅位移測的是固定局部位置，不涵蓋大量不同玩家連續穿越所有 AOI 邊界。
2. 進／離半徑的正確性由 in-process 移動壓測與 `PlayerSpatialAoiSmokeTests` 檢查；需額外搭配多角色人工測試切圖、瞬移、跨牆、怪物及地面物 AOI。
3. 1000 人都擠在相同 AOI 範圍時，即使正確實作 AOI，仍可能有近百萬個接收關係；這是**最壞負載**，不應期待分散人群才有的 98% 傳輸節省。
4. 1000 個虛擬連線**不會繪製 1000 個 Godot 畫面**；Client GPU/RAM 10 開要用另一份 `client/tests/MULTICLIENT_PERFORMANCE.md` 測。
5. 不要使用玩家既有 DB。若要可重現比較，先記錄 CPU 型號、核心數、Server build commit、Windows 資源、資料庫初始大小、網路環境，然後保持參數一致測優化前後。
6. 本文件與新增程式尚需在具備 .NET 8 SDK 的機器執行後才可宣稱 PASS。
