# 舊版星夢 Lua NPC 對話格式（相容實作）

來源：`Old/服務端/Dat/Quest/`，尤其是 `MS007_C.txt`、`MS008_D.txt`、`NQ020_F.txt`、`NQ024_A.txt`。

## 已確認的行為

| Lua 前綴 | 實際用途（依原始腳本） | 目前相容處理 |
| --- | --- | --- |
| `n` | NPC 選擇式對話 | 顯示問題與原選項 |
| `n1` | NPC 一般發言 | 於 NPC 上方顯示氣泡 |
| `p` | 玩家／非 NPC 互動選單 | 顯示玩家來源標籤與原選項 |
| `p1` | 玩家一般發言 | 於玩家上方顯示氣泡 |
| `n2..n6`, `p2..p6` | 角色情緒／演出差異（具體對照未知） | 保留 `LegacyPrefix`，暫不臆測表情貼圖 |
| `n7` | NPC 動作、旁白或事件描述 | 事件文字以較柔和的斜體顯示 |
| `p7` | 結果／系統提示（物品獲得、錯誤等） | 事件文字以較柔和的斜體顯示 |
| `s` | 特殊角色／場景對話（具體外觀未知） | 保留來源；選單加特殊對話標籤 |
| `b` | 教學段落，`#` 隔段 | 轉成雙換行、使用可捲動說明顯示 |
| `h` | 長篇說明頁 | 可捲動、頂端對齊文字 |
| `c` | 具體意義未知 | 保留前綴，避免錯誤映射 |
| `m#...` | 舊版圖像／音樂演出 | 解析原始兩張 `.spr`、一個 `.mp3` 名稱為 `LegacyMedia`，沒有圖資時顯示「劇情演出」並保留下一步節點；仍未播放原版媒體 |

### RGB 顏色

原版 Lua 使用 `Color(0,45,198)` 將後續字句設為深藍色，再使用 `Color(0,0,0)` 回到黑色。

```lua
Say("n1請去找"..Color(0,45,198).."大牧師"..Color(0,0,0).."吧！")
Ask("p確定要去尼尼亞皇城嗎?", "確定", "取消", "Answer")
```

Server 執行 Lua 時以私有控制標記代替原先空字串，再於 `DialogFormatter` 轉為成對的 Godot BBCode：

```text
請去找[color=#002DC6]大牧師[/color]吧！
```

傳輸 `NpcDialogResponse` 新增向後相容欄位：

- `Text`：可顯示的 BBCode 文字。
- `PlainText`：不含 Lua 顏色控制標記的純文字。
- `LegacyPrefix`：原版前綴，例如 `p7`、`n1`、`h`。
- `DialogLayout`：`Default`、`Narration`、`Help`、`Media`。
- `LegacyMedia`：只有原始 `m#imageA#imageB#audio#` 會附帶 `ImageA`、`ImageB`、`Audio` 欄位。沒有對應素材時不推測圖像或音效。

原有 `DialogType`、`NextNode`、`Options`、`TargetNode`、`BubbleType` 均未改動；選項 callback 邏輯不變。玩家聊天仍使用原本的 `Label`，只有經過 NPC 對話流程的富文字才建立輕量 `RichTextLabel`。

## Ask 選項的原版關閉語意（第四階段）

對照 `Old/服務端/Dat/Quest/NQ024_A.txt`：`start()` 先呼叫 `BuyTicket0()`，在裡面 `Ask(..., "BuyTicket0Reply")`，然後在 `start()` 結尾執行 `QuestEnd()`。這種 **Ask → QuestEnd 是合法的舊版用法**；任務函式返回不代表使用者已經選擇。因此 Client 現在在互動選項可見時保留選項，直到玩家選擇或明確關閉，而不讓尾端 `QuestEnd` 立刻關掉買票視窗。

同時在 `legacy_dialog_sequence.gd` 維護是否正在等待選項的狀態；一般 `Say`、空選項以及非舊版 Lua NPC 封包仍維持原有流程。選項觸發後會重設對話狀態，避免影響下一次 NPC 互動。

