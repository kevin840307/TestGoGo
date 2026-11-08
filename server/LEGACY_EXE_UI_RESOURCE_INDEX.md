# SGO 2009 Client.exe UI 資源索引（105 種 .itf）

> 2026-11-08：直接從使用者提供的 `Client.exe(1).lst` 中掃描 `.text` 的 `push offset "...itf"`，按 **完整字串去重，共 105 種**。此索引配合 [主功能對照](LEGACY_EXE_PARITY_AUDIT.md)，不是新版缺少 105 個視窗的宣告。
>
> 每列地址與行數指向上傳的 **2009 Client.exe IDA .lst**，不指向 GitHub 檔案。舊 UI 可能被藏在遊戲資料封包，檔案沒出現在 GitHub 不代表未存在於原始遊戲。2004 年的 Paradise.exe 是不同客戶端版本，不應直接拿其 UI 當 2009 的完整清單。
>
> **只看目前需求**：好友、複合機器人、Salon、社團／社交進階固定最後且 **P-LAST 暫不開發**。下面按先做/待查/最後三種區分。

## 收藏簿定位（優先回答 UI 對應）

- 2009 收藏主 UI：`..\\095_Collection.itf`，IDA 第 **340901** 行，VA `004D72CA`。
- 主介面類別識別：`CCollectionSystemItf`，第 **341476** 行，VA `004D77DE`。
- 收藏子介面類別識別：`CCollectionSystemSubItf`，第 **342810** 行，VA `004D8409`。
- 子介面命名前綴：`..\\095_Collection_`，第 **343419／343433** 行，完整後綴需原資源/動態值確認。
- 收藏 icon：`..\\B_COLLECTION_ICON.spr`，第 **344204** 行（另有重複載入）。
- 收藏資料：`Item\\Collection.itd` 第 **344661** 行、`Interface\\FlagCollectionData.dat` 第 **11284** 行、`Interface\\ItemCollectionData.dat` 第 **11401** 行。
- 初始化文字：`LIB - Collection Book.` 第 **3497** 行，因此可以先用 Collection Book／收藏簿尋找。**目前尚未確認遊戲內的繁中視窗標題、工具列按鈕或快捷鍵**。

## 分組完整 UI 資源清單

### 角色登入與伺服器選擇（8 種）

新版對照：client/LoginScreen.tscn、client/character_select.tscn。登入/選角已有；選服、郵件驗證與密碼細節尚需行為比對。

| 舊版 UI 檔名 | 2009 IDA .lst 行號 |
|---|---:|
| `011_LOGIN_LOADING.itf` | 68477 |
| `005_Create_Character.itf` | 70139 |
| `001_Account_Login.itf` | 77123 |
| `004_Select_Character.itf` | 79031 |
| `003_Select_Server.itf` | 80803 |
| `082_LOGIN_PASSWORD.itf` | 462966 |
| `012_DEL_EMAIL.itf` | 463250 |
| `081_LOGIN_CHANGE_PASSWORD.itf` | 463679 |

### 角色／HUD／任務／戰鬥（36 種）

新版對照：client/game_hud.gd、client/scripts/chat_panel.gd、client/scripts/quest_window.gd、client/scripts/npc_dialog_panel.gd、client/scripts/skill_window.gd。主要 UI 具備；職業、快捷鍵頁數、表情/泡泡/介面變體須驗證。

