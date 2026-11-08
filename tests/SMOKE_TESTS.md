# SGO 測試架構與撰寫規範

此文件描述 `feature/local-server-net8` 目前的自動驗證層級。新增測試應依領域放檔案，**不要再把不同功能全部塞進大型 `Program.cs`**。

## 測試層級

| 層級 | 位置 | 目的 | 啟動 Server |
| --- | --- | --- | --- |
| 靜態協定契約 | `tests/packet_contract_audit.py`、`packet_schema_audit.py`、`packet_response_audit.py` | Client/Server Type、Action、請求 schema、關鍵回應欄位 | 否 |
| Client source contract | `test_client_network_contract.py` | Godot packet ownership、重要 response 消費欄位 | 否 |
| Quest host audit | `quest_host_audit.py --strict` | 1,566 份 legacy Quest 直接 host function 是否都有 binding | 否 |
| Scripted item coverage | `SGO.ItemCoverageReport --strict` | Current Client scripted general items 是否仍有 UNKNOWN effect | 否 |
| Historical mapping coverage | `SGO.MappingCoverageReport` | legacy ID/name collision、safe remap、SKIPPED/UNMAPPED | 否 |
| .NET domain smoke | `SGO.Server.SmokeTests/Protocol`、`World`、`Announcements`、`GameData` | 低耦合資料/協定規則 | 否 |
| .NET integration smoke | `SGO.Server.SmokeTests/Integration` | SQLite、Session、交易、掉物、怪物、道具、重連與競態 | 否 |
| 真 AES/WebSocket E2E | `SGO.Server.WireSmokeTests` | 透過真正運行中的 Server 發送加密封包 | 是 |
| 真 AES/WebSocket load | `SGO.Server.WireLoadProbe` | 10/50/100 個同圖 client 的 fanout、延遲、bytes/sec 與 failure | 是 |
| Godot headless regression | `client/tests/*.gd` | 怪物追擊/插值與 client hot-path regression | 否；需要 Godot executable |
| 真 Godot Scene/UI E2E | 尚未完整自動化 | 切圖、戰鬥、掉寶、寵物/機甲與 UI 操作 | 是 |

## 目前 Smoke 模組

```text
tests/SGO.Server.SmokeTests/
  SmokeAssert.cs
  Fixtures/
    TemporaryDirectoryFixture.cs
  Protocol/
    RequestSchemaSmokeTests.cs
    ActionSchemaSmokeTests.cs
    DynamicActionSchemaCoverageSmokeTests.cs
    IdentitySchemaSmokeTests.cs
    PreloginPacketSmokeTests.cs
    RuntimeResponseContractSmokeTests.cs
  World/
    BlockingMapSmokeTests.cs
  Announcements/
    AnnouncementSmokeTests.cs
  GameData/
    CurrentClientCatalogSmokeTests.cs
  Integration/
    ConcurrencySmokeTests.cs
    EconomyConcurrencySmokeTests.cs
    MarketplaceResponseContractSmokeTests.cs
    TradeTerminalRaceSmokeTests.cs
    MonsterConcurrencySmokeTests.cs
    SharedMapAiConcurrencySmokeTests.cs
    SessionLifecycleSmokeTests.cs
    BoosterSmokeTests.cs
    RecoveredBoxSmokeTests.cs
    ChoiceRewardSmokeTests.cs
    ItemUseClassificationSmokeTests.cs
    WaspPackageSmokeTests.cs
    CosmeticTransformationSmokeTests.cs
  Program.cs
```

Protocol 動態 action 也已獨立化：`DynamicActionSchemaCoverageSmokeTests` 會直接讀取 Server 的完整 `ActionRules`，對每個 Shop/Pet/Mall/Party/Club action 自動產生合法 payload，並對每個必填欄位做錯型別拒絕測試；新增 action rule 時不需要人工複製 30 多份固定測試。

動態 Server→Client payload 也不再只靠 source grep：`RuntimeResponseContractSmokeTests` 會對真 Router/SQLite 產生的 LoginResponse 與角色 bootstrap 做 JSON runtime 型別驗證，涵蓋 Inventory/Equip/Pet/Horse/Skill/Club/Buff/Quest 的 Dictionary/array 欄位；AUTH_REQUIRED、INVALID_PACKET、WORLD_REQUIRED、UNHANDLED_PACKET 四種失敗也統一驗證為 typed `PacketError`。