額外的原版佐證：`Old/服務端/CDELua.dll` 的可讀字串包含 `Say("p7Error while running the chunk!")`，所以 `p7` 不只表示物品獲得，也是系統結果及錯誤提示。原始人物表情映射仍不臆測。

## 對話逐句播放（第三階段）

**已確認原版腳本會在一個函式內連續執行 `Say()`、`Say()`、`Ask()`、`QuestEnd()`。** Server 原本會順序送出所有封包，但 Client 立即逐筆覆蓋畫面，造成前幾句無法閱讀、`QuestEnd()` 立即關閉。

Client 現在以 `legacy_dialog_sequence.gd` **只緩衝同一 NPC（EntityID + ScriptName）的後續對話事件**，第一句立即顯示、其餘等待使用者點擊再顯示，切換時收起前一句 NPC／玩家氣泡，`Ask()` 最後才呈現選項；尾端的 `QuestEnd()` 在 NPC 正顯示 `Say()` 時延後處理。不同 NPC 的對話會清除舊佇列。選項按下後仍使用原本的 `NpcOptionRequest`、`TargetNode`，不插入新的 Server 狀態機。

- 已有 `NextNode` 的 `Say()`：若目前批次沒有待顯示事件，點擊後仍送出原本的 `NpcOptionRequest`。
- 沒有 `NextNode` 的 `Say()`：若有後續 `Say()/Ask()`，點擊會顯示下一筆；若無，按原本結束方式關閉。
- `Ask()` 並非逐句氣泡，選項邏輯照舊；獨立 `Ask()` 後收到 `QuestEnd()` 仍維持原有關閉邏輯。
- 原本 `m#` 媒體資料尚缺對應可播放的 Godot sprite/audio，`LegacyMedia` 先保留檔名及順序，以無素材的低侵入方式保持任務續接；火車 `SuppressLegacyMediaDialogs` 的既有特殊處理保留不變。

## 相容性與防回歸（第二階段）

- **嚴格辨識前綴**：僅辨識原版已觀察到的單字母控制碼及 `n1..n7`／`p1..p7`。例如 `hello`、`please`、`shop` 等普通英文必須原樣顯示；`n8`、`p12`、`s3` 等尚未確認格式也不得擅自截斷。
- **選項純文字保護**：Lua 可在選項字串內接上 `Color()`，但 Godot `Button` 不支援 BBCode，因此保留選項文字、移除不應顯示的控制標記；`TargetNode` 及索引完全不變。
- **連續 Ask 保護**：新選單顯示時立即將舊按鈕從畫面移除，避免同一影格收到多個回應時出現舊選項或重複按鈕。
- **重用 RichTextLabel**：富文字氣泡沿用單一顯示元件，下一則普通聊天會切回原本的 `Label`。
- 未經證實的原版 NPC 表情、`s/c` 版型、`m#` 多媒體不臆測貼圖或修改任務流程。

## 驗證

```powershell
dotnet run --project tests/SGO.Server.SmokeTests -- --suite protocol
godot --headless --path client --script res://tests/npc_legacy_dialog_regression.gd
godot --headless --path client --script res://tests/npc_dialog_sequence_regression.gd
```

測試額外包含 `NQ024_A` 型態的 `Ask → QuestEnd` 選項保持可選。其他測試涵蓋 Lua `Say/Ask/Color`、NPC／玩家來源、`p7`、`b/h`、顏色 span、選項 `TargetNode`、普通英文文字／未知前綴不被誤刪、選項控制碼淨化、快速重開選單、Godot 富文字氣泡與純文字回退／元件重用。

**尚待原始 Client 資產／行為比對**：編號 `2..6` 的確切情緒／頭像、`s/c` 的精確視覺、`m#` 原版 `.spr/.mp3` 檔案的轉換與播放、`b` 教學排版及不同解析度的介面微調。這些不應宣稱已與原版 100% 相同。