| 舊版 UI 檔名 | 2009 IDA .lst 行號 |
|---|---:|
| `097_Quest_List.itf` | 181476 |
| `010_MESSAGE02.itf` | 96895 |
| `038_waitingbox.itf` | 100214 |
| `010_MESSAGE01.itf` | 100711 |
| `017_CHANNEL_BIG.itf` | 111909 |
| `016_CHANNEL.itf` | 112974 |
| `013_Character_info.itf` | 116691 |
| `015_Character_JOB.itf` | 122227 |
| `006_StatusLite_G.itf` | 123177 |
| `006_StatusLite.itf` | 123917 |
| `014_Character_NICK.itf` | 126064 |
| `007_HOTKEY_yl.itf` | 135109 |
| `008_MINIMAP_L.itf` | 137543 |
| `008_MINIMAP_S.itf` | 139511 |
| `018_EMOTION_BIG.itf` | 141962 |
| `029_SYSTEM.itf` | 146474 |
| `019_TALKBUBBLE_BIG.itf` | 148668 |
| `026_TEAM.itf` | 150180 |
| `021_COMMAND.itf` | 178918 |
| `027_Skill.itf` | 182448 |
| `079_MainQuest.itf` | 224410 |
| `041_balloon_01.itf` | 226277 |
| `047_NPC_message.itf` | 226330 |
| `041_balloon_02.itf` | 226108 |
| `041_balloon_03.itf` | 226125 |
| `041_balloon_04.itf` | 226142 |
| `041_balloon_05.itf` | 226159 |
| `041_balloon_06.itf` | 226176 |
| `041_balloon_07.itf` | 226193 |
| `049_NPC_message02.itf` | 226352 |
| `049_NPC_message02B.itf` | 226376 |
| `051_NPC_message04.itf` | 226482 |
| `051_NPC_message05.itf` | 226420 |
| `051_NPC_message06.itf` | 226445 |
| `053_SCROLLMESSAGE.itf` | 240638 |
| `084_ENEMY_HP.itf` | 316018 |

### 排行榜與榜首資訊（2 種）

新版對照：client/scripts/rank_window.gd、server/Application/Rank/RankService.cs。有一般排行，但不可等同 PK 賽程。

| 舊版 UI 檔名 | 2009 IDA .lst 行號 |
|---|---:|
| `024_RANK.itf` | 142901 |
| `025_RANK_02.itf` | 278054 |

### 物品、商店、交易、製作及退款（16 種）

新版對照：client/scripts/inventory_window.gd、client/scripts/npc_shop_window.gd、client/scripts/trade_window.gd、client/scripts/craft_dismantle_window.gd。主要交易/製作有實作；098_Refund 只見 UI 資源，實際用途與 Client 對應尚未確定。

| 舊版 UI 檔名 | 2009 IDA .lst 行號 |
|---|---:|
| `022_equip.itf` | 153753 |
| `034_Bank.itf` | 186459 |
| `032_SHOP_02.itf` | 215201 |
| `028_Mix_item.itf` | 190017 |
| `096_Dissolve.itf` | 190029 |
| `030_REPAIR.itf` | 192751 |
| `031_Shop.itf` | 206960 |
| `035_TRADE_02.itf` | 211246 |
| `055_TRADE_MONEY.itf` | 222530 |
| `092_PShop_Buy.itf` | 326184 |
| `092_PShop_Buy_Item_02.itf` | 326921 |
| `092_pshop_name.itf` | 327748 |
| `092_pshop.itf` | 328557 |
| `092_PShop_Sell_Item_02.itf` | 331230 |
| `092_PShop_Sell_Item_01.itf` | 331381 |
| `098_Refund.itf` | 345287 |

### 寵物與機甲（9 種）

新版對照：client/scripts/pet_window.gd、client/scripts/horse_preview_window.gd。主要功能有實作；公式、捕捉/技能/卡片語意待核。

| 舊版 UI 檔名 | 2009 IDA .lst 行號 |
|---|---:|
| `024_Pet.itf` | 166967 |
| `037_PetEquip.itf` | 228341 |
| `039_PetInfo.itf` | 237956 |
| `036_PetItem.itf` | 239140 |
| `056_Driver_item.itf` | 296560 |
| `059_Driver_Repair.itf` | 301642 |
| `056_Driver_INFO.itf` | 305994 |
| `083_PET_CARD.itf` | 314384 |
| `056_Driver_INFO_01.itf` | 314869 |

### 改名介面（適用對象未定）（1 種）

新版對照：client/scripts/pet_window.gd 的寵物改名；舊版角色/機甲改名需細分。此介面在舊 EXE 多處使用，不能單憑檔名判為角色改名。

| 舊版 UI 檔名 | 2009 IDA .lst 行號 |
|---|---:|
| `046_Rename.itf` | 317015 |

### 收藏簿／Collection Book（1 種）

新版對照：新版未找到對等 Collection UI。舊檔可以定位收藏主畫面；開啟按鈕、解鎖規則仍未知。

| 舊版 UI 檔名 | 2009 IDA .lst 行號 |
|---|---:|
| `..\\095_Collection.itf` | 340901 |

### 好友與離線訊息（暫不開發）（3 種）

新版對照：無好友 CRUD/歷史/黑名單完整流程。P-LAST：僅存檔案索引，不列為近期待辦。

