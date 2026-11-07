# Client ↔ Server 封包契約檢查

## 執行方式

在 Repository 根目錄執行：

```bash
python3 -m unittest discover -s tests -p 'test_packet_contract_audit.py' -v
python3 -m unittest discover -s tests -p 'test_packet_schema_audit.py' -v
python3 tests/packet_contract_audit.py --strict --output packet-contract-report.json
python3 tests/packet_schema_audit.py --strict --output packet-schema-report.json
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
- 目前定義了 **19 種**請求的最低必要欄位，包括 `UseSkill`、`SyncPosition`、`BankTransfer`、`TradeReply`、`ShopAction`、`PetAction`、`UpdateShortcut` 等。
- 整數不接受字串、布林值不接受 `"true"` 等字串；必要欄位缺失會回傳 `PacketError(Code=INVALID_PACKET)`，不呼叫修改角色或資產的 Handler。
- `tests/packet_schema_audit.py` 從 C# 規則擷取每種必要欄位，檢查 Godot 發送的**固定格式 Dictionary** 是否提供相同欄位；報告包含每個 Type 的 `MATCHED`／`FIELD_MISMATCH`／`NOT_FOUND` 與範例檔案。
- 預留舊 Client 的非必要欄位（例如沒有目標的技能）為可省略，不強制新協定覆蓋舊協定。
- Python 靜態檢查**只比對必要欄位存在**，實際欄位的數值型別由 Server 執行時驗證與 C# SmokeTests 測試。未涵蓋的封包與 Server → Client 事件欄位還需要擴充。
- CI 的報告 `packet-schema-report.json` 將與 `packet-contract-report.json` 一同上傳；若 GitHub Actions 尚未能啟動 Runner，需另外在本機執行上述命令，不能宣稱 CI 已通過。

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
