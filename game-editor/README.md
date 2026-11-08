# SGO Game Editor

獨立於遊戲 Client / Server 的本機 Web 編輯器。預設只監聽 `127.0.0.1:8765`，**不掛進遊戲 Server，也不會增加遊戲 runtime CPU / RAM 負擔**。

## 能做什麼

- **技能 CRUD**：以常用數值優先的表單編輯 `client/Skills/*.tres`；進階類型、特效與屬性加成預設收合，每級可直接複製上一級再微調。
- **怪物 CRUD**：以名稱與圖片預覽為主，編輯 `Old/服務端/Dat/Enemy/Enemy_Data.csv` 常用欄位；新增怪物可直接從「怪物名稱」選擇相近怪物 Clone，不必背 ID。
- **視覺化地圖怪物編輯**：搜尋怪物名稱後，點座標畫布放置、拖曳移動、點標記編輯 / 複製 / 刪除。支援 Shift 框選、多選群組拖曳、Ctrl+C/V、Ctrl+Z/Y、Delete、Ctrl+A、名稱顯示切換、只顯示目前怪物種類、縮放 / Fit / Space 或中鍵平移，以及一次 2~100 隻的合法區域批量散佈。畫布直接使用 `client/maps/map_bounds.json` 的遊戲範圍，疊加 `Blocking.dat` 碰撞層（紅＝不可走、黃＝特殊地形）與 `MapSpawnConfig.json` 怪物區域參考；放到阻擋格時預設自動吸附到最近可走格。底層仍安全寫入 `MapXX/Enemy.dat`，既有記錄保留原本 207-byte payload。
- **視覺化地圖 NPC 編輯**：點畫布新增 NPC、拖曳改位置，右側直接改名稱 / Script / Type；同樣支援框選、多選、群組拖曳、複製貼上、Undo/Redo、縮放/平移與 Blocking 自動吸附，仍保留進階原始清單。
- **NPC 腳本編輯器**：管理 `Old/服務端/Dat/Quest/*.txt`，支援搜尋、function outline、常用 Say / Ask / Function 範本、`#include` 檢查、callback 與 block/括號/字串基本驗證。
- **素材管理**：瀏覽 `client/Assets`、圖片預覽、按鈕選檔替換，也可直接拖檔案到素材卡片上替換。
- **安全寫入**：所有修改先備份到 `game-editor/.backups/`，再使用 atomic replace；所有可輸入路徑都有 traversal guard。
- **Project Health**：Dashboard 會掃描跨檔案引用與資料完整性，包含不存在的 Enemy、NPC Script、Quest 驗證錯誤、重複出生點、Blocking 衝突、技能 TRES 與 Skill FX / Icon 參考。
- **儲存前驗證**：地圖儲存前先顯示新增 / 刪除 / 移動 / 修改摘要。新產生的壞引用或 Blocking 衝突會阻止寫入；既有歷史問題只警告，避免舊資料讓整張地圖無法編輯。
- **修復預覽**：Blocking 衝突會顯示原座標與最近合法座標，可一鍵套用到 Editor 草稿，確認後再另行儲存；不會偷偷直接改檔。
- **變更歷史 / 還原**：直接瀏覽 `.backups` 內的 Editor 自動備份；文字檔顯示 unified diff，二進位檔顯示大小與 SHA256。還原前會再備份目前版本，避免「還原」本身成為不可逆操作。
- **Health 篩選**：Project Health 支援關鍵字、Error/Warning、category 篩選，資料很多時不必人工捲整份清單。
- **首次使用導引**：第一次開啟會顯示精簡流程與常用快捷鍵；之後可從右上角「? 使用說明」隨時叫出。
- **安全偏好記憶**：記住上次地圖、怪物/NPC tab、目前怪物種類，以及背景 / Blocking / Spawn Zone / 名稱 / 吸附等純顯示偏好；刻意不儲存 `mapRows`、Undo/Redo、Clipboard 或 dirty 草稿，避免瀏覽器快取成為另一份資料來源。
- **真實地圖背景**：可由 Godot 直接載入實際 `MapXX.scn`，依與 Editor 相同的 `map_bounds.json + 16px cell` 契約輸出 PNG；Editor 有圖就自動疊上，沒有圖仍安全退回 Blocking / 座標畫布。

> NPC 腳本的即時檢查是 Editor 端的快速靜態檢查，用來提早抓常見錯誤；Server 執行時仍以 `QuestRuntime + MoonSharp` 為最終語意。

## 啟動

Windows 可直接執行：

```bat
cd C:\SGO\Recovered\game-editor
start_editor.bat
```

或：

```bash
python game-editor/editor.py --project-root .
```

瀏覽器開啟：`http://127.0.0.1:8765`

Editor 會自動從 repository root 找：

- `client/Skills`
- `client/Assets`
- `Old/服務端/Dat/Enemy/Enemy_Data.csv`
- `Old/服務端/Dat/Map/MapXX`
- `Old/服務端/Dat/Quest`

