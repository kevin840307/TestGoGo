# Client ↔ Server 封包契約檢查

## 執行方式

在 Repository 根目錄執行：

```bash
python3 -m unittest discover -s tests -p 'test_packet_contract_audit.py' -v
python3 -m unittest discover -s tests -p 'test_packet_schema_audit.py' -v
python3 -m unittest discover -s tests -p 'test_packet_response_audit.py' -v
python3 tests/packet_contract_audit.py --strict --output packet-contract-report.json
python3 tests/packet_schema_audit.py --strict --output packet-schema-report.json
python3 tests/packet_response_audit.py --strict --output packet-response-report.json
dotnet build server/SGO.Server.csproj -c Release
dotnet run --project tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj -c Release
# 在另一個終端機啟動 server：dotnet run --project server/SGO.Server.csproj -c Release
dotnet run --project tests/SGO.Server.WireSmokeTests/SGO.Server.WireSmokeTests.csproj -c Release -- ws://127.0.0.1:5678/
```

GitHub Actions `Server build` 工作流程已納入前兩項檢查，且會上傳 `packet-contract-report.json`。報告的 `routes[]` 會逐一列出封包 `direction`、`type`、`status`（`ROUTED`／`PARTIAL`／`MISSING_SERVER_HANDLER`／`MISSING_CLIENT_HANDLER`）與說明；`ROUTED` 只代表路由存在，不代表功能或 E2E 已通過。

## 契約稽核內容

1. **Client → Server**：掃描 `client/**/*.gd` 中的固定字串 `Type`，對照 `PacketRouter` 的封包 `case`、登入前分支，以及 `IClientPacketHandler.PacketType`。
2. **Server → Client**：掃描 `server/**/*.cs` 中的 `Type = "..."`，對照 `network_manager.gd` 的 `match packet_type`。
3. **動態 Action**：檢查 `send_trade_action`、`send_shop_action`、`send_pet_action`、`send_mall_action`、`send_party_action` 的固定 Action 字串有無實際路由。
4. **錯誤回應**：未知 `Type` 回傳 `PacketError(Code=UNHANDLED_PACKET)`；未登入操作回傳 `PacketError(Code=AUTH_REQUIRED)`；Handler 拋出例外時回傳 `PacketError(Code=HANDLER_FAILED)`，不暴露內部堆疊。
5. **Client 診斷**：收到 `PacketError` 顯示訊息；收到未知 Server Type 留下 `[UNHANDLED_SERVER_PACKET]`，單一 Type 只警告一次。

## 新增：必要欄位與 JSON 型別驗證

- `server/Protocol/PacketSchemaValidator.cs` 在已登入且通過目前連線驗證後，先驗證常用可修改資料的請求，再交給 Handler。
- 目前定義了 **40 種**請求的最低必要欄位，包括 `UseSkill`、`SyncPosition`、`BankTransfer`、`TradeReply`、`ShopAction`、`PetAction`、`UpdateShortcut` 等。
- 整數不接受字串、布林值不接受 `"true"` 等字串；必要欄位缺失會回傳 `PacketError(Code=INVALID_PACKET)`，不呼叫修改角色或資產的 Handler。
- `tests/packet_schema_audit.py` 從 C# 規則擷取每種必要欄位，檢查 Godot 發送的**固定格式 Dictionary** 是否提供相同欄位；報告包含每個 Type 的 `MATCHED`／`FIELD_MISMATCH`／`NOT_FOUND` 與範例檔案。
- 預留舊 Client 的非必要欄位（例如沒有目標的技能）為可省略，不強制新協定覆蓋舊協定。
- Python 靜態檢查**只比對必要欄位存在**，實際欄位的數值型別由 Server 執行時驗證與 C# SmokeTests 測試。未涵蓋的封包與 Server → Client 事件欄位還需要擴充。
- CI 的報告 `packet-schema-report.json` 將與 `packet-contract-report.json` 一同上傳；若 GitHub Actions 尚未能啟動 Runner，需另外在本機執行上述命令，不能宣稱 CI 已通過。

## 持續補強（2026-11-07）：數值範圍、UID 與動態 Action

