# SGO：Windows 一鍵 Pull / Build / Test / AOI

使用分支：`feature/local-server-net8`，專案根目錄 `pull_build_test.bat`（雙擊）。需要 Windows 10／11、Git for Windows、.NET 8 SDK、Windows PowerShell 5.1。既有的完整 Smoke 測試包含 Python 靜態回歸檢查；如有相應 `.py` 檔而系統沒有 Python，測試會明確失敗、不會默默略過。

## 立即執行

1. 用檔案總管打開 `C:\SGO\Recovered`（或實際專案根目錄）。
2. **雙擊 `pull_build_test.bat`**。
3. 選擇模式：

| 選單 | 做什麼 | 是否啟動額外 Server |
|---|---|---|
| 1 | **Pull + Release Build + 全部 Server Smoke + Python 回歸** | 否 |
| 2 | 選項 1 + **100／250／500／1,000 虛擬移動 AOI 測試**（密集與分散） | 否 |
| 3 | 選項 2 + **100 個真實 WebSocket 玩家**（建立測試帳號、角色、移動、聊天與戰鬥） | 是，埠 5688 |
| 4 | 選項 2 + **100／250／500／1,000 個真實 WebSocket 玩家** | 是，埠 5688，每個規模獨立重啟 |
| 5 | 只做 Git Pull + Release Build（不宣稱測試通過） | 否 |
| Q | 退出 | 否 |

按 Enter 不會立刻跑大型壓測；模式 4 還需要手動輸入 `RUN-1000` 才會開始真實連線壓測。

也可在 cmd 執行：

```bat
pull_build_test.bat full
pull_build_test.bat aoi
pull_build_test.bat wire100
pull_build_test.bat wireall
pull_build_test.bat build
```

## 安全保護

- Git 僅允許在 **`feature/local-server-net8`** 分支執行。對已有提交檔案的任何未提交修改會直接停止；不自動切分支、不 stash、不 reset、不 force。更新使用 `git pull --ff-only origin feature/local-server-net8`。
- 只在程式碼 Pull／Build／Smoke 通過後才會繼續 AOI 壓測。
- Wire 模式會自動建立 `test-results\<時間>-<識別碼>\wire-100\game.db`，並把 `Database__Sqlite__Path` 指向它；絕不使用原本 `server\bin\...\Data\game.db`。
- 新建測試 Server 只綁 `127.0.0.1:5688`，不會停止正在使用 5678 埠的遊戲 Server；若 5688 被占用會報錯，不會任意關閉其他進程。
- **測試 Server** 才啟用 GM 指令，以便壓測建立怪物與設定位置；指令測試結束後會自動停止其進程，變數作用範圍只限新進程。
- 真實連線的 100／250／500／1,000 測試按順序執行；任何一個失敗就停止，不會偷偷越過失敗。
- `test-results/` 已加入 `.gitignore`；報告留存供除錯，腳本不刪 DB 或報告。

## 測試報告位置

每次啟動都在根目錄建立獨立資料夾：

```text
test-results/
  20261108-153000-a1b2c3/
    run.log               ← pull、build、smoke 與總結
    commit.txt            ← 實際測試的 Git Commit
    aoi-moving.json       ← 選單 2～4：AOI 100/250/500/1000
    wire-100/
      game.db             ← 獨立測試 SQLite
      server.stdout.log
      server.stderr.log
      wire.console.txt    ← 壓測 CLI 輸出及錯誤
      wire-aoi-100.json   ← 壓測指標與檢查結果
    wire-250/ ...         ← 選單 4 額外規模
```

若失敗，把 `run.log`、對應的 `wire.console.txt`、`wire-aoi-*.json` 分享回來即可。**不要分享正式帳號密碼或正式 DB。**

## 如何判斷 PASS

- 選單 1：Server 與三個測試專案 Release Build 成功；`SmokeTests --suite all` 完全通過；存在的 Python 回歸測試也 PASS。
- 選單 2：`aoi-moving.json` 必須有 8 筆結果（四種人數 × 密集／分散），每筆 `Success=true`。看 `WouldDeliverWithoutAoi`、`AoiMoveDeliveries`、`AvoidedDeliveryPercent`、`UpdateLatencyMs.P95/P99`、`CpuMs`。
- 選單 3／4：`wire-aoi-<人數>.json` 的 `Results[0].Success=true`、`SetupFailures=0`、`WorldAoi.FarSpawnLeaks=0`；看 `Movement.RoundLatencyMs.P95/P99` 與 `MixedSoak.Runtime` 的 CPU、WorkingSet／GC heap、AI tick。
- 只有成功通過以上各步，終端機才會顯示 **SUCCESS**，並以 exit code 0 結束。

## 壓測範圍與限制

AOI-only 模式實際更改虛擬玩家座標，但不模擬網路、DB、地形及 Godot 畫面。Wire 模式會以每次 ±4 遊戲單位送出真實移動封包，也檢查 `PositionCorrection`。當起點靠近不可走區域、實際地圖有問題時，壓測可能因位置被 Server 正確拒絕而失敗，這時要依 `server.stderr.log` 檢查座標與碰撞資料，而不是關閉 Server 移動檢查。

**1,000 個虛擬玩家不等於 1,000 個 Godot 用戶端實際繪製**，多開 GPU／RAM 需另外執行 `client/tests/profile_multiclient_windows.ps1`。在同一台電腦上進行真實 1,000 連線測試，壓測程式本身也會使用 CPU／RAM，若要量測 Server 真正極限，建議把負載端分到另一台電腦。

這些 Windows 指令已依專案目前參數撰寫並做程式碼靜態檢查，但工具環境沒有 Windows PowerShell 與 .NET 8，所以無法宣稱已在 Windows 實際跑通。