多人交易/玩家商店的動態 Dictionary 另外由 `MarketplaceResponseContractSmokeTests` 驗證：TradeSync 的雙方視角與 lock/money state、ShopSync/ShopViewResponse 的 listing 欄位與寵物 nested ExtraData 都走真 service runtime；Client headless AES dispatcher 也餵入相同類型的 Trade/Shop payload，避免 Server 正確但 Godot 消費型別退化。

Client 真傳輸另有 `real_server_network_e2e.gd`：protocol runner 啟動隔離 Server 後，Godot 直接使用 production `NetworkManager` / `WebSocketPeer` 完成 register/login/create/relogin/select、九個 bootstrap sync、WorldReady ACK 與 nearby chat round-trip。它與 server-free dispatcher regression 分離，沒有 Godot executable 時會明確 skip，不會假裝 PASS。

重要的多人情境已獨立化：

- `ConcurrencySmokeTests`：10 人同時撿同一掉物只能成功 1 人；重複/並行 FinalConfirm 交易只提交一次；重複 Lock 不會清除已確認狀態。
- `EconomyConcurrencySmokeTests`：10 個並發請求覆蓋 NPC 購買不得超額扣款/生物品、同 UID 出售只入帳一次、銀行同堆疊搬移守恆、玩家商店 10 人搶 5 件商品只成交 5 次、直接轉帳金額守恆。
- `MarketplaceResponseContractSmokeTests`：真 TradeService/PlayerShopService + SQLite + broadcast queue；驗證 TradeSync 雙方 My/Other 動態 view、ShopSync/ShopViewResponse 一般物品與寵物 ExtraData，並在 Service 層再做 10 buyer 搶 5 件的競態、金錢/物品守恆與售罄自動關店。
- `TradeTerminalRaceSmokeTests`：用 DispatchProxy 暫停 DB commit，模擬取消與斷線撞上提交；終態僅能是一次成功，不能先發 Cancel 後發 Success。
- `ChoiceRewardSmokeTests`：已知 2209 選項驗證、偽造拒絕、重放 UID、10 請求同時領同一寶箱只能成功一次。
- `MonsterConcurrencySmokeTests`：10 人同時攻擊同一怪物，死亡/掉寶/獎勵不重複。
- `SharedMapAiConcurrencySmokeTests`：10 人/10 怪同地圖，SharedMapAiTicker 每張地圖每 pass 只 tick 一次，怪物 owner arbitration 唯一。
- `SessionLifecycleSmokeTests`：disconnect/reconnect/map-transition race 與 current-session ownership。

## Wire E2E 現況

`tests/SGO.Server.WireSmokeTests/Program.cs` 使用與 Godot 相同的 ClientProtocol/AES WebSocket，不直接呼叫 Router。現已涵蓋：

- 公告、錯誤 schema、未登入 AUTH_REQUIRED。
- 註冊、登入、三角色 slot、建角、重登、選角。
- EnterWorld bootstrap、WorldReady、商城 list、登出。
- 第二個真 WebSocket client 完成相同登入/進圖。
- 經濟封包走真 wire：BankTransfer、DirectTransfer，以及玩家商店 AddItem → OpenShop → ViewShop → BuyItem，並驗證雙方金錢/物品/商店列表同步。
- NPC 商店 `BuyNpcItem` 與製作 `CraftItemRequest` / `DismantleItemRequest` 也走真 wire；使用 100% 成功率的 Recipe 1291/1292，避免隨機 flaky。
- Client A `SyncPosition` → Client B 收 `MoveEntity`。
- Client A nearby `ChatMessage` → Client B 收 `ChatBroadcast`。
- Party Invite → Accept → 雙方 `PartySync`。
- 選擇型寶箱 `UseItemRequest` → `ItemChoiceRequired` → `UseItemChoiceRequest`，涵蓋偽造 ItemIndex、合法選項、UID 重放。
- Trade Invite → Accept → Lock → `TradeFinalConfirmReq` → 雙方 `TradeSuccess`。
- 多人 server-push 允許 interleaved packet，不假設「下一包一定是 response」。

