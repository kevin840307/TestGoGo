# SGO Client ↔ Server 功能缺口與驗收清單

> 基準：`feature/local-server-net8`，2026-11-08 持續重新核對。
> **本文件是待完成項目的唯一人工維護清單**；Type／Action／欄位完整清單以
> `tests/packet_contract_audit.py`、`tests/packet_schema_audit.py`、
> `tests/packet_response_audit.py` 每次執行產生的報告為準。
> **Handler 存在、測試已寫、靜態稽核 ROUTED 都不等於實機 PASS。**

## 狀態規則

| 狀態 | 定義 |
| --- | --- |
| `PARTIAL` | 已有安全實作，但仍有明確未完成的資料／互動語意 |
| `BLOCKED_EXTERNAL` | 需要付款服務、舊服資料庫或外部授權；不可用假的成功回應代替 |
| `IMPLEMENTED_UNVERIFIED` | 程式與自動測試已補，尚缺可實際執行的 CI／Godot 實機證據 |
| `TEST_GAP` | 缺的是驗證證據或尚未能自動化的測試，不代表功能一定壞掉 |
| `TECH_DEBT` | 非封包缺漏，但會影響長期效能、維護或部署彈性 |

每項從清單移除前，都要保留 commit／測試／實際 Client 相容性證據。

## 協定稽核仍標示 PARTIAL 的路由

- `UseItemRequest` → **P1-01**：已支援大量可安全判定的 scripted item；需要玩家選擇或原始機率不明的效果仍不得猜測。
- `MallAction` → **P2-01／P2-02**：一般商城、點數購買、Donation Gift 已可用；PayPal／舊服點數需外部來源。
- `PayPalDonationCreated` → **P2-01**：沒有 provider 時明確 `Success=false`。
- `LegacyPointWithdrawalResponse` → **P2-02**：沒有舊服來源時明確失敗，不會假入帳。

`tests/test_feature_gap_tracker.py` 會檢查所有稽核標示 `PARTIAL` 的 wire route 在本文件都有對應。

## P0 — 協定、多人一致性、實際驗證

