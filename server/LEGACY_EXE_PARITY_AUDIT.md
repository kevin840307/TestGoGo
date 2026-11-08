# SGO 舊版 EXE → Godot/.NET 8 功能對等稽核（2026-11-08）

> 工作分支：`feature/local-server-net8`。此文件是**舊版功能覆蓋**稽核，不是現有 Godot 的封包對照表、也不是「測試已通過」的聲明。  
> **需求優先順序**：好友（含黑名單、離線訊息）、複合機器人、Salon、社團／社交進階**全部放在最後（P-LAST，暫不開發）**；社團目前基本功能維持即可。它們不是目前發布阻塞。  
> 詳細執行缺口仍見 [FEATURE_GAPS.md](FEATURE_GAPS.md)；封包基線見 [PACKET_CONTRACT.md](../tests/PACKET_CONTRACT.md)。

## 0. 審查結論

1. **現在不能宣稱「功能 100% 等於舊版 EXE」**。即使 `packet_*_audit.py` 全部 `ROUTED`，那也只表示 Godot 已產生的 Type/Action 可找到 Handler，不能推出舊版 EXE 上另有的畫面／互動／資料語意都存在。
2. 優先缺口應是**實際可玩性、資料正確性、掉物/交易多人一致性、舊版特殊技能/任務/道具資料與實機測試**。PvP 與角色改名是明確的舊版差異，但不自動等於目前必須做。
3. 很多功能在目前版**已有實作**（技能、寵物、機甲、戰團、排行榜、混合製作、掉落、AOI）。不要使用過時的 `server/README.md`、`server/STATUS.md` 階段文字將這些誤判成完全缺少。
4. **收藏系統在 2009 客戶端有可靠 UI 資源線索**：原介面為 `095_Collection.itf`，由 `CCollectionSystemItf` 使用；細節見 §2。
5. CI 最近的 `Server build` / `Game editor E2E` workflow run 曾顯示失敗，但 job `steps=null`。沒有可讀 steps 就不能判定是代碼編譯錯，亦不能說測試 PASS。

## 1. 範圍、來源、證據等級

- **目標版本 2009**：上傳的 `Client.exe(1).lst`，IDA 反組譯，PE header Timestamp `2009-02-18`，SHA-256 `9B45569A0F619B5BF7D99F122C6ED12AE44A1E54576686D88DE4FAE72DD13147`；約 110.9 萬行。Release 符號較少，以字串/介面資源/呼叫序列推論。
- **補充版本 2004**：上傳的 `Paradise.exe(3).lst`，IDA 反組譯，PE header Timestamp `2004-11-24`，SHA-256 `66CC048DCA009E933D42CAC974611C9539CF0053B7C767FB531189D6E063F6FA`；約 103.2 萬行。保留較多 C++ 方法名稱，利於辨識業務功能。
- **現代版本**：GitHub `kevin840307/SGO` 分支 `feature/local-server-net8`，查 `client/`、`server/`、`tests/` 和 `Old/`；倉庫在查核期間有其他提交，所有差異需在實作前用最新 SHA 再核一次。
- **判讀原則**：2004 與 2009 **不是同一版**。2004 有函式只能證明該版編譯過該函式，不能保證 2009 一定有一樣的語意；2009 字串能證明資源/接口線索，不保證正式伺服器當年開放該畫面。
- **狀態**：「已有程式」≠ E2E PASS；「部分還原」= 功能有一部分但未達舊語意；「缺少」= 已查到舊功能且新版主要工作流無對應；「疑似」= IDA/新版資訊不足，待實際畫面或素材確認；「P-LAST」= 使用者**明確暫不開發**。
- 本文件是**靜態檢查**；沒有在本次任務執行舊客戶端、完整 .NET/Godot build、真 socket soak 或原始舊服資料庫。不要把證據不完整的判斷提升成確定性。
- 稽核涵蓋**51 個檢查條目**：P0=11、P1=19、P2=11、P3=4、P-LAST=6。一個條目可能涵蓋多個 UI/封包/數據行為，不能用條目數當已還原百分比。

