# SGO Game Editor

獨立於遊戲 Client / Server 的本機 Web 編輯器。預設只監聽 `127.0.0.1:8765`，**不掛進遊戲 Server，也不會增加遊戲 runtime CPU / RAM 負擔**。

## 能做什麼

- **技能 CRUD**：編輯 `client/Skills/*.tres`，包含技能基本資訊、特效 ID 與每級數值。
- **怪物 CRUD**：編輯 `Old/服務端/Dat/Enemy/Enemy_Data.csv` 的常用欄位；其他 legacy 欄位保留。新增怪物可 Clone 既有怪物。
- **地圖怪物 CRUD**：編輯 `MapXX/Enemy.dat` 的 Enemy ID / X / Y / flags。既有記錄會保留原本 207-byte payload。
- **地圖 NPC CRUD**：編輯 `MapXX/NPC.dat`，可由 Script 欄直接跳到 NPC 腳本。
- **NPC 腳本編輯器**：管理 `Old/服務端/Dat/Quest/*.txt`，支援搜尋、function outline、`#include` 檢查、Ask/Say callback 檢查、block/括號/字串基本驗證。
- **素材管理**：瀏覽 `client/Assets`、圖片預覽、直接替換素材。
- **安全寫入**：所有修改先備份到 `game-editor/.backups/`，再使用 atomic replace；所有可輸入路徑都有 traversal guard。

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

不需要 pytest 或額外 pip package：

```bat
cd game-editor
run_tests.bat
```

或：

```bash
python -m unittest discover -s game-editor/tests -v
```

測試涵蓋：

- 安全路徑與 traversal 防護
- Skill `.tres` CRUD / 未知欄位保留
- Enemy CP950 CSV CRUD / 未編輯欄位保留
- `Enemy.dat` round-trip / 207-byte payload 保留
- `NPC.dat` round-trip / reserved byte 保留
- Quest include / callback / block 驗證
- CP950 Quest script 儲存
- 素材替換與備份路徑安全

## 資料來源原則

Editor 改的是 repository 內的 **source-of-truth**，不是 `bin/Debug` 或 publish 產物。例如 NPC Quest 直接改 `Old/服務端/Dat/Quest`；目前 `SGO.Server.csproj` build/publish 時會把 `Old/服務端/Dat/**/*` link 到 `GameData`，因此下次 build 會自然帶入修改。
