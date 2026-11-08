# 舊版星夢劇情圖片／音效匯入說明

本目錄供原版 Lua 的 `Say("m#圖A.spr#圖B.spr#音效.mp3#", "Next")` 使用。
伺服器保留原始 `LegacyMedia.ImageA/ImageB/Audio`，Godot 依照檔名尋找對應素材。

## 對應命名

- 原版 `STORY_NAME_00A.spr` → `STORY_NAME_00A.png`（或 `.webp/.jpg`）。
- 原版 `STORY_NAME_00B.spr` → `STORY_NAME_00B.png`。
- 原版 `030.mp3` → `030.mp3`（也接受 `.ogg/.wav`）。
- 原版 `X_mas_card\BG_STATION_01.spr` → `X_mas_card/BG_STATION_01.png`。
- 原版 `SE0136.mp3` → `SE0136.mp3`，若已轉成音效也可使用 `res://Assets/Audio/SE/SE0136.wav`。
- 原版 `na` 或空白圖片／音效欄位表示略過該項。

請保留檔案的大小寫及相對資料夾。沒有相應檔案時畫面仍可按「繼續」，不會中斷任務。

## 安全與保真

1. 原始 `.spr` 不是 Godot 可直接使用的圖片格式，需要先從你合法持有的 Client 檔案轉換為 PNG／WEBP／JPG。
2. **目前 GitHub 專案內沒有 `STORY_NAME_*.spr`／`BG_STATION_*.spr` 及其對應音效的實體檔案**；不可把劇情專用圖像誤認為 NPC 行走 Sprite。
3. 解析器僅允許受控的 `res://Assets/Quest/LegacyMedia/` 及 `res://Assets/Quest/` 路徑，拒絕 `..`、絕對路徑及未支援副檔名。
4. `m#` 的 NextNode 與任務條件保留原本 Server 規則；圖像／音效缺檔只會降級為可繼續的替代畫面，不會執行外部命令或抓取網址。
5. 此功能是**素材播放器及相容路徑完成**，不是原版過場美術 100% 還原；若未提供原始素材，不能聲稱其內容已恢復。
