# 多人移動：最後位置同步與 Client 停止動畫修正

> 2026-11-10；獨立 PR 修補，基底 `feature/local-server-net8`。不覆蓋其他開發者分支。完整原版反編譯證據見 [multiplayer-legacy-movement-audit.md](multiplayer-legacy-movement-audit.md)。

## 原版與現版的差異

原版反編譯研究已確認 `OnPlayerSyncDest` / `OnCharSyncDest` / `OnEnemyMove` 與 `OnCharSyncPosition` 有不同命令，角色/怪物由本地更新循環逐步運算。原版 50 FPS 設定不是網路發包 50 Hz 的證據，尚無正式服封包時間戳可推導確切頻率。

新版：Godot Client `player.gd::_stop_movement` 先送最後 `SyncPosition`、再送 `SyncState(idle)`。Server `PacketRouter` 的 `SyncPosition` 接受並更新 `state.LastX/LastY`，但每角色 `MoveEntity` 每 100ms 最多廣播一次。若最後位置落在節流窗口內，附近其他玩家只收到 idle，沒有最後座標；遠端 Dead Reckoning 可能停在過時座標、補幀時倒退或下一次更新時順移。此點由控制流推導，需在真實多人畫面做驗收。

## 本 PR 修改

- Server `ClientSession` 記住上次真正廣播的 AOI 座標（nullable X/Y），登入選角時清空。
- 一般移動仍保留原有 **100ms 節流、Blocking.dat 驗證、AOI 過濾與背景批次持久化**，僅成功廣播時更新最後廣播座標。
- 收到 `SyncState(idle)` 時，若位置已被驗證且和最後廣播座標不同，先送 **唯一一份** `MoveEntity`（`SpeedTime=0`，表示終點校正），再送 `SyncState(idle)`；重複 idle、完全沒動以及已發過同座標不額外產生移動封包。
- Client `network_player.gd` 一收到 idle 就切換待機動畫，仍以原本物理更新逐步消化最多 40px 視覺預測偏差；避免遠端角色「向後走路」的動畫假象。
- 只動遠端顯示與 Server AOI 通知，不影響本地操控、戰鬥判定、碰撞、技能、撿取、寵物、AFK/失焦、渲染 FPS 或 Client↔Server 封包處理頻率。

## 封包順序

```text
Client:   SyncPosition(X_last,Y_last) -> SyncState(idle)
Server:   Validate -> state.LastX/Y 更新 -> [100ms 位置節流可能略過]
          收到 idle -> 比對 last published X/Y -> 發最後 MoveEntity(X_last,Y_last,SpeedTime=0)
                    -> 發 SyncState(idle)
Observer: MoveEntity 先更新最後位置，再停 Dead Reckoning 與步行動畫
```

## 回歸測試

1. `remote_multiplayer_motion_regression.gd`：100ms 附近的最後座標 + `SpeedTime=0` + idle，視覺收斂到最後權威座標，單幀補正不超過現有上限。
2. `WireLoadScenario.VerifyFinalStopPositionAsync`：真實加密 WebSocket 連線、同 AOI 的兩名玩家，先發 baseline 座標，再發鄰近最後座標和 idle，確認觀察者收到最後正確座標及有效 `SpeedTime`。
3. 專用 `client-multiplayer-motion.yml` 與 `server-multiplayer-latency.yml` 新增針對這條修補 PR 的事件，在 GitHub Actions 上執行演算法及 20 人連線測試。
4. 完整 Client 圖資缺少時，Godot scene/GPU 實測可能 SKIP；測試跳過不等於實機已 PASS。必須用兩個 Godot 視窗＋20 bots 在 Map13 對照 `MULTIPLAYER_PACKET_AUDIT`、`REMOTE_PLAYER_PACKET_GAP`、`SLOW_MOVEMENT_HANDLER` 與錄影。

## 需觀察的剩餘議題

- 目前玩家/怪物視覺移動已用 Server `SpeedTime`，但仍可能因 Server session 鎖、DB、AOI 排隊或 Client 解包時間預算出現尖峰；不能靠提高封包頻率掩蓋。
- 怪物 250ms 共用 AI Tick 仍可能造成「同時起步」的視覺感；需以 `MAP_AI_PROFILE`、Client 每角色收包間隔和影像記錄識別是否真有同步阻塞，再做下一步修改。
- 這份修補只解決**最後位置被節流吞掉**及 idle 動畫倒退；不代表已全面解決 20 人實機卡頓。PR 不自動合併。

## GitHub PR #3 CI 實測（2026-11-10）

- [Godot movement #42834433668](https://github.com/kevin840307/TestGoGo/actions/runs/42834433668)：**SUCCESS**。純演算法 4.7.2 headless regression 通過；真實動畫圖片不在 checkout 內，完整場景未測。
- [.NET 8 + persistence + 20 players #42834433702](https://github.com/kevin840307/TestGoGo/actions/runs/42834433702)：**SUCCESS**，包含 .NET Server/Probe build、`[POSITION_PERSIST_TEST] ALL PASS`、`[WIRE_FINAL_STOP_PASS] entity=3 x=10007 y=10000 speed_time=0`。
- 同一輪 Movement 1140/1140，P50 9.0ms / **P95 905.2ms** / P99 905.2ms；附近聊天 1200/1200，P95 6.9ms；AI Tick P95 約 21.69ms。單輪尖峰不能忽略；不代表畫面 20 人實機流暢度。
- 慢路徑記錄：`SLOW_PACKET_DETAIL ChatMessage HandlerMs` 超過 10s、`SLOW_WORLD_TICK LockWaitMs` 超過 9s，`MAP_AI_PROFILE PlayerSessionLockWaitMs` 超過 8s。同時 `SLOW_CHAT_PHASE=0`、`POSITION_BATCH_FAILED=0`；部分緩慢 ChatMessage 可能是 GM 測試指令，不能直接視為一般聊天延遲。需與 GM setup/其他任務分離確認。
- [一般 Server build #42834433901](https://github.com/kevin840307/TestGoGo/actions/runs/42834433901)：在 Quest host strict audit 發現既有 `UNBOUND` 指令而失敗，後續一般 build 步驟未執行。隔離式 .NET server/probe build 在上面 20 人 workflow **已通過**。