## 2. 收藏系統：到底是哪一個 UI？（原始檔證據）

**2009 `Client.exe` 明確載入的視窗資源：`..\\095_Collection.itf`。** 名稱可以先用「收藏簿／Collection Book」辨認，但無足夠畫面資訊確認遊戲中顯示的精確繁體中文標題或工具列按鈕位置。

| 證據 | 2009 IDA 位置（`.lst` 行／VA） | 含義 |
|---|---|---|
| `..\\095_Collection.itf` | 行 **340901**／`004D72CA` | 收藏主視窗原始 .itf 排版檔 |
| `CCollectionSystemItf` | 行 **341476**／`004D77DE` | 收藏主介面 RTTI 字串 |
| `CCollectionSystemSubItf` | 行 **342810**／`004D8409` | 收藏次視窗／子介面 |
| `..\\095_Collection_` | 行 **343419/343433**／`004D8972/004D89A0` | 子頁檔名**前綴**，完整後綴待追查，不能亂猜 |
| `..\\B_COLLECTION_ICON.spr` | 行 **344204** 等／`004D9234` | 收藏相關圖示 Sprite |
| `Item\\Collection.itd` | 行 **344661/344696**／`004D97F0` | 收藏功能使用的資料表 |
| `Interface\\FlagCollectionData.dat` | 行 **11284**／`0040841D` | 旗標收藏資料 |
| `Interface\\ItemCollectionData.dat` | 行 **11401**／`004085D6` | 道具收藏資料 |
| `LIB - Collection Book.` | 行 **3497**／`0040340D` | 載入初始化紀錄字串，支援「Collection Book」稱呼 |

**你可以這樣在原版找**：
1. 在完整舊遊戲資源或資源解包後的檔案中找 `095_Collection.itf`、`095_Collection_*.itf`、`B_COLLECTION_ICON.spr`。
2. 若資料是打包的，查 `SGOData*.dat` 解包內容；**不要因實體資料夾找不到就判定檔案不存在**。
3. 試著在原版遊戲中尋找接近「收藏簿／收藏圖鑑／Collection Book」名稱或圖示的視窗，但**此處尚無反組譯證據可指定哪個 UI 按鈕、快捷鍵或 NPC 開啟**。找到截圖後再確認主 UI 的位置和是否可用。
4. 新 Godot `client/game_hud.gd` 有各式視窗管理，但沒有與 `095_Collection.itf` 對應的 Collection scene/script；倉庫路徑盤點也未發現上述 `Collection.itd` / .itf 資源。若遊戲確實要收藏功能，須先取得舊版資料而非將 Quest 的 `RefreshCollectedItems` 誤當收藏功能。
   
**Windows 搜尋（在完整舊遊戲目錄執行）**：
```powershell
Get-ChildItem 'C:\SGO' -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -match '^(095_Collection.*\.itf|B_COLLECTION_ICON\.spr|Collection\.itd|FlagCollectionData\.dat|ItemCollectionData\.dat)$' } |
  Select-Object FullName, Length
```
如果找不到，請提供資源封包名稱或收藏視窗截圖，再追真正的入口位置。

## 3. 舊版 → 新版完整功能矩陣

下列優先級以**目前可玩、可驗證且資料不會掉**為主；P-LAST 一律排最後，並非技術上比較簡單。

