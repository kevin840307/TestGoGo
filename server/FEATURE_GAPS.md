# SGO Client ↔ Server 功能缺口與驗收清單

> 基準：`feature/local-server-net8`，2026-11-08 重新核對。
> **本文件是待完成項目的唯一人工維護清單**；完整 Type / Action 清單應以
> `tests/packet_contract_audit.py`、`tests/packet_schema_audit.py`、
> `tests/packet_response_audit.py` 每次執行產生的報告為準。
> **存在 Handler、測試程式已編寫、或靜態稽核顯示 ROUTED，均不代表實機功能 PASS。**

## 狀態規則

| 狀態 | 定義 |
| --- | --- |
| `PARTIAL` | 已有部分功能，但至少有明確缺少的路徑／規則 |
| `BLOCKED_EXTERNAL` | 需要付款服務、舊服資料庫或外部授權，不應以假的成功回應代替 |
| `IMPLEMENTED_UNVERIFIED` | 已有程式碼，尚未取得指定 E2E／長期測試 PASS 證據 |
| `TEST_GAP` | 缺的是回歸／整合／壓力測試，不表示功能一定壞掉 |
| `TECH_DEBT` | 影響維護或效能，可分階段改善 |

每項從清單移除前：附上實際 commit、通過的測試名稱／紀錄，並檢查既有 Client 相容性。

## 協定稽核已知部分完成對照

- `UseItemRequest` → **P1-01**（未識別道具腳本效果仍部分完成）。
- `MallAction` → **P2-01／P2-02**（點數購買／禮物可用；真實金流、舊服資料依賴外部服務）。
- `PayPalDonationCreated` → **P2-01**（未接入供應商時明確回傳失敗）。
- `LegacyPointWithdrawalResponse` → **P2-02**（未接入舊服資料時明確回傳失敗）。

`tests/test_feature_gap_tracker.py` 會檢查契約稽核標示 `PARTIAL` 的封包有列在此文件，避免 Type 對上就被當成已完成。

## P0 — 封包、資料一致性及驗證基礎

| ID | 狀態 | 具體待辦 | 主要位置 | 完成驗收 |
| --- | --- | --- | --- | --- |
| P0-01 | TEST_GAP | **實際 Godot ↔ .NET 封包 E2E**：登入、角色建立／選取、WorldReady、切圖、怪物同步、技能、掉寶、拾取、交易、寵物／機甲、商城點數、登出 | `client/scripts/network_manager.gd`、`server/Application/PacketRouter.cs` | 真實 Client 逐項操作，兩端封包及資料一致；失敗案例有回應，不會無限等待 |
| P0-02 | TEST_GAP | **多人同步與並行安全**：2／10 人同地圖、相同怪物同時擊殺、交易兩端同時確認、同一掉寶同時拾取、切圖與斷線 | `SharedMapAiTicker`、`CombatService`、`TradeService`、`PacketRouter` | 單怪更新一次、獎勵與物品不重複、角色位置不倒退，重連後狀態一致 |
| P0-03 | TEST_GAP | **CI 真正執行**：GitHub Actions 過去多筆 Job 在取得 Runner 前失敗，尚無 Build／Smoke／Wire 全綠證據 | `.github/workflows/server-build.yml`、`tests/run_protocol_checks.ps1` | 取得有步驟的 CI run：Python 契約、.NET build、遊戲 Smoke、AES/WebSocket smoke 全 PASS |
| P0-04 | TEST_GAP | **完整欄位契約**：已補 Server → Client 巢狀 anonymous object 解析、`DriveMechaResponse` 明確 ACK 契約，以及 Godot `network_manager.gd` client ownership 回歸測試；仍缺非固定 Dictionary、動態組裝、全部欄位型別與錯誤分支 | `tests/packet_*_audit.py`、`tests/test_client_network_contract.py`、`server/Protocol/PacketSchemaValidator.cs` | 報告列出每個實際用到的 Type／Action、必要／選填欄位、型別、成功與失敗回應；無未分類路由 |

## P1 — 已知部分實作功能

| ID | 狀態 | 具體待辦 | 主要位置 | 完成驗收 |
| --- | --- | --- | --- | --- |
| P1-01 | PARTIAL | **未識別的舊版物品腳本效果**：目前已實作多種恢復、機甲、寶箱、召喚及技能道具，但未有對照規則的效果仍應拒絕，不可誤給獎勵 | `PacketRouter.UseItemRequest`、`server/Application/Items/` | 依新版 Client 定義建立 ID → 效果 → 資產變更 → 封包 → 測試對照表，逐項完成 |
| P1-02 | PARTIAL | **NPC／Lua 任務相容性**：仍需逐份檢查舊腳本的 Host Function、條件、持久化與任務獎勵 | `server/Infrastructure/Quest/QuestRuntime.cs`、`QuestProgressService.cs` | 每個支援腳本的成功／拒絕／重登情境可重現，未支援指令有明確診斷 |
| P1-03 | PARTIAL | **歷史資源／商品對照**：舊版 ID 不保證對應新版物品；需要處理剩餘碰撞與資料缺漏 | `server/Infrastructure/GameData/` | 產出可審核的 ID／名稱／來源／狀態對照；不確定的項目明確跳過 |
| P1-04 | IMPLEMENTED_UNVERIFIED | **多寵物、技能、固定獎勵寶箱近期新增路徑**：最新提交有實作及部分 SmokeTests，但仍需要 Client 實機操作與重連驗證 | `client/`、`CombatService`、`PacketRouter`、`SqliteGameStore` | 召喚與狀態同步、技能傷害、開箱扣道具及獎勵均在重登入後一致 |

