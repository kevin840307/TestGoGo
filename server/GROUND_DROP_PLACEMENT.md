# 地面物品落點安全（Blocking.dat）

## 問題與修正

舊版 SGO 有兩條容易將物品放進障礙物的流程：

1. 怪物死亡：`CombatService.AddMonsterDrops` 直接使用怪物座標 + 固定 X/Y 偏移，未檢查障礙格。
2. 玩家丟物品：`PacketRouter.DropItemRequest` 直接使用 Client 傳來的 `DropX/DropY`，在扣物品前也沒有檢查。

現在兩者均呼叫 `GroundDropPlacementService.TryResolve`，以 **Server 載入的原始 `Blocking.dat`**（16 像素一格、0 代表一般可走區域）選擇落點；不信任 Client 提議的座標是否安全。

流程：
- 優先接受原始位置（位於可走格，且從來源位置以不穿牆／不切斜角的直線可到達）。
- 若撞牆、特殊地形或超界，在原定位置附近最多 5 格（80 像素）半徑內尋找合法格中心，選擇原始來源可直接到達的格子。
- 若怪物原始位置無法走，改從擊殺玩家的 Server 座標尋找可達落點；必要時把掉落物放在擊殺玩家腳邊，避免物品落到牆內。
- 玩家丟棄仍可向滑鼠位置放物品，但超過 Server 座標 1024 像素的請求會退回玩家腳邊，避免超遠座標濫用。
- **若已載入 `Blocking.dat` 但找不到任何合法落點，玩家丟棄會被拒絕，且完全不會執行資料庫的扣除交易。** 怪物掉落會記錄 `[GROUND_DROP_NO_SAFE_TILE]`，不寫入已知不安全的位置。
- 原有的所有權保護、60 秒自然消失、SQLite 地面物品持久化、拾取及 AOI `SpawnEntity/DespawnEntity` 都保留。

## 舊地圖資料缺失

此版本回收的 `Old/服務端/Dat` 有多張地圖（包括 Map13、Map15）的 `Blocking.dat`，這些地圖可以直接完成碰撞驗證。

如果有地圖未安裝 `Blocking.dat`：系統會 **明確標記 `CollisionVerified=false`**，保留原來的掉落座標，以免因缺檔突然不掉寶或扣除背包物品。第一次碰到該地圖會記錄一次
`[GROUND_DROP_MAP_MISSING]`；不是每掉落一件就刷警告。這種地圖**不能宣稱落點安全**，應補上原始碰撞資料。

## 效能與驗證

碰撞圖由既有 `BlockingMapService` 快取（每地圖載入一次），正常掉落只需檢查當前格與短距離直線；障礙物回退最多掃描附近 11×11 格，不搜尋整張地圖，也不更改移動/尋路系統。所有落點判斷都在建立 SQLite 地面物品前。

```powershell
dotnet run --project tests/SGO.Server.SmokeTests -- world
dotnet run --project tests/SGO.Server.SmokeTests -- all
```

`World/GroundDropPlacementSmokeTests.cs` 覆蓋：正常落點、牆內偏移、隔牆請求、特殊／機甲地形、惡意超遠座標、怪物站在不可走格、全阻擋地圖拒絕，以及缺少地圖資料時的相容性與記錄限流。

還應在真正 Server + Client 人工驗收：Map13/15 貼牆打怪、角落擊殺 5～20 隻怪、多重掉落、手動從背包拖曳丟物、撿回物品、切圖返回與重新登入後地面物品恢復。尤其確認物品沒有消失、不可跨牆拾取，且 `[GROUND_DROP_NO_SAFE_TILE]` 未異常增加。

**尚未在本工具環境執行 .NET 測試**，需要在實機/CI 執行後才能宣稱全 PASS。
