# 多人移動第二階段：速度語意、合併封包與殘餘順移（2026-11-11）

目標分支：`feature/local-server-net8`。上一輪已修正真實 viewport AOI；本輪針對實機仍可見的玩家/怪物順移。

## 原版確認

`Old/ClientData/Paradise.exe.lst` 的角色/怪物 `SetDest` 只更新目的地；`SetMoveloop` 依 entity 自身 Speed 計算固定步進與剩餘步數，之後由 `MoveStep/movelogic` 持續前進。`SynStopPosition` 才是權威位置校正。因此原版核心是「destination + entity speed + 本地連續步進」，不是依 WebSocket 封包抵達間隔決定顯示速度。反編譯檔無封包 timestamp，不能宣稱精確送包頻率。

## 這輪找到的殘餘問題

1. Server 可把多個未送出的 monster `MoveEntity` 合併成最新座標，並附 `SnapshotSpanSeconds`；舊 Client monster path 卻只吃被相容性限制在 0.75s 的 `SpeedTime`，數秒累積位移會被壓縮成高速追趕。
2. Remote player 舊邏輯只看距離，>=1200px 就 reset；合理的多秒 coalesced walk 也可能被誤判成 teleport。
3. Monster 進入攻擊距離時 Server 會送 `SpeedTime=0` 權威位置校正。Body 必須立即服從 Server，但先前只有 <=160px 的 sprite/UI 落差會平滑，較大的 backlog correction 會直接看成跳躍。

## 最終設計

- Server monster waypoint 新增 `MoveSpeed`（實際 segment distance / server movement duration）。
- World coalesce 累積保留 `SnapshotSpanSeconds`、`CoalescedMoves`，並用加權 path distance / server time 產生平均 `MoveSpeed`。
- Client monster 以 `MoveSpeed` 為主要呈現速度。若 client 落後，普通 packet 最多約 1.5x、coalesced packet 最多約 2x nominal speed 追趕；不再把歷史數秒位移壓成 900px/s sprint。
- Remote player 只有 Server 明確提供有效 `SnapshotSpanSeconds` 且位移在合理速度上限內，才允許跨過 1200px hard-warp guard 並平滑追趕；無標記或不合理跳躍仍立即 snap。
- NetworkPlayer 用角色 MoveSpeed 推導 expected visual catchup；一般 200px/s 角色 backlog 約限制在 400px/s，避免舊 900px/s burst。
- Monster attack-entry `SpeedTime=0` 仍立即更新 authoritative body；sprite/UI 只做 render-only correction easing，距離上限 700，時間依距離約 0.18~1.0s。Combat、collision、AOI、targeting 不延後。
- `CoalescedMoves` 僅診斷，不改送包頻率或排序。

## 不變項

- 玩家 Server broadcast 基準 100ms。
- Monster AI/world cadence 250ms。
- Client network poll / physics tick 不因 FPS、AFK、失焦而降低。
- Spawn/Despawn/SyncState/attack/death/0ms correction 仍是 coalesce causal boundary。
- Server authoritative position、戰鬥、碰撞、撿取、DB persistence 不使用 predicted/visual position。

## 驗證

- Godot 4.7.2 isolated movement regression。
- 20 remote players + 60 monsters deterministic motion。
- Server BroadcastPriorityProbe：world/player coalescing、FIFO boundary、MoveSpeed/CoalescedMoves。
- Server viewport AOI probe。
- 20 encrypted WebSocket clients：AOI movement/chat/combat/mixed load。
- 完整 proprietary art scene 若 CI 缺 PNG 會 SKIP，不得宣稱等同實機視覺驗收。