| ID | 狀態 | 現況與真正剩餘缺口 | 已有自動驗證 | 完成驗收 |
| --- | --- | --- | --- | --- |
| P0-01 | IMPLEMENTED_UNVERIFIED | **真實傳輸已從單人擴成雙 Client**。AES/WebSocket 已涵蓋註冊→登入→建角→重登→選角→bootstrap→WorldReady→商城→登出，並新增兩個真實 socket 的 Party/Trade、聊天/移動、掉寶撿取、寵物/機甲、戰鬥/技能、切圖、選擇型寶箱，以及 BankTransfer、DirectTransfer、玩家商店 AddItem/OpenShop/ViewShop/BuyItem、NPC Shop BuyNpcItem，以及 deterministic CraftItemRequest/DismantleItemRequest 的完整 wire 流程。仍缺真正 Godot Scene/UI 驅動的整段多人互動驗證。 | `tests/SGO.Server.WireSmokeTests/Program.cs`、`client/tests/*.gd`、`test_client_network_contract.py` | 有 Godot executable 的環境跑完 headless client tests，並補真正 Scene/UI 驅動的戰鬥／切圖 E2E |
| P0-02 | IMPLEMENTED_UNVERIFIED | **多人競態程式與測試已補**：10 人同撿一個掉物、重複/同時 TradeFinalConfirm、10 人同怪擊殺、斷線/重連/切圖 race、10 人同地圖 SharedMapAiTicker owner arbitration、重複 TradeLock 冪等、最後確認與取消/斷線的受控提交競態；另外新增 NPC 商店超額競購、同 UID 重複出售、銀行同堆疊搬移、玩家商店 10 人搶同商品、直接轉帳守恆；WireLoadProbe 再以 10/50/100 個真 AES/WebSocket client 同時 AttackEntity 同一怪，要求全圖只收到一次死亡終態的 N 份 delivery。掉物使用 SQLite `DELETE ... RETURNING` 原子 claim；交易 FinalConfirm 使用 single-owner commit；經濟扣款改 DB 條件式原子更新，SQLite 啟用 WAL 降低多人讀寫互卡。剩餘是有 runner 的實際執行證據與長時間混合戰鬥/AI 容量報告；10/50/100 的 correctness/load harness 已在 repository 內，不再是主要邏輯缺測。 | `Integration/ConcurrencySmokeTests.cs`、`MonsterConcurrencySmokeTests.cs`、`SessionLifecycleSmokeTests.cs`、`SharedMapAiConcurrencySmokeTests.cs` | Smoke 真正 PASS；同怪/同掉物/交易不重複；SharedMapAI 一張地圖每 scheduler pass 只處理一次 |
| P0-03 | TEST_GAP | **CI 基礎設施仍擋住實際 PASS 證據**。2026-11-08 最新確認 push run `42447665573`（head `f5feb273`）仍在取得 Runner 前結束，job `128629939346` 的 `steps=null`，不是 build/test failure。 | workflow 與 `tests/run_protocol_checks.ps1` 已包含 strict audits、build、domain/full smoke、concurrency、Wire E2E、10/50/100 WireLoadProbe、coverage reports | 取得至少一筆有實際 steps 的 run，所有 Python/.NET/Wire 檢查全 PASS |
| P0-04 | TEST_GAP | 固定 Type/Action、請求 schema、40+ 關鍵 Server→Client response shape、nested anonymous response、`ItemChoiceRequired` / `UseItemChoiceRequest`、Trade/Shop/Pet/Mall/Party/Club 動態封包 helper 的 Client 組裝契約、Godot packet ownership 已守門；DynamicActionSchemaCoverageSmokeTests 會 reflection 掃過 PacketSchemaValidator 全部 ActionRules。新增 `RuntimeResponseContractSmokeTests` 直接序列化真 Router/SQLite 回應，驗證 LoginResponse.Characters 與 Inventory/Equip/Pet/Horse/Skill/Club/Buff/Quest bootstrap 的 Dictionary/array/number/string runtime 型別，並覆蓋 AUTH_REQUIRED / INVALID_PACKET / WORLD_REQUIRED / UNHANDLED_PACKET 四種 typed PacketError；Godot headless `network_response_contract_regression.gd` 也會走同一 AES-CBC decode/dispatch 路徑驗證動態陣列與 fallback。剩餘主要是玩家商店/交易等更深層動態 item view 的逐欄 optional/value semantics，以及實際 runner/Godot PASS 證據。 | `packet_*_audit.py`、`RuntimeResponseContractSmokeTests.cs`、`test_client_network_contract.py`、`client/tests/network_response_contract_regression.gd` | 所有動態 payload 也有 runtime contract/E2E；失敗路徑均回 typed `PacketError` 或既有相容失敗 response |

## P1 — 遊戲資料與舊版相容

