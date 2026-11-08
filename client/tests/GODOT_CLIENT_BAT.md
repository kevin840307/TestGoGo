# Godot Client 專用測試 BAT（Windows）

這個工具**只測 Client**，不會執行 Server Smoke Tests，也不會連線正式遊戲 Server。

## 第一次使用

在 `C:\SGO\Recovered` 目錄執行：

```bat
run_godot_client_tests.bat
```

第一次執行，會詢問 Godot EXE 完整路徑，預設採用你提供的位置：

```text
C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe
```

如果 EXE 存在，**直接按 Enter** 就會採用此路徑。你也可以拖曳別的 Godot EXE 到命令列視窗、再按 Enter。

選定後，路徑會儲存到使用者本機的：

```text
%LOCALAPPDATA%\SGO\GodotClientTests\godot_path.txt
```

**這是本機設定，不在 Git Repository 內**；下次雙擊自動讀取。如果 EXE 被移動，會重新要求設定。選單 `P` 可以隨時變更路徑；也可以執行 `run_godot_client_tests.bat configure`。

## 選單

| 選項 | 功能 |
|---|---|
| `1` | 先 Import，再執行四個 Client 回歸測試（登入／渲染／焦點／資源） |
| `2` | 重複渲染、AOI 怪物快取與小地圖刷新邏輯 |
| `3` | 失焦後的背景視覺處理、FPS 限流與記憶體快取策略 |
| `4` | 地面物品碰撞形狀、材質共用與怪物特效資源 |
| `5` | 只匯入 Godot 專案，重建匯入／class／UID 快取 |
| `6` | 登入流程測試：不等待 18 幀、影片失敗不阻止輸入 |
| `P` | 更改 Godot EXE 路徑並記錄 |
| `Q` | 離開 |

可用 CMD 非互動執行：

```bat
run_godot_client_tests.bat all
run_godot_client_tests.bat render
run_godot_client_tests.bat focus
run_godot_client_tests.bat resources
run_godot_client_tests.bat login
run_godot_client_tests.bat import
run_godot_client_tests.bat configure
```

## 避免先前的 Compile Error

之前使用 `godot --headless --script res://tests/multiclient_render_dedup_regression.gd`，會遇到 `Identifier not found: ItemLoader`、`NetworkManager`。這種獨立腳本啟動方式，不能正確代表遊戲專案正常執行時 AutoLoad 的載入流程。

因此本 BAT：

1. 先呼叫 Godot `--headless --path <client> --editor --import`。
2. 再用 `--scene res://tests/<name>.tscn` **以正常專案場景啟動**，讓 `project.godot` 的 AutoLoad 生效。
3. 優先使用同資料夾下的 `_console.exe`，否則用 `start /wait` 等待一般版 Godot 完成。
4. 每個測試都有 `--quit-after 1200` 安全影格上限，避免測試未啟動時無限等待。退出碼 0 以外、未印出自己的 `ALL PASS`、或出現 `SCRIPT ERROR` 都是失敗。

**注意**：這裡的「全部」只指上述四個目前支援場景模式的登入與效能測試，不是整個 `client/tests` 中所有 GDScript，更不是多個完整 Godot 遊戲視窗的 E2E 壓測。

## Log

每次執行都建立獨立資料夾：

```text
C:\SGO\Recovered\test-results\godot-client-yyyymmdd-hhmmss-xxxx\
    import.log
    login_visual_readiness_regression.log
    multiclient_render_dedup_regression.log
    focus_performance_regression.log
    render_resource_regression.log
```

失敗時請提供該次目錄下的 `import.log` 以及第一個 FAIL 的 `.log`。報告在專案的 `test-results/` 下，已被 Git 忽略。

## 驗證狀態

已對 BAT 的選單、EXE 路徑持久化、Import 與 Scene 啟動、錯誤／Log 判斷執行靜態回歸檢查，但此工作環境沒有 Windows 與 Godot 4.7.2，尚未完成實際 Windows Runtime 測試。以你實機的 BAT 顯示 `PASS` 為準。

## 登入與焦點 FPS 規則（2026-11）

登入畫面、角色選擇與首次世界載入都使用原本的前景 FPS 設定（例如 balanced 120 FPS），**即使失焦也不刻意降為 10／20 FPS**。只有角色進入遊戲世界、初始化已完成後，才在視窗失焦時停止非必要渲染並套用遊戲背景 FPS 設定；網路、計時器、自動打怪仍繼續運作。登入 UI 準備完成即顯示，影片解碼與連續影格不會阻擋登入。

## 83～84% 首次進圖黑屏修復驗證

更新 `feature/local-server-net8` 分支後，直接執行：

```bat
run_godot_client_tests.bat focus
```

此模式除了原本的前景／背景 FPS 測試，也會執行 `world_loading_render_regression.tscn`，驗證：

- `GameManager` 建立完地圖時，**不會直接停止渲染**；等 `EntryManager` 的 WorldReady、進度條、載入遮罩淡出完成後才恢復背景省電。
- 切換地圖時，等待 HUD 的地圖 Loading Overlay 真正隱藏後，才允許失焦 Client 關閉渲染迴圈。
- 進圖完成後仍保留 1.2 秒畫面恢復緩衝，且每 0.25 秒檢查一次焦點／渲染政策，避免漏掉 Windows Focus-In 通知後必須手動切出切回。
- Spawn Queue 等待有 12 秒上限；若一直有新實體進來，會顯示地圖、**保留佇列並繼續載入**，不會清空待生成的怪物／物品。

上述測試是 Godot 專案內的 **狀態與邊界回歸測試**；實際 83～84% 的黑屏仍須用 Godot F5、選角進圖、Alt+Tab、切圖情境人工驗證。若重現，請提供新一輪的 `godot(...).log`，特別是 `[WORLD_PRESENTED]`、`[FOCUS_POWER_SAVE]`、`[INITIAL_SPAWN_GATE_TIMEOUT]` 及 `[FRAME_GAP]`。