- Runtime schema 現涵蓋 **40 種 Client 封包 Type**；其中直接交易的 `TradeAddItem`、`TradeRemoveItem`、`TradeSetMoney`，也會驗證必要欄位。
- `ShopAction` 另對 **6 種操作**實施分支欄位檢查：`BuyNpcItem`、`SellNpcItem`、`AddItem`、`RemoveItem`、`BuyItem`、`ViewShop`。不會要求 `CloseShop` 帶購買欄位。
- `Int32` 欄位必須可以由 `TryGetInt32` 解析；貨幣、角色 ID、物品 UID 等需要長整數的欄位使用 `TryGetInt64`，避免溢位後由 Handler 靜默回落預設值。
- `DismantleItemRequest.ItemUIDs` 必須是陣列，每筆都必須為 `Int64` 數字，限制單次至多 512 筆。
- Server 的銀行轉移、裝備、使用物品與 NPC 賣物已將 UID 讀取修正為 `Int64`。
- `packet_schema_audit.py` 現可讀取 `send_trade_action("...", {...})` 和 `send_shop_action("...", {...})` 這些 Client 動態呼叫的欄位，並與 Server 對應規則比對。
- 測試已補整數溢位、分解 UID 型別、缺少交易金額與商店數量、錯誤賣價等回歸案例。

這仍只是**最低必要欄位驗證**。所有 Server → Client 回傳欄位、數值業務約束、真正 Godot UI E2E、多人競態與商城／舊服外部資料源均未宣稱完成；CI 必須實際執行並通過才能宣稱測試 PASS。

## 新增：Server → Client 回應欄位與寵物 Action 契約

- `tests/packet_response_audit.py` 檢查 **16 種重要 Server 回應 Type**，逐一檢查所有固定格式的 C# 匿名回應宣告都包含最低必要欄位，並確認 Godot `network_manager.gd` 有接收分支。包含登入／註冊、錯誤回應、WorldReady、快速鍵、背包／裝備／寵物／機甲／技能同步、SkillCast、EntityDamage／EntityHeal／EntityDeath／DespawnEntity。
- `tests/test_packet_response_audit.py` 覆蓋固定回應缺欄位、Client 分支遺漏、Server 封包宣告遺漏等情況。
- `PetAction` 新增 **12 種 Action 專屬欄位規則**（Summon、Recall、AddLife、ResetStat、Rename、Equip、Unequip、Feed、SetMainSkill、AllocateStat、SetCombatMode、CommandAttack）；前 10 種透過 `send_pet_action` 傳 PetUID，後 2 種為不同封包格式。
- `packet_schema_audit.py` 也比對 `send_pet_action` 的額外欄位與直接 `PetAction` Dictionary。動態商店、寵物加總共有 **18 種 Action 規則**。
- 寵物操作的 PetUID 和寵物裝備 ItemUID 在 Server 改用 `Int64`，防止持久化識別值超過 Int32 時被錯誤解析。
- `ClientProtocol.Int/Long/Str` 在不合法 JSON 欄位（包括非數字、非物件）時安全使用 fallback，避免非必要欄位型別錯誤直接拋出例外。
- CI 現會執行新增的回應稽核、Python 單元測試，並上傳 `packet-response-report.json`。WebSocket smoke 的管線已加入 `pipefail`，避免 `tee` 掩蓋失敗。

這是**靜態來源與必要欄位檢查**，不等於所有回傳欄位或業務邏輯的完整型別測試，也不是 Godot 實機端到端測試。

## 2026-11-07 契約再補強：戰團／商城 Action 與更多回應