| ID | 狀態 | 現況與真正剩餘缺口 | 已有自動驗證 | 完成驗收 |
| --- | --- | --- | --- | --- |
| P1-01 | PARTIAL | **Scripted item 已改為全量可執行 coverage**。現有實作涵蓋恢復、容量卡、多寵物、機甲/蜂型套件、技能書、EXP 卡、booster、變身、外觀變身、幸福娃娃、Festival reward、固定/可安全還原寶箱、製作藍圖分類等。Coverage 現在 `--strict`：只要仍有 `UNKNOWN_SCRIPT_EFFECT` 就直接 fail。已新增可靠選項解析的 `ChoiceRewardCatalog`、`UseItemChoiceRequest`、Godot 選擇 UI 與合法選項原子發獎（含 2209 等已辨識寶箱），並補偽造選項／重放 UID 的 integration + AES/WebSocket 測試；原始隨機機率不可可靠推定的項目仍維持 `CHOICE_REQUIRED` 或資料不足狀態，不會猜獎勵或誤消耗。 | `tests/SGO.ItemCoverageReport/`、`BoosterSmokeTests.cs`、`RecoveredBoxSmokeTests.cs`、`ItemUseClassificationSmokeTests.cs`、`WaspPackageSmokeTests.cs`、`CosmeticTransformationSmokeTests.cs` | strict report 的 `UNKNOWN_SCRIPT_EFFECT=0`；CHOICE_REQUIRED 另有明確 Client 選擇流程後才能視為完整 |
| P1-02 | PARTIAL | **Quest Host 相容已改為全 1,566 份腳本稽核**，CI/local runner 使用 `--strict`，任何直接呼叫未綁 host function 都 fail。曾實際出錯的 `Add_Gift.txt/CheckGift` 已直接執行 smoke，`AddGift` 無 provider 時安全走原腳本 `None()` callback。仍不能宣稱 1,566 份腳本的每一條業務分支、持久化與獎勵都已做 E2E。 | `tests/quest_host_audit.py`、`test_quest_host_audit.py`、主 Smoke 的真實 QuestRuntime 案例 | strict host audit=0 unbound；高使用率 NPC/Quest 持續補成功/拒絕/重登 integration，不再出現 nil global |
| P1-03 | IMPLEMENTED_UNVERIFIED | **歷史資源 ID 不再直接信任**：同 ID 名稱/分類不同視為 collision；先用名稱安全 remap，找不到就 `SKIPPED/UNMAPPED`。新增 machine-readable mapping report，列出 SAFE_SAME_ID / NAME_REMAP / LEGACY_ID_COLLISION / SKIPPED 與 legacy drop RESOLVED/UNMAPPED。 | `ShopCatalog.MappingInfo/Warnings`、`EnemyDropCatalog.ResolveItem`、`tests/SGO.MappingCoverageReport/` | 報告可審核；任何不確定項目保持跳過而非錯配；需要的遺留內容逐項補正確名稱/來源 |
| P1-04 | IMPLEMENTED_UNVERIFIED | 多寵物、技能、booster、固定/還原寶箱、蜂型機甲、外觀變身等近期功能均已有持久化/Router smoke；還缺真正 Godot 實機操作結果。 | 多個 `Integration/*SmokeTests.cs` + Client contract/headless regression | 使用真實 Client 操作後重登，資產、狀態、外觀、技能與召喚狀態一致 |

## P2 — 外部依賴與非封包阻塞項目

| ID | 狀態 | 具體待辦 | 目前安全行為 | 完成驗收 |
| --- | --- | --- | --- | --- |
| P2-01 | BLOCKED_EXTERNAL | **PayPal 付款**需要 provider credentials/API/Webhook endpoint | `CreatePayPalDonation` 明確失敗，不建立假的訂單或點數 | 真 provider sandbox：簽章、Webhook 冪等、退款/重送、可稽核入帳 |
| P2-02 | BLOCKED_EXTERNAL | **舊服點數查詢/提領**需要真實 legacy account/point source | Query/Withdraw 明確失敗，不模擬舊服餘額 | 真來源身分驗證 + 原子單次轉移 + 重試不重複入帳 |
| P2-03 | TECH_DEBT | **50/100 人性能容量**。正確性已有 10 人 race/shared-AI/economy coverage，SQLite 也已啟用 WAL + 5s busy timeout；LoadProbe 已分開量測 10/50/100 人 fanout、同 row worst-case contention、distinct-player SQLite 寫入 P50/P95/P99 與 final-state consistency；另新增 WireLoadProbe，以 10/50/100 個真 AES/WebSocket client 同圖驗證 SyncPosition/ChatMessage fanout 守恆、每輪 P50/P95/P99、setup failure、wire bytes/sec，以及 10/50/100 client 同怪 AttackEntity death-conservation race。仍需有 runner 的實際報告，以及長時間混合戰鬥/AI 負載下的 CPU/RAM/tick 指標 | Shared AI 已由每 socket tick 改成每 map 單一 250ms scheduler；client hot-path/headless perf regressions已建立 | 10/50/100 人報告含 tick P50/P95/P99、DB latency、bytes/sec、CPU/RAM，依瓶頸再優化 |
| P2-04 | TECH_DEBT | **其他 DB Provider**。Domain/Application 已經透過 store interfaces 隔離，但目前 production persistence 只有 SQLite | 不宣稱 PostgreSQL/其他 DB 已支援 | 真有替換需求時新增 provider，跑同一組交易/掉物/資產/重連 contract tests |

