# Client ↔ Server 封包契約檢查

## 執行方式

在 Repository 根目錄執行：

```bash
python3 -m unittest discover -s tests -p 'test_packet_contract_audit.py' -v
python3 tests/packet_contract_audit.py --strict --output packet-contract-report.json
dotnet build server/SGO.Server.csproj -c Release
dotnet run --project tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj -c Release
```

GitHub Actions `Server build` 工作流程已納入前兩項檢查，且會上傳 `packet-contract-report.json`。

## 契約稽核內容

1. **Client → Server**：掃描 `client/**/*.gd` 中的固定字串 `Type`，對照 `PacketRouter` 的封包 `case`、登入前分支，以及 `IClientPacketHandler.PacketType`。
2. **Server → Client**：掃描 `server/**/*.cs` 中的 `Type = "..."`，對照 `network_manager.gd` 的 `match packet_type`。
3. **動態 Action**：檢查 `send_trade_action`、`send_shop_action`、`send_pet_action`、`send_mall_action`、`send_party_action` 的固定 Action 字串有無實際路由。
4. **錯誤回應**：未知 `Type` 回傳 `PacketError(Code=UNHANDLED_PACKET)`；未登入操作回傳 `PacketError(Code=AUTH_REQUIRED)`；Handler 拋出例外時回傳 `PacketError(Code=HANDLER_FAILED)`，不暴露內部堆疊。
5. **Client 診斷**：收到 `PacketError` 顯示訊息；收到未知 Server Type 留下 `[UNHANDLED_SERVER_PACKET]`，單一 Type 只警告一次。

## 此工具不能證明的事項

靜態稽核 **不等於** 封包功能全部完成：若 Type 使用動態組合、欄位型別不一致、伺服器 Handler 只回傳占位資訊、或廣播發生競態，靜態對照仍可能通過。需要另行補足真實 Godot WebSocket E2E、多人同步測試、Schema 欄位驗證及壓力測試。

## 已知刻意未開通的功能

- 商城 `MallAction(BuyItem)`：目前商品表未設定，拒絕扣點。
- `MallAction(CreatePayPalDonation)`：未設定 PayPal 金流，不建立付款訂單。
- `MallAction(QueryLegacyPoints / WithdrawLegacyPoints)`：未設定舊服點數資料來源。
- `AnnouncementListRequest`：目前回傳空清單。
- 部分 `UseItemRequest` 物品效果與舊 Lua 任務仍需補充實作。

這些都不應使用假的「Success=true」或發放資產方式補齊。後續真正接入功能時應補相應的契約、資料庫交易及 E2E 測試。
