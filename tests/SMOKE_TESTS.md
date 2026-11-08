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
- `Integration/EconomyRequestGuardSmokeTests.cs`：防偽造/錯誤數量/負單價/超量購買與出售。`BuyAsset` 允許的一般堆疊上限 9999、裝備單次 1 件，使用 long 計算 9999 × int.MaxValue 的價格；檢查拒絕時 DB money/item 不變，正常 Buy/Sell 後重複出售不能再次入帳。由 `--suite concurrency` 與 full suite 執行。
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

## 2026-11-08 AOI／地面物／Client 回歸補充

- `World/WorldEntityAoiStationaryRefreshSmokeTests.cs`：
  不動座標只更新 factory 不重掃 observer；1000 次刷新不得
  重複 Spawn，late observer 看最新快照；同 cell 移動超出
  exit radius 一樣 Despawn，跨 map identity 不留下舊 known-set。
- `World/WorldEntityAoiSnapshotRaceSmokeTests.cs`：
  決定性重現舊快照競態 A(取得 AOI generation)→
  B(提交/新增地面物 AOI)→A(清舊索引)。B 的物品必須保留，
  但之後新的 authoritative snapshot 仍能正常清除它。
  同時覆蓋 `ServerBroadcastHub` 的 generation 傳遞。
- `Integration/GroundDropRequestGuardSmokeTests.cs`：
  在獨立 SQLite 中測零/負/超過持有的丟物數量、
  NaN 座標、裝備多件丟棄、正常部分丟物→撿回、
  重複撿取的資產總數守恆。加入 `--suite concurrency` 和 full。
- `client/tests/aoi_monster_snapshot_order_regression.gd`：
  驗證 server 重複 Spawn 與較新的 Move 交錯時，
  deferred/queued 怪物不倒退到舊 X/Y，
  不重複排入實體；AOI exit 與切圖必清快取。
  有 Godot 時由 `tests/run_protocol_checks.ps1` 執行。
- `test_smoke_suite_structure.py`：
  保護上述模組與主 Runner 的 wiring，
  但**只驗證程式結構，不能替代真正的 .NET/Godot 執行**。

此工作環境沒有 `dotnet` / `godot` 命令，且所查 GitHub
Actions job 未取得 runner steps；新增測試仍屬
`IMPLEMENTED_UNVERIFIED`。需在有 SDK/引擎的電腦執行：

```powershell
python -m unittest discover -s tests -p test_smoke_suite_structure.py -v
dotnet run --project tests/SGO.Server.SmokeTests -c Release -- --suite world
dotnet run --project tests/SGO.Server.SmokeTests -c Release -- --suite concurrency
powershell -ExecutionPolicy Bypass -File tests/run_protocol_checks.ps1
```

## 2026-11-08 延伸修復：WorldReady 舊 SELECT 不可讓已撿物復活（M-13）

上輪 M-09 只防護 A 舊地圖快照「誤刪 B 剛丟的新物」；
本輪另外防護 **A SELECT 包含物品 X → B 已從 SQLite 撿走 X → A 用
舊結果把 X 再次加入 AOI**。新的 `WorldEntityAoiSnapshotLeaseSmokeTests`
位於 `World/`，檢查：已存在/尚未索引的物品被撿走時都不能由舊
快照再 Spawn；之後的新位置/狀態不可被舊結果覆蓋；兩個 WorldReady
快照重疊時，第一個未結束前不能提早清除 tombstone；全部結束後
會清理刪除紀錄，不會永久阻止新的真正資料。

`Integration/GroundSnapshotPersistenceRaceSmokeTests` 則用**真 SQLite**
測試丟出 1 件物品 → A 讀 `GetGroundItems` → B 成功
`PickUpGroundItem` 並 Remove AOI → A 嘗試索引舊 SELECT；
要求 DB ground row=0、玩家背包數量恢復、AOI 無幽靈物件。