- `ClubAction` 已增加 **6 種 Action** 的必要欄位型別檢查：邀請、接受邀請、更新公告、捐金幣、捐經驗及研發。清空公告允許 `ClubMsg=""`，避免破壞原本 UI。
- `MallAction` 已增加 **5 種 Action** 的必要欄位型別檢查：購買商品、建立 PayPal 贊助請求、查詢舊服點數、舊服點數提領、領取獎勵。這**不代表**金流或舊服資料來源已接通，Server 仍照原本策略拒絕未設定服務，不扣款、不轉點。
- `Client` 的 `send_mall_action`、戰團 `ClubAction` 固定 Dictionary 也加入 `tests/packet_schema_audit.py` 的欄位比對；新增 Python fixture 測試，驗證缺少商城數量會正確檢出。
- 戰團目標 DBID、金幣與經驗捐款的 Router 參數改用 `Int64`，避免 `Int32` 溢位後被當成 0。
- Server → Client 重要回應契約由 **16 種擴充到 27 種**，新增商城／PayPal／舊服點數／製作／分解／組隊／戰團／交易／排名回應。靜態分析檢查固定匿名封包必要欄位與 Client 接收入口，**不代表值的型別、封包順序或實際功能已測通**。
- 目前共 **40 種請求 Type Schema、34 種動態 Action Schema、27 種關鍵 Server 回應 Schema**。這些是納入檢查的規則數量，而不是整個遊戲所有封包的百分比。

## 2026-11-07 補充：組隊、戰團建立與可省略的戰鬥欄位

- `PartyAction` 的 Invite、Accept、Decline、Kick 新增 `TargetDBID (Int64)` 必要欄位；Create、Leave、Disband 不需指定目標，保留舊 Client 行為。
- `ClubAction(Create)` 新增非空的 `ClubName` 驗證，避免空名稱進入資料庫處理。
- `tests/packet_schema_audit.py` 同時檢查 Client 的 `send_party_action` 送出格式（Helper 固定包含 `TargetDBID`），新增對應 Python 單元測試。
- 針對 `UseSkill` 的 TargetID、TargetType、TargetX、TargetY，以及 `AttackEntity.TargetType`、`PartyAction.TargetDBID`、`TradeAddItem.ItemIndex`：**可以省略，但若有帶就必須符合型別**。無目標技能不會被當作封包錯誤。
- `tests/run_protocol_checks.ps1` 可在 Windows PowerShell 5.1／PowerShell 7 一鍵執行 Python 三組契約單元測試、三組靜態稽核、.NET Build、Server SmokeTests 和本機 AES/WebSocket 真實收發測試。會清理自行啟動的 Server，並將報告與 Server 日誌存放於暫存目錄；本腳本不會啟用商城金流、舊服點數或完成真正 Godot 多人 E2E。

本機執行：

```powershell
powershell -ExecutionPolicy Bypass -File tests/run_protocol_checks.ps1
```

## 真正的網路封包整合測試

`tests/SGO.Server.WireSmokeTests/` 是**非破壞性**的 .NET WebSocket client，使用與 Godot 一致的 AES/Base64 封包格式，連至正在執行的伺服器，驗證：
- AnnouncementListRequest → AnnouncementListResponse（封包／欄位形狀，**不保證有公告內容**）
- 錯誤註冊欄位 → RegisterAccountResponse（失敗，不建立帳號）
- 缺少登入欄位 → LoginResponse（失敗，不建立會話）
- 未登入 UseSkill → PacketError(AUTH_REQUIRED)
- 非物件 JSON → PacketError(INVALID_PACKET)

CI 會在 .NET Build + SmokeTests 後啟動 localhost Server 執行 WireSmokeTests，並保留 `wire.log`／`server.log`。這驗證了實際 WebSocket 加密收發，但**不是 Godot UI 的 E2E**、不測真實多玩家，也不會啟用付費商品或舊服點數。

## 此工具不能證明的事項

靜態稽核 **不等於** 封包功能全部完成：若 Type 使用動態組合、欄位型別不一致、伺服器 Handler 只回傳占位資訊、或廣播發生競態，靜態對照仍可能通過。需要另行補足真實 Godot WebSocket E2E、多人同步測試、Schema 欄位驗證及壓力測試。

## 已知刻意未開通的功能

- 商城 `MallAction(BuyItem)`：目前商品表未設定，拒絕扣點。
- `MallAction(CreatePayPalDonation)`：未設定 PayPal 金流，不建立付款訂單。
- `MallAction(QueryLegacyPoints / WithdrawLegacyPoints)`：未設定舊服點數資料來源。
- `AnnouncementListRequest`：目前回傳空清單。
- 部分 `UseItemRequest` 物品效果與舊 Lua 任務仍需補充實作。

這些都不應使用假的「Success=true」或發放資產方式補齊。後續真正接入功能時應補相應的契約、資料庫交易及 E2E 測試。