| ID | 2009/2004 舊版功能 | 舊版證據（符號/資源） | 新版對應與觀察 | 判定 | 優先 | 必要驗收 |
|---|---|---|---|---|---|---|
| A01 | 登入、建立與刪除角色 | 2004 CLoginItf / CCreateCharacterItf / CSelectCharacterItf；2009 同系介面 | client/LoginScreen.tscn、character_select.gd；PacketRouter / Store | 已有程式 | P0 | 註冊→登入→建角→刪角→重登、失敗與多分身隔離 |
| A02 | 角色密碼更新、選角 | 2004 CSelectCharacterItf::OnChangePwd | client/character_select.gd；ModifyPasswordRequest | 已有程式 | P1 | 字元長度、錯密碼、重登與角色槽 |
| A03 | 角色成長、基本數值／配點 | 2004 CCharInfoFullItf::OnAddSTR/DEX/CON/WIS；2009 CCharInfoJobItf | character_window.gd；CharacterStatCalculator | 已有程式／公式待核 | P1 | 各職系練功、屬性點、等級上限、裝備效果與舊成長表逐項比對 |
| A04 | 職業及轉職 UI／Job Counter | 2004 CCharInfoFullItf::OnChangeJob、CCharInfoJobItf::CalJobCounter；2009 CCharInfoJobItf | character_window.gd 顯示 Career/Job；QuestRuntime.SetCareer → SqliteStore | 部分還原 | P1 | 至少各職系一次轉職／前置條件／職業技能／重登；舊 Job counter 需確認 |
| A05 | 稱號解鎖與切換 | 2004 CCharInfoTitleItf::OnTitle；2009 CCharInfoTitleItf | character_window.gd、SetTitleRequest、UnlockTitle | 已有程式／待 E2E | P2 | 稱號來源、解鎖、切換、遠端同步、重登 |
| A06 | 角色改名 | 2004 CChangeNameItf::OnOK/ValidateData；2009 CChangeNameItf | 現有 Rename 為寵物改名，未找到角色改名 UI／封包 | 缺少專用流程 | P2 | 重名/非法名、交易/隊伍/戰團/排行榜關聯與重新登入 |
| W01 | 地圖載入與傳送 | 2004 CSGOMap、CQuestItf；2009 對應 map/quest | client/maps、game_manager.gd；WorldDataService/PacketRouter | 已有程式／待實機驗證 | P0 | 進圖、回圖、斷線重連、NPC/怪/地面物不遺失 |
| W02 | 地圖格、障礙與怪物尋路 | 舊地圖配置/Blocking 資產，舊版客戶端渲染 | BlockingMapService、MonsterNavigationService、legacy_pathfinder.gd | 已有程式／待回歸 | P0 | 不可穿牆、格座標一致、無路可走及切圖阻塞 |
| W03 | 小地圖／大地圖／座標 | 2004 CMiniMapItf/CMiniMapLItf、2009 CMiniMapItf | minimap_npc_overlay.gd、big_map_window.gd、world_map_window.gd | 已有 UI／待資產核 | P2 | 可見 NPC/傳送點與實際 MapID/位置一致 |
| W04 | NPC 對話、選項與任務事件 | 2004 CQuestItf::DisplayChoice/OnChoice；2009 CQuestItf | QuestRuntime、DialogFormatter、npc_dialog_panel.gd | 部分還原 | P1 | 1,566 舊腳本主要路徑真跑：選項/失敗/背包不足/重登/回報 |
| W05 | 任務進度、領獎與放棄 | 2004 CQuestItf / CMainQuestItf | QuestProgressService、quest_window.gd、quest_tracker_panel.gd | 已有程式／分支待核 | P1 | 分支、角色等級、前置任務、獎勵選擇、重覆領獎、回檔 |
| W06 | 劇情／過場動畫／BGM | 2004 CMainQuestItf::SetTitleAnime/SetThemeBGM、COpeningItf | legacy_cinematic_panel.gd、legacy_media_resolver.gd、game_manager.gd | 部分還原 | P2 | 各腳本 media 是否有資產、文字時間、跳過、切圖不凍結 |
| W07 | 世界事件／節慶 | 2004 存在 NPC/任務/PK 活動；2009 可能版本不同 | festival_theme_manager.gd、FestivalRewardCatalog | 部分還原／要用同版資料核 | P2 | 舊活動日期、特殊 NPC、專用兌獎、獎品機率 |
| C01 | 普攻與怪物傷害死亡 | 2004 CSGOEnemHPItf、CSGOMap/CSGOUim | CombatService.Attack、monster_entity.gd、shared AI | 已有程式／待壓測 | P0 | 雙人同怪擊殺、掉物一次、擊退/仇恨/攻擊方向 |
| C02 | 技能傷害／範圍／冷卻／消耗 | 2004 CSkillItf、CChatItf::SendMsg；2009 CSkillItf | CombatService.UseSkill，client skill_window/skills | 已有多數流程／未全對等 | P1 | 逐技能核 CastRange、SP/MP、CD、方向、AOE、追蹤與特效 |
| C03 | 特殊／製造／劇情技能 | 2004 CSGOMap::OnSpecialSkillRequest；2009 skill class | UseSkill 明確有「特殊/製造類型效果尚未還原」分支 | 確認仍有缺口 | P1 | 盤點每個被拒 SkillID、Lua 特殊效果與完整使用案例 |
| C04 | Buff／恢復／復活 | 2004 技能／狀態系統 | CombatService.TickBuffs、ResourceRecoveryService、ReviveAccept | 已有程式／待核公式 | P1 | 死亡/復活、負面狀態、持續時間、多玩家同時加持 |
| C05 | 原版傷害／EXP／掉落公式 | 2004 角色、裝備、Enemy 原資料 | LegacyBattleRulesCatalog、LegacyPlayerGrowthCatalog、EnemyDropCatalog | 部分還原 | P1 | 舊版同條件分布、掉落率/EXP/金錢及重複/錯配 ID |
| C06 | 玩家 PvP／PK 開關、合法攻擊 | 2004 CSGOPlayer::SetPKMode、CSGOUim::OnMapPkMode、CSystemMenuItf::OnPKUP/OnPKDOWN；2009 CSystemMenuItf | CombatService.Attack targetType!=1 拒絕；攻擊技主要鎖怪物 | 確認缺少完整 PvP | P2 | 安全區、PKMode、玩家受擊、死亡/回復及濫用防護 |
| C07 | PK 賽程、支持、歷屆冠軍與獎勵 | 2004 CPKFixtureTable/2、CPKRankCurr/Ever/Prev/Gift、2009 CPKFixtureTable、CPKRankGift | RankService 只有一般排行/榜首宣言；無競技流程 | 確認主要流程缺少 | P3 | 若要啟用再規劃賽程與獎勵發放；不阻塞 PvE 釋出 |
| I01 | 道具／裝備／分類與耐久 | 2004 CItemSysItf、CEquipItf、CItemItf | InventorySync/EquipSync、inventory_window、Store | 已有程式／資料待核 | P1 | 一般/裝備/機甲各 Tab、UI、斷線與重登一致 |
| I02 | 道具使用、特殊禮盒與選項 | 2004 CItemItf::OnUseItem、舊版 Item 資產 | UseItemRequest、ChoiceRewardCatalog、item_choice_window.gd | 部分還原 | P1 | UNKNOWN/CHOICE_REQUIRED 不可假發獎；原機率與交易原子性 |
| I03 | 道具 ID 舊版對應新版 | 2004/2009 不同 client data、舊服 Shop/Drop | CurrentClientDataCatalog、ShopCatalog、MappingCoverageReport | 部分還原 | P1 | resolved/skipped/unmapped 按 SKU 過帳與怪物掉落 E2E |
| I04 | NPC 商店買賣 | 2004 CShopItf::SendBuyPacket/SendSellPacket | ShopService、NpcShopViewResponse、npc_shop_window.gd | 已有程式／待 E2E | P0 | 多次購售/關閉/跨 Tab、負數/溢位、庫存與金錢守恆 |
| I05 | 玩家擺攤／購買 | 2004 SellingOpenItf、SellingBasicItf、SellingCloseItf | PlayerShopService、shop_window.gd、AOI | 已有程式／待壓測 | P0 | 買賣vs關店/斷線/重登、相同物件不能雙賣 |
| I06 | 雙人交易 | 2004 CTradeItf::OnTrade_ConfirmFail、SendGiveItem | TradeService、trade_window.gd | 已有程式／待壓測 | P0 | 取消vs確認、兩階段確定、異常斷線、寵物機甲資產完整 |
| I07 | 銀行、跨背包轉移與直接轉帳 | 2004 CBankSysItf、CTradeMoneyItf | BankService、bank_window、DirectTransfer | 已有程式／待壓測 | P1 | 雙向轉帳、多資產 Tab、超上限與重放 |
| I08 | 修理裝備與機甲裝備 | 2004 CItemRepairItf、CRidingEquipRepairItf | RepairItemRequest、repair_window.gd | 已有程式／待回歸 | P1 | 損耗費用/滿耐久、同一件重覆維修/扣款 rollback |
| I09 | 混合/製作/拆解配方 | 2004 CItemMixItf::OnMix | CraftingService、craft_dismantle_window.gd | 已有程式／配方待核 | P1 | 逐配方、材料、金錢、產物與多人競態 |
| P01 | 寵物資料、裝備／技能／召回 | 2004 CPetInfoItf、CPetEquipItf、CPetItf | PetAction、pet_window.gd、PetSkillCatalog | 已有程式／待公式核 | P1 | 升階/餵食/生命/技能設定、長時間召喚與重登 |
| P02 | 寵物捕捉／取得流程 | 2004 CSGOUim::OnPetCaptureOK；2009 寵物介面 | 目前 PetAction 未見專用 Capture 動作；道具腳本可能給寵物 | 疑似缺口／需區分取得方式 | P2 | 先確認舊版捕捉 UI、道具封包與成功/失敗語意，再實作 |
| P03 | 機甲騎乘／機甲卡／電池／裝備 | 2004 CRidingInfoItf、CRidingEquipItf、CRidingCardItf | HorseSync、DriveMechaRequest、horse_preview_window.gd | 已有程式／待驗證 | P1 | 上下機甲後座標碰撞、電池耗能、裝備影響及切圖 |
| P04 | 寵物/機甲與 AI 的 AOI 行為 | 舊版同步實體；新版另外做 AOI | PlayerCompanionAoi、WorldEntitySpatialAoi、network_entity_manager.gd | 新版已改善／待壓測 | P0 | Spawn→Move→Despawn 順序、遠距零泄漏、回圖不殘影 |
| S01 | 聊天：附近、世界、隊伍、戰團 | 2004 CChatItf::ProcessSendToChannel、2009 CChatItf | ChatMessage+AOI/Team/Club 廣播 | 已有程式／頻道語義待核 | P1 | 舊版陣營vs全服差異、對應顏色/泡泡和敏感資料 |
| S02 | 密語及訊息傳送 UI | 2004 CChatItf::SendPrivateMsg、CFriendMsgItf | Server `/name 玩家名 訊息` 可發送在線玩家；Godot PRIVATE=4 但切換 %4 | 確認 UI 缺口／部分功能 | P2 | UI 正確選到私人頻道、指定收件人、二人 roundtrip |
| S03 | 聊天泡泡／表情／任務取表情 | 2004 CBalloonMenuItf、CEmotionMenuItf、CSGOMap::OnEmotionSync | chat_panel.gd、emotion_panel.gd；GetTargetEmotion 固定回傳 0 | 部分還原 | P2 | 表情他人可見與 Lua 查詢值正確、泡泡圖資回歸 |
| S04 | 隊伍邀請／踢人／解散／組隊任務 | 2004 CTeamItf、CReqMenuItf::OnTeam | PartyService、party_window.gd | 已有程式／待 E2E | P1 | 10 人組隊與擊殺、跨圖、隊長掉線、任務隊伍旗標 |
| S05 | 戰團基本功能 | 2004/2009 CClubSysItf、CToolBarItf::OnClub | ClubService Create/Invite/Accept/Donate/Research + clan_window.gd | 已有主要流程 | P3 | 目前維持，不為追求全部舊版社交功能重寫 |
| S06 | 排行榜／榜首宣言 | 2004 CRankMainItf、CRankSayItf | RankService、rank_window.gd | 已有主要流程 | P2 | 資料來源、排名類型、榜首展示與宣言權限 |
| S07 | 好友名單／邀請／忽略名單 | 2004 CFriendItf::OnAddFriendEntry/OnAddIgnoreEntry；2009 CFriendItf | 目前無 friend UI/Store/Request | 明確缺少，暫緩 | P-LAST | 使用者目前不打算加入，不排入近期 Roadmap |
| S08 | 好友 P2P 歷史／離線留言 | 2004 CFriendMsgItf、CFriendItf::ArchiveP2PHistory/OnSuccessSendOffline；2009 CFriendMsgItf | 新版僅在線密語，無好友專用儲存 | 明確缺少，暫緩 | P-LAST | 與好友功能一起最後處理 |
| S09 | 高階社團/社交活動/聯盟 | 2009 CSociSysItf/CSociEventItf；2004 CClubSysItf | ClubService 已夠目前需求；舊進階介面未對等 | 部分還原，暫緩 | P-LAST | 保留現有戰團基本功能，不擴進階系統 |
| V01 | 收藏圖鑑／Collection Book | 2009 CCollectionSystemItf、CCollectionSystemSubItf；`095_Collection.itf`、`095_Collection_...`、`B_COLLECTION_ICON.spr`、`Item\Collection.itd`、`FlagCollectionData.dat`、`ItemCollectionData.dat` | 新版未找到 Collection 視窗及對應資產，舊物件不等於任務拾取 | 舊 UI 證據明確／新版未找到 | P2 | 先找原畫面與資料、搞清楚收藏分類與解鎖，確定是否需要開發 |
| V02 | Salon 系統 | 2009 CSalonSystemItf | 新版未找到對應 UI | 舊版存在，暫緩 | P-LAST | 使用者不打算加入 |
| V03 | 複合機器人／Robot Complex | 2004 CRobotComplexInfoItf::OnBreak/OnSkillUse、2009 CRobotComplexInfoItf / `085_COMPLEX_INFO.itf` | 一般機甲≠複合機器人專用介面 | 疑似缺口，暫緩 | P-LAST | 使用者不打算加入 |
| V04 | BGM/SFX、設定與畫質 | 2004 CSystemMenuItf::OnBGM*/OnSFX*/On25FPS/On50FPS | client/game_hud.gd SettingsWindow、audio_manager.gd | 已有主要 UI／待比較 | P3 | 設定持久化、音量、失焦與多開資源消耗 |
| V05 | 新 Server 管理後台與網頁編輯器 | 舊版沒有相同 .NET/Godot 管理工具需求 | server/AdminUi、game-editor/ | 新版加值，不列作舊版缺口 | P3 | 管理 API 只允 loopback；素材與座標編輯測試 |
| T01 | 固定 Client↔Server 封包契約 | 舊 exe 原有的 2004/2009 不同協定並非現在 Godot WebSocket 協定 | packet_contract_audit、packet_schema_audit、packet_response_audit | 對新版封包有靜態守門，不等於舊版完整 | P0 | 每項老功能必須追 UI→request→response→Store→E2E |
| T02 | 多人競態與 AOI 容量 | 舊 EXE 不提供新版 .NET 8 性能保證 | SGO.Server.SmokeTests、WireSmokeTests、WireLoadProbe | 已有測試程式，缺實際驗收 | P0 | 10/50/100 人測試＋近遠 AOI + 30-60min soak，檢查資產守恆 |
| T03 | CI／編譯／Godot E2E 真實結果 | 舊 EXE 僅供功能參考 | server/FEATURE_GAPS.md 記載 CI steps=null；現有工作流程不能視為 PASS | 實際驗證缺口 | P0 | 必須取得可讀 Actions steps，所有有執行的 Suite PASS |
| T04 | 外部付款／舊服點數 | 不是本地遊戲基本單機功能；涉及外部實際提供者 | MallService、外部 provider 介面但未接通 | 外部依賴阻塞 | P-LAST | 未要啟用時保持明確失敗，不偽造儲值或轉入 |