`test_smoke_suite_structure.py` 會守住雙 Client、MoveEntity、ChatBroadcast、Party、Trade 等關鍵字，避免 E2E 不小心退回單人版本。

## Wire Load Probe

`tests/SGO.Server.WireLoadProbe/` 與 WireSmoke 分離，專門做多人容量驗證。預設依序建立 10、50、100 個真 AES/WebSocket client，全部完成註冊、登入、建角、選角、bootstrap 與 WorldReady 後，在同一張地圖執行：

- `SyncPosition`：每一個 sender 每輪必須讓其餘 `N-1` 個 client 各收到一次 `MoveEntity`，不允許漏包或重複 fanout。
- Nearby `ChatMessage`：每一個 sender 每輪預期全圖 `N` 個 client（含 sender response）各收到一次 `ChatBroadcast`。
- 同怪 combat race：測試 seed 將所有角色提升能力並生成 Enemy 245，之後 10/50/100 個 client 只走正常 `AttackEntity`；同一怪死亡時全圖只允許 `N` 份 `EntityDeath` delivery（每 client 一份），多一份或少一份都 fail。
- Mixed soak：先 seed 5~20 隻 Enemy 245，讓 SharedMapAiTicker 真正持續處理同圖怪物；同時送 SyncPosition、nearby ChatMessage 與部分 AttackEntity。每 500ms 讀 Server `/health`，輸出 working set、GC heap、CPU total delta/normalized CPU%、SharedMapAI active sessions/maps/monster count 與 tick elapsed P50/P95/P99/max。CI 用 `--mixed-seconds 5` 做短 smoke；本機可拉到 600 秒、3600 秒以上。
- 報告 setup 成功/失敗、每輪 fanout completion P50/P95/P99、deliveries/sec、實際 sent/received bytes、received bytes/sec、combat attack waves/death deliveries，以及 MixedSoak 的 CPU/RAM/GC/SharedMapAI tick 指標。
- CI/local runner 使用 `--sizes 10,50,100 --rounds 2`；若任一 client setup 失敗或 delivery conservation 不一致即非 0 exit code。

`test_wire_load_probe_structure.py` 守住真 ClientProtocol、10/50/100、fanout conservation 與 CI/local runner wiring。

## Coverage / Audit 守門

### Packet

```powershell
python tests/packet_contract_audit.py --strict
python tests/packet_schema_audit.py --strict
python tests/packet_response_audit.py --strict
```

### Quest

```powershell
python tests/quest_host_audit.py --strict --output quest-host-report.json
```

`--strict` 代表任何直接呼叫但沒有 QuestRuntime host binding 的 function 都是失敗，不只列警告。

### Scripted item

```powershell
dotnet run --project tests/SGO.ItemCoverageReport/SGO.ItemCoverageReport.csproj -c Release -- --strict --output item-use-coverage.json
```

- `SUPPORTED`：有明確 resolver/實作。
- `NONCONSUMABLE_CRAFTING`：材料/藍圖，不由 UseItem 消耗。
- `CHOICE_REQUIRED`：尚無可靠可選清單或原始機率資料，不得猜獎勵；可可靠解析的選擇型箱已由 `ChoiceRewardCatalog` + Client UI 實作，列為 `SUPPORTED`。
- `UNKNOWN_SCRIPT_EFFECT`：真正未識別；`--strict` 會直接 exit non-zero。

### Historical mapping

```powershell
dotnet run --project tests/SGO.MappingCoverageReport/SGO.MappingCoverageReport.csproj -c Release -- --output mapping-coverage.json
```

報告會列 SAFE_SAME_ID、NAME_REMAP、LEGACY_ID_COLLISION、SKIPPED，以及 legacy drop RESOLVED/UNMAPPED。

## 執行方式

從 Repository 根目錄：

