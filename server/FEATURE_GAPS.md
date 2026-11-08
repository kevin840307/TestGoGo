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

## 2026-11-08 本輪追蹤：多人效能與測試真實性

| 編號 | 狀態 | 本輪改動或未完成事項 | 必須取得的驗證 |
| --- | --- | --- | --- |
| M-01 | IMPLEMENTED_UNVERIFIED | `Application/World/MapAiOwnerSpatialIndex.cs`：共用 AI scheduler 對「有 target 怪物」使用 CharacterId dictionary，對 idle 怪物以玩家/寵物位置的 exact KD-tree 找最近執行 owner；小集合直接線性掃描，不限制 AOI 半徑。 | `--suite world` + 完整 shared AI integration PASS；10/50/100 人 tick P95/P99 和 allocation 實測，檢查 AI 對象/追擊結果不變 |
| M-02 | IMPLEMENTED_UNVERIFIED | `World/MapAiOwnerSpatialIndexSmokeTests.cs`：無玩家、失效座標、等距 tie、死亡角色活躍 pet、1024 owners/2000 查詢與 reference full-scan 一致、併行只讀查詢。已接入 world/full suite。 | dotnet build + world/full PASS；有真正測試結果才可稱通過 |
| M-03 | IMPLEMENTED_UNVERIFIED | 修正 Economy / AssetMutation / CrossFeature 併發測資：`Create` 預設 10,000 money，舊測試對 100/1000 只做 `AdjustMoney(+N)`，造成「資產不足、只成功一次」斷言不成立；現在用 `Fixtures/ExactMoneyFixture.cs` 將餘額設定到指定值後確認讀回值。 | `--suite concurrency` PASS；失敗時區分真正 overspend / mint 與測資初始化錯誤 |
| M-04 | TEST_GAP | Monster AI 每個 tick 仍需逐玩家讀取 `GetCharacter`，且 Combat Tick/pathfinding、DB write lock、transport queue、Client 渲染成本需 profile；不可把 AOI CPU 節省當成 100 人實測。 | 10/50/100 真 socket，near/far/進出 AOI、bytes/sec、AI tick p50/p95/p99、CPU/RAM/GC、10 分鐘及 1 小時 soak |
| M-05 | TEST_GAP | 真正的 Godot Scene/UI 串接以及同怪/同物/交易壓力下 Client 消失/重複物件和顯示一致性仍待實跑。 | Client headless + Scene/UI、斷線/重連、撿物/交易/換圖完整 E2E |
| M-06 | TECH_DEBT | `SqliteGameStore.cs` 與 `SmokeTests/Program.cs` 仍偏大。先分小模組/Fixture，待 CI 稳定且交易 contract PASS 後再按領域拆 store。 | 不改原封包/SQL 語意的逐模組重構，後續必須有全套 regression |
| M-07 | IMPLEMENTED_UNVERIFIED | `BuyAsset/SellAsset` 的持久層邊界現在拒絕無效數量/負價格，不再把 0 或負數購售量自動改成 1；一般品項 9999 上限、非堆疊品項 1 件上限；購買總價用 long 計算防整數溢位。新增獨立 `Integration/EconomyRequestGuardSmokeTests.cs` 測合法/非法/超量/重放出售與物品、金錢守恆。 | `--suite concurrency` + full + true AES/WebSocket NPC Shop Purchase/Sell 回歸 PASS |