## 4. 直接由程式碼確認的重要差異（不是猜的）

### 4.1 玩家 PK 不是一般 PvE 技能
- `server/Application/Combat/CombatService.cs` 的 `Attack(session, accountId, targetEntityId, targetType)`，當 `targetType != 1` 直接回「攻擊目標類型無效」。`UseSkill` 對 offensive attack 同樣以怪物實體為主要攻擊目標；不能因存在 `UseSkill` 就聲稱玩家 PK 完成。
- 舊 `CSystemMenuItf::OnPKUP/OnPKDOWN` 與 `CPKFixtureTable` 表示 PK 設定與競技賽是不同子系統。**若未來要做，先分離合法攻擊與賽事，不要一口氣補超大系統。**

### 4.2 密語：Server 有基本功能，UI 的切換路徑不完整
- `PacketRouter.cs` 的 `case "ChatMessage"`，`/name 玩家名 訊息` 查在線玩家並發 `ChatBroadcast(Channel=4)`。這**不是**好友管理／離線留言。
- `client/scripts/chat_panel.gd` 定義 `Channel.PRIVATE`（值 4），但 `_on_btn_channel_pressed()` 使用 `(current_channel + 1) % 4`，一般點擊無法切到第五個密語頻道；直接送 `ChatMessage.Channel=4` 也會被 Server 的 `0..3` 驗證拒絕（密語要有收件對象）。
- 使用者已把**好友**排最後，但上述「密語 UI 操作缺陷」可獨立修正，不需要實作好友系統。