## P2 — 依賴外部系統／效能改善

| ID | 狀態 | 具體待辦 | 主要位置 | 完成驗收 |
| --- | --- | --- | --- | --- |
| P2-01 | BLOCKED_EXTERNAL | **PayPal 付款**：`CreatePayPalDonation` 會回傳 `Success=false`，並沒有建立實際付款訂單 | `server/Application/Mall/MallService.cs` | 取得付款提供者設定後，驗證服務端簽章、Webhook 冪等與可稽核的點數入帳 |
| P2-02 | BLOCKED_EXTERNAL | **舊服點數查詢／提領**：`QueryLegacyPoints`、`WithdrawLegacyPoints` 回傳未設定來源 | `MallService.cs` | 接入真實來源、驗證身分、單次且原子轉移、記錄與重試不重複入帳 |
| P2-03 | TECH_DEBT | **大量玩家的傳輸與資料庫成本**：測量 AOI 廣播、AI Tick 的資料查詢與地圖快照流量，不先假設可支援固定人數 | `ServerBroadcastHub`、`SharedMapAiTicker`、`WorldRuntimeService` | 提供 10／50／100 人負載與封包、Tick、DB 指標，再按瓶頸優化 |
| P2-04 | TECH_DEBT | **更換資料庫 Provider**：目前使用 SQLite，其他 Provider 尚未作實際替換驗證 | `server/Infrastructure/Persistence/` | 有需要時新增 Provider 並通過相同持久化與交易語意測試 |

## 測試架構整理狀態

- `tests/SGO.Server.SmokeTests/Program.cs` 原先約 **9,518 行**，大量不同領域案例集中在一份檔案。
- **本輪已搬出**無需資料庫的請求 Schema、Action Schema、UID／DBID、公告等獨立測試：
  - `Protocol/RequestSchemaSmokeTests.cs`
  - `Protocol/ActionSchemaSmokeTests.cs`
  - `Protocol/IdentitySchemaSmokeTests.cs`
  - `Protocol/PreloginPacketSmokeTests.cs`
  - `World/BlockingMapSmokeTests.cs`
  - `Announcements/AnnouncementSmokeTests.cs`
  - `Fixtures/TemporaryDirectoryFixture.cs`（測試暫存資料自動清理）
- 共用 `SmokeAssert.cs`，仍由原 Smoke runner 呼叫，避免增加額外測試框架依賴。
- 新增 `tests/test_client_network_contract.py`：直接檢查 Godot `network_manager.gd` 的封包責任邊界；目前鎖住機甲 ACK、`PlayerStatusSync` 能力值來源與商城 `Gifts` 消費欄位，CI 與 PowerShell runner 都會執行。
- `packet_response_audit.py` 已改為平衡 C# anonymous initializer 大括號，避免 `MallListResponse` 這類巢狀 `new { ... }` 被第一個 `}` 截斷造成假缺欄位。
- `GameData/CurrentClientCatalogSmokeTests.cs` 已從大型 `Program.cs` 抽出真實 Client skill/item/reward 對照，並由 `test_smoke_suite_structure.py` 防止測試再塞回主檔。
- **2026-11-08 路由來源複核**：逐檔比對 95 個 Godot `.gd` 腳本及 Server 固定回傳 Type 的來源，未發現新的固定 Type 遺漏對接；`BankTransfer` 由 `BankPacketHandler` 模組處理，而不是 `PacketRouter.case`。此結論**不包含**動態組合的所有欄位、實際多人遊戲流程與外部服務可用性。
- **待拆**的世界／交易／任務等大型整合案例需先建立可複用的 fixture（獨立 SQLite、地圖資料、Player sessions），然後分成不同測試模組；不可只把原本依賴同一個可變 Session 的程式碼切開，否則容易產生跨測試污染。
- 新增測試請優先放進相關領域檔案；不要繼續往 `Program.cs` 最前面塞大量獨立 `using (Json(...))`。

## 已接線不等於功能完成

目前已知正向能力包含世界、戰鬥、玩家商店、銀行、組隊／戰團、製作／分解、背包、寵物／機甲、公告清單、設定型商城點數與禮物等，但每一類的**驗證完成度**不相同。需使用 `tests/PACKET_CONTRACT.md` 的工具檢查 Type／Action 覆蓋，並以真實 Client 行為作為最終驗收依據。
