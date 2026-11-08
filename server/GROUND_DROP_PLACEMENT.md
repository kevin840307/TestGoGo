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
- **若已載入 `Blocking.dat` 但找不到任何合法落點，玩家丟棄會被拒絕，且完全不會執行資料庫的扣除交易。** 怪物掉落則記錄 `[GROUND_DROP_NO_SAFE_TILE]`，並直接把掉寶放進擊殺者背包（一般物品）或裝備欄（裝備），發送對應 `InventorySync/EquipSync`，避免已擲出的戰利品消失。只有資料庫給予獎勵也失敗時才會記錄 `RewardedDirectly=false`，必須追查資料庫錯誤。
- **拾取也驗證阻擋地形**：維持原本的 100 像素可撿距離；但玩家與地面物品之間若隔著牆、斜角障礙或不可步行區域，Server 會拒絕撿取，且不會執行 SQLite 物品轉移交易。若 Server 紀錄玩家正在駕駛具有特殊地形通行能力的機甲，允許視線穿過旗標 3（機甲專用格），**仍絕不可穿過實體牆**；物品本身仍只能放在一般可走格。只有缺少 `Blocking.dat` 的舊地圖仍沿用距離驗證，並不宣稱防穿牆已驗證。
- 原有的所有權保護、60 秒自然消失、SQLite 地面物品持久化、拾取及 AOI `SpawnEntity/DespawnEntity` 都保留。

## 舊地圖資料缺失

此版本回收的 `Old/服務端/Dat` 有多張地圖（包括 Map13、Map15）的 `Blocking.dat`，這些地圖可以直接完成碰撞驗證。

如果有地圖未安裝 `Blocking.dat`：系統會 **明確標記 `CollisionVerified=false`**，保留原來的掉落座標，以免因缺檔突然不掉寶或扣除背包物品。第一次碰到該地圖會記錄一次
`[GROUND_DROP_MAP_MISSING]`；不是每掉落一件就刷警告。這種地圖**不能宣稱落點安全**，應補上原始碰撞資料。

## 舊資料自動修復（WorldReady）

為避免舊版 SQLite 已存在的牆內物品在新拾取檢查下永遠不可撿，當 Server 在 `BuildGroundItemSnapshotPackets` 讀取地圖掉落物時：

- 正常可走座標**不移動、不寫 DB**。
- 若原本座標位於障礙／特殊地形，且存在原地附近 5 格以內的可走格，便把座標修正到原位置附近；**不根據登入玩家的位置搬運物品**，防止遠距離瞬移。
- 透過 `RelocateGroundItemIfUnchanged` 使用 SQLite 條件更新與交易；兩個 WorldReady 同時嘗試修復，也不能互相覆蓋座標或復活已撿走的物品。修正後位置會直接更新 Session 與 AOI 的 Spawn 快照。
- 修正只更新 X／Y，保留地面物品 ID、物品數量、擁有者、保護時間、建立時間、原本的 60 秒消失時間；不重新開始倒數。
- 如果附近根本沒有可走格、資料缺失或地面物品已被刪除，不會假裝修復成功。可用 `[GROUND_DROP_LEGACY_RELOCATED]` 追蹤成功修復項目；若已知是阻擋格卻無法搬移，則每張地圖最多紀錄一次 `[GROUND_DROP_LEGACY_UNREPAIRED]`，保留原始資料供後續處理。

這是針對歷史資料的有限半徑修正，不是重新跑整張地圖的尋路，也不能保證所有地面物品都在任意玩家可到達的連通區域。即使格子可走，撿取時仍必須通過玩家到物品之間的阻擋檢查。

## 效能與驗證

碰撞圖由既有 `BlockingMapService` 快取（每地圖載入一次），正常掉落只需檢查當前格與短距離直線；障礙物回退最多掃描附近 11×11 格，不搜尋整張地圖，也不更改移動/尋路系統。所有落點判斷都在建立 SQLite 地面物品前。

```powershell
dotnet run --project tests/SGO.Server.SmokeTests -- world
dotnet run --project tests/SGO.Server.SmokeTests -- all
```

`World/GroundDropPlacementSmokeTests.cs` 覆蓋：正常落點、牆內偏移、隔牆請求、特殊／機甲地形、惡意超遠座標、怪物站在不可走格、全阻擋地圖拒絕、拾取距離與隔牆阻擋、歷史資料重定位、6 筆併發修復的持久化與 TTL 保護，以及缺少地圖資料時的相容性與記錄限流。

還應在真正 Server + Client 人工驗收：Map13/15 貼牆打怪、角落擊殺 5～20 隻怪、多重掉落、手動從背包拖曳丟物、撿回物品、切圖返回與重新登入後地面物品恢復。尤其確認物品沒有消失、正常撿回、**隔牆不可撿取**、切圖／重登後仍可同步，且 `[GROUND_DROP_NO_SAFE_TILE]` 未異常增加。

**尚未在本工具環境執行 .NET 測試**，需要在實機/CI 執行後才能宣稱全 PASS。