### 4.3 職業並非完全沒有
- `character_window.gd` 可顯示 `Career`/ `Job`，`QuestRuntime.cs` 已註冊 `SetCareer`，`PacketRouter.cs` 對 `SetCareer` 調用 `_store.SetCareer`；只需確認舊 `CCharInfoJobItf` 的介面和 job counter 是否還需要，不應整套重寫。

### 4.4 寵物捕捉尚不能直接說不存在
- 2004 `CSGOUim::OnPetCaptureOK` 證明舊版存在該回呼；新版 `PetAction` 可見 Summon/Recall/Rename/Equip/Feed/SetMainSkill 等，但沒有獨立名為 `Capture` 的分支。
- 目前可透過物品/腳本獲取寵物，所以先核對舊版實際捕捉入口與規則；**不能只看沒 Capture 封包就斷言所有取得寵物功能缺少**。

### 4.5 表情不是完全缺少
- `client/emotion_panel.gd` 和 `client/game_hud.gd` 有表情按鈕與玩家動畫；但 `PacketRouter.cs` 的 Quest binding `GetTargetEmotion` 有註解表明尚無狀態持久化、目前返回固定的 0 或 -1。先查是否影響舊腳本的條件分支。

### 4.6 新舊資料不相容是會直接影響「功能看得到但不能用」的主要風險
- `client/Items/*.tres`、`client/Skills/*.tres` 才是現有 Runtime ID 基準。老舊 `Old/...` 的 Shop、Drop、Quest ID 可能碰撞；`ShopCatalog` / `EnemyDropCatalog` 的 remap/skip 是保守策略，不保證每個舊物品都有替代品。
- `tests/SGO.MappingCoverageReport` 和 `tests/SGO.ItemCoverageReport --strict` 必須留報告與未映射清單。嚴禁把錯配項目直接發給玩家。