| 舊版 UI 檔名 | 2009 IDA .lst 行號 |
|---|---:|
| `042_FRIENDlIST_B.itf` | 174339 |
| `043_FRIEND_MSG.itf` | 175458 |
| `020_FRIENDlIST.itf` | 178229 |

### 複合機器人（暫不開發）（3 種）

新版對照：一般機甲 ≠ 複合機器人。P-LAST。

| 舊版 UI 檔名 | 2009 IDA .lst 行號 |
|---|---:|
| `087_DRIVE_COMPLEX_BAR.itf` | 315061 |
| `085_COMPLEX_INFO.itf` | 316540 |
| `088_COMPLEX_HP.itf` | 319926 |

### Salon / 髮型店（暫不開發）（1 種）

新版對照：新版無 Salon 專用頁。P-LAST。

| 舊版 UI 檔名 | 2009 IDA .lst 行號 |
|---|---:|
| `..\\094_Hair_Shop.itf` | 338530 |

### 社團／社交進階（暫不開發）（25 種）

新版對照：client/scripts/clan_window.gd、server/Application/Club/ClubService.cs。P-LAST：維持現有基本戰團，不追舊版所有進階介面。

| 舊版 UI 檔名 | 2009 IDA .lst 行號 |
|---|---:|
| `094_CLUB.itf` | 243815 |
| `075_CLUB_TechDevelop02.itf` | 247795 |
| `080_CLUB_RANK.itf` | 249873 |
| `068_CLUB_OKBox.itf` | 284811 |
| `063_CLUB_GoldList.itf` | 256378 |
| `065_CLUB_BookMark.itf` | 257093 |
| `058_CLUB_Ext.itf` | 258928 |
| `062_CLUB_ALLYList.itf` | 261181 |
| `078_CLUB_DisAlly.itf` | 262287 |
| `070_CLUB_News_EDIT.itf` | 262718 |
| `067_CLUB_News.itf` | 263634 |
| `069_CLUB_INTRO_EDIT.itf` | 273165 |
| `066_CLUB_Intro.itf` | 265226 |
| `060_CLUB_MemList.itf` | 265894 |
| `060_CLUB_MemList02.itf` | 267708 |
| `057_CLUB_Menu.itf` | 268112 |
| `077_CLUB_Takemoney.itf` | 272875 |
| `073_CLUB_ResourceInput.itf` | 273009 |
| `069_CLUB_NEWS_EDIT.itf` | 273597 |
| `072_CLUB_DonateInput.itf` | 273931 |
| `074_CLUB_TechDevelop.itf` | 275500 |
| `064_CLUB_TechList.itf` | 279884 |
| `068_CLUB_YesNoBox.itf` | 282130 |
| `076_CLUB_TechUPGRADE.itf` | 287064 |
| `061_CLUB_Name.itf` | 287324 |

## 不能從檔名直接推定的問題

- `098_Refund.itf`（行 345287）僅能確認舊版有介面資源；**Refund 是遊戲內哪一種退款/返還、是否可使用、是否與點數有關都未確認**，不能誤當 PayPal 退款。
- `046_Rename.itf`（行 317015）在 2009 其他位置也有呼叫，屬共用改名視窗的可能性高；需先查呼叫者，再說它是角色、寵物還是機甲改名。
- `003_Select_Server.itf` 是老式選服 UI，本地單服 .NET Server 未保留它**可能是架構取捨**，不是必須修復的遊戲核心缺口。
- `007_HOTKEY_yl.itf` 可能對應舊版多頁快捷欄；2004 `CHotKeyItf::OnPage1/OnPage2` 有更明確線索，現代 `F1~F12` 與快捷資料持久化需要檢查是否滿足使用需求。
- 舊 `094_Hair_Shop.itf`、`095_Collection.itf`、`096_Dissolve.itf`、`097_Quest_List.itf`、`098_Refund.itf` 是不同子系統的 UI；不要只看尾號接近就推論功能連結。

## 後續檢查優先順序

1. **核心操作**：物品／技能／地圖／NPC／Quest／交易／寵物／機甲的實機 E2E 與資料對齊。
2. **待確認的 UI 語意**：收藏簿入口、舊 `098_Refund.itf`、`046_Rename.itf` 適用範圍、快捷欄分頁。
3. **暫不開發**：好友／複合機器人／Salon／社團進階，只保留本索引，不增加近期待辦或測試強制要求。

