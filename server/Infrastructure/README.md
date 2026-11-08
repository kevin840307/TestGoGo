# Infrastructure

這個資料夾只放「技術實作與外部資料來源的適配」，不要放核心遊戲規則。

目前分成四個明確區域：

## GameData

負責讀取、解析與對應遊戲資料，例如：

- Current client `.tres`
- Legacy `.itd` / `.dat` / `.csv`
- Item / Skill / Enemy / Shop / Recipe catalog
- Legacy → current client 的資料相容層

不應在這裡處理玩家交易、戰鬥流程、AOI 排程等 runtime gameplay orchestration。

## Persistence

負責資料持久化與資料庫實作：

- `SqliteGameStore`
- schema migration
- persistence DI registration

如果未來加入 PostgreSQL，應新增平行實作，例如：

```
Persistence/
  Sqlite/
  Postgres/
```

不要讓 Application / Domain 直接依賴 SQLite-specific 細節。

## Quest

負責 legacy NPC / Lua script runtime 的技術適配：

- `QuestRuntime`
- `DialogFormatter`
- legacy Lua/native callback compatibility

Quest 的遊戲規則如果逐步轉成原生 C# feature，應放到 `Application` 對應功能，而不是繼續擴充這個資料夾。

## WorldData

負責地圖與世界靜態資料的解析 / 查詢：

- blocking map
- ground drop placement
- monster navigation data
- world data loading

這裡可以提供「資料與純計算」，但不要放 session、WebSocket、broadcast 或 shared tick scheduler。

## 放置原則

新增程式前先判斷：

- 玩家/怪物/交易/戰鬥流程 → `Application`
- 核心狀態與規則 → `Domain`
- DB / 檔案 / legacy parser / 外部技術適配 → `Infrastructure`
- WebSocket / packet/session transport → networking 相關區域
- 管理頁 / diagnostics → 對應的 Admin / Diagnostics 區域

如果一個類別同時做「資料解析 + gameplay 流程」，應優先拆責任，不要直接把整個功能塞進 Infrastructure。

## 目前策略

目前不進行大規模 namespace / folder 搬移。

原因是 Server 正在進行多人、AOI、經濟、Quest 與 regression hardening；此階段優先保持低風險。等 full smoke suite 穩定後，再評估是否把 `GameData`、`Persistence`、`Quest` 提升成頂層模組。