## 測試

一般 repository / contract / JavaScript syntax 測試不需要 pytest 或額外 pip package：

```bat
cd game-editor
run_tests.bat
```

或：

```bash
python -m unittest discover -s game-editor/tests -v
```

真正的 Chromium E2E：

```bat
cd game-editor
run_e2e.bat
```

E2E 會建立獨立 temp SGO 專案並啟動真正的 `editor.py`，再由 Chromium 實際操作「進地圖 → 新增怪物 → 拖曳 → Undo / Redo → 儲存 → Reload → 確認座標仍一致」，不會修改 repository 的真實 `Enemy.dat / NPC.dat`。

測試涵蓋：

- 安全路徑與 traversal 防護
- Skill `.tres` CRUD / 未知欄位保留
- Enemy CP950 CSV CRUD / 未編輯欄位保留
- `Enemy.dat` round-trip / 207-byte payload 保留
- `NPC.dat` round-trip / reserved byte 保留
- Quest include / callback / block 驗證
- CP950 Quest script 儲存
- 素材替換與備份路徑安全
- 視覺化地圖編輯器的放置 / 拖曳 / 複製操作是否有正確掛載
- `map_bounds.json` / `Blocking.dat` 解析、阻擋層 PNG、不可走格最近可走位置計算
- Blocking cache 來源檔變更刷新、batch snap、5000 點 request guard
- 地圖 Undo/Redo、多選、框選、複製貼上、縮放、批量放置、獨立 `app-map-editor.js` wiring
- Client / Server / Editor 三邊 16px grid contract、真實 MapXX 座標、Map13 byte-identical round-trip
- 瀏覽器 world↔pixel / world→cell / cell-center 數學測試
- NPC 範本、素材拖放、技能常用/進階欄位 UI 是否有正確掛載
- Project Health 跨檔案引用、歷史問題 vs 新錯誤的 severity、刪除引用影響
- Skill FX runtime fallback / Icon 素材驗證
- Save 前 Change Summary、Blocking 修復預覽與一鍵套用 wiring
- Map 怪物/NPC Tab 必須使用 query-all 綁定，防止單一 selector regression
- Backup history list / search / text diff / binary hash / deleted-file restore
- Restore 前自動備份目前版本，以及 unmanaged path / traversal 防護
- History UI/API wiring、Health 搜尋 / severity / category filter wiring
- 首次使用 Help / 快捷鍵 UI、地圖 preview layer 與 preview endpoint
- Godot MapXX.scn preview exporter 與 Editor bounds 契約一致性
- Playwright Chromium 真實瀏覽器流程：新增 / 拖曳 / Undo / Redo / Save / Reload
- Map view preference reload persistence，並驗證 localStorage 不含 mapRows / Undo / Redo / Clipboard / dirty 草稿

## 變更歷史與安全還原

側邊欄的「變更歷史」會列出 Editor 自動建立的修改前版本，可依檔案或備份原因搜尋。

- 文字資料（Quest、Skill TRES、CSV、JSON 等）：顯示 backup → current 的 unified diff。
- 二進位資料（例如 `Enemy.dat / NPC.dat`）：不假裝做文字 Diff，改顯示檔案大小與 SHA256。
- 目前檔案已被刪除時，也可以由備份重新建立。
- 還原前會自動把目前版本另存成 `restore` backup，因此可以再次回復。
- Restore API 只允許 Editor 管理範圍：`client/Skills`、`client/Assets`、Legacy Map / Quest 與 `Enemy_Data.csv`；不能拿 backup id 覆寫 Server 設定或任意專案檔案。
- Backup id 一樣經過 traversal guard，`..`、絕對路徑與 symlink escape 都不允許。

## Project Health 與引用保護

Dashboard 的健康檢查會主動找出：

- Map 使用不存在的 Enemy ID
- NPC 使用不存在的 Quest Script
- Quest 靜態驗證錯誤 / 警告
- 完全重複的 Enemy / NPC 出生點
- 超出 Blocking grid 或位在不可走格的出生點
- 無效的 `Blocking.dat`
- Skill TRES 無法解析或 ID 與檔名不一致
- Skill `fx_use / fx_hit / fx_extra` 按 Client 真正 EffectLoader fallback 規則找不到素材
- Skill Icon 目錄存在時，檢查 `ICON_%03d.png`；若整個 Icon 目錄尚未 recovered，只產生一筆專案級 Warning，不製造幾百筆假警告

刪除 Enemy 或 NPC Script 前，Editor 會先列出 Map / `#include` 的引用影響。後端也有 reference guard：仍被引用的資料不能被普通 DELETE API 直接移除，只有 UI 已向使用者明確確認後才會送出 `force=1`。Health issue 的「前往」可以直接跳到對應 Map、NPC Script 或 Skill。

## 座標與遊戲格契約

Editor 不自己發明一套座標規則，而是與 Client / Server 共用同一個 Blocking grid 契約：

