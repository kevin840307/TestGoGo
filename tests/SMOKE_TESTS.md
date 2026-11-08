# SGO 測試架構與撰寫規範

此文件描述現在的測試層級、實際已拆的模組，以及後續如何從龐大的 `Program.cs` 分階段抽離。**新增測試不應預設繼續寫進 Program.cs**。

## 測試層級

| 層級 | 目錄／檔案 | 目的 | 是否需要啟動 Server |
| --- | --- | --- | --- |
| 靜態協定契約 | `tests/packet_contract_audit.py`、`packet_schema_audit.py`、`packet_response_audit.py` | Client／Server Type、Action、欄位是否有對應（不宣稱功能正確） | 否 |
| Python 契約單元測試 | `tests/test_packet_*.py` | 驗證稽核工具本身：漏欄位、未對應 Handler、忽略動態欄位等 | 否 |
| .NET 領域 Smoke | `tests/SGO.Server.SmokeTests/Protocol/`、`World/`、`Announcements/` | 純資料／封包驗證，不使用既有主測試的共用 DB 狀態 | 否 |
| .NET 遊戲整合 Smoke | `tests/SGO.Server.SmokeTests/Program.cs`，未來拆為 `Integration/` | SQLite、角色、技能、寵物、交易、世界狀態等實際整合 | 否，直接組合服務 |
| 加密傳輸 E2E | `tests/SGO.Server.WireSmokeTests/` | 對真實運作中的 WebSocket Server 編碼／傳送／接收／解碼 | 是 |
| Godot 多人 E2E（未完成） | 將來獨立 Client 測試專案 | 真實場景切圖、UI、2 人以上互動、掉寶／重連 | 是，另需 Godot |

## 目前 .NET Smoke 目錄

```text
tests/SGO.Server.SmokeTests/
  SmokeAssert.cs                     # Check/Json 共用工具
  Program.cs                         # 目前剩餘大型遊戲整合測試；逐步縮減
  Protocol/
    RequestSchemaSmokeTests.cs       # 請求必填／型別
    ActionSchemaSmokeTests.cs        # Trade/Shop/Pet/Club/Mall/Party Action
    IdentitySchemaSmokeTests.cs      # Int64 UID、DBID、機甲及掉物
    PreloginPacketSmokeTests.cs      # 直接呼叫 PacketRouter 驗證登入前錯誤／Pong
  World/
    BlockingMapSmokeTests.cs         # Blocking.dat、嚴格穿牆／斜角／A* 繞路
  Announcements/
    AnnouncementSmokeTests.cs        # Date/Title/Content 與清單過濾
```

原本 `Program.cs` 約 **9,518 行**。本次先將上述相對獨立的測試抽走，保留原本呼叫時序與世界測試內容。這是維護性改善的第一階段，不代表其餘單檔整合測試都已拆完。

## 新增測試規則

1. **依功能放檔案**：新增封包案例放 `Protocol/`；碰撞、尋路放 `World/`；持久化或交易先建立相對應 `Integration/` fixture，再抽離測試。
2. **每個模組有明確 `Run()` 或 `Run(依賴)`**，而不是依賴其他測試執行後留下的全域資料。必要時由單一 Fixture 管理 `SqliteGameStore`、角色 Session、臨時地圖及清理。
3. **共享的只放工具**：`SmokeAssert.Check` 與 `SmokeAssert.Json`；不要把不同功能的新斷言或規則集中進同一個大型 helper。
4. **正向＋錯誤＋邊界**：請求缺欄位、錯型別、最大值、重複請求；交易／掉寶要有多人併發與資料守恆；不要只測有沒有回傳封包。
5. **不要用假 PASS**：測試程式已建立不代表通過。調整 CI 及 PowerShell Runner 時，必須真實執行並記錄完成證據。
6. **先加新測試、再改實作**：修正舊封包時保留既有 Type、欄位、成功與失敗回應契約；必要時提供向後相容處理。

## 執行方式

從 Repository 根目錄：

```powershell
# 不需要啟動 Server：Python 稽核／工具測試
python -m unittest discover -s tests -p 'test_*.py' -v
python tests/packet_contract_audit.py --strict
python tests/packet_schema_audit.py --strict
python tests/packet_response_audit.py --strict

# .NET 領域與遊戲整合測試（目前共用同一個 Console 專案）
dotnet run --project tests/SGO.Server.SmokeTests/SGO.Server.SmokeTests.csproj -c Release

# 包含真正本機 WebSocket 的完整測試
powershell -ExecutionPolicy Bypass -File tests/run_protocol_checks.ps1
```

`test_smoke_suite_structure.py` 在 CI 中會檢查各模組存在、已由 Runner 呼叫，且獨立的 Schema／公告／BlockingMap 案例沒有被塞回 `Program.cs`。

## 下一階段拆分（尚未完成）

- **Integration/Character**：使用測試角色 fixture，驗證登入／重登／狀態／持久化。
- **Integration/Combat**：真實怪物、技能、AOE、死亡、掉寶／經驗。
- **Integration/Trade**：兩端 session、同時 Confirm、Cancel、Rollback，物品與金錢守恆。
- **Integration/PetsAndMecha**：多寵物及機甲，測試地圖切換、重連。
- **Integration/QuestAndShop**：Lua NPC、商城點數、可用物品的資料庫變更。
- **E2E**：Godot Client 自動化與 2／10／50 人負載分開跑，不與單元測試混淆。

完整未完成項目與優先度請看 [server/FEATURE_GAPS.md](../server/FEATURE_GAPS.md)。