| M-08 | IMPLEMENTED_UNVERIFIED | `WorldEntitySpatialAoi.UpsertEntity` 在位置未變時只更新最新 Spawn factory/state，不重新掃描周圍 observer。持續移動或跨 cell 時仍走完整 enter/exit reconcile。新增 `WorldEntityAoiStationaryRefreshSmokeTests.cs`，覆蓋 1000 次重複狀態更新、後進玩家取得最新資料、同 cell 移動、切圖。 | World suite 真 PASS；量測 50/100 人、500+ 怪物的 AOI update CPU / Tick P95 |
| M-09 | IMPLEMENTED_UNVERIFIED | 修正 `WorldReady` 舊 DB ground snapshot 與多人新丟物交錯時，舊快照錯誤刪掉新 ground AOI entity 的 race。AOI entity 建立時紀錄單調遞增 generation，DB SELECT 前擷取 version；舊 snapshot 只能清理擷取時已存在的 index。新增 `WorldEntityAoiSnapshotRaceSmokeTests.cs`，重現 A 查快照→B 丟物→A 清理的順序，並驗證後續新快照仍能移除真正過期物。 | World + full + Wire E2E，真多人交錯丟物／WorldReady／回圖；DB 物品/Client AOI known-set 一致 |
| M-10 | IMPLEMENTED_UNVERIFIED | Godot `network_entity_manager.gd` 針對 queued/deferred 怪物的 MoveEntity 記錄已收到移動標記；較舊的重複 SpawnEntity 不再覆蓋最新 LocX/LocY。新增 `client/tests/aoi_monster_snapshot_order_regression.gd`，覆蓋 Spawn→Move→重複 Spawn、Queue 去重、Despawn、重新進 AOI、換圖清理，並加入 Windows 正常 runner。 | Godot headless + 真 WebSocket／Scene UI 實跑，確認 monster 不跳回舊位置、無幽靈實體 |
| M-11 | IMPLEMENTED_UNVERIFIED | `DropAssetToGround` 持久層與 `DropItemRequest` 拒絕 count<=0、非單件裝備 count!=1，持久層另拒絕無效 UID/map/NaN 座標，避免非法請求悄悄消耗一件裝備。新增 `GroundDropRequestGuardSmokeTests.cs`，驗證錯誤請求不改 DB、合法部分丟棄、撿回及重複撿取的總數守恆。 | Concurrency + full + AES/WebSocket + Godot 實跑，物品總數不增不減 |

