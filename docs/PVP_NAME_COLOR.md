# 玩家 PvP 與名稱顏色（實作草案）

來源：`Old/服務端/map.cfg` 的 `[NonPKMap]` 包含 7、9、15、17、22、25、38、40。舊版反編譯 `.lst` 的完整刑罰秒數與技能 PK 公式尚未核對，因此 **60 秒粉名是暫定值**，不是宣稱原版設定。

- `/pk on`：切白名，允許在 PvP 地圖攻擊、被攻擊。
- `/pk off`：切藍名，雙向禁止 PvP；粉／紅名及死亡時不能切。
- 攻擊白名後粉名 60 秒，最後 3 秒閃爍；重複攻擊延長時間。
- **擊殺白名**轉紅名；攻擊／擊殺粉名及紅名不增加 PK 罪名。
- 紅名長期保留，直到 GM 重設（原版赦免／死亡降罪規則尚待反編譯確認）。
- `/gm namecolor blue|white|pink|flash|red [onlineDBID]`：GM 設定自己或在線指定玩家。GM 權限沿用 `Game:GmCommandsEnabled`。
- Server 在單一 SQLite transaction 結算玩家普攻傷害、首次死亡和 PK 罪名；`WorldReady`/AOI `SpawnEntity` 同步顏色與剩餘時間，Client 只作名稱顯示。
- 正常怪物普攻／AOE、治療、復活與所有非 PvP 遊戲邏輯未更動。

## 尚未完成與必測

- 玩家間 **攻擊技能、範圍技能、寵物／機甲 PvP** 尚未開啟：需逐技能核對原版規則與合法隊友免傷、AOI、MISS、暴擊。
- PvP 普攻傷害目前採 `max(1, Attack - Defense)` 作初版，正式精確公式須依原版 BattleSys 反編譯校正。
- 請用雙真實 Client 驗證 Map13 白名 PvP、Map15 禁止 PvP、藍名雙向保護、同時致命競速、粉名閃爍與重登、紅名持久化、GM 顏色同步；並跑 .NET SmokeTests、Godot headless tests。
- 目前尚無已通過的 .NET 8/Godot 執行測試證據，請勿視為通過驗收。