核心 `WorldEntitySpatialAoi` 的 scope 在 SQL SELECT **之前**開始，
在所有 snapshot upsert 完成後才釋放。新 `TryUpsertWorldEntityFromSnapshot`
同一把 AOI lock 下檢查刪除與較新異動 generation，拒絕過時
snapshot 時也不能填入 `ClientSession.GroundItems`。
刪除紀錄只在有 active snapshot 時保留，最後一個 scope dispose
會釋放，不應造成無限 memory growth。

已加入 `--suite world`、`--suite concurrency` 和 full。
目前只有檔案、測試結構接線與程式碼檢查；尚無有實際
steps 的 .NET/Godot CI PASS，狀態維持
`IMPLEMENTED_UNVERIFIED`。

### Windows 一鍵執行（不需改程式）

`tests/run_local_verification.bat` 新增以下安全模式：

```bat
REM 只在目前分支為 feature/local-server-net8 時允許 fast-forward，不會 reset
tests\run_local_verification.bat pull

REM 先 pull，再跑 Python audits、.NET build、domain/full smoke、AES Wire、
REM 10/50/100 load 與可用時的 Godot headless/真 NetworkManager E2E
tests\run_local_verification.bat pull-all

REM 只測世界 AOI/怪物與多人金錢、撿物、交易
tests\run_local_verification.bat world
tests\run_local_verification.bat concurrency

REM 需另開本機測試 Server（localhost:5678，使用隔離的 DB）
REM 真加密 WebSocket + 移動（--move-pixels 4）+ mixed combat/chat soak
tests\run_local_verification.bat load 10,50,100 60
tests\run_local_verification.bat load 100,500,1000 600
```

`load` 只對**已經啟動**的本機 `ws://127.0.0.1:5678/`
發送，會產生臨時測試帳號，不可對正式 DB/Server 執行。
結果 JSON 放在 Windows `%TEMP%`。其中實測 1000 人是需要
自行執行並取得結果的能力，不代表已在 CI 或此環境達標。

## 2026-11-08 Client 預先移動封包與撿物失敗一致性（M-15～M-16）

新增 `client/scripts/network_entity_pending_moves.gd`，
把少數併發交錯造成的 `MoveEntity` 早於 `SpawnEntity` 的
座標暫存獨立於 `network_entity_manager.gd`。
只記錄尚未有實體、沒有怪物 canonical template 的事件，
不為遠方未知物件建立場景；每個 EntityID 只保留最新 X/Y/Dir。
暫存 3 秒、最多 256 個 ID，Despawn 後的 10 秒暫存
tombstone 最多 512 個，所有資料在換圖時清除。
不在 `_process` 每個 frame 重複掃描；只在封包活動期間
定期清理。正式 Godot 測試：
`client/tests/aoi_pre_spawn_move_regression.gd`：

- 首次 Move→Spawn：使用最新位置，不跳回舊座標；
- Spawn 已排隊但 Player/Pet Node 還沒 instantiate：
  後來的 Move 仍會套到真正 instantiate 時；
- Despawn→延遲 Move→新 Spawn：
  舊的 Move 不得讓已離開 AOI 的物件復活；
- 異常大量未知 Move 不能無限放大記憶體；
- WorldReady／換圖需清除舊快取。

執行（已加入 `tests/run_protocol_checks.ps1` 的 Godot 區段）：

```powershell
godot --headless --path client --script res://tests/aoi_pre_spawn_move_regression.gd
```

`PickUpItemRequest` 在 **SQLite 已明確確認物品不存在／過期**
時清除 requester's cached ground target、讓附近觀察者
收到 Despawn，並直接回應要求者 `DespawnEntity` +
`SystemMessageFlash`。`受掉落者保護`／距離太遠等
非終止失敗不會移除物品；需要真 AES/WebSocket 的
多人競速測試確認發送順序、UI 不留 ghost 與資產不複製。

目前新增測試與程式碼均已提交但 **沒有 .NET／Godot
實際 PASS 證據**，不得將 Python 結構檢查代替真實測試。
