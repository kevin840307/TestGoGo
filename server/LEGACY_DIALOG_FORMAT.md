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
| `n7`, `p7` | 旁白、獲得／失去物品等事件 | 事件文字以較柔和的斜體顯示 |
| `s` | 特殊角色／場景對話（具體外觀未知） | 保留來源；選單加特殊對話標籤 |
| `b` | 教學段落，`#` 隔段 | 轉成雙換行、使用可捲動說明顯示 |
| `h` | 長篇說明頁 | 可捲動、頂端對齊文字 |
| `c` | 具體意義未知 | 保留前綴，避免錯誤映射 |
| `m#...` | 舊版圖像／音樂演出 | 維持既有 `SuppressLegacyMediaDialogs` 行為，尚未實作媒體播放 |

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
- `DialogLayout`：`Default`、`Narration`、`Help`。

原有 `DialogType`、`NextNode`、`Options`、`TargetNode`、`BubbleType` 均未改動；選項 callback 邏輯不變。玩家聊天仍使用原本的 `Label`，只有經過 NPC 對話流程的富文字才建立輕量 `RichTextLabel`。

## 驗證

```powershell
dotnet run --project tests/SGO.Server.SmokeTests -- --suite protocol
godot --headless --path client --script res://tests/npc_legacy_dialog_regression.gd
```

測試涵蓋 Lua `Say/Ask/Color`、NPC／玩家來源、`p7`、`b/h`、顏色 span、選項 `TargetNode`、Godot 富文字氣泡與純文字回退。

**尚待原始 Client 資產／行為比對**：編號 `2..6` 的確切情緒／頭像、`s/c` 的精確視覺、`m#` 的舊媒體演出、`b` 教學排版及不同解析度的介面微調。這些不應宣稱已與原版 100% 相同。