## 5. 驗收分層與執行順序

### P0 — 不新增玩具功能，先讓遊戲穩定可玩
1. **測試真的有執行**：`dotnet build`、領域 Smoke、Full、Wire、Godot headless；Actions job `steps=null` 不能說 PASS。
2. **資產守恆**：10/50/100 人同怪、同掉物、Trade/Shop/Bank/Craft/Repair 在正反例、斷線與重試下不能雙倍發獎或丟物。
3. **AOI 和地圖重同步**：近遠 Spawn/Move/Despawn 單一真相、回圖物件不消失、已撿走物品不復活、怪物不瞬移。
4. **遊戲格與座標**：Client 原始地圖/Blocking 與 Server 路徑、NPC 觸發距離一致。
5. **出錯可追蹤**：PACKET_HANDLER_FAILED/NO_RESPONSE/DB/AOI warnings 及服務管理 UI 可排查。

### P1 — 核心老遊戲實際體驗
1. 大量舊技能對照（特別是傳回未還原訊息的技能），逐 SkillID 建立正反例。
2. 舊 NPC/Quest 的高頻主線與重要支線：選擇→完成→領取→重登，不能只跑 host function 名稱稽核。
3. Legacy item ID/shop/drop/script mapping 回報，確認外觀、數量、機率、原始效果。
4. 職業與成長數值、寵物/機甲公式比對，而不是把已存在功能重寫。

