# 原版 Paradise.exe.lst 與 Godot 大量物件效能對照

## 可驗證的原版線索

來源：`Old/ClientData/Paradise.exe.lst`，IDA 反組譯清單，40,117,423 bytes、1,031,946 行。
可在 [Legacy Paradise EXE listing architecture audit](https://github.com/kevin840307/TestGoGo/actions/runs/42803421912) 的 CI log 中重現。

| 原版清單位置 | 可驗證事實 | 不應延伸的結論 |
|---|---|---|
| L447355 | `Direct3DCreate8` | 無法據此確定單一 draw call 數量 |
| L341626、L505151 | `CELDxDisplay::CreateSpriteVMCache` | 不能確定快取策略與容量 |
| L448049 | `CDIdxVBMgr::GetIndexBuffer` | 不能直接斷定全部 Sprite 都合批 |
| L448061、L448136 | `CDVBMgr::GetBuffer` | 不能直接推定相機裁切方式 |
| L448026 | `CDSurfMgr::SyncTexture` | 不能直接確定紋理卸載時機 |
| L22127、L22201 | `CelSocket`、`CSGONetwork` | 不能證實原版採用 AOI |

**結論**：原版至少有獨立的 C++/Direct3D 8 繪製資源管理層與 Sprite 快取，和 Godot「每個掉落物都建立完整 Area2D + Sprite + Labels」有不同成本模型。原版是否有特定 AOI、固定 Tick/物件池，仍需針對組語控制流程驗證，這份清單不支持直接下結論。

## 已實作的安全優化

* Server 既有 250ms AI Tick、Server 物件 AOI 和 WebSocket 玩家優先快線維持，不調低戰鬥/玩家封包、Physics 或 FPS。
* Client 新增 `scripts/ground_drop_spatial_index.gd`：以 512 單位的空間格記錄地面物品位置、類型，按玩家 AOI 查詢附近候選並選取每種最近 5 件。索引只保存精簡資料，不建立 Godot 場景節點。
* 大批 `SpawnEntity` 在「每種最多 5 件」模式下先記錄權威資料、延後到 AOI 掃描再批次決定視覺，避免每個封包都重建候選排名。
* 掉落物的保護倒數由每件自己的 `Timer.new()` 改為一個共用的 1Hz 畫面更新脈衝，不影響後端所有權或物品過期權威。
* 掉落動畫改從最近死亡事件位置判斷，避免對每個掉落物掃描所有活躍怪物。
* `[RENDER_STATS]` 新增 `ground_index_cells`、`ground_index_examined`、`ground_index_rebuilds` 等診斷欄位。

## 已驗證與未驗證

* CI `Godot Client visual allocation contract`：4504 筆模擬掉落資料，查詢 5/500/1500 半徑、1/5/10 上限，與獨立排序參考答案一致；移動、Despawn、相同 Snapshot、地圖清理亦通過。
* 靜態契約測試檢查所有原始資料保存、集中計時與死亡動畫索引。
* **尚未驗證**真實 Map13 多人與大量掉落時的 Godot FPS 改善幅度，必須提供更新前後同場景的 `PERF_ANALYSIS`、`RENDER_STATS`、`PHYSICS_CADENCE` log 才能量化。
* **未實作** Direct3D 類型的批次 Draw／無節點地面圖示渲染：「全部顯示」仍會有每件獨立 Area2D，保留可點擊撿取語意。若仍有大量節點瓶頸，再以合批圖示與獨立點擊命中模型分階段實作及驗證。
* 「不顯示掉落物」模式仍保留物品網路資料，但無法直接用滑鼠點擊被隱藏的地面物件。

## 測試命令

```sh
python -m unittest discover -s tests -p 'test_ground_drop_hotpath_contract.py' -v
```

Godot 4.7.2 regression：`client/tests/ground_drop_selection_regression.gd`，由 `.github/workflows/client-memory-lifecycle.yml` 在隔離的最小 Godot 專案自動執行。
