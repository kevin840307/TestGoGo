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
    IdentitySchemaSmokeTests.cs
    PreloginPacketSmokeTests.cs
  World/
    BlockingMapSmokeTests.cs
  Announcements/
    AnnouncementSmokeTests.cs
  GameData/
    CurrentClientCatalogSmokeTests.cs
  Integration/
    ConcurrencySmokeTests.cs
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

重要的多人情境已獨立化：

- `ConcurrencySmokeTests`：10 人同時撿同一掉物只能成功 1 人；重複/並行 FinalConfirm 交易只提交一次；重複 Lock 不會清除已確認狀態。
- `TradeTerminalRaceSmokeTests`：用 DispatchProxy 暫停 DB commit，模擬取消與斷線撞上提交；終態僅能是一次成功，不能先發 Cancel 後發 Success。
- `MonsterConcurrencySmokeTests`：10 人同時攻擊同一怪物，死亡/掉寶/獎勵不重複。
- `SharedMapAiConcurrencySmokeTests`：10 人/10 怪同地圖，SharedMapAiTicker 每張地圖每 pass 只 tick 一次，怪物 owner arbitration 唯一。
- `SessionLifecycleSmokeTests`：disconnect/reconnect/map-transition race 與 current-session ownership。

## Wire E2E 現況

`tests/SGO.Server.WireSmokeTests/Program.cs` 使用與 Godot 相同的 ClientProtocol/AES WebSocket，不直接呼叫 Router。現已涵蓋：

- 公告、錯誤 schema、未登入 AUTH_REQUIRED。
- 註冊、登入、三角色 slot、建角、重登、選角。
- EnterWorld bootstrap、WorldReady、商城 list、登出。
- 第二個真 WebSocket client 完成相同登入/進圖。
- Client A `SyncPosition` → Client B 收 `MoveEntity`。
- Client A nearby `ChatMessage` → Client B 收 `ChatBroadcast`。
- Party Invite → Accept → 雙方 `PartySync`。
- 選擇型寶箱 `UseItemRequest` → `ItemChoiceRequired` → `UseItemChoiceRequest`，涵蓋偽造 ItemIndex、合法選項、UID 重放。
- Trade Invite → Accept → Lock → `TradeFinalConfirmReq` → 雙方 `TradeSuccess`。
- 多人 server-push 允許 interleaved packet，不假設「下一包一定是 response」。

`test_smoke_suite_structure.py` 會守住雙 Client、MoveEntity、ChatBroadcast、Party、Trade 等關鍵字，避免 E2E 不小心退回單人版本。

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
```

### Godot headless

Windows runner 會優先讀 `GODOT_BIN`，否則尋找 `godot` / `godot4`。找到時自動執行：

```powershell
godot --headless --path client --script res://tests/monster_chase_regression.gd
godot --headless --path client --script res://tests/perf_regression_smoke.gd
godot --headless --path client --script res://tests/item_choice_regression.gd
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