- Cell Size：`16`
- 世界座標轉格：`floor(world / 16)`
- Blocking mask：`0x07`
- 特殊地形 flag：`3`
- 格中心：`cell * 16 + 8`

**既有 Enemy/NPC 的 X/Y 不會被強制改成格中心。** 遊戲本來就允許任意世界座標，再用 `floor(x/16), floor(y/16)` 找所在格。只有當 Editor 新增 / 拖曳到不可走格、且「吸附可走區」開啟時，才會把位置修正到最近合法格中心，避免把原始位置無謂量化造成偏移。

CI 會額外跑：

- Client `legacy_pathfinder.gd`、Server `BlockingMapService.cs`、Editor constants / formula 契約比對
- 所有實際 `MapXX/Blocking.dat` header 與 `map_bounds.json` 世界範圍檢查
- 所有可解析的實際 `Enemy.dat / NPC.dat` 座標 → Blocking cell / flags 一致性檢查
- 真實 Blocking blocked cell → Editor snap → `16n+8` 合法格中心檢查
- Map13 真實 `Enemy.dat / NPC.dat` 複製到 temp 後，無修改 round-trip 必須 byte-identical
- 瀏覽器畫布 `world ↔ pixel` round-trip、world→cell、cell center 與批量散佈數學測試

## 地圖編輯快捷操作

- `Shift + 拖曳空白處`：框選多個怪物 / NPC
- `Shift / Ctrl + 點標記`：加入或移除多選
- `Ctrl+A`：選取目前可見物件；若啟用「只看選中怪物」，只會選該種類
- `Ctrl+C / Ctrl+V`：複製 / 貼上選取物件
- `Ctrl+Z / Ctrl+Y`：Undo / Redo（最多保留 60 個編輯狀態，並保留最後儲存基準）
- `Delete`：刪除選取物件，可立即 Undo
- `Ctrl + 滑鼠滾輪` 或工具列 `＋ / −`：縮放
- `Space + 左鍵拖曳` 或 `滑鼠中鍵拖曳`：平移
- `Fit`：依地圖長寬比自動縮放到可視區
- 「批量放置」：設定數量與半徑後點地圖，以該點為中心散佈；Blocking 檢查會用單次 batch request 完成

地圖切換、頁籤切換、重新整理或離開地圖工具時，若有未儲存修改會先提示，避免誤丟資料。

## 效能設計

- 地圖清單只讀 `Enemy.dat / NPC.dat` 前 4 bytes 的 count，不為了顯示數量完整解析所有 record。
- `Blocking.dat` 依 mtime/size 快取，批量移動與批量放置在一次 request 內共用同一份 collision grid。
- Blocking PNG / collision grid / JSON metadata cache 都有限制容量，長時間切換大量地圖不會無限累積記憶體。
- 地圖拖曳時只更新 DOM 標記位置；放開後才做 Blocking 驗證與完整 render，避免 pointermove 每幀打 API。
- 啟動 Editor 時不等待完整 Project Health 掃描才顯示 UI；健康檢查會在 Dashboard 載入後更新，避免大量 Quest/Map 掃描拖慢第一畫面。

## 資料來源原則

Editor 改的是 repository 內的 **source-of-truth**，不是 `bin/Debug` 或 publish 產物。例如 NPC Quest 直接改 `Old/服務端/Dat/Quest`；目前 `SGO.Server.csproj` build/publish 時會把 `Old/服務端/Dat/**/*` link 到 `GameData`，因此下次 build 會自然帶入修改。


## 真實地圖背景預覽

遊戲內的大地圖本身是 Godot `SubViewport` 重畫當前 World2D，不是既有的單一 `MapXX.png`。因此 Editor 不使用假的拼圖，而是提供可重複產生的實際場景預覽：

```bat
cd C:\SGO\Recovered\game-editor
export_map_previews.bat
```

可只輸出指定地圖：

```bat
export_map_previews.bat --map=13,22
```

或調整最長邊輸出尺寸：

```bat
export_map_previews.bat --max-size=2048
```

匯出器讀取 `client/maps/map_generated_manifest.json` 的 `scenes` 清單，再載入本機 recovered/generated 的 `client/maps/MapXX.scn`，並依 `client/maps/map_bounds.json` 使用和 Editor 相同的 16px cell 對齊規則。注意 `*.scn` 本來就被 repository 的 `.gitignore` 排除，所以乾淨 clone 只有 manifest、沒有 generated scene 是正常的；這種情況 exporter 會明確提示要使用完整 recovered/generated client workspace，而不是輸出假的地圖。輸出放在 `game-editor/.generated/map-previews/`，不提交進 Git。Editor 重新開啟 / 切地圖後會自動偵測；有預覽時顯示「地圖背景」開關，沒有預覽時仍使用 Blocking / Spawn Zone / 世界座標畫布。

如果 Godot 不在 PATH，可先設定：

```bat
set GODOT=C:\path\to\Godot_v4.x.exe
export_map_previews.bat
```
