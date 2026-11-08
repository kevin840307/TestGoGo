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


## Windows CPU／RAM／GPU 實機效能基準測試

原本選單 1～6 保持不變。新增 7～9 使用**正常 Godot 遊戲視窗**，並非 `--headless`；執行檔路徑沿用先前記住的 `%LOCALAPPDATA%\SGO\GodotClientTests\godot_path.txt`。

| 選項 | 功能 |
|---|---|
| **7** | 單開 A/B：Balanced／Low／Multibox（怪物簡化 On），以及 Balanced（簡化 Off） |
| **8** | 多開效能：選擇 1、5、10 開並測量硬體資源 |
| **9** | 合併歷次測試，產生離線 HTML 與 CSV，不需要啟動 Godot |

命令列：

```bat
run_godot_client_tests.bat perf-single
run_godot_client_tests.bat perf-multi
run_godot_client_tests.bat perf-report
```

### 單開測試（選項 7）

1. 選 **L** 啟動正常 Godot 遊戲，或選 **A** 附加符合設定 EXE 路徑的既有遊戲視窗。**L 才會產生完整 Godot Log；A 只能保證 Windows 硬體數據。**
2. 登入固定地圖與位置，按提示在遊戲內設定 Balanced／Low／Multibox 與怪物簡化 On／Off。工具**不會自動替你切換遊戲設定**。
3. 回 CMD 按 Enter，立刻切回 Godot，利用 **10 秒暖機**使視窗回到前景；再記錄 **60 秒**。測試時保持相同地圖、解析度與怪物數，勿切圖或 Alt+Tab。
4. 依序測試 `balanced-on`、`low-on`、`multibox-on`、`balanced-off`；每個階段需手動切換設定。可在提示處輸入 Q 提早結束。
5. 工具完成後詢問是否關閉本次**由工具啟動**的遊戲視窗，預設不關閉；絕不終止附加的既有 Client。
6. 使用 Python 3 自動產生並打開離線 HTML；若沒有 Python，原始數據仍保留。

**注意：** 回 CMD 按 Enter 會讓遊戲失焦；一定要在暖機期間切回遊戲，不然會量到背景 5／10／20 FPS，而非預期的前景效能。

### 多開（選項 8）

選擇同時測量的 **1／5／10** 開。L 模式會新開這些遊戲視窗；5 或 10 開需明確輸入 `LAUNCH-5` 或 `LAUNCH-10` 才會執行，避免誤開。A 模式只附加符合 Godot EXE 路徑的現有視窗，不關閉它們；已開啟程序通常無法補建此測試的 Godot FPS／Draw Calls Log。

請先使用 1 開確認資源，再測 5 開，最後評估是否有足夠 VRAM 測 10 開。Server 若不允許同一帳號多重登入，請使用不同的測試帳號。每個視窗使用相同地圖與效能設定；只有一個視窗能獲得前景焦點，其餘背景狀態是多開情境的一部分。

每次選項 8 量測一組視窗數，並詢問這組測試的模式標籤（例如 `multibox-on`、`balanced-off`）；之後選項 9 把 1／5／10 開的結果合併比較。**標籤是人工指定**，報告會另外顯示 Godot Log 中實際觀測的模式與怪物簡化狀態，若不一致會發出警告。

### 測試輸出

```text
C:\SGO\Recovered\test-results\performance\
    report.html                  # 歷次比較圖
    summary.csv                  # 模式統計表
    yyyymmdd-hhmmss-xxxxxx\
        hardware.csv             # CPU/RAM/GPU/VRAM × PID × 秒
        godot_events.csv         # FPS/DrawCalls/Nodes/怪物數（L 模式）
        session.json             # 視窗數、模式名稱、計數器支援情形
        godot-client-01.log      # 新啟動 Godot 才會有
        godot-client-02.log
```

每次測試建立獨立資料夾，不會覆蓋前次數據；`test-results/` 已被 Git 忽略。如果要我分析，直接把整個 `test-results\performance` 壓成 ZIP 上傳。

### 指標與限制

| 指標 | 計算方式 |
|---|---|
| CPU % | Windows `Get-Process` 的累積 CPU 時間差，除以經過時間與邏輯處理器數；多開時加總 |
| RAM Working Set | `WorkingSet64`，可能重複計入共用頁 |
| RAM Private | `PrivateMemorySize64`，各 Client 私有記憶體 |
| GPU 3D % | Windows WDDM `GPUPerformanceCounters_GPUEngine`，依 PID／3D Engine 取值，多開時加總 |
| GPU 專用 VRAM | Windows WDDM `GPUPerformanceCounters_GPUProcessMemory`，DedicatedUsage，各程序加總 |
| FPS、Draw Calls、Canvas Nodes、怪物數 | Godot `RENDER_STATS`，限有 Log、前景且有效的快照 |
| Godot Process ms | `PERF_ANALYSIS` 的每幀耗時快照，不等於 Windows CPU % |

**GPU 3D 合計不是整張顯卡的 GPU 使用率；程序 VRAM 合計也不是實體顯卡的總使用量。** Windows 顯示驅動與 WDDM 不支援計數器時會顯示空值或 —，不會猜測或填 0。Godot FPS 取樣並非逐幀紀錄，不能用它推導可靠的 1% Low／P99 Frame Time。

報表使用 **Python 3 標準函式庫**，不需要 pandas、matplotlib。若無 Python 3，保留 CSV，之後再執行選項 9 即可。

### 回歸測試

```bat
python -m unittest tests/test_client_perf_report.py tests/test_godot_client_bat_contract.py -v
```

已新增報表的中位數／每秒 PID 加總、空白 GPU、有效前景事件、離線 HTML 生成等單元測試。實際 Windows Godot／WDDM 指標仍需在使用者電腦上實測。

## 怪物回屏 default 動畫與技能視窗 Parse error 回歸

如果 Godot 顯示：

- `monster_entity.gd @ _on_screen_entered(): There is no animation with name 'default'`
- `Failed to load script "res://scripts/skill_window.gd" with error "Parse error"`

請先 `git pull --ff-only origin feature/local-server-net8`，再執行：

```bat
run_godot_client_tests.bat resources
```

此回歸測試會 **preload 真實的技能視窗與技能卡腳本**，驗證其可被 Godot 編譯與建立；也會在沒有 `default` 的怪物 `SpriteFrames` 上測試離屏暫停、回屏恢復 `IDLE_1`、簡化怪物無圖幀，以及缺少目標指示器圖幀的情況。測試最後應印出 `[RENDER_RESOURCE_REGRESSION] ALL PASS`。

修正不會改動怪物的 Server 狀態、AOI 邏輯、攻擊方向、碰撞或技能資料；只防止沒有可用動畫時呼叫 Godot 的 `AnimatedSprite2D.play()`。

如果技能視窗仍有 Parse error，請提供最先出現的 **`SCRIPT ERROR: Parse Error: ...` 與 `at: GDScript::reload (res://scripts/skill_window.gd:行號)`**，因為 `Failed to load script` 本身只是後續總結訊息，不包含實際錯誤行號。