## 已完成的測試結構

目前不再把新案例全部塞回原本約 9,518 行的 `Program.cs`。已拆出：

- `Protocol/`：Request / Action / Identity / Prelogin schema；DynamicActionSchemaCoverageSmokeTests 會自動跑完整 ActionRules 的合法/錯型別 runtime 驗證。
- `Protocol/RuntimeResponseContractSmokeTests.cs`：真 Router/SQLite 動態 response JSON 型別、bootstrap Dictionary 與 typed PacketError runtime contract。
- `World/BlockingMapSmokeTests.cs`：Blocking、穿牆、A*。
- `Announcements/AnnouncementSmokeTests.cs`。
- `GameData/CurrentClientCatalogSmokeTests.cs`。
- `Integration/ConcurrencySmokeTests.cs`：10 人同掉物 + Trade duplicate confirm / duplicate Lock。
- `Integration/EconomyConcurrencySmokeTests.cs`：NPC 購買、同 UID 出售、銀行搬移、玩家商店搶購、直接轉帳的 10-way race 與資產/金錢守恆。
- `Integration/TradeTerminalRaceSmokeTests.cs`：受控攔截 DB commit，驗證取消／斷線在提交中不會同時產生 TradeCancel 與 TradeSuccess。
- `Integration/MonsterConcurrencySmokeTests.cs`：10 人同怪死亡 race。
- `Integration/SharedMapAiConcurrencySmokeTests.cs`：10 人/10 怪 SharedMapAiTicker ownership。
- `Integration/SessionLifecycleSmokeTests.cs`：disconnect/reconnect/map transition race。
- `Integration/BoosterSmokeTests.cs`、`RecoveredBoxSmokeTests.cs`、`ItemUseClassificationSmokeTests.cs`、`WaspPackageSmokeTests.cs`、`CosmeticTransformationSmokeTests.cs`。
- `tests/SGO.Server.WireSmokeTests/`：真 AES/WebSocket，現已含兩 client Party/Trade。
- `tests/SGO.Server.WireLoadProbe/`：10/50/100 個真 AES/WebSocket client，同圖移動/聊天 fanout conservation、round latency、wire throughput，以及同怪 AttackEntity death-conservation race。
- `client/tests/monster_chase_regression.gd`、`perf_regression_smoke.gd`、`network_response_contract_regression.gd`：Godot headless regression；Windows runner 有 Godot 時自動執行，後者直接餵 AES-CBC Server response 驗證 client dispatcher。
- `tests/SGO.ItemCoverageReport/`：scripted item 全量分類，`--strict` 未知效果守門。
- `tests/SGO.MappingCoverageReport/`：歷史 ID/名稱 remap、collision、skipped/unmapped 報告。
- `tests/quest_host_audit.py --strict`：1,566 份 Quest host surface 守門。
- `test_smoke_suite_structure.py`：防止上述模組悄悄被移回 monolith，並守住真雙 Client Wire flow。

## 「全部補齊」目前的邊界

**Repository 內可安全推定的固定封包、主要功能、競態修正與自動化守門會繼續補。**
但以下兩類不會用猜測偽裝成完成：

1. **外部服務**：PayPal、舊服點數來源，沒有 credentials/source 就只能保持明確失敗。
2. **原始語意不足**：描述只說「任選/隨機」但沒有可靠選項流程或原始機率的舊道具，不會擅自給獎；會留在 `CHOICE_REQUIRED`，直到 Client 選擇流程/可靠資料補上。

完整 wire route 仍以自動產出的 contract reports 為準。
