# 原版 SGO 普通攻擊距離公式：IDA 反編譯實證

> 查核日期：2026-11-10。以下區分 **IDA 直接可驗證** 與 **新版為相容需求而設計** 的部分；兩者不是同一件事。

## 原版 Client 的實際證據

來源 1：`Client.exe(1).lst`，2009-02-18，IDA x86 反組譯，
這是較接近現有遊戲資料的版本。

* `sub_5170ED`，`.text:0051711E..0051716D`：
  讀取裝備的 `MotherItem+0x45A` 兩次，相乘後存入
  玩家 `+0x858`。如果沒有有效裝備，`0051716D`
  寫入 `0x7E9 = 2025 = 45²`。
* `RunPlayerLogic` 一帶，`.text:0050AB33..0050AB41`：
  取玩家 `+0x858` 的射程平方，減去 `0xC8 = 200`，
  再與玩家／目標已算好的兩點距離平方相比，選擇追擊或攻擊。
  原指令是 `cmp` 後接 `jge`，所以**等於門檻時仍繼續追擊**，
  普攻允許攻擊的比較必須是嚴格小於 `<`。
* 騎乘支線 `.text:00517216..00517233` 把物品數值加上
  `0x2D = 45` 後再平方；機甲支線不同，**不可**把普通攻擊
  公式不加區分地套用到騎乘與機甲。

來源 2：`Paradise.exe(4).lst`，2004 年版本，有符號名稱：

* `CSGOPlayer::SyncAttackRange`，
  `.text:004ED034..004ED05A`：武器數值相乘；無資料則
  `this+0x828 = 0x7E9`。
* `CSGOPlayer::RunPlayerLogic`，
  `.text:004E16E9..004E16FB`：
  `this+0x828 - 0xC8` 與目標距離平方比較。
* `CSGOPlayer::SubLogic_Attacking` 以封包進行攻擊請求，
  這不代表已還原原版 Server 所有射程驗證條件。

因此原版 **Client** 的普通攻擊距離比較是：

```text
weapon_range = valid_weapon ? weapon.attack_range : 45
distance_squared = (target_x - player_x)^2 + (target_y - player_y)^2
can_enter_attack_logic = distance_squared < max(0, weapon_range^2 - 200)
```

原版沒有目前新版使用的「`(角色基礎 80 + 武器 120) × 50%`」
公式；那是先前推測，已撤掉。也**沒有證據**支持「所有劍應自動
限制在相同距離」這種依武器名稱分類的規則。

## 原版與 SGO 新版需求的差異

使用者指定：**空手跟一般劍攻擊距離相同**，只有額外數值
才延長，且不看武器類別。這項需求與原版 `無武器 45`、`武器
直接取物品數值` 的資料算法有所不同，不能誤稱原版完全一致。

新版的 `PlayerStatusSync.AttackRange` 是角色基礎（預設 80）
加上武器與其他裝備的 `attack_range`。資料例子：

| 實際資料 | `attack_range` | 依新版相容規則的實際門檻 |
|---|---:|---:|
| 空手（角色基礎 80） | 0 | 約 42.72 |
| `30001` 元帥之劍 | 120 | 約 42.72 |
| `30102` 星塵劍．吸血鬼 | 200 | 約 124.20 |
| `30120` 啟示錄之銃．吸血鬼 | 400 | 約 324.69 |
| 一般武器 + 其他裝備額外 50 射程 | +50 | 約 93.94 |

為了滿足這一**明確的新版需求**，新規則把普通武器前 120
數值視作一般近戰基準（不算額外射程），但不把此數字說成
反編譯得到的原版常數。其他裝備直接提供的射程加成以及
武器高於 120 的部分仍照數值加算，不看 Sword、Gun 等類型。

```text
normal_weapon_points = min(120, max(0, equipped_weapon_attack_range))
extra_points = max(0, effective_attack_range - 80 - normal_weapon_points)
radius = 45 + extra_points
client_stop_distance = sqrt(max(0, radius^2 - 200))
client_enters_attack_only_when = distance_squared < max(0, radius^2 - 200)
```

Client：`scripts/normal_attack_range.gd`，
`player.gd` 只有普攻使用它；
Server：`CombatService.NormalAttackWorldRange`，
以及 `CharacterStatCalculator.GetEquippedWeaponAttackRange`。

Server 目前在上述門檻外仍保有 **+24 座標單位**的傳輸延遲
容差，這是新版 Server 的防誤判策略，**不是反編譯原版
Client 的公式**。原版 Server 來源 `SGOPlayer.dll.lst` /
`SGOMapService.dll.lst` 在此不能完整證明權威傷害驗證規則。
先不更動技能 `CastRange`、騎乘／機甲或怪物／寵物攻擊距離。

## 自動測試與驗收

* Godot：`client/tests/normal_attack_range_regression.gd`
  對照平方門檻、空手＝一般武器、特殊數值、加成。
* Server：`tests/SGO.Server.SmokeTests/World/NormalAttackRangeSmokeTests.cs`
  使用 `CurrentClientDataCatalog` 的真實物品數值。
* Windows 實測時分別驗收普通武器、裸手、特殊長射程武器、
  額外射程配件；確認 Client 不在距離外發攻擊封包、
  Server 不判定「距離太遠」；需一併檢查網路延遲。

原版公式是**客戶端接近／進入攻擊邏輯**的判定，不能單憑
Client 反組譯把 Server 權威命中驗證說成完全相同。