### P2 — 可獨立規劃的舊版差異
- 玩家 PK/PvP、角色改名、收藏簿（**先確認實際 UI 與資料，並不代表現在必做**）、寵物捕捉（先確定資料和觸發）、聊天密語切換/表情、舊劇情 media、排行榜語意。
- PvP/Pk 競技賽程分開規劃；不要把一般排行榜等同 PK 專賽。

### P3 — 有基礎能力或較低急迫性
- PK 歷屆賽程/獎品、視窗音效/FPS 選項、非必要節慶活動，以及新版 Server 管理/編輯器的維護。
- 目前社團基本功能在 `ClubService` 已可用；**不要為舊版高階社團功能擴寫**。

### P-LAST — 使用者決定暫不加入，固定放在文件最後
- **好友名單、加刪好友、好友黑名單、好友 P2P/離線訊息與好友歷史**。
- **複合機器人／Robot Complex**。
- **Salon System**。
- **社團／社交進階系統、聯盟與社交活動**（保留現有戰團基本功能）。
- **外部付款／舊服點數**：屬外部依賴，沒有 provider 時不得用假成功代替（是否開發另行決定）。

## 6. 建議新增的回歸測試文件／證據格式

每新增或修正舊版功能，都要在測試目錄記錄：`LegacyClass/IDAAddress`、原始資料檔案/版本、現有 `Godot UI/Signal`、`Request Type/Action`、`Router/Service`、`Store`、正反例、併行結果、重登結果、真跑 log、commit SHA。報告狀態只用：
- **LEGACY_IDENTIFIED**（辨識出舊接口/資源）
- **PARTIAL**（部分可用）
- **IMPLEMENTED_UNVERIFIED**（有實作，但未真跑）
- **VERIFIED_PASS**（有同一個 commit 的執行證據）
- **DEFERRED_BY_OWNER**（明確暫不做）

此文件的每一列並未宣稱已有 VERIFIED_PASS。採最小修改、沿用既有 Client/Server 架構、逐功能補資料與測試，而非為了跟舊 EXE 名稱一樣就重新引入舊架構。

## 7. 對照原始碼與工具入口

- [目前執行缺口](FEATURE_GAPS.md)
- [封包契約](../tests/PACKET_CONTRACT.md)
- [Server 測試架構](../tests/SMOKE_TESTS.md)
- [PacketRouter](Application/PacketRouter.cs)
- [CombatService](Application/Combat/CombatService.cs)
- [QuestRuntime](Infrastructure/Quest/QuestRuntime.cs)
- [ClubService](Application/Club/ClubService.cs)
- [RankService](Application/Rank/RankService.cs)
- [Godot Game HUD](../client/game_hud.gd)
- [Godot 聊天](../client/scripts/chat_panel.gd)
- [Godot 任務](../client/scripts/quest_window.gd)
- [Godot 寵物](../client/scripts/pet_window.gd)

> 此次交付是文件與差異盤點，**不修改遊戲程式、不宣稱已跑 .NET/Godot**。