本輪不新增偽裝成功的未支援封包：已實作項目依
`packet_*_audit.py`、真 Wire、Godot E2E 交叉驗證，
PayPal/舊服點數仍為 `BLOCKED_EXTERNAL`。
CI 最新可讀到的 push run
[42457284437](https://github.com/kevin840307/SGO/actions/runs/42457284437)
仍是 job `steps=null`，表示未取得 dotnet/Godot 的執行結果；
**所有本輪新增 C# 程式和測試仍標記 IMPLEMENTED_UNVERIFIED**。最新分支也已追加 M-08～M-11；CI Runner 仍無步驟紀錄，不能推定已通過。

## P0 — 協定、多人一致性、實際驗證

| ID | 狀態 | 現況與真正剩餘缺口 | 已有自動驗證 | 完成驗收 |
| --- | --- | --- | --- | --- |
| P0-01 | IMPLEMENTED_UNVERIFIED | **真實傳輸已從單人擴成雙 Client**。AES/WebSocket 已涵蓋註冊→登入→建角→重登→選角→bootstrap→WorldReady→商城→登出，並新增兩個真實 socket 的 Party/Trade、聊天/移動、掉寶撿取、寵物/機甲、戰鬥/技能、切圖、選擇型寶箱，以及 BankTransfer、DirectTransfer、RepairItemRequest、玩家商店 AddItem/OpenShop/ViewShop/BuyItem、NPC Shop BuyNpcItem，以及 deterministic CraftItemRequest/DismantleItemRequest 的完整 wire 流程。新增 `client/tests/real_server_network_e2e.gd`，會用真正 Godot `NetworkManager` / `WebSocketPeer` 連隔離的 .NET Server，跑註冊→登入→建角→重登→選角→9 個 bootstrap sync→WorldReady ACK→nearby chat round-trip，並新增 GM seed→DropItemRequest→ChangeMap 離開→回原圖→WorldReady ground snapshot→PickUpItemRequest 的真 Godot 網路流程；仍缺真正 Scene/UI 驅動的戰鬥／切圖／交易整段互動驗證。 | `tests/SGO.Server.WireSmokeTests/Program.cs`、`client/tests/real_server_network_e2e.gd`、`client/tests/*.gd`、`test_client_network_contract.py` | 有 Godot executable 的環境跑完 headless client tests，並補真正 Scene/UI 驅動的戰鬥／切圖 E2E |
| P0-02 | IMPLEMENTED_UNVERIFIED | **多人一致性 + AOI 已實作並有自動測試，但尚缺實跑 PASS**。既有 10 人同撿一物、同怪擊殺、Trade duplicate/final-confirm/cancel-disconnect、SharedMapAI owner arbitration、NPC/玩家商店、銀行、直接轉帳、跨功能 Trade/Shop/Craft money/asset races 皆保留；新增 PlayerSpatialAoi（1800 enter / 2200 exit）與 WorldEntitySpatialAoi（monster/ground 2600 enter / 3000 exit），均使用 1024 spatial grid + per-observer known-set，不以每人掃全圖實作。玩家 Move/附近聊天/狀態/外觀/技能視覺/寵物伴隨事件/PlayerShop board 按 Player AOI 投遞；怪物 Spawn/Move/State/Target/Damage/Heal/Skill/Death/Respawn 與玩家/怪物地面掉落按 World AOI 投遞。AOI enter 一律先 Spawn、exit 一律 Despawn；Death/Pickup/Expire 清 known-set；WorldReady 依 runtime/SQLite authoritative state 重建。地面物 DB row 不因離開 AOI 刪除，且 DB snapshot 會 purge stale AOI cache，避免空圖期間過期物回圖後復活。PlayerShop View/Buy 另要求 buyer 真正看得到 seller，遠距離 forged packet 不得改 money/item。dynamic/GM 9M monster EntityID 已加入 mapId，避免不同地圖 AOI key collision。SharedMapAI driver 也套同一 monster visibility，避免 scheduler owner 收整圖怪物封包。 | `PlayerSpatialAoiSmokeTests.cs`、`WorldEntitySpatialAoiSmokeTests.cs`、`PlayerShopAoiSmokeTests.cs`、`ConcurrencySmokeTests.cs`、`MonsterConcurrencySmokeTests.cs`、`SharedMapAiConcurrencySmokeTests.cs`、WireSmoke / WireLoadProbe AOI phase、Godot AOI regressions | Smoke/Wire/Godot 真正 PASS；10/50/100 人 near/far AOI 0 leak + re-entry Spawn；同怪/同掉物/交易不重複；長時間 mixed soak 無 AOI ghost/missing entity |
| P0-03 | TEST_GAP | **CI 基礎設施仍擋住實際 PASS 證據**。最新再次檢查 AOI 文件 commit `08af599e` 的 2026-11-08 push run `42456025684`，job `128655108427` 仍在取得 Runner 前結束且 `steps=null`；因此 workflow 顯示 `failure` 仍不是 checkout/build/test failure，也不能拿來宣稱新增 C#/Godot/AOI 測試已 PASS。 | workflow 與 `tests/run_protocol_checks.ps1` 已包含 strict audits、build、domain/full smoke、world/concurrency、Wire E2E、10/50/100 WireLoadProbe（含 WorldAoi near/far/re-entry）+ MixedSoak、可用 Godot 時的真 NetworkManager E2E / AOI regressions、coverage reports | 取得至少一筆有實際 steps 的 run，所有 Python/.NET/Wire/Godot（有安裝時）檢查全 PASS |
| P0-04 | TEST_GAP | 固定 Type/Action、請求 schema、40+ 關鍵 Server→Client response shape、nested anonymous response、`ItemChoiceRequired` / `UseItemChoiceRequest`、Trade/Shop/Pet/Mall/Party/Club 動態封包 helper 的 Client 組裝契約、Godot packet ownership 已守門；`DynamicActionSchemaCoverageSmokeTests` 會 reflection 掃完整 ActionRules。`RuntimeResponseContractSmokeTests` 已直接序列化真 Router/SQLite 回應，驗證 LoginResponse.Characters 與 Inventory/Equip/Pet/Horse/Skill/Club/Buff/Quest bootstrap runtime 型別，並覆蓋 AUTH_REQUIRED / INVALID_PACKET / WORLD_REQUIRED / UNHANDLED_PACKET。再新增 `MarketplaceResponseContractSmokeTests`：真 TradeService/PlayerShopService + SQLite + broadcast queue 驗證 `TradeSync` 雙方 My/Other 視角、金額/鎖定狀態、一般物品與寵物 `ExtraData`，以及 `ShopSync` / `ShopViewResponse` 的動態 listing 欄位；另做 10 buyer 搶 5 件的 Service-level race，要求只 5 成交、物品/金錢守恆、售罄後 ShopSync 清空且商店原子關閉。Godot headless `network_response_contract_regression.gd` 走 AES-CBC decode/dispatch 並包含 Trade/Shop 動態 payload；另新增真 socket `real_server_network_e2e.gd`，並讓 `WorldReadyResponse` 有 typed ACK signal 可直接驗證。主要剩餘是實際 runner/Godot PASS 證據，以及未來新增特殊 payload 時持續擴充 runtime contract。 | `packet_*_audit.py`、`RuntimeResponseContractSmokeTests.cs`、`MarketplaceResponseContractSmokeTests.cs`、`test_client_network_contract.py`、`client/tests/network_response_contract_regression.gd` | 動態 payload 有 runtime contract/E2E；多人交易/商店視角一致；失敗路徑回 typed `PacketError` 或既有相容失敗 response；取得實際執行 PASS |

### 2026-11-08 本輪補強：跨功能多人資產競態

- **雙向轉帳**：`EconomyConcurrencySmokeTests.ConcurrentBidirectionalTransfersConserveMoney` 以兩位玩家各 1,000 金錢、20 個同步工作（各方向 10 次）驗證相反方向轉帳不死鎖，最終 A=1,040、B=960、總額=2,000。
- **維修原子性**：新增獨立 `AssetMutationConcurrencySmokeTests.cs`。同角色只有 100 金錢，兩件裝備同時各花 60 維修；只能成功一件、餘額必須 40，失敗那件裝備的耐久修改必須一起 rollback；另外同一 UID 兩個並發維修請求只能成功/扣款一次。
- **跨功能交易/製作**：獨立 `CrossFeatureConcurrencySmokeTests.cs` 已覆蓋 A↔B Trade + C→A DirectTransfer（A=105、B=110、C=85、總額 300）、同一買家 Trade + PlayerShop Buy（Buyer=75、Peer=110、Seller=115、總額 300、商品只移轉一次），以及 Craft + incoming DirectTransfer；後者要求 30 金錢製作成本只扣一次、轉入 15 正確疊加、材料只消耗一次並只產生一份成品。
- **DB 修正**：`RepairAsset` 使用 `damage < repairedDurability` 條件式更新，並在同一 transaction 內做 `money=money-cost AND money>=cost`；扣款失敗時裝備修改一起 rollback。`CommitCraft/CommitDismantle` 也改為條件式 delta 扣款，不再用讀取後的絕對餘額覆寫。核對 Microsoft.Data.Sqlite 8.0.11 後確認一般 `BeginTransaction()` 已是非 deferred 的 serializable transaction，因此移除曾暫時加入的 `UPDATE money=money` no-op，避免增加多人 DB write 開銷。
- **真 Wire**：`SGO.Server.WireSmokeTests` 新增 `RepairItemRequest`，以真 AES/WebSocket 驗證 15113 裝備從 Damage=0 維修到 50、Server 扣款並回傳 `SyncMoney` + `EquipSync`。
- **PlayerShop 生命周期**：`PlayerShopLifecycleConcurrencySmokeTests.cs` 現在同時重複競速 `BuyItem vs CloseShop` 與 `BuyItem vs Disconnect`。不管 Buy、Close 或 Disconnect 誰先取得 Service lock，最後都只能是完整成交或完整關店，商品總數/金錢守恆，且 Seller 不得殘留任何 `InShop=1` row。另修正真正 WebSocket `DisconnectPresence()` 原本沒有關閉玩家商店的缺口。
- **Client 維修契約**：新增獨立 `tests/test_client_repair_contract.py`，守住 `repair_window.gd` 的 Equip→`ItemID/Equip`、MECHA→`EquipUID/HorseEquip` 映射、滿耐久不送封包，以及 NetworkManager 的 `RepairItemRequest` schema；已加入 `run_protocol_checks.ps1`。
- **Ground-item map lifecycle**：C# 真 AES/WebSocket Wire 與 Godot `real_server_network_e2e.gd` 都新增「丟物 → 離開原地圖 → 回原地圖 → `WorldReady` 收到同一 `EntityID` 且 `IsSnapshot=true` → 正常 `PickUpItemRequest`」；C# 雙 client 版本再驗證同圖 peer 收到 `DespawnEntity`。這直接守住換圖回來地上物品不能消失的回歸。
- **Session identity cleanup**：新增 `SessionIdentityCleanupSmokeTests.cs`，驗證已進世界的 socket 直接重新登入另一帳號，以及 `LogoutRequest`，都會在舊 AccountID/CharacterID 被清空前先執行 `DisconnectPresence`；舊玩家 Presence、Party/Trade、PlayerShop 記憶體狀態與 DB `InShop` 保留列都必須清乾淨。
- **PlayerShop crash recovery**：`SelectCharacterRequest` 在第一次資產 bootstrap 前呼叫 `RecoverStaleReservations`；若角色沒有任何 live in-memory shop，會解除 crash/restart 遺留的 DB `InShop=1`。測試會人工製造孤兒保留列，再重新登入選角，要求第一個世界狀態前已恢復可用資產。
- **仍未完成**：上述新增程式目前仍缺有實際 steps 的 CI/.NET PASS 證據。PlayerShop disconnect vs Buy、正常 disconnect/logout/relogin cleanup、以及 crash 後孤兒 `InShop` recovery 都已補；同一 socket 的封包處理與 transport disconnect 由 `lock(state)` 串行化，因此「同角色同時切圖又撿取」不是實際平行 race。ground-item 的切圖離開/返回 snapshot + pickup 已補真 Wire E2E；目前剩下真 Godot Scene/UI 的維修/交易/切圖互動、實際掉線/重連 UI 狀態，以及 10/50/100 人長時間 soak。
- **執行**：`dotnet run --project tests/SGO.Server.SmokeTests -- --suite concurrency`，再執行 `dotnet run --project tests/SGO.Server.WireSmokeTests -- <server-url>`；任何一項未 PASS 均不可把 P0-02 標示完成。

### 2026-11-08 本輪補強：Server AOI

- **Player AOI**：`PlayerSpatialAoi` 使用 map + 1024 grid，1800 enter / 2200 exit。遠方玩家不再互收每一步 Move；靠近補完整 Spawn、離開補 Despawn。附近聊天、人物狀態/外觀/技能視覺、稱號、Party name color、PlayerShop board、寵物/伴隨實體事件都沿用 owner Player AOI。
- **Companion AOI known-set**：ServerBroadcastHub 保存 active companion 的完整 Spawn + 最新位置。玩家進 AOI 時固定 owner Spawn → companion Spawn；離開時 companion Despawn → owner Despawn；Move 更新 snapshot，Despawn/map/logout/disconnect 清 snapshot，避免新觀察者先收到未知 pet Move/Attack 或 owner 離開後殘留孤兒寵物。Server-side persisted map reconcile 也會同 tick 清舊 AOI，不再多留一個 250ms world tick。
- **Monster / Ground AOI**：新增 `WorldEntitySpatialAoi`，observer/entity 各自有 spatial grid 與 reciprocal known-set，2600 enter / 3000 exit。玩家移動只掃附近 entity cells，怪物移動只掃附近 observer cells，不把頻寬最佳化換成 O(players×monsters) CPU 全掃。
- **怪物事件完整性**：Spawn → Move/State/Target/Damage/Heal/Skill → Death/Despawn 順序受 known-set 保護；SharedMapAI driver 自己也受 visibility gate。GM/dynamic monster ID 改成 `9_000_000 + mapId * 10_000 + sequence`，避免跨 map key collision。
- **地面物不丟失**：玩家 Drop、怪物掉寶、WorldReady snapshot、Pickup、Expire 都走 persisted ground AOI。SQLite row 是唯一真相；離開 AOI 只省傳輸不刪資料；late entrant 的保護/存在倒數用最新時間重算；DB snapshot 會 purge stale AOI cache，避免回圖突然出現已過期的幽靈物。
- **PlayerShop**：View/Buy 必須 seller 在線、同地圖且 buyer AOI 可見 seller；遠距離手工封包會拒絕且不可改 money/item。Open/Close/SoldOut/Disconnect board 只送 nearby observers。
- **刻意不套距離 AOI**：玩家自身 request/response、Party/Trade/Club 定向/群組訊息、全服/global announcement、DB transaction、NPC 靜態一次性 map snapshot。NPC 保留完整 snapshot 是相容性選擇，因其可能供 quest/minimap/portal 使用且不是高頻流量。
- **Server 測試**：`World/PlayerSpatialAoiSmokeTests.cs`、`World/WorldEntitySpatialAoiSmokeTests.cs`（含 1000 entities、1000 observers、concurrent movement、stale ground purge）、`Integration/PlayerShopAoiSmokeTests.cs`。
- **Client / Wire**：`ground_item_aoi_regression.gd`、`multiclient_render_dedup_regression.gd` 已加入 normal protocol runner；WireLoadProbe 新增 10/50/100 真 client near/far/re-entry world AOI phase。
- **仍未驗收**：目前 Actions 仍無 runner steps，本環境也無 .NET/Godot，所以只能標 `IMPLEMENTED_UNVERIFIED`；真正 10/50/100 長時間 throughput/CPU/RAM 與 Godot Scene/UI 實測仍是 release gate。

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
| P2-03 | IN_PROGRESS | **50/100 人性能容量是目前多人版本驗收基線，不是未來擴充**。除了原本 movement/chat fanout、同 row contention、DB latency、同怪 death-conservation 與 MixedSoak，WireLoadProbe 現新增 WorldAoi phase：每個 10/50/100 client scenario 真正分 near/far 群，在 near group 旁 spawn monster，near 必須收到 Spawn、far 必須 0 leak，再把一位 far client 移回近區並要求補 Spawn；之後恢復 compact group 繼續 mixed move/chat/AttackEntity soak。movement/chat expected delivery 也依 PlayerSpatialAoi 距離計算，不再假設同地圖 N² fanout。`/health` 新增 `aoi.playerReady`、`aoi.worldObservers`、`aoi.worldEntities`，MixedSoak 與 working set/GC/CPU/SharedMapAI tick 一起採樣並驗證 AOI population。仍需有 runner 的實際 10/50/100 報告與 10 分鐘/1 小時長 soak 數據。 | Shared AI 每 map 單一 250ms scheduler；Player/World spatial grids 避免 O(players×entities)；client ground visual AOI + render dedup regressions 已加入 normal runner | 10/50/100 人報告含 AOI near/far leak=0、re-entry Spawn、tick P50/P95/P99、DB latency、bytes/sec、CPU/RAM；長 soak 不出現 ghost/missing entity 或 backlog |
| P2-04 | TECH_DEBT | **其他 DB Provider**。Domain/Application 已經透過 store interfaces 隔離，但目前 production persistence 只有 SQLite | 不宣稱 PostgreSQL/其他 DB 已支援 | 真有替換需求時新增 provider，跑同一組交易/掉物/資產/重連 contract tests |

## 已完成的測試結構

目前不再把新案例全部塞回原本約 9,518 行的 `Program.cs`。已拆出：

- `Protocol/`：Request / Action / Identity / Prelogin schema；DynamicActionSchemaCoverageSmokeTests 會自動跑完整 ActionRules 的合法/錯型別 runtime 驗證。
- `Protocol/RuntimeResponseContractSmokeTests.cs`：真 Router/SQLite 動態 response JSON 型別、bootstrap Dictionary 與 typed PacketError runtime contract。
- `World/BlockingMapSmokeTests.cs`：Blocking、穿牆、A*。
- `World/PlayerSpatialAoiSmokeTests.cs`：玩家 AOI enter/exit、map/disconnect、1000 sparse players、concurrent movement。
- `World/PlayerCompanionAoiSmokeTests.cs`：companion Spawn/Move snapshot、observer delivery、Despawn/map cleanup，以及 future AOI enter 不復活孤兒 pet。
- `World/WorldEntitySpatialAoiSmokeTests.cs`：怪物/地面物 known-set、1000 entities/observers、並行移動、stale ground authoritative purge。
- `Integration/PlayerShopAoiSmokeTests.cs`：遠距 View/Buy 拒絕、靠近可交易、離開再拒絕、資產/金錢不變量。
- `Announcements/AnnouncementSmokeTests.cs`。
- `GameData/CurrentClientCatalogSmokeTests.cs`。
- `Integration/ConcurrencySmokeTests.cs`：10 人同掉物 + Trade duplicate confirm / duplicate Lock。
- `Integration/EconomyConcurrencySmokeTests.cs`：NPC/出售/銀行/玩家商店/直接轉帳，以及雙向同時轉帳守恆。
- `Integration/AssetMutationConcurrencySmokeTests.cs`：同角色多件裝備同時維修的資產 + 金錢 transaction 原子性。
- `Integration/CrossFeatureConcurrencySmokeTests.cs`：Trade + DirectTransfer、Trade + PlayerShop Buy、Craft + DirectTransfer 同時碰同一角色時的跨功能資產/金錢守恆。
- `Integration/PlayerShopLifecycleConcurrencySmokeTests.cs`：BuyItem vs CloseShop / Disconnect 的 Service/DB 生命周期競態，驗證關店、InShop 清理、資產與金錢守恆。
- `Integration/SessionIdentityCleanupSmokeTests.cs`：同 socket relogin、LogoutRequest，以及 crash/restart 孤兒 InShop row 的選角恢復。
- `SGO.Server.WireSmokeTests` ground lifecycle：地面物品跨 map leave/return 仍由 WorldReady snapshot 重建，返回後可正常拾取並廣播 despawn。
- `client/tests/real_server_network_e2e.gd` ground lifecycle：同一流程再經 production Godot `NetworkManager` / `WebSocketPeer` 驗證。
- `Integration/MarketplaceResponseContractSmokeTests.cs`：TradeSync 雙方動態 view、ShopSync/ShopViewResponse listing runtime contract，以及 PlayerShopService 10 buyer/5 item race。
- `Integration/TradeTerminalRaceSmokeTests.cs`：受控攔截 DB commit，驗證取消／斷線在提交中不會同時產生 TradeCancel 與 TradeSuccess。
- `Integration/MonsterConcurrencySmokeTests.cs`：10 人同怪死亡 race。
- `Integration/SharedMapAiConcurrencySmokeTests.cs`：10 人/10 怪 SharedMapAiTicker ownership。
- `Integration/SessionLifecycleSmokeTests.cs`：disconnect/reconnect/map transition race。
- `Integration/BoosterSmokeTests.cs`、`RecoveredBoxSmokeTests.cs`、`ItemUseClassificationSmokeTests.cs`、`WaspPackageSmokeTests.cs`、`CosmeticTransformationSmokeTests.cs`。
- `tests/SGO.Server.WireSmokeTests/`：真 AES/WebSocket，現已含兩 client Party/Trade。
- `tests/SGO.Server.WireLoadProbe/`：10/50/100 個真 AES/WebSocket client，Player AOI movement/chat conservation、WorldAoi near/far 0-leak + re-entry Spawn、round latency、wire throughput、同怪 AttackEntity death-conservation 與 MixedSoak health/AOI 採樣。
- `client/tests/monster_chase_regression.gd`、`perf_regression_smoke.gd`、`network_response_contract_regression.gd`：Godot headless regression；Windows runner 有 Godot 時自動執行，後者直接餵 AES-CBC Server response 驗證 client dispatcher。
- `client/tests/real_server_network_e2e.gd`：隔離 Server 啟動後，用 production NetworkManager/WebSocketPeer 做真 Godot login/create/select/bootstrap/world-ready/chat E2E；沒有 Godot executable 時明確 skip。
- `tests/SGO.ItemCoverageReport/`：scripted item 全量分類，`--strict` 未知效果守門。
- `tests/SGO.MappingCoverageReport/`：歷史 ID/名稱 remap、collision、skipped/unmapped 報告。
- `tests/quest_host_audit.py --strict`：1,566 份 Quest host surface 守門。
- `test_smoke_suite_structure.py`：防止上述模組悄悄被移回 monolith，並守住真雙 Client Wire flow。


## Server 診斷與本機管理 UI

目前 Server 已有統一的 in-memory diagnostics ring buffer（最近 500 筆）與 `/admin` 本機管理頁，並新增以下 operational diagnostics：

- WebSocket connect/disconnect、非法 message、超大 packet、解碼錯誤。
- Logging level 已明確區分：Release/Production 預設 `SGO=Information`，不輸出 Debug/Trace；Development 由 `appsettings.Development.json` 覆寫為 `SGO=Debug`。Warning/Error 與必要 Information 不依賴 Debug，因此正式版仍保留異常線索。
- `PACKET_HANDLER_FAILED`、`INVALID_PACKET`、`UNHANDLED_PACKET`、未登入操作、`SLOW_PACKET`。
- Shared Map AI：慢 tick、respawn/tick exception、單圖玩家/怪物壓力門檻。
- Broadcast：單連線 queue backlog（>=500）節流告警。
- Trade：交易中重入、雙方 final confirm 競態、commit failure。
- Ground item：搶撿競態（已不存在/已消失）與 drop mutation rejected。
- Database：packet path 發生 `SqliteException` 時分類為 DB failure。
- Runtime resource：Working Set >= 1 GB、1 分鐘內成長 >= 256 MB、Gen2 GC 壓力；每分鐘最多記一筆，避免 log storm。
- counters 已同步顯示於 `/admin`：AI slow、map pressure、broadcast backlog、trade/pickup conflict、DB/world tick/resource warnings。
- `/admin` 已整理為正式 Server Console：Runtime、Shared Map AI、GameData、地圖分布、在線玩家、事件 Level/關鍵字篩選、全服公告與 2 秒自動刷新。
- 管理 API 與頁面仍只允許 loopback，外部玩家無法直接存取。

自動測試新增 `Integration/ServerDiagnosticsSmokeTests.cs`，守住 counters 與 500 筆 ring buffer 行為。真正的 CPU/RAM threshold 與長時間 backlog/GC 情境仍應由 50/100 人 WireLoadProbe / soak 驗收。


## 「全部補齊」目前的邊界

**Repository 內可安全推定的固定封包、主要功能、競態修正與自動化守門會繼續補。**
但以下兩類不會用猜測偽裝成完成：

1. **外部服務**：PayPal、舊服點數來源，沒有 credentials/source 就只能保持明確失敗。
2. **原始語意不足**：描述只說「任選/隨機」但沒有可靠選項流程或原始機率的舊道具，不會擅自給獎；會留在 `CHOICE_REQUIRED`，直到 Client 選擇流程/可靠資料補上。

完整 wire route 仍以自動產出的 contract reports 為準。