```powershell
# Python contracts
python -m unittest discover -s tests -p 'test_*.py' -v

# 快速 domain suites
dotnet run --project tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj -c Release -- --suite protocol
dotnet run --project tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj -c Release -- --suite announcements
dotnet run --project tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj -c Release -- --suite world
dotnet run --project tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj -c Release -- --suite concurrency

# 全遊戲 integration
dotnet run --project tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj -c Release

# 一鍵：strict audits + build + reports + smoke + 真 WebSocket
powershell -ExecutionPolicy Bypass -File tests/run_protocol_checks.ps1

# 較長的真 socket + SharedMapAI mixed soak（Server 需已啟動且 GM test mode 開啟）
dotnet run --project tests/SGO.Server.WireLoadProbe/SGO.Server.WireLoadProbe.csproj -c Release -- --url ws://127.0.0.1:5678/ --sizes 10,50,100 --rounds 2 --mixed-seconds 600 --output wire-load-soak.json
```

### Godot headless

Windows runner 會優先讀 `GODOT_BIN`，否則尋找 `godot` / `godot4`。找到時自動執行：

```powershell
godot --headless --path client --script res://tests/monster_chase_regression.gd
godot --headless --path client --script res://tests/perf_regression_smoke.gd
godot --headless --path client --script res://tests/item_choice_regression.gd
godot --headless --path client --script res://tests/network_response_contract_regression.gd
# 下列測試需要本機 .NET Server 已啟動
SGO_TEST_SERVER_URL=ws://127.0.0.1:5678 godot --headless --path client --script res://tests/real_server_network_e2e.gd
```

沒有 Godot 時 runner 會明確印 `[SKIP]`，不會把「沒跑」寫成 PASS。

## 撰寫規則

1. 新封包案例放 `Protocol/`；多人/持久化放 `Integration/`；不要新增另一個 monolith。
2. 每個測試自行建立/清理 fixture，避免依賴前一個案例留下的 SQLite/Session 狀態。
3. 多人資料要測**守恆**，不只測封包存在：物品總數、金錢、死亡、掉物只能發生一次。
4. 成功、拒絕、邊界、重複請求、disconnect 都要測。
5. Handler/route 存在不等於 PASS；CI 未真正執行時文件必須標 `IMPLEMENTED_UNVERIFIED`。
6. Client packet 改動需同時檢查 Python contract、Wire E2E、Godot headless（若可跑）。
7. 修舊功能盡量維持 wire Type/field 相容；新增語意優先用可選欄位或新 Type。

完整仍未完成/外部阻塞項目請看 [server/FEATURE_GAPS.md](../server/FEATURE_GAPS.md)。

## 2026-11-08 新增回歸守門：AI Owner 與精確金錢測資

- `World/MapAiOwnerSpatialIndexSmokeTests.cs`：單獨測試
  `MapAiOwnerSpatialIndex`；包含小集合、大型稀疏座標、
  1024 owners / 2000 queries 與原本 full-scan reference 一致性、
  角色死亡但 pet 有效、等距 tie、併行只讀及無效輸入。
  由 `--suite world` 與 full suite 執行。
- `Fixtures/ExactMoneyFixture.cs`：新角色預設有 **10,000**
  金錢；需要測 overspend / 不足額 / rollback 的測試必須先把
  金錢讀出並設定為**精確的 0、100、1000** 等值，不能
  `AdjustMoney(+100)` 後假設餘額為 100。
  `EconomyConcurrencySmokeTests`、
  `AssetMutationConcurrencySmokeTests`、
  `CrossFeatureConcurrencySmokeTests` 共享 Fixture。
- `test_smoke_suite_structure.py`：檢查新 owner index 被 AI
  ticker 呼叫、world suite 正確登錄，且三個金錢競態 suite
  不再直接以 `AdjustMoney` 加法初始化。
- Client 仍使用 `ground_item_aoi_regression.gd`、
  `multiclient_render_dedup_regression.gd`、
  `real_server_network_e2e.gd` 驗證 render/AOI/真 WebSocket；
  這一輪沒有修改 Client 邏輯，並非宣稱這些用例已實際 PASS。

建議依序執行：

```powershell
python -m unittest discover -s tests -p test_smoke_suite_structure.py -v
dotnet run --project tests/SGO.Server.SmokeTests -c Release -- --suite world
dotnet run --project tests/SGO.Server.SmokeTests -c Release -- --suite concurrency
powershell -ExecutionPolicy Bypass -File tests/run_protocol_checks.ps1
```

注意：結構測試通過不能替代 C# 編譯或實際
10/50/100 客戶端壓測；沒有實際執行紀錄前都只標
`IMPLEMENTED_UNVERIFIED`。
