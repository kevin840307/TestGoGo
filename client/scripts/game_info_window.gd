extends Panel

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const UI_MONSTER = preload("res://scripts/ui_monster.gd")
const MONSTER_DATA_PATH: = "res://Assets/GameInfo/Enemy_Data.json"
const MONSTER_SPAWN_DATA_PATH: = "res://Assets/GameInfo/MapSpawnConfig.json"
const SKILL_INFO_DATA_PATH: = "res://Assets/GameInfo/Skill.csv"
const FESTIVAL_INFO_DATA_PATH: = "res://Assets/GameInfo/Festival_Data.json"
const MONSTER_DROP_PAGE_SIZE: = 10
const EQUIPMENT_PAGE_SIZE: = 24
const EQUIPMENT_LOAD_BATCH_SIZE: = 64
const EQUIP_EXCLUDED_ID_MIN: = 19263
const EQUIP_EXCLUDED_ID_MAX: = 19347
const WEAPON_EXCLUDED_ID_MIN: = 31639
const WEAPON_EXCLUDED_ID_MAX: = 31641
const EQUIPMENT_SLOT_FILTERS: = [
	{"label": "全部", "slot": -1}, 
	{"label": "頭飾", "slot": 0}, 
	{"label": "左手", "slot": 1}, 
	{"label": "右手", "slot": 2}, 
	{"label": "上身", "slot": 3}, 
	{"label": "下身", "slot": 4}, 
	{"label": "武器", "slot": 5}, 
	{"label": "盾牌", "slot": 6}, 
	{"label": "背飾", "slot": 7}, 
	{"label": "護符", "slot": 8}
]
const SKILL_INFO_MIN_ID: = 20101
const SKILL_INFO_MAX_ID: = 33013
const ENGRAVING_SKILL_MIN_ID: = 45102
const ENGRAVING_SKILL_MAX_ID: = 45118
const SKILL_INFO_COLUMN_COUNT: = 54
const SKILL_INFO_EXCLUDED_CLASSES: = ["道具使", "精靈使"]
const SKILL_FILTER_CLASSES: = [
	"全部", "靈鬥士", "守護劍士", "聖劍士", "銃戰士", "魔動使", "守護使", 
	"聖靈使", "魔銃使", "噬魂使", "無限制"
]
const SKILL_CLASS_COLORS: = {
	"靈鬥士": Color(0.94, 0.27, 0.27, 1.0), 
	"守護劍士": Color(0.23, 0.51, 0.96, 1.0), 
	"聖劍士": Color(0.92, 0.72, 0.12, 1.0), 
	"銃戰士": Color(0.98, 0.45, 0.09, 1.0), 
	"魔動使": Color(0.66, 0.33, 0.97, 1.0), 
	"守護使": Color(0.02, 0.71, 0.83, 1.0), 
	"聖靈使": Color(0.13, 0.77, 0.37, 1.0), 
	"魔銃使": Color(0.93, 0.28, 0.6, 1.0), 
	"道具使": Color(0.45, 0.52, 0.62, 1.0), 
	"精靈使": Color(0.31, 0.72, 0.58, 1.0), 
	"噬魂使": Color(0.6, 0.11, 0.11, 1.0), 
	"無限制": Color(0.34, 0.41, 0.51, 1.0)
}
const ENGRAVING_REQUIREMENTS: = {
	45104: {
		"mode": "all", 
		"items": [18089, 18090, 18091, 18092, 18093, 18094, 18095, 18096, 18097, 18098, 18099, 18100]
	}, 
	45105: {
		"mode": "all", 
		"items": [19093, 19094, 19110, 19111]
	}, 
	45109: {
		"mode": "all", 
		"items": [18029, 18043, 19179]
	}, 
	45110: {
		"mode": "all", 
		"items": [18021, 19003, 19004, 19005, 19178]
	}, 
	45111: {
		"mode": "groups", 
		"groups": [
			{"label": "頭飾", "items": [16013, 16014, 16015, 16016, 16017, 16018]}, 
			{"label": "上身", "items": [16019, 16020, 16021, 16022, 16023, 16024]}, 
			{"label": "下身", "items": [16025, 16026, 16027, 16028, 16029, 16030]}, 
			{"label": "護手", "items": [16031, 16032, 16033, 16034, 16035, 16036]}, 
			{"label": "武器", "items": [30201, 30202, 30203, 30204, 30205, 30206]}
		]
	}, 
	45116: {
		"mode": "all", 
		"items": [15127, 15128, 15129, 15130]
	}
}
const COLOR_INDEX_TO_DEGREES: = {
	0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 
	4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0
}

const CATEGORIES: = [
	"節日活動", "皇家領地戰", "世界王", "種族", "職業", "技能", "刻紋", "裝備", 
	"寵物", "戰團", "怪物掉落", "獻寶娃娃", "變身卷軸", "快捷鍵"
]

const FESTIVAL_SKELETON_ENTRIES: = [
	{"id": "lunar_new_year", "name": "農曆新年"}, 
	{"id": "valentine", "name": "情人節"}, 
	{"id": "white_valentine", "name": "白色情人節"}, 
	{"id": "easter", "name": "復活節"}, 
	{"id": "dragon_boat", "name": "端午節"}, 
	{"id": "grand_hunt", "name": "大獵祭"}, 
	{"id": "mid_autumn", "name": "中秋節"}, 
	{"id": "halloween", "name": "萬聖節"}, 
	{"id": "christmas", "name": "聖誕節"}
]

const ROYAL_TERRITORY_WAR_SECTIONS: = [
	{
		"title": "報名與入場", 
		"accent": Color(0.35, 0.7, 0.96, 1.0), 
		"items": [
			["報名期間", "每週一 00:00 ～ 週五 18:00"], 
			["戰團報名", "團長先到尼尼亞皇城找貝爾迪諾報名，每週最多四團，依序分配 A、B、C、D。"], 
			["個人報名", "戰團報名成功後，團員仍須在期限內自行完成個人報名。"], 
			["進場期間", "週五 20:00 ～ 21:00，先進入預備區，再找「進入戰場」前往正式戰場。"]
		]
	}, 
	{
		"title": "入場限制", 
		"accent": Color(0.95, 0.66, 0.25, 1.0), 
		"items": [
			["角色狀態", "只有開啟戰鬥狀態且為白名的玩家可以進入。"], 
			["機甲與寵物", "進場時不可駕駛機甲或召喚寵物，領地戰場地內同樣禁止使用。"]
		]
	}, 
	{
		"title": "戰場規則", 
		"accent": Color(0.94, 0.39, 0.36, 1.0), 
		"items": [
			["自由交戰", "預備區與正式戰場內，任何玩家皆可互相攻擊。"], 
			["活動豁免", "攻擊或擊殺玩家不會變成紫名或增加紅名懲罰，被擊殺不會增加死亡經驗懲罰。"]
		]
	}, 
	{
		"title": "能量石與積分", 
		"accent": Color(0.42, 0.84, 0.63, 1.0), 
		"items": [
			["1　取得", "到戰場中央點擊能量柱，每位玩家同時最多持有一個能量石。"], 
			["2　護送", "把能量石安全帶回自己戰團的領地柱，不能交給其他戰團。"], 
			["3　得分", "成功繳交一個能量石，戰團獲得 1 分。"], 
			["死亡掉落", "正式戰場中被擊殺會掉落攜帶的能量石，其他未持有能量石的玩家可以拾取。"]
		]
	}, 
	{
		"title": "結算與獎勵", 
		"accent": Color(0.93, 0.76, 0.3, 1.0), 
		"items": [
			["勝負判定", "週五 21:00 結算。最高分戰團獲勝，同分並列獲勝，零分不產生勝方。"], 
			["戰團獎勵", "每個獲勝戰團獲得 500 萬戰團經驗與 500 萬戰團資金。"], 
			["個人紀錄", "獲勝戰團中，本週已報名且結算時仍在團內的成員，累計 1 次獲勝次數。"], 
			["活動結束", "可點擊場內活動 NPC 返回尼尼亞皇城，離圖、結束退出或下次登入時會移除活動能量石。"]
		]
	}
]

const SHORTCUTS: = [
	[["Ctrl", "E"], "角色資料"], [["Ctrl", "W"], "背包"], [["Ctrl", "S"], "技能"], [["Ctrl", "D"], "隊伍"], 
	[["Ctrl", "J"], "任務"], [["Ctrl", "Q"], "系統設定"], [["Ctrl", "G"], "合成/分解物品"], 
	[["Ctrl", "F"], "開啟/關閉 全螢幕"], [["Ctrl", "H"], "隱藏使用者介面"], 
	[["Alt"], "蹲下（可回復角色 HP 和 MP）"]
]

const ATTRIBUTE_RANKINGS: = [
	{"name": "體質 （+生命上限）", "tiers": [["人族男", "亞斯人"], ["人族女"], ["米古族", "機動靈"]]}, 
	{"name": "力量 （+攻擊）", "tiers": [["機動靈"], ["人族男", "人族女", "米古族", "亞斯人"]]}, 
	{"name": "速度 （+命中/迴避）", "tiers": [["米古族"], ["人族女"], ["人族男", "亞斯人", "機動靈"]]}, 
	{"name": "智慧 （+魔力上限/魔攻）", "tiers": [["米古族"], ["人族男", "人族女", "機動靈", "亞斯人"]]}
]

const ATTACK_SPEED_RANKINGS: = [
	{"name": "基礎攻速", "tiers": [["米古族"], ["人族男", "人族女", "亞斯人"], ["機動靈"]]}
]

const JOB_INFOS: = [
	{
		"name": "靈鬥士", 
		"description": "擁有強大的物理攻擊力，是物理攻擊最高的職業，也能使出大範圍技能。", 
		"races": ["人族男", "人族女", "米古族", "機動靈"], 
		"stats": "每三級一點速度，其餘全部加力量。"
	}, 
	{
		"name": "守護劍士", 
		"description": "防禦力最強的職業，大大增加了續戰能力，最適合擔任肉盾和引怪的工作。", 
		"races": ["人族男", "人族女", "米古族", "機動靈"], 
		"stats": "速度和力量平均分配，維持力量高於速度三點。"
	}, 
	{
		"name": "聖劍士", 
		"description": "沒有強大的物理攻擊力，也沒有堅固的物理防禦力，但能夠治療自己就是他的優點。", 
		"races": ["人族男", "人族女", "米古族", "機動靈"], 
		"stats": "速度和力量平均分配，維持力量高於速度六點，並增加小量智慧。"
	}, 
	{
		"name": "銃戰士", 
		"description": "速度彌補了防禦較低的缺點。雖然攻擊力比靈鬥士低，但遠距離攻擊能為他奠定勝局。", 
		"races": ["人族男", "人族女", "米古族", "機動靈"], 
		"stats": "力量和速度平均分配。"
	}, 
	{
		"name": "魔動使", 
		"description": "擁有強大的魔法攻擊力，但也成為防禦力最低的職業，能使出大範圍技能。", 
		"races": ["人族男", "人族女", "米古族", "機動靈"], 
		"stats": "增加智慧和速度，智慧維持高於速度兩倍。"
	}, 
	{
		"name": "守護使", 
		"description": "擁有大量輔助技能，能為自己和別人增加能力，還能令死亡的人復生。", 
		"races": ["人族男", "人族女", "米古族", "機動靈"], 
		"stats": "一點力量、一點速度、一點智慧。"
	}, 
	{
		"name": "聖靈使", 
		"description": "隊伍中不可缺少的夥伴，能快速為別人增加生命，是人見人愛的職業。", 
		"races": ["人族男", "人族女", "米古族", "機動靈"], 
		"stats": "增加智慧和速度，智慧維持高於速度兩倍。"
	}, 
	{
		"name": "魔銃使", 
		"description": "和銃戰士一樣都是遠距離攻擊，分別在於魔銃使使用魔法攻擊，並有驚人的迴避能力。", 
		"races": ["人族男", "人族女", "米古族", "機動靈"], 
		"stats": "智慧和速度平均分配，維持智慧高於速度 4～6 點。"
	}, 
	{
		"name": "噬魂使", 
		"description": "兼具近戰與魔法輸出能力，具備範圍攻擊技能，是熱門的練功打手選擇之一。", 
		"races": ["亞斯人"], 
		"stats": "物理型：力量與速度平均分配，魔法型：智慧與速度平均分配。"
	}
]

const DOLL_INFOS: = [
	{
		"name": "幸福娃娃", 
		"item_id": 2126, 
		"rewards": [
			1011, 1012, 1013, 50101, 50102, 50103, 2615, 2616, 
			19179, 19178, 19183, 19180, 19059, 19109, 19110, 19112, 
			19003, 19008, 19094, 19093, 19111, 18101, 19019, 18029, 
			18030, 18031, 18032, 18037, 18038, 18039, 19085, 19086, 
			19087, 19088, 2130, 2128, 2129, 19061, 19062, 19063, 
			19064, 19065, 19066, 19067, 19068, 19182, 19023, 19026, 
			19029, 2401, 2402, 2403, 2404, 18021
		]
	}, 
	{
		"name": "神聖娃娃", 
		"item_id": 2127, 
		"rewards": [
			1011, 1012, 1013, 50101, 50102, 50103, 2615, 2616, 
			18010, 18108, 18109, 18110, 19129, 19128, 19005, 19004, 
			19060, 2769, 2205, 18033, 18034, 18035, 18040, 18041, 
			18042, 19089, 19090, 19091, 19024, 19027, 19030, 2130, 
			2128, 2129, 2405, 2406, 2407, 2408, 2409, 2410
		]
	}, 
	{
		"name": "十二星座帽娃娃", 
		"item_id": 2128, 
		"rewards": [
			18053, 18054, 18055, 18056, 18057, 18058, 
			18059, 18060, 18061, 18062, 18063, 18064, 
			18065, 18066, 18067, 18068, 18069, 18070, 
			18071, 18072, 18073, 18074, 18075, 18076
		]
	}, 
	{
		"name": "十二星座背飾娃娃", 
		"item_id": 2129, 
		"rewards": [
			18077, 18078, 18079, 18080, 18081, 18082, 
			18083, 18084, 18085, 18086, 18087, 18088
		]
	}, 
	{
		"name": "十二生肖帽娃娃", 
		"item_id": 2130, 
		"rewards": [
			18089, 18090, 18091, 18092, 18093, 18094, 
			18095, 18096, 18097, 18098, 18099, 18100
		]
	}, 
	{
		"name": "熊爪娃娃", 
		"item_id": 2131, 
		"rewards": [2451, 30207]
	}, 
	{
		"name": "熊護手娃娃", 
		"item_id": 2132, 
		"rewards": [2452, 19247]
	}, 
	{
		"name": "情人娃娃", 
		"item_id": 2133, 
		"rewards": [
			2816, 2006, 2007, 2008, 2009, 2010, 2011, 2012, 
			2013, 2014, 2015, 2016, 2017, 2018, 2019, 
			2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027
		]
	}, 
	{
		"name": "南瓜娃娃", 
		"item_id": 2134, 
		"rewards": [1070, 2821]
	}
]

const DOLL_REWARD_COUNTS: = {
	2006: 30, 2007: 30, 2008: 30, 2009: 30, 2010: 30, 2011: 30, 
	2012: 30, 2013: 30, 2014: 30, 2015: 30, 2016: 30, 2017: 30, 
	2018: 30, 2019: 30, 2020: 30, 2021: 30, 2022: 30, 2023: 30, 
	2024: 30, 2025: 30, 2026: 30, 2027: 30
}

const CLUB_CREATION_REQUIREMENTS: = [
	{"label": "申請資格", "value": "角色目前未加入戰團，且等級達到 30 級"}, 
	{"label": "領取文件", "value": "到首都．尼尼亞皇城找皇家戰團管理員領取三份申請文件"}, 
	{"label": "完成簽署", "value": "分別由人族、米古族、機動靈的 30 級以上角色簽署對應文件"}, 
	{"label": "回交文件", "value": "帶回完成．人族企劃書、完成．米古族推薦報告與完成．機動靈計策報告"}, 
	{"label": "成立費用", "value": "備妥 50 萬星幣並向皇家戰團管理員申請成立，未持有戰團時可再次辦理"}
]

const CLUB_SHOP_LEVELS: = [
	{
		"level": 1, 
		"item_ids": [1, 4, 7, 10]
	}, 
	{
		"level": 2, 
		"item_ids": [1, 2, 4, 5, 7, 8, 10, 11]
	}, 
	{
		"level": 3, 
		"item_ids": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]
	}, 
	{
		"level": 4, 
		"item_ids": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 31569, 19237]
	}, 
	{
		"level": 5, 
		"item_ids": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 1011, 1012, 1013, 31569, 19237]
	}, 
	{
		"level": 6, 
		"item_ids": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 20, 1011, 1012, 1013, 31569, 19237]
	}, 
	{
		"level": 7, 
		"item_ids": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 20, 1011, 1012, 1013, 31569, 19237, 50705, 50706, 50707, 50708]
	}
]

const CLUB_RESEARCH_INFOS: = [
	{"name": "等級", "effect": "每級增加戰團人數上限 10 人，並解鎖更多戰團商店商品。"}, 
	{"name": "攻擊", "effect": "每級增加 10 點攻擊。"}, 
	{"name": "防禦", "effect": "每級增加 10 點防禦。"}, 
	{"name": "魔攻", "effect": "每級增加 10 點魔攻。"}, 
	{"name": "魔防", "effect": "每級增加 10 點魔防。"}, 
	{"name": "命中", "effect": "每級增加 5 點命中。"}, 
	{"name": "迴避", "effect": "每級增加 5 點迴避。"}, 
	{"name": "移速", "effect": "每級增加 5 點。"}, 
	{"name": "攻速", "effect": "每級增加 5 點。"}, 
	{"name": "暴擊率", "effect": "每級增加 3% 暴擊率。"}, 
	{"name": "暴擊傷害", "effect": "每級增加 5% 暴擊傷害。"}
]

const CLUB_RESEARCH_REQUIREMENTS: = [
	{"label": "戰團資金", "value": "研發等級 × 50 萬星幣"}, 
	{"label": "戰團經驗", "value": "研發等級 × 100 萬經驗"}, 
	{"label": "研發時間", "value": "研發等級 × 6 小時"}, 
	{"label": "研發權限", "value": "僅團長可以進行研發"}, 
	{"label": "研發限制", "value": "每項最高 5 級，且同一時間只能研發一個項目"}
]

const CLUB_COMMAND_INFOS: = [
	{
		"command": "/poweron 職位代號 玩家名稱", 
		"summary": "變更玩家職位", 
		"details": [
			"職位代號：0 為團員、1 為騎士、2 為驍將騎士。", 
			"團長可以任命驍將騎士、騎士與團員。", 
			"驍將騎士可以任命騎士與團員。", 
			"不能變更自己的職位，也不能變更同職位或更高職位的成員。"
		]
	}, 
	{
		"command": "/fire 玩家名稱", 
		"summary": "將玩家剔除戰團", 
		"details": [
			"僅團長與驍將騎士可以使用。", 
			"只能剔除職位低於自己的成員。"
		]
	}, 
	{
		"command": "/quit", 
		"summary": "退出戰團", 
		"details": ["團長無法直接退出戰團。"]
	}, 
	{
		"command": "/dismiss", 
		"summary": "解散戰團", 
		"details": ["僅團長可以使用。"]
	}
]

const WORLD_BOSS_INFOS: = [
	{
		"name": "波奇王", 
		"monster_id": "EN010B", 
		"spawn_time": "每天 19:15", 
		"locations": ["流星之河．上游", "流星之河．下游"], 
		"rewards": [
			{"item_id": 4002, "count": 100}, 
			{"item_id": 2101, "count": 5}
		]
	}, 
	{
		"name": "遊俠立普", 
		"monster_id": "EN029B", 
		"spawn_time": "每天 19:45", 
		"locations": ["流星之河．上游", "流星之河．下游"], 
		"rewards": [
			{"item_id": 4003, "count": 100}, 
			{"item_id": 2105, "count": 5}
		]
	}, 
	{
		"name": "索爾巨魔", 
		"monster_id": "EN008B", 
		"spawn_time": "每天 20:15", 
		"locations": ["尼西亞台地", "尼尼亞盆地"], 
		"rewards": [
			{"item_id": 4004, "count": 100}, 
			{"item_id": 2102, "count": 5}
		]
	}, 
	{
		"name": "魔機王偶", 
		"monster_id": "EN032B", 
		"spawn_time": "每天 20:45", 
		"locations": ["尼西亞台地", "尼尼亞盆地"], 
		"rewards": [
			{"item_id": 4005, "count": 100}, 
			{"item_id": 2106, "count": 5}
		]
	}, 
	{
		"name": "帝王白兔", 
		"monster_id": "EN045B", 
		"spawn_time": "每天 21:15", 
		"locations": ["飢餓荒地", "戰爭平原"], 
		"rewards": [
			{"item_id": 4006, "count": 100}, 
			{"item_id": 2103, "count": 5}
		]
	}
]

const TRANSFORM_ITEM_IDS: = {
	"scroll": 2802, 
	"restore_scroll": 2806, 
	"monster_stone": 2819, 
	"dragon_stone": 2820, 
	"true_dragon_stone": 2821
}

const TRANSFORM_PRIORITY: = [
	{"item_id": 2821, "label": "真．龍族之石", "result": "飛龍（固定外型）"}, 
	{"item_id": 2820, "label": "龍族之石", "result": "幼龍（固定外型）"}, 
	{"item_id": 2819, "label": "妖怪靈石", "result": "特殊怪物（隨機外型）"}, 
	{"item_id": 0, "label": "未持有上述道具", "result": "一般怪物（隨機外型）"}
]

const NORMAL_TRANSFORM_NAMES: = [
	"波奇鳥", "波奇王", "黑森魔獸", "巨毛蟲", "幸運球", "蘑菇蟲", 
	"滾滾豬", "星靈騎士", "精靈哈密", "油燈幽靈", "帝王白兔", "星夢女管家．小葵"
]

const MONSTER_STONE_TRANSFORMS: = [
	{"name": "蝙蝠", "server_name": "蝙蝠", "bonuses": [{"stat": "迴避", "value": 400}]}, 
	{
		"name": "傴僂劍士", 
		"server_name": "傴僂劍士", 
		"bonuses": [{"stat": "物攻", "value": 1000}, {"stat": "迴避", "value": 300}]
	}, 
	{"name": "傴僂重擊手", "server_name": "傴僂重擊手", "bonuses": [{"stat": "物攻", "value": 1300}]}, 
	{"name": "死靈角鬥士", "server_name": "死靈角鬥士", "bonuses": [{"stat": "物防", "value": 1300}]}, 
	{"name": "死靈魔動使", "server_name": "死靈魔動使", "bonuses": [{"stat": "魔攻", "value": 1300}]}, 
	{
		"name": "魔女", 
		"server_name": "魔女琪莉亞", 
		"bonuses": [
			{"stat": "魔攻", "value": 1500}, 
			{"stat": "魔防", "value": 1500}, 
			{"stat": "迴避", "value": 500}
		]
	}, 
	{
		"name": "御龍者", 
		"server_name": "御龍者", 
		"bonuses": [
			{"stat": "物攻", "value": 1500}, 
			{"stat": "物防", "value": 1500}, 
			{"stat": "命中", "value": 500}
		]
	}
]

const DRAGON_STONE_TRANSFORMS: = [
	{
		"item_id": 2820, 
		"name": "幼龍", 
		"server_name": "火之幼龍", 
		"bonuses": [
			{"stat": "物攻", "value": 600}, {"stat": "物防", "value": 600}, 
			{"stat": "魔攻", "value": 600}, {"stat": "魔防", "value": 600}
		]
	}, 
	{
		"item_id": 2821, 
		"name": "飛龍", 
		"server_name": "火龍", 
		"bonuses": [
			{"stat": "物攻", "value": 1200}, {"stat": "物防", "value": 1200}, 
			{"stat": "魔攻", "value": 1200}, {"stat": "魔防", "value": 1200}
		]
	}
]

const TRANSFORM_EFFECT_INFOS: = [
	{"label": "持續時間", "value": "1 小時"}, 
	{"label": "提前解除", "value": "使用「還原卷軸」即可解除變身效果。"}, 
	{
		"label": "重新變身", 
		"value": "可再次使用變身卷軸重新判定外型，持有龍族之石或真．龍族之石時，仍會獲得對應的固定外型。"
	}
]

const PET_COMBAT_MODE_INFOS: = [
	{
		"name": "和平模式", 
		"description": "寵物只會跟隨主人移動，不會參與任何戰鬥。", 
		"accent": Color(0.48, 0.78, 0.93, 1.0)
	}, 
	{
		"name": "被動戰鬥", 
		"description": "當主人發起攻擊時，寵物會一起攻擊目標，主人或寵物本身遭受攻擊時，也會對目標展開反擊。", 
		"accent": Color(0.96, 0.72, 0.3, 1.0)
	}, 
	{
		"name": "主動戰鬥", 
		"description": "寵物會自動搜尋主人身旁視野內的怪物並進行攻擊。", 
		"accent": Color(0.93, 0.46, 0.42, 1.0)
	}
]

const PET_PERMANENT_STAT_FOODS: = [
	{"item_id": 1011, "stat": "力量", "accent": Color(0.94, 0.43, 0.37, 1.0)}, 
	{"item_id": 1012, "stat": "智慧", "accent": Color(0.5, 0.65, 0.98, 1.0)}, 
	{"item_id": 1013, "stat": "體質", "accent": Color(0.42, 0.83, 0.58, 1.0)}
]

const TIER_LABELS: = ["最高", "次高", "第三", "第四"]
const TIER_COLORS: = [
	Color(0.95, 0.72, 0.25, 1.0), 
	Color(0.42, 0.72, 0.94, 1.0), 
	Color(0.66, 0.7, 0.76, 1.0), 
	Color(0.55, 0.58, 0.64, 1.0)
]

@onready var category_list: VBoxContainer = $CategoryPanel / CategoryScroll / CategoryList
@onready var detail_title: Label = $ContentPanel / ContentTitle
@onready var content_scroll: ScrollContainer = $ContentPanel / ContentScroll
@onready var detail_text: RichTextLabel = $ContentPanel / ContentScroll / ContentText
@onready var skill_info_content: Control = $ContentPanel / SkillInfoContent
@onready var skill_info_search: LineEdit = $ContentPanel / SkillInfoContent / Layout / SearchRow / SearchInput
@onready var skill_info_result_count: Label = $ContentPanel / SkillInfoContent / Layout / SearchRow / ResultCount
@onready var skill_filter_buttons: HBoxContainer = $ContentPanel / SkillInfoContent / Layout / ClassFilterScroll / ClassFilterButtons
@onready var skill_info_scroll: ScrollContainer = $ContentPanel / SkillInfoContent / Layout / SkillScroll
@onready var skill_info_rows: VBoxContainer = $ContentPanel / SkillInfoContent / Layout / SkillScroll / SkillRows
@onready var skill_info_status: Label = $ContentPanel / SkillInfoContent / Layout / SkillScroll / SkillRows / StatusLabel
@onready var engraving_info_content: Control = $ContentPanel / EngravingInfoContent
@onready var engraving_skill_tabs: VBoxContainer = $ContentPanel / EngravingInfoContent / Layout / SkillTabsPanel / SkillTabsScroll / SkillTabs
@onready var engraving_tabs_status: Label = $ContentPanel / EngravingInfoContent / Layout / SkillTabsPanel / SkillTabsScroll / SkillTabs / StatusLabel
@onready var engraving_detail_scroll: ScrollContainer = $ContentPanel / EngravingInfoContent / Layout / DetailPanel / DetailScroll
@onready var engraving_detail_content: VBoxContainer = $ContentPanel / EngravingInfoContent / Layout / DetailPanel / DetailScroll / DetailContent
@onready var engraving_detail_status: Label = $ContentPanel / EngravingInfoContent / Layout / DetailPanel / DetailScroll / DetailContent / StatusLabel
@onready var equipment_info_content: Control = $ContentPanel / EquipmentInfoContent
@onready var equipment_search: LineEdit = $ContentPanel / EquipmentInfoContent / Layout / SearchRow / SearchInput
@onready var equipment_result_count: Label = $ContentPanel / EquipmentInfoContent / Layout / SearchRow / ResultCount
@onready var equipment_slot_filter_scroll: ScrollContainer = $ContentPanel / EquipmentInfoContent / Layout / SlotFilterScroll
@onready var equipment_slot_filter_buttons: HBoxContainer = $ContentPanel / EquipmentInfoContent / Layout / SlotFilterScroll / SlotFilterButtons
@onready var equipment_scroll: ScrollContainer = $ContentPanel / EquipmentInfoContent / Layout / EquipmentBody / EquipmentScroll
@onready var equipment_grid: GridContainer = $ContentPanel / EquipmentInfoContent / Layout / EquipmentBody / EquipmentScroll / EquipmentGrid
@onready var equipment_status: Label = $ContentPanel / EquipmentInfoContent / Layout / EquipmentBody / StatusLabel
@onready var equipment_previous_page: Button = $ContentPanel / EquipmentInfoContent / Layout / Pagination / PreviousPage
@onready var equipment_page_label: Label = $ContentPanel / EquipmentInfoContent / Layout / Pagination / PageLabel
@onready var equipment_next_page: Button = $ContentPanel / EquipmentInfoContent / Layout / Pagination / NextPage
@onready var monster_drop_content: Control = $ContentPanel / MonsterDropContent
@onready var monster_drop_search: LineEdit = $ContentPanel / MonsterDropContent / Layout / SearchRow / SearchInput
@onready var monster_drop_result_count: Label = $ContentPanel / MonsterDropContent / Layout / SearchRow / ResultCount
@onready var monster_drop_scroll: ScrollContainer = $ContentPanel / MonsterDropContent / Layout / MonsterScroll
@onready var monster_drop_rows: VBoxContainer = $ContentPanel / MonsterDropContent / Layout / MonsterScroll / MonsterRows
@onready var monster_drop_status: Label = $ContentPanel / MonsterDropContent / Layout / MonsterScroll / MonsterRows / StatusLabel
@onready var monster_drop_previous_page: Button = $ContentPanel / MonsterDropContent / Layout / Pagination / PreviousPage
@onready var monster_drop_page_label: Label = $ContentPanel / MonsterDropContent / Layout / Pagination / PageLabel
@onready var monster_drop_next_page: Button = $ContentPanel / MonsterDropContent / Layout / Pagination / NextPage
@onready var shortcut_scroll: ScrollContainer = $ContentPanel / ShortcutScroll
@onready var shortcut_rows: VBoxContainer = $ContentPanel / ShortcutScroll / ShortcutRows
@onready var race_scroll: ScrollContainer = $ContentPanel / RaceScroll
@onready var race_rows: VBoxContainer = $ContentPanel / RaceScroll / RaceRows
@onready var job_scroll: ScrollContainer = $ContentPanel / JobScroll
@onready var job_rows: VBoxContainer = $ContentPanel / JobScroll / JobRows
@onready var royal_territory_war_scroll: ScrollContainer = $ContentPanel / RoyalTerritoryWarScroll
@onready var royal_territory_war_rows: VBoxContainer = $ContentPanel / RoyalTerritoryWarScroll / RoyalTerritoryWarRows
@onready var pet_info_content: Control = $ContentPanel / PetInfoContent
@onready var pet_combat_mode_rows: VBoxContainer = $ContentPanel / PetInfoContent / PetTabs / 飼養指南 / GuideRows / CombatModeRows
@onready var pet_hunger_rows: VBoxContainer = $ContentPanel / PetInfoContent / PetTabs / 飼養指南 / GuideRows / HungerRows
@onready var pet_permanent_stat_rows: VBoxContainer = $ContentPanel / PetInfoContent / PetTabs / 飼養指南 / GuideRows / PermanentStatRows
@onready var pet_main_skill_rows: VBoxContainer = $ContentPanel / PetInfoContent / PetTabs / 飼養指南 / GuideRows / MainSkillRows
@onready var pet_family_list: VBoxContainer = $ContentPanel / PetInfoContent / PetTabs / 進化圖鑑 / PetFamilyPanel / PetFamilyScroll / PetFamilyList
@onready var pet_family_title: Label = $ContentPanel / PetInfoContent / PetTabs / 進化圖鑑 / RoadmapPanel / RoadmapHeader / PetFamilyTitle
@onready var pet_stage_count: Label = $ContentPanel / PetInfoContent / PetTabs / 進化圖鑑 / RoadmapPanel / RoadmapHeader / StageCount
@onready var pet_roadmap_rows: VBoxContainer = $ContentPanel / PetInfoContent / PetTabs / 進化圖鑑 / RoadmapPanel / RoadmapBody / RoadmapScroll / RoadmapRows
@onready var pet_roadmap_empty_state: Label = $ContentPanel / PetInfoContent / PetTabs / 進化圖鑑 / RoadmapPanel / RoadmapBody / EmptyState
@onready var doll_content: Control = $ContentPanel / DollContent
@onready var doll_category_list: VBoxContainer = $ContentPanel / DollContent / Layout / CategoryPanel / DollCategoryScroll / DollCategoryList
@onready var doll_title: Label = $ContentPanel / DollContent / Layout / RewardPanel / RewardHeader / DollTitle
@onready var doll_reward_count: Label = $ContentPanel / DollContent / Layout / RewardPanel / RewardHeader / RewardCount
@onready var doll_reward_scroll: ScrollContainer = $ContentPanel / DollContent / Layout / RewardPanel / DollRewardScroll
@onready var doll_reward_grid: GridContainer = $ContentPanel / DollContent / Layout / RewardPanel / DollRewardScroll / DollRewardGrid
@onready var club_info_content: Control = $ContentPanel / ClubInfoContent
@onready var club_tab_buttons: Array[Button] = [
	$ContentPanel / ClubInfoContent / Layout / ClubTabs / Creation, 
	$ContentPanel / ClubInfoContent / Layout / ClubTabs / Shop, 
	$ContentPanel / ClubInfoContent / Layout / ClubTabs / Research, 
	$ContentPanel / ClubInfoContent / Layout / ClubTabs / Commands
]
@onready var club_pages: Array[Control] = [
	$ContentPanel / ClubInfoContent / Layout / ClubBody / CreationScroll, 
	$ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage, 
	$ContentPanel / ClubInfoContent / Layout / ClubBody / ResearchScroll, 
	$ContentPanel / ClubInfoContent / Layout / ClubBody / CommandsScroll
]
@onready var club_creation_rows: VBoxContainer = $ContentPanel / ClubInfoContent / Layout / ClubBody / CreationScroll / CreationRows
@onready var club_shop_level_buttons: Array[Button] = [
	$ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage / LevelTabs / Level1, 
	$ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage / LevelTabs / Level2, 
	$ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage / LevelTabs / Level3, 
	$ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage / LevelTabs / Level4, 
	$ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage / LevelTabs / Level5, 
	$ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage / LevelTabs / Level6, 
	$ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage / LevelTabs / Level7
]
@onready var club_shop_title: Label = $ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage / ShopHeader / ShopTitle
@onready var club_shop_item_count: Label = $ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage / ShopHeader / ShopItemCount
@onready var club_shop_scroll: ScrollContainer = $ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage / ShopScroll
@onready var club_shop_grid: GridContainer = $ContentPanel / ClubInfoContent / Layout / ClubBody / ShopPage / ShopScroll / ShopGrid
@onready var club_research_rows: VBoxContainer = $ContentPanel / ClubInfoContent / Layout / ClubBody / ResearchScroll / ResearchRows
@onready var club_commands_rows: VBoxContainer = $ContentPanel / ClubInfoContent / Layout / ClubBody / CommandsScroll / CommandsRows
@onready var festival_info_content: Control = $ContentPanel / FestivalInfoContent
@onready var festival_status_banner: PanelContainer = $ContentPanel / FestivalInfoContent / Layout / StatusBanner
@onready var festival_status_text: Label = $ContentPanel / FestivalInfoContent / Layout / StatusBanner / StatusMargin / StatusText
@onready var festival_tab_buttons: HBoxContainer = $ContentPanel / FestivalInfoContent / Layout / FestivalTabsScroll / FestivalTabs
@onready var festival_detail_scroll: ScrollContainer = $ContentPanel / FestivalInfoContent / Layout / FestivalDetailScroll
@onready var festival_detail_title: Label = $ContentPanel / FestivalInfoContent / Layout / FestivalDetailScroll / FestivalDetail / FestivalTitle
@onready var festival_detail: VBoxContainer = $ContentPanel / FestivalInfoContent / Layout / FestivalDetailScroll / FestivalDetail
@onready var world_boss_scroll: ScrollContainer = $ContentPanel / WorldBossScroll
@onready var world_boss_rows: VBoxContainer = $ContentPanel / WorldBossScroll / WorldBossRows
@onready var transform_scroll: ScrollContainer = $ContentPanel / TransformScroll
@onready var transform_rows: VBoxContainer = $ContentPanel / TransformScroll / TransformRows

var _category_buttons: Array[Button] = []
var _doll_category_entries: Array[PanelContainer] = []
var _selected_doll_index: int = -1
var _pet_family_buttons: Dictionary = {}
var _pet_evolution_graphs: Dictionary = {}
var _selected_pet_family_id: int = -1
var _monster_drop_all: Array[Dictionary] = []
var _monster_drop_filtered: Array[Dictionary] = []
var _monster_maps_by_name: Dictionary = {}
var _monster_drop_load_error: String = ""
var _monster_drop_page: int = 0
var _skill_catalog: Array[Dictionary] = []
var _skill_load_error: String = ""
var _skill_card_entries: Array[Dictionary] = []
var _skill_filter_button_nodes: Dictionary = {}
var _selected_skill_class: = "全部"
var _engraving_catalog: Array[Dictionary] = []
var _engraving_load_error: = ""
var _engraving_tab_buttons: Dictionary = {}
var _selected_engraving_skill_id: = ENGRAVING_SKILL_MIN_ID
var _target_player: CharacterBody2D = null
var _equipment_catalog: Array[Dictionary] = []
var _equipment_filtered: Array[Dictionary] = []
var _equipment_catalog_loading: bool = false
var _equipment_catalog_loaded: bool = false
var _equipment_slot_buttons: Dictionary = {}
var _selected_equipment_slot: int = -1
var _equipment_page: int = 0
var _festival_buttons: Dictionary = {}
var _festival_data: Dictionary = {}
var _festival_load_error: = ""
var _selected_festival_id: = ""
var _pet_evolution_catalog_loaded: bool = false
const PET_EVOLUTION_BATCH_MAX_FAMILIES: int = 2
const PET_EVOLUTION_BATCH_BUDGET_USEC: int = 1800

static func should_yield_pet_evolution_batch(families_since_yield: int, elapsed_usec: int) -> bool:
	return families_since_yield >= PET_EVOLUTION_BATCH_MAX_FAMILIES or elapsed_usec >= PET_EVOLUTION_BATCH_BUDGET_USEC
var _skill_catalog_loaded: bool = false
var _engraving_catalog_loaded: bool = false
var _monster_drop_catalog_loaded: bool = false
var _doll_categories_loaded: bool = false
var _club_pages_loaded: bool = false
var _world_boss_rows_loaded: bool = false
var _transform_rows_loaded: bool = false

func _ready() -> void :
	for category in CATEGORIES:
		var button: = Button.new()
		button.text = category
		button.custom_minimum_size.y = 38.0
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.focus_mode = Control.FOCUS_NONE
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.pressed.connect(_select_category.bind(category))
		category_list.add_child(button)
		_category_buttons.append(button)
	monster_drop_search.text_changed.connect(_on_monster_drop_search_changed)
	monster_drop_previous_page.pressed.connect(_change_monster_drop_page.bind(-1))
	monster_drop_next_page.pressed.connect(_change_monster_drop_page.bind(1))
	equipment_search.text_changed.connect(_on_equipment_search_changed)
	equipment_previous_page.pressed.connect(_change_equipment_page.bind(-1))
	equipment_next_page.pressed.connect(_change_equipment_page.bind(1))
	_build_equipment_slot_filters()
	_build_shortcut_rows()
	_build_race_rows()
	_build_job_rows()
	_build_royal_territory_war_page()
	# Heavy reference catalogs are intentionally lazy-loaded from _select_category()
	# so entering the game world does not synchronously scan/load hidden pages.
	_build_pet_guide()
	_load_festival_data()
	_build_festival_skeleton()
	if GameManager:
		GameManager.festival_activity_changed.connect(_on_festival_activity_changed)
		_update_festival_activity_display(GameManager.current_festival_activity)
	visibility_changed.connect(_on_window_visibility_changed)
	_select_category(CATEGORIES[0])

func _select_category(category: String) -> void :
	detail_title.text = category
	shortcut_scroll.visible = category == "快捷鍵"
	race_scroll.visible = category == "種族"
	job_scroll.visible = category == "職業"
	royal_territory_war_scroll.visible = category == "皇家領地戰"
	skill_info_content.visible = category == "技能"
	engraving_info_content.visible = category == "刻紋"
	equipment_info_content.visible = category == "裝備"
	pet_info_content.visible = category == "寵物"
	doll_content.visible = category == "獻寶娃娃"
	festival_info_content.visible = category == "節日活動"
	club_info_content.visible = category == "戰團"
	monster_drop_content.visible = category == "怪物掉落"
	world_boss_scroll.visible = category == "世界王"
	transform_scroll.visible = category == "變身卷軸"
	content_scroll.visible = category != "快捷鍵" and category != "種族" and category != "職業" and category != "皇家領地戰" and category != "技能" and category != "刻紋" and category != "裝備" and category != "寵物" and category != "戰團" and category != "怪物掉落" and category != "獻寶娃娃" and category != "節日活動" and category != "世界王" and category != "變身卷軸"
	_hide_doll_item_tooltip()
	if category == "技能":
		if not _skill_catalog_loaded:
			_skill_catalog_loaded = true
			_load_skill_catalog()
			_build_skill_info_page()
		skill_info_scroll.scroll_vertical = 0
	if category == "刻紋":
		if not _engraving_catalog_loaded:
			_engraving_catalog_loaded = true
			_load_engraving_catalog()
			_build_engraving_info_page()
		refresh_engraving_requirements()
		engraving_detail_scroll.scroll_vertical = 0
	if category == "裝備":
		equipment_scroll.scroll_vertical = 0
		_ensure_equipment_catalog_loaded()
	if category == "寵物" and not _pet_evolution_catalog_loaded:
		_pet_evolution_catalog_loaded = true
		pet_family_title.text = "正在整理寵物進化資料…"
		pet_roadmap_empty_state.text = "正在整理寵物進化資料…"
		pet_roadmap_empty_state.visible = true
		call_deferred("_build_pet_evolution_catalog")
	if category == "怪物掉落" and not _monster_drop_catalog_loaded:
		_monster_drop_catalog_loaded = true
		_load_monster_drop_catalog()
	if category == "獻寶娃娃" and not _doll_categories_loaded:
		_doll_categories_loaded = true
		_build_doll_categories()
	if category == "戰團" and not _club_pages_loaded:
		_club_pages_loaded = true
		_build_club_pages()
	if category == "世界王" and not _world_boss_rows_loaded:
		_world_boss_rows_loaded = true
		_build_world_boss_rows()
	if category == "變身卷軸" and not _transform_rows_loaded:
		_transform_rows_loaded = true
		_build_transform_rows()
	if category == "皇家領地戰":
		royal_territory_war_scroll.scroll_vertical = 0
	if content_scroll.visible:
		content_scroll.scroll_vertical = 0
		detail_text.text = "內容準備中"
	for button in _category_buttons:
		_apply_button_style(button, button.text == category)

func _build_festival_skeleton() -> void :
	for festival in FESTIVAL_SKELETON_ENTRIES:
		var festival_id: = str(festival["id"])
		var button: = Button.new()
		button.text = str(festival["name"])
		button.custom_minimum_size = Vector2(104.0, 38.0)
		button.focus_mode = Control.FOCUS_NONE
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.set_meta("festival_name", str(festival["name"]))
		button.pressed.connect(_select_festival_skeleton.bind(festival_id))
		festival_tab_buttons.add_child(button)
		_festival_buttons[festival_id] = button
	_select_festival_skeleton(str(FESTIVAL_SKELETON_ENTRIES[0]["id"]))

func _select_festival_skeleton(festival_id: String) -> void :
	_selected_festival_id = festival_id
	_render_festival_detail(festival_id)
	_update_festival_button_states(GameManager.current_festival_activity if GameManager else "")

func _on_festival_activity_changed(festival_id: String) -> void :
	_update_festival_activity_display(festival_id)

func _update_festival_activity_display(festival_id: String) -> void :
	var active_name: = ""
	for entry in FESTIVAL_SKELETON_ENTRIES:
		if str(entry["id"]) == festival_id:
			active_name = str(entry["name"])
			break
	var banner_style: = StyleBoxFlat.new()
	banner_style.set_border_width_all(1)
	banner_style.set_corner_radius_all(6)
	if active_name.is_empty():
		festival_status_text.text = "節日活動狀態\n尚未收到目前進行中的活動資訊。"
		festival_status_text.add_theme_color_override("font_color", Color(0.72, 0.8, 0.87, 1.0))
		banner_style.bg_color = Color(0.12, 0.16, 0.21, 0.88)
		banner_style.border_color = Color(0.42, 0.51, 0.59, 0.65)
	else:
		festival_status_text.text = "當前活動 ➡️ %s\n詳細活動資訊請至下方查閱" % active_name
		festival_status_text.add_theme_color_override("font_color", Color(1.0, 0.88, 0.55, 1.0))
		banner_style.bg_color = Color(0.25, 0.2, 0.08, 0.92)
		banner_style.border_color = Color(0.96, 0.72, 0.25, 0.9)
	festival_status_banner.add_theme_stylebox_override("panel", banner_style)
	_update_festival_button_states(festival_id)

func _update_festival_button_states(active_festival_id: String) -> void :
	for entry_id in _festival_buttons:
		var button: = _festival_buttons[entry_id] as Button
		var is_active: = str(entry_id) == active_festival_id
		button.text = " %s (進行中) " % str(button.get_meta("festival_name")) if is_active else str(button.get_meta("festival_name"))
		_apply_festival_tab_style(button, str(entry_id) == _selected_festival_id, is_active)

func _apply_festival_tab_style(button: Button, selected: bool, active: bool) -> void :
	var normal: = StyleBoxFlat.new()
	if active:
		normal.bg_color = Color(0.34, 0.27, 0.1, 0.96) if not selected else Color(0.43, 0.33, 0.11, 0.98)
		normal.border_color = Color(0.96, 0.73, 0.27, 0.92)
		normal.set_border_width_all(1)
		button.add_theme_color_override("font_color", Color(1.0, 0.88, 0.55, 1.0))
	else:
		normal.bg_color = Color(0.236, 0.378, 0.576, 0.9) if selected else Color(0.15, 0.254, 0.403, 0.9)
		normal.border_color = Color(0.4, 0.57, 0.72, 0.45)
		normal.set_border_width_all(1)
		button.add_theme_color_override("font_color", Color(0.88, 0.92, 0.96, 1.0))
	normal.set_corner_radius_all(5)
	var hover: = normal.duplicate() as StyleBoxFlat
	hover.bg_color = Color(0.48, 0.36, 0.12, 0.98) if active else Color(0.236, 0.378, 0.576, 0.94)
	button.add_theme_stylebox_override("normal", normal)
	button.add_theme_stylebox_override("hover", hover)
	button.add_theme_stylebox_override("pressed", hover)

func _load_festival_data() -> void :
	_festival_data.clear()
	_festival_load_error = ""
	if not FileAccess.file_exists(FESTIVAL_INFO_DATA_PATH):
		_festival_load_error = "找不到節日活動資料。"
		return
	var file: = FileAccess.open(FESTIVAL_INFO_DATA_PATH, FileAccess.READ)
	if file == null:
		_festival_load_error = "無法讀取節日活動資料。"
		return
	var parsed: Variant = JSON.parse_string(file.get_as_text())
	if not parsed is Dictionary or not parsed.has("festivals") or not parsed["festivals"] is Array:
		_festival_load_error = "節日活動資料格式錯誤。"
		return
	for festival in parsed["festivals"]:
		if festival is Dictionary and festival.has("id"):
			_festival_data[str(festival["id"])] = festival

func _render_festival_detail(festival_id: String) -> void :
	for child in festival_detail.get_children():
		if child != festival_detail_title:
			child.queue_free()
	var fallback_name: = festival_id
	for entry in FESTIVAL_SKELETON_ENTRIES:
		if str(entry["id"]) == festival_id:
			fallback_name = str(entry["name"])
			break
	if not _festival_data.has(festival_id):
		festival_detail_title.text = fallback_name
		festival_detail.add_child(_make_festival_message(_festival_load_error if not _festival_load_error.is_empty() else "此節日的活動資料尚未提供。"))
		festival_detail_scroll.scroll_vertical = 0
		return
	var festival: Dictionary = _festival_data[festival_id]
	festival_detail_title.text = str(festival.get("name", fallback_name))
	var summary: = Label.new()
	summary.text = str(festival.get("summary", ""))
	summary.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	summary.add_theme_color_override("font_color", Color(0.74, 0.84, 0.92, 1.0))
	summary.add_theme_font_size_override("font_size", 16)
	festival_detail.add_child(summary)
	var sections: Variant = festival.get("sections", [])
	if sections is Array:
		for section in sections:
			if section is Dictionary:
				festival_detail.add_child(_make_festival_text_section(str(section.get("title", "")), str(section.get("body", ""))))
	var item_groups: Variant = festival.get("item_groups", [])
	if item_groups is Array:
		for group in item_groups:
			if group is Dictionary:
				festival_detail.add_child(_make_festival_item_group(group))
	var random_reward_groups: Variant = festival.get("random_reward_groups", [])
	if random_reward_groups is Array:
		for group in random_reward_groups:
			if group is Dictionary:
				festival_detail.add_child(_make_festival_item_group(group))
	festival_detail_scroll.scroll_vertical = 0

func _make_festival_message(message: String) -> Label:
	var label: = Label.new()
	label.text = message
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.add_theme_color_override("font_color", Color(0.88, 0.62, 0.58, 1.0))
	label.add_theme_font_size_override("font_size", 15)
	return label

func _make_festival_text_section(title_text: String, body_text: String) -> PanelContainer:
	var panel: = PanelContainer.new()
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.07, 0.13, 0.2, 0.92)
	style.border_color = Color(0.24, 0.43, 0.58, 0.72)
	style.set_border_width_all(1)
	style.set_corner_radius_all(8)
	style.content_margin_left = 14.0
	style.content_margin_top = 11.0
	style.content_margin_right = 14.0
	style.content_margin_bottom = 12.0
	panel.add_theme_stylebox_override("panel", style)
	var layout: = VBoxContainer.new()
	layout.add_theme_constant_override("separation", 6)
	panel.add_child(layout)
	var title: = Label.new()
	title.text = title_text
	title.add_theme_color_override("font_color", Color(0.95, 0.76, 0.3, 1.0))
	title.add_theme_font_size_override("font_size", 17)
	layout.add_child(title)
	var body: = Label.new()
	body.text = body_text
	body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	body.add_theme_color_override("font_color", Color(0.84, 0.88, 0.92, 1.0))
	body.add_theme_font_size_override("font_size", 15)
	layout.add_child(body)
	return panel

func _make_festival_item_group(group: Dictionary) -> VBoxContainer:
	var section: = VBoxContainer.new()
	section.add_theme_constant_override("separation", 8)
	var title: = Label.new()
	title.text = str(group.get("title", "活動物品"))
	title.add_theme_color_override("font_color", Color(0.84, 0.92, 0.98, 1.0))
	title.add_theme_font_size_override("font_size", 18)
	section.add_child(title)
	var cards: = HFlowContainer.new()
	cards.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	cards.add_theme_constant_override("h_separation", 8)
	cards.add_theme_constant_override("v_separation", 8)
	section.add_child(cards)
	var items: Variant = group.get("items", [])
	var item_count_ranges: Variant = group.get("item_count_ranges", {})
	if items is Array:
		for item_value in items:
			var item_id: = int(item_value)
			if item_id > 0:
				var count_range: Array = []
				if item_count_ranges is Dictionary:
					var raw_count_range: Variant = item_count_ranges.get(str(item_id), [])
					if raw_count_range is Array:
						count_range = raw_count_range
				cards.add_child(_make_festival_item_card(item_id, count_range))
	var pets: Variant = group.get("pets", [])
	if pets is Array:
		for pet_value in pets:
			var pet_id: = int(pet_value)
			if pet_id > 0:
				cards.add_child(_make_festival_pet_card(pet_id))
	return section

func _make_festival_item_card(item_id: int, count_range: Array = []) -> PanelContainer:
	var count_range_text: = ""
	if count_range.size() >= 2:
		var minimum_count: = int(count_range[0])
		var maximum_count: = int(count_range[1])
		if minimum_count > 0 and maximum_count > minimum_count:
			count_range_text = "%d~%d" % [minimum_count, maximum_count]
	var card: = PanelContainer.new()
	card.set_meta("festival_item_id", item_id)
	card.set_meta("festival_item_count_range", count_range_text)
	card.custom_minimum_size = Vector2(146.0, 138.0 if not count_range_text.is_empty() else 116.0)
	card.mouse_filter = Control.MOUSE_FILTER_STOP
	card.mouse_default_cursor_shape = Control.CURSOR_HELP
	card.mouse_entered.connect(_on_festival_item_mouse_entered.bind(card, item_id))
	card.mouse_exited.connect(_on_festival_item_mouse_exited.bind(card))
	_apply_festival_item_style(card, false)
	var content: = VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	content.add_theme_constant_override("separation", 4)
	card.add_child(content)
	var visual: = _get_doll_item_visual(item_id)
	var icon_center: = CenterContainer.new()
	icon_center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(icon_center)
	var icon: = TextureRect.new()
	icon.custom_minimum_size = Vector2(58.0, 58.0)
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.texture = visual["texture"]
	icon.material = visual["material"]
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon_center.add_child(icon)
	var name_label: = Label.new()
	name_label.text = str(ItemLoader.get_item_property(item_id, "item_name", "未知物品"))
	name_label.custom_minimum_size.y = 36.0
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.add_theme_font_size_override("font_size", 14)
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(name_label)
	if not count_range_text.is_empty():
		var count_label: = Label.new()
		count_label.name = "CountRange"
		count_label.text = count_range_text
		count_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		count_label.add_theme_color_override("font_color", Color(0.95, 0.76, 0.36, 1.0))
		count_label.add_theme_font_size_override("font_size", 14)
		count_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		content.add_child(count_label)
	return card

func _on_festival_item_mouse_entered(card: PanelContainer, item_id: int) -> void :
	_apply_festival_item_style(card, true)
	_show_doll_item_tooltip(item_id)

func _on_festival_item_mouse_exited(card: PanelContainer) -> void :
	_apply_festival_item_style(card, false)
	_hide_doll_item_tooltip()

func _apply_festival_item_style(card: PanelContainer, hovered: bool) -> void :
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.19, 0.32, 0.47, 0.96) if hovered else Color(0.1, 0.19, 0.3, 0.94)
	style.border_color = Color(0.93, 0.72, 0.32, 0.88) if hovered else Color(0.36, 0.55, 0.7, 0.55)
	style.set_border_width_all(1)
	style.set_corner_radius_all(7)
	style.content_margin_left = 8.0
	style.content_margin_top = 7.0
	style.content_margin_right = 8.0
	style.content_margin_bottom = 7.0
	card.add_theme_stylebox_override("panel", style)

func _make_festival_pet_card(pet_id: int) -> PanelContainer:
	var pet_config: Dictionary = PetLoader.get_pet_config(pet_id) if PetLoader else {}
	var evolutions: Dictionary = pet_config.get("evolutions", {})
	var initial_evolution: Dictionary = evolutions.get("0", evolutions.get(0, {}))
	var pet_name: = str(pet_config.get("name", "未知寵物"))
	var outlook: = str(initial_evolution.get("outlook", ""))
	var color_index: = int(initial_evolution.get("color", 0))

	var card: = PanelContainer.new()
	card.set_meta("festival_pet_id", pet_id)
	card.set_meta("festival_pet_outlook", outlook)
	card.set_meta("festival_pet_color", color_index)
	card.custom_minimum_size = Vector2(146.0, 142.0)
	card.clip_contents = true
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.09, 0.22, 0.24, 0.95)
	style.border_color = Color(0.39, 0.75, 0.68, 0.72)
	style.set_border_width_all(1)
	style.set_corner_radius_all(7)
	style.content_margin_left = 8.0
	style.content_margin_top = 7.0
	style.content_margin_right = 8.0
	style.content_margin_bottom = 7.0
	card.add_theme_stylebox_override("panel", style)

	var content: = VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	content.add_theme_constant_override("separation", 4)
	card.add_child(content)

	var preview: = Control.new()
	preview.custom_minimum_size = Vector2(128.0, 92.0)
	preview.clip_contents = true
	preview.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(preview)

	var pet_sprite = UI_MONSTER.new()
	pet_sprite.name = "PetPreview"
	pet_sprite.monster_id = outlook
	pet_sprite.default_anim = "IDLE_1"
	pet_sprite.anim_speed_scale = 1.0
	pet_sprite.position = Vector2(64.0, 68.0)
	pet_sprite.scale = Vector2(0.59, 0.59)
	if color_index > 0:
		var material: = ShaderMaterial.new()
		material.shader = PALETTE_SHADER
		material.set_shader_parameter("effect_type", 0)
		material.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
		pet_sprite.material = material
	preview.add_child(pet_sprite)

	var name_label: = Label.new()
	name_label.text = pet_name
	name_label.custom_minimum_size.y = 30.0
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.add_theme_color_override("font_color", Color(0.86, 0.96, 0.92, 1.0))
	name_label.add_theme_font_size_override("font_size", 14)
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(name_label)
	return card

func _ensure_equipment_catalog_loaded() -> void :
	if _equipment_catalog_loaded or _equipment_catalog_loading:
		return
	_equipment_catalog_loading = true
	equipment_result_count.text = "載入中"
	equipment_status.text = "正在載入裝備資料…"
	equipment_status.visible = true
	_load_equipment_catalog()

func _load_equipment_catalog() -> void :
	_equipment_catalog.clear()
	var item_ids: Array[int] = []
	for item_id in ItemLoader.get_item_ids_in_folders(["equip"]):
		if item_id < EQUIP_EXCLUDED_ID_MIN or item_id > EQUIP_EXCLUDED_ID_MAX:
			item_ids.append(item_id)
	for item_id in ItemLoader.get_item_ids_in_folders(["weapon"]):
		if item_id < WEAPON_EXCLUDED_ID_MIN or item_id > WEAPON_EXCLUDED_ID_MAX:
			item_ids.append(item_id)
	item_ids.sort()

	var failed_count: = 0
	for item_index in range(item_ids.size()):
		var item_id: = item_ids[item_index]
		var item_data: Resource = ItemLoader.get_item_data(item_id)
		if item_data == null:
			failed_count += 1
		else:
			_equipment_catalog.append({
				"item_id": item_id, 
				"name": str(item_data.get("item_name")), 
				"slot": int(item_data.get("equip_slot"))
			})
		if (item_index + 1) % EQUIPMENT_LOAD_BATCH_SIZE == 0:
			equipment_result_count.text = "載入中 %d / %d" % [item_index + 1, item_ids.size()]
			await get_tree().process_frame

	_equipment_catalog_loaded = true
	_equipment_catalog_loading = false
	if failed_count > 0:
		push_warning("[GameInfo] %d equipment resources could not be loaded" % failed_count)
	_apply_equipment_filters()

func _build_equipment_slot_filters() -> void :
	for filter_value in EQUIPMENT_SLOT_FILTERS:
		var filter_info: Dictionary = filter_value
		var slot: = int(filter_info["slot"])
		var button: = Button.new()
		button.text = str(filter_info["label"])
		button.custom_minimum_size = Vector2(62.0, 34.0)
		button.focus_mode = Control.FOCUS_NONE
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.pressed.connect(_select_equipment_slot.bind(slot))
		equipment_slot_filter_buttons.add_child(button)
		_equipment_slot_buttons[slot] = button
		_apply_button_style(button, slot == _selected_equipment_slot)

func _select_equipment_slot(slot: int) -> void :
	_selected_equipment_slot = slot
	for raw_slot in _equipment_slot_buttons:
		var button: Button = _equipment_slot_buttons[raw_slot]
		_apply_button_style(button, int(raw_slot) == slot)
	_apply_equipment_filters()

func _on_equipment_search_changed(_new_text: String) -> void :
	_apply_equipment_filters()

func _apply_equipment_filters() -> void :
	_equipment_page = 0
	_equipment_filtered.clear()
	if not _equipment_catalog_loaded:
		return

	var query: = equipment_search.text.strip_edges()
	for entry in _equipment_catalog:
		if _selected_equipment_slot >= 0 and int(entry.get("slot", -1)) != _selected_equipment_slot:
			continue
		if not query.is_empty() and str(entry.get("name", "")).findn(query) < 0:
			continue
		_equipment_filtered.append(entry)

	equipment_result_count.text = "共 %d 件" % _equipment_filtered.size()
	if _equipment_filtered.is_empty():
		equipment_status.text = "查無符合條件的裝備" if not query.is_empty() or _selected_equipment_slot >= 0 else "沒有可顯示的裝備資料"
	else:
		equipment_status.text = "已找到 %d 件裝備" % _equipment_filtered.size()
	equipment_status.visible = true
	_render_equipment_page()

func _render_equipment_page() -> void :
	_clear_equipment_cards()
	var total_count: = _equipment_filtered.size()
	if total_count == 0:
		equipment_status.visible = true
		equipment_previous_page.disabled = true
		equipment_next_page.disabled = true
		equipment_page_label.text = "第 0 / 0 頁"
		return

	var page_count: = ceili(float(total_count) / float(EQUIPMENT_PAGE_SIZE))
	_equipment_page = clampi(_equipment_page, 0, page_count - 1)
	var start_index: = _equipment_page * EQUIPMENT_PAGE_SIZE
	var end_index: = mini(start_index + EQUIPMENT_PAGE_SIZE, total_count)
	for index in range(start_index, end_index):
		equipment_grid.add_child(_make_equipment_card(_equipment_filtered[index]))

	equipment_status.visible = false
	equipment_previous_page.disabled = _equipment_page <= 0
	equipment_next_page.disabled = _equipment_page >= page_count - 1
	equipment_page_label.text = "第 %d / %d 頁" % [_equipment_page + 1, page_count]
	equipment_scroll.set_deferred("scroll_vertical", 0)

func _clear_equipment_cards() -> void :
	_hide_doll_item_tooltip()
	for child in equipment_grid.get_children():
		equipment_grid.remove_child(child)
		child.queue_free()

func _change_equipment_page(direction: int) -> void :
	var page_count: = ceili(float(_equipment_filtered.size()) / float(EQUIPMENT_PAGE_SIZE))
	if page_count <= 0:
		return
	var next_page: = clampi(_equipment_page + direction, 0, page_count - 1)
	if next_page == _equipment_page:
		return
	_equipment_page = next_page
	_render_equipment_page()

func _make_equipment_card(entry: Dictionary) -> PanelContainer:
	var item_id: = int(entry.get("item_id", 0))
	var card: = PanelContainer.new()
	card.custom_minimum_size = Vector2(150.0, 150.0)
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	card.mouse_filter = Control.MOUSE_FILTER_STOP
	card.mouse_default_cursor_shape = Control.CURSOR_HELP
	card.set_meta("item_id", item_id)
	card.mouse_entered.connect(_on_equipment_card_mouse_entered.bind(card))
	card.mouse_exited.connect(_on_equipment_card_mouse_exited.bind(card))
	_apply_doll_reward_style(card, false)

	var content: = VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	content.add_theme_constant_override("separation", 4)
	card.add_child(content)

	var visual: = _get_doll_item_visual(item_id)
	var icon_center: = CenterContainer.new()
	icon_center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(icon_center)

	var icon: = TextureRect.new()
	icon.custom_minimum_size = Vector2(64.0, 64.0)
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.texture = visual["texture"]
	icon.material = visual["material"]
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon_center.add_child(icon)

	var name_label: = Label.new()
	name_label.text = str(entry.get("name", "未知裝備"))
	name_label.custom_minimum_size.y = 38.0
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.add_theme_font_size_override("font_size", 14)
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(name_label)
	return card

func _on_equipment_card_mouse_entered(card: PanelContainer) -> void :
	_apply_doll_reward_style(card, true)
	_show_equipment_item_tooltip(int(card.get_meta("item_id", 0)))

func _on_equipment_card_mouse_exited(card: PanelContainer) -> void :
	_apply_doll_reward_style(card, false)
	_hide_doll_item_tooltip()

func _show_equipment_item_tooltip(item_id: int) -> void :
	if item_id <= 0 or not GameManager or not GameManager.current_hud:
		return
	var shared_tooltip = GameManager.current_hud.get("shared_tooltip")
	if shared_tooltip:
		shared_tooltip.show_item(item_id, 1, {})

func _load_skill_catalog() -> void :
	_skill_catalog.clear()
	_skill_load_error = ""
	skill_info_result_count.text = "載入中"
	skill_info_status.text = "正在載入技能資料…"

	if not FileAccess.file_exists(SKILL_INFO_DATA_PATH):
		_set_skill_load_error("技能資料不存在：%s" % SKILL_INFO_DATA_PATH)
		return
	var file: = FileAccess.open(SKILL_INFO_DATA_PATH, FileAccess.READ)
	if file == null:
		_set_skill_load_error("無法讀取技能資料：%s" % SKILL_INFO_DATA_PATH)
		return

	var headers: = file.get_csv_line()
	if headers.size() != SKILL_INFO_COLUMN_COUNT:
		_set_skill_load_error("技能資料標題欄位數錯誤：預期 %d 欄，實際 %d 欄" % [SKILL_INFO_COLUMN_COUNT, headers.size()])
		return
	var expected_headers: = {
		0: "技能編號", 
		1: "技能名稱", 
		2: "技能描述", 
		3: "職業限制", 
		4: "等級限制", 
		33: "縮圖編號", 
		38: "特殊功能", 
		53: "減免死經比例"
	}
	for column_index in expected_headers:
		if headers[column_index].strip_edges() != expected_headers[column_index]:
			_set_skill_load_error("技能資料第 %d 欄標題錯誤：預期「%s」，實際「%s」" % [column_index + 1, expected_headers[column_index], headers[column_index]])
			return

	var skills_by_id: Dictionary = {}
	var line_number: = 1
	while not file.eof_reached():
		var columns: = file.get_csv_line()
		line_number += 1
		if columns.size() == 1 and columns[0].strip_edges().is_empty():
			continue
		if columns.size() != SKILL_INFO_COLUMN_COUNT:
			_set_skill_load_error("技能資料第 %d 行欄位數錯誤：預期 %d 欄，實際 %d 欄" % [line_number, SKILL_INFO_COLUMN_COUNT, columns.size()])
			return
		var id_text: = columns[0].strip_edges()
		if not id_text.is_valid_int():
			_set_skill_load_error("技能資料第 %d 行的技能編號無效：%s" % [line_number, id_text])
			return
		var skill_id: = id_text.to_int()
		if skill_id < SKILL_INFO_MIN_ID or skill_id > SKILL_INFO_MAX_ID:
			continue

		if not skills_by_id.has(skill_id):
			skills_by_id[skill_id] = {
				"id": skill_id, 
				"name": columns[1].strip_edges(), 
				"description": columns[2].strip_edges(), 
				"job_requirement": columns[3].strip_edges(), 
				"skill_type": columns[5].to_int(), 
				"damage_type": columns[13].to_int(), 
				"icon_id": columns[33].to_int(), 
				"levels": [] as Array[Dictionary]
			}
		var skill: Dictionary = skills_by_id[skill_id]
		var levels: Array[Dictionary] = skill["levels"]
		levels.append({
			"level": levels.size() + 1, 
			"required_level": columns[4].to_int(), 
			"cast_range": columns[6].to_int(), 
			"aoe_radius": columns[7].to_int(), 
			"cost_hp": columns[8].to_int(), 
			"cost_mp": columns[9].to_int(), 
			"cost_sp": columns[10].to_int(), 
			"base_damage": columns[11].to_int(), 
			"damage_multiplier": columns[12].to_int(), 
			"cooldown": columns[14].to_int(), 
			"duration": columns[15].to_int(), 
			"heal_hp": columns[16].to_int(), 
			"heal_mp": columns[17].to_int(), 
			"heal_sp": columns[18].to_int(), 
			"max_hp_bonus": columns[19].to_int(), 
			"max_mp_bonus": columns[20].to_int(), 
			"max_sp_bonus": columns[21].to_int(), 
			"attack_bonus": columns[22].to_int(), 
			"defense_bonus": columns[23].to_int(), 
			"magic_attack_bonus": columns[24].to_int(), 
			"magic_defense_bonus": columns[25].to_int(), 
			"hit_bonus": columns[26].to_int(), 
			"dodge_bonus": columns[27].to_int(), 
			"lucky_bonus": columns[28].to_int(), 
			"attack_speed_bonus": columns[29].to_int(), 
			"move_speed_bonus": columns[30].to_int(), 
			"crit_rate_bonus": columns[31].to_int(), 
			"crit_damage_bonus": columns[32].to_int(), 
			"tick_count": columns[34].to_int(), 
			"special_skill": columns[38].strip_edges(), 
			"max_hp_percent": columns[39].to_int(), 
			"max_mp_percent": columns[40].to_int(), 
			"max_sp_percent": columns[41].to_int(), 
			"attack_percent": columns[42].to_int(), 
			"defense_percent": columns[43].to_int(), 
			"magic_attack_percent": columns[44].to_int(), 
			"magic_defense_percent": columns[45].to_int(), 
			"hit_percent": columns[46].to_int(), 
			"dodge_percent": columns[47].to_int(), 
			"lucky_percent": columns[48].to_int(), 
			"attack_speed_percent": columns[49].to_int(), 
			"move_speed_percent": columns[50].to_int(), 
			"crit_rate_percent": columns[51].to_int(), 
			"crit_damage_percent": columns[52].to_int(), 
			"revive_exp_percent": columns[53].to_int()
		})

	for raw_skill in skills_by_id.values():
		if raw_skill is Dictionary:
			_skill_catalog.append(raw_skill)
	_skill_catalog.sort_custom(_sort_skill_catalog_entries)
	var level_count: = 0
	for skill in _skill_catalog:
		var levels: Array[Dictionary] = skill.get("levels", [] as Array[Dictionary])
		level_count += levels.size()
	if _skill_catalog.is_empty():
		_set_skill_load_error("技能資料中沒有 ID %d～%d 的技能" % [SKILL_INFO_MIN_ID, SKILL_INFO_MAX_ID])
		return
	skill_info_result_count.text = "共 %d 項" % _skill_catalog.size()
	skill_info_status.text = "已載入 %d 個技能、%d 筆等級資料" % [_skill_catalog.size(), level_count]

func _set_skill_load_error(message: String) -> void :
	_skill_catalog.clear()
	_skill_load_error = message
	skill_info_result_count.text = "載入失敗"
	skill_info_status.text = message
	push_error("[GameInfo] " + message)

func _load_engraving_catalog() -> void :
	_engraving_catalog.clear()
	_engraving_load_error = ""
	engraving_tabs_status.text = "正在載入刻紋技能…"
	engraving_detail_status.text = "正在載入刻紋技能詳細資訊…"
	if not FileAccess.file_exists(SKILL_INFO_DATA_PATH):
		_set_engraving_load_error("刻紋技能資料不存在：%s" % SKILL_INFO_DATA_PATH)
		return
	var file: = FileAccess.open(SKILL_INFO_DATA_PATH, FileAccess.READ)
	if file == null:
		_set_engraving_load_error("無法讀取刻紋技能資料：%s" % SKILL_INFO_DATA_PATH)
		return
	var headers: = file.get_csv_line()
	if headers.size() != SKILL_INFO_COLUMN_COUNT:
		_set_engraving_load_error("刻紋技能資料標題欄位數錯誤：預期 %d 欄，實際 %d 欄" % [SKILL_INFO_COLUMN_COUNT, headers.size()])
		return

	var skills_by_id: Dictionary = {}
	var line_number: = 1
	while not file.eof_reached():
		var columns: = file.get_csv_line()
		line_number += 1
		if columns.size() == 1 and columns[0].strip_edges().is_empty():
			continue
		if columns.size() != SKILL_INFO_COLUMN_COUNT:
			_set_engraving_load_error("刻紋技能資料第 %d 行欄位數錯誤：預期 %d 欄，實際 %d 欄" % [line_number, SKILL_INFO_COLUMN_COUNT, columns.size()])
			return
		var id_text: = columns[0].strip_edges()
		if not id_text.is_valid_int():
			_set_engraving_load_error("刻紋技能資料第 %d 行的技能編號無效：%s" % [line_number, id_text])
			return
		var skill_id: = id_text.to_int()
		if skill_id < ENGRAVING_SKILL_MIN_ID or skill_id > ENGRAVING_SKILL_MAX_ID:
			continue
		if not skills_by_id.has(skill_id):
			skills_by_id[skill_id] = {
				"id": skill_id, 
				"name": columns[1].strip_edges(), 
				"description": columns[2].strip_edges(), 
				"skill_type": columns[5].to_int(), 
				"damage_type": columns[13].to_int(), 
				"icon_id": columns[33].to_int(), 
				"levels": [] as Array[Dictionary]
			}
		var skill: Dictionary = skills_by_id[skill_id]
		var levels: Array[Dictionary] = skill["levels"]
		levels.append({
			"level": levels.size() + 1, 
			"required_level": columns[4].to_int(), 
			"cast_range": columns[6].to_int(), 
			"aoe_radius": columns[7].to_int(), 
			"cost_hp": columns[8].to_int(), 
			"cost_mp": columns[9].to_int(), 
			"cost_sp": columns[10].to_int(), 
			"base_damage": columns[11].to_int(), 
			"damage_multiplier": columns[12].to_int(), 
			"cooldown": columns[14].to_int(), 
			"duration": columns[15].to_int(), 
			"heal_hp": columns[16].to_int(), 
			"heal_mp": columns[17].to_int(), 
			"heal_sp": columns[18].to_int(), 
			"max_hp_bonus": columns[19].to_int(), 
			"max_mp_bonus": columns[20].to_int(), 
			"max_sp_bonus": columns[21].to_int(), 
			"attack_bonus": columns[22].to_int(), 
			"defense_bonus": columns[23].to_int(), 
			"magic_attack_bonus": columns[24].to_int(), 
			"magic_defense_bonus": columns[25].to_int(), 
			"hit_bonus": columns[26].to_int(), 
			"dodge_bonus": columns[27].to_int(), 
			"lucky_bonus": columns[28].to_int(), 
			"attack_speed_bonus": columns[29].to_int(), 
			"move_speed_bonus": columns[30].to_int(), 
			"crit_rate_bonus": columns[31].to_int(), 
			"crit_damage_bonus": columns[32].to_int(), 
			"special_skill": columns[38].strip_edges(), 
			"max_hp_percent": columns[39].to_int(), 
			"max_mp_percent": columns[40].to_int(), 
			"max_sp_percent": columns[41].to_int(), 
			"attack_percent": columns[42].to_int(), 
			"defense_percent": columns[43].to_int(), 
			"magic_attack_percent": columns[44].to_int(), 
			"magic_defense_percent": columns[45].to_int(), 
			"hit_percent": columns[46].to_int(), 
			"dodge_percent": columns[47].to_int(), 
			"lucky_percent": columns[48].to_int(), 
			"attack_speed_percent": columns[49].to_int(), 
			"move_speed_percent": columns[50].to_int(), 
			"crit_rate_percent": columns[51].to_int(), 
			"crit_damage_percent": columns[52].to_int(), 
			"revive_exp_percent": columns[53].to_int()
		})

	for raw_skill in skills_by_id.values():
		if raw_skill is Dictionary:
			_engraving_catalog.append(raw_skill)
	_engraving_catalog.sort_custom(_sort_skill_catalog_entries)
	if _engraving_catalog.is_empty():
		_set_engraving_load_error("技能資料中沒有 ID %d～%d 的刻紋技能" % [ENGRAVING_SKILL_MIN_ID, ENGRAVING_SKILL_MAX_ID])

func _set_engraving_load_error(message: String) -> void :
	_engraving_catalog.clear()
	_engraving_load_error = message
	engraving_tabs_status.text = "載入失敗"
	engraving_detail_status.text = message
	push_error("[GameInfo] " + message)

func _build_engraving_info_page() -> void :
	_engraving_tab_buttons.clear()
	if not _engraving_load_error.is_empty():
		return
	engraving_tabs_status.visible = false
	for skill in _engraving_catalog:
		var skill_id: = int(skill.get("id", 0))
		var button: = Button.new()
		button.text = str(skill.get("name", "未知刻紋"))
		button.custom_minimum_size = Vector2(0.0, 44.0)
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.focus_mode = Control.FOCUS_NONE
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		button.tooltip_text = str(skill.get("name", "未知刻紋"))
		button.icon = SkillLoader.get_icon(int(skill.get("icon_id", -1))) if SkillLoader else null
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.pressed.connect(_select_engraving_skill.bind(skill_id))
		engraving_skill_tabs.add_child(button)
		_engraving_tab_buttons[skill_id] = button
	if not _engraving_tab_buttons.has(_selected_engraving_skill_id) and not _engraving_catalog.is_empty():
		_selected_engraving_skill_id = int(_engraving_catalog[0].get("id", ENGRAVING_SKILL_MIN_ID))
	_select_engraving_skill(_selected_engraving_skill_id)

func _select_engraving_skill(skill_id: int) -> void :
	var selected_skill: Dictionary = {}
	for skill in _engraving_catalog:
		if int(skill.get("id", 0)) == skill_id:
			selected_skill = skill
			break
	if selected_skill.is_empty():
		return
	_selected_engraving_skill_id = skill_id
	for tab_skill_id in _engraving_tab_buttons:
		_apply_engraving_tab_style(_engraving_tab_buttons[tab_skill_id], int(tab_skill_id) == skill_id)
	_render_engraving_detail(selected_skill)
	engraving_detail_scroll.scroll_vertical = 0

func setup(player: CharacterBody2D) -> void :
	_target_player = player
	refresh_engraving_requirements()

func refresh_engraving_requirements() -> void :
	if not engraving_info_content.visible:
		return
	_select_engraving_skill(_selected_engraving_skill_id)

func _apply_engraving_tab_style(button: Button, selected: bool) -> void :
	var normal: = StyleBoxFlat.new()
	normal.bg_color = Color(0.22, 0.4, 0.57, 0.96) if selected else Color(0.1, 0.19, 0.31, 0.82)
	normal.border_color = Color(0.48, 0.78, 0.92, 0.92) if selected else Color(0.28, 0.45, 0.6, 0.48)
	normal.set_border_width_all(2 if selected else 1)
	normal.set_corner_radius_all(6)
	normal.content_margin_left = 8.0
	normal.content_margin_right = 8.0
	var hover: = normal.duplicate() as StyleBoxFlat
	hover.bg_color = Color(0.2, 0.34, 0.5, 0.96)
	button.add_theme_stylebox_override("normal", normal)
	button.add_theme_stylebox_override("hover", hover)
	button.add_theme_stylebox_override("pressed", hover)
	button.add_theme_color_override("font_color", Color(0.92, 0.97, 1.0, 1.0))
	button.add_theme_color_override("font_hover_color", Color.WHITE)

func _render_engraving_detail(skill: Dictionary) -> void :
	for child in engraving_detail_content.get_children():
		child.free()
	engraving_detail_content.add_theme_constant_override("separation", 14)

	var header: = HBoxContainer.new()
	header.add_theme_constant_override("separation", 12)
	engraving_detail_content.add_child(header)
	header.add_child(_make_skill_icon(int(skill.get("icon_id", -1))))
	var heading: = VBoxContainer.new()
	heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading.alignment = BoxContainer.ALIGNMENT_CENTER
	heading.add_theme_constant_override("separation", 4)
	header.add_child(heading)
	var title: = Label.new()
	title.text = str(skill.get("name", "未知刻紋"))
	title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	title.tooltip_text = title.text
	title.add_theme_color_override("font_color", Color(0.88, 0.96, 1.0, 1.0))
	title.add_theme_font_size_override("font_size", 22)
	heading.add_child(title)

	var description: = Label.new()
	var description_text: = str(skill.get("description", "")).strip_edges()
	description.text = description_text if not description_text.is_empty() else "無技能描述"
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	description.add_theme_color_override("font_color", Color(0.76, 0.82, 0.88, 1.0))
	description.add_theme_font_size_override("font_size", 14)
	engraving_detail_content.add_child(description)

	var levels: Array[Dictionary] = skill.get("levels", [] as Array[Dictionary])
	if levels.is_empty():
		engraving_detail_content.add_child(_make_engraving_empty_label("此技能沒有可顯示的等級資料"))
		return
	var level_data: Dictionary = levels[0]
	engraving_detail_content.add_child(_make_engraving_info_line("技能消耗", _format_engraving_cost(level_data), Color(0.94, 0.72, 0.29, 1.0)))
	engraving_detail_content.add_child(_make_engraving_info_line("冷卻時間", _format_engraving_seconds(int(level_data.get("cooldown", 0))), Color(0.35, 0.72, 0.94, 1.0)))
	engraving_detail_content.add_child(_make_engraving_info_line("持續時間", _format_engraving_seconds(int(level_data.get("duration", 0))), Color(0.38, 0.83, 0.63, 1.0)))
	_append_engraving_requirement_section(int(skill.get("id", 0)))

func _append_engraving_requirement_section(skill_id: int) -> void :
	var divider: = HSeparator.new()
	divider.add_theme_constant_override("separation", 8)
	engraving_detail_content.add_child(divider)
	var title: = Label.new()
	title.text = "解鎖刻紋"
	title.add_theme_color_override("font_color", Color(0.92, 0.74, 0.35, 1.0))
	title.add_theme_font_size_override("font_size", 16)
	engraving_detail_content.add_child(title)

	if not ENGRAVING_REQUIREMENTS.has(skill_id):
		var unavailable: = _make_engraving_empty_label("尚未開放")
		unavailable.custom_minimum_size.y = 72.0
		unavailable.add_theme_color_override("font_color", Color(0.67, 0.72, 0.78, 1.0))
		engraving_detail_content.add_child(unavailable)
		return

	var note: = Label.new()
	note.text = "集齊所有需求裝備後，到尼尼亞皇城找「刻紋大師」解鎖刻紋技能（不會消耗裝備），每次施放技能時，需求裝備必須存在背包或穿在身上。"
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	note.add_theme_color_override("font_color", Color(0.74, 0.79, 0.84, 1.0))
	note.add_theme_font_size_override("font_size", 13)
	engraving_detail_content.add_child(note)

	var requirement: Dictionary = ENGRAVING_REQUIREMENTS[skill_id]
	var ownership: = _get_engraving_owned_items()
	var ownership_ready: = bool(ownership.get("ready", false))
	var owned_items: Dictionary = ownership.get("items", {})
	var progress_label: = Label.new()
	progress_label.add_theme_font_size_override("font_size", 13)
	engraving_detail_content.add_child(progress_label)
	if str(requirement.get("mode", "")) == "all":
		var required_items: Array = requirement.get("items", [])
		var owned_count: = 0
		for raw_item_id in required_items:
			if owned_items.has(int(raw_item_id)):
				owned_count += 1
		_set_engraving_progress_label(progress_label, ownership_ready, owned_count, required_items.size())
		var all_label: = Label.new()
		all_label.text = "需求裝備"
		all_label.add_theme_color_override("font_color", Color(0.39, 0.78, 0.93, 1.0))
		all_label.add_theme_font_size_override("font_size", 16)
		engraving_detail_content.add_child(all_label)
		engraving_detail_content.add_child(_make_engraving_requirement_items(required_items, ownership_ready, owned_items))
		return

	var groups: Variant = requirement.get("groups", [])
	if not groups is Array:
		return
	var completed_groups: = 0
	for raw_group in groups:
		if raw_group is Dictionary and _engraving_group_is_owned(raw_group.get("items", []), owned_items):
			completed_groups += 1
	_set_engraving_progress_label(progress_label, ownership_ready, completed_groups, groups.size())
	for raw_group in groups:
		if not raw_group is Dictionary:
			continue
		var group: Dictionary = raw_group
		var group_title: = Label.new()
		group_title.text = "%s｜下列任一件" % str(group.get("label", "需求組"))
		group_title.add_theme_color_override("font_color", Color(0.39, 0.78, 0.93, 1.0))
		group_title.add_theme_font_size_override("font_size", 14)
		engraving_detail_content.add_child(group_title)
		engraving_detail_content.add_child(_make_engraving_requirement_items(group.get("items", []), ownership_ready, owned_items))

func _get_engraving_owned_items() -> Dictionary:
	if not is_instance_valid(_target_player) or not "data_model" in _target_player or _target_player.data_model == null:
		return {"ready": false, "items": {}}
	var player_data = _target_player.data_model
	var owned_items: Dictionary = {}
	for item in player_data.inventory:
		if not item is Dictionary:
			continue
		if int(item.get("in_bank", item.get("InBank", 0))) != 0 or int(item.get("in_shop", item.get("InShop", 0))) != 0:
			continue
		if int(item.get("count", item.get("Count", 0))) <= 0:
			continue
		var item_id: = int(item.get("item_index", item.get("ItemIndex", item.get("item_idx", item.get("ItemIdx", 0)))))
		if item_id > 0 and not owned_items.has(item_id):
			owned_items[item_id] = item
	for equip in player_data.equip_list:
		if not equip is Dictionary:
			continue
		if int(equip.get("in_bank", equip.get("InBank", 0))) != 0 or int(equip.get("in_shop", equip.get("InShop", 0))) != 0:
			continue
		var item_id: = int(equip.get("item_idx", equip.get("ItemIdx", equip.get("item_index", equip.get("ItemIndex", 0)))))
		if item_id > 0 and not owned_items.has(item_id):
			owned_items[item_id] = equip
	return {"ready": true, "items": owned_items}

func _set_engraving_progress_label(label: Label, ready: bool, completed: int, total: int) -> void :
	if not ready:
		label.text = "蒐集進度：狀態尚未同步"
		label.add_theme_color_override("font_color", Color(0.67, 0.72, 0.78, 1.0))
		return
	label.text = "蒐集進度：%d / %d" % [completed, total]
	label.add_theme_color_override("font_color", Color(0.35, 0.84, 0.57, 1.0) if completed >= total else Color(0.94, 0.68, 0.3, 1.0))

func _engraving_group_is_owned(raw_item_ids: Variant, owned_items: Dictionary) -> bool:
	if not raw_item_ids is Array:
		return false
	for raw_item_id in raw_item_ids:
		if owned_items.has(int(raw_item_id)):
			return true
	return false

func _make_engraving_requirement_items(raw_item_ids: Variant, ownership_ready: bool, owned_items: Dictionary) -> MarginContainer:
	var margin: = MarginContainer.new()
	margin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	margin.add_theme_constant_override("margin_left", 6)
	margin.add_theme_constant_override("margin_right", 6)
	var items: = HFlowContainer.new()
	items.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	items.add_theme_constant_override("h_separation", 7)
	items.add_theme_constant_override("v_separation", 7)
	margin.add_child(items)
	if not raw_item_ids is Array:
		return margin
	for raw_item_id in raw_item_ids:
		var item_id: = int(raw_item_id)
		items.add_child(_make_engraving_requirement_item(item_id, ownership_ready, owned_items.get(item_id, {})))
	return margin

func _make_engraving_requirement_item(item_id: int, ownership_ready: bool, owned_instance: Dictionary) -> PanelContainer:
	var card: = PanelContainer.new()
	card.custom_minimum_size = Vector2(96.0, 126.0)
	card.mouse_filter = Control.MOUSE_FILTER_STOP
	card.mouse_default_cursor_shape = Control.CURSOR_HELP
	card.set_meta("item_id", item_id)
	card.set_meta("ownership_ready", ownership_ready)
	card.set_meta("owned", ownership_ready and not owned_instance.is_empty())
	card.set_meta("owned_instance", owned_instance)
	var is_owned: = ownership_ready and not owned_instance.is_empty()
	card.mouse_entered.connect(_on_engraving_requirement_mouse_entered.bind(card, item_id, is_owned))
	card.mouse_exited.connect(_on_engraving_requirement_mouse_exited.bind(card, is_owned))
	_apply_engraving_requirement_card_style(card, is_owned, false)

	var content: = VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	content.add_theme_constant_override("separation", 4)
	card.add_child(content)
	var visual: = _get_engraving_item_visual(item_id, owned_instance)
	var icon: = TextureRect.new()
	icon.custom_minimum_size = Vector2(48.0, 48.0)
	icon.texture = visual.get("texture")
	icon.material = visual.get("material")
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(icon)
	var item_name: = str(ItemLoader.get_item_property(item_id, "item_name", "未知裝備"))
	var name_label: = Label.new()
	name_label.text = item_name
	name_label.custom_minimum_size.y = 38.0
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	name_label.tooltip_text = item_name
	name_label.add_theme_color_override("font_color", Color(0.82, 0.88, 0.93, 1.0))
	name_label.add_theme_font_size_override("font_size", 11)
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(name_label)
	var ownership_label: = Label.new()
	ownership_label.text = "已擁有" if is_owned else "未擁有"
	ownership_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	ownership_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	ownership_label.add_theme_color_override(
		"font_color", 
		Color(0.96, 0.77, 0.3, 1.0) if is_owned else Color(0.62, 0.74, 0.84, 1.0)
	)
	ownership_label.add_theme_font_size_override("font_size", 11)
	ownership_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(ownership_label)
	return card

func _on_engraving_requirement_mouse_entered(card: PanelContainer, item_id: int, is_owned: bool) -> void :
	_apply_engraving_requirement_card_style(card, is_owned, true)
	_show_equipment_item_tooltip(item_id)

func _on_engraving_requirement_mouse_exited(card: PanelContainer, is_owned: bool) -> void :
	_apply_engraving_requirement_card_style(card, is_owned, false)
	_hide_doll_item_tooltip()

func _apply_engraving_requirement_card_style(card: PanelContainer, is_owned: bool, hovered: bool) -> void :
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.13, 0.24, 0.37, 0.98) if hovered else Color(0.08, 0.16, 0.26, 0.94)
	if is_owned:
		style.border_color = Color(1.0, 0.84, 0.34, 1.0) if hovered else Color(0.92, 0.7, 0.22, 0.92)
	else:
		style.border_color = Color(0.48, 0.7, 0.88, 0.9) if hovered else Color(0.31, 0.51, 0.66, 0.62)
	style.set_border_width_all(2 if hovered else 1)
	style.set_corner_radius_all(6)
	style.content_margin_left = 6.0
	style.content_margin_right = 6.0
	style.content_margin_top = 6.0
	style.content_margin_bottom = 6.0
	card.add_theme_stylebox_override("panel", style)

func _get_engraving_item_visual(item_id: int, owned_instance: Dictionary) -> Dictionary:
	var raw_image_id: = int(ItemLoader.get_item_property(
		item_id, 
		"photo_id", 
		ItemLoader.get_item_property(item_id, "icon_id", 1)
	))
	var texture: = ItemLoader.get_photo(raw_image_id - 1)
	var color_index: = int(owned_instance.get(
		"Color", 
		owned_instance.get("color", ItemLoader.get_item_property(item_id, "color_index", 0))
	))
	var effect_type: = int(owned_instance.get(
		"EffectType", 
		owned_instance.get("effect_type", ItemLoader.get_item_property(item_id, "effect_type", 0))
	))
	var material: Material = null
	if color_index > 0 or effect_type > 0:
		var shader_material: = ShaderMaterial.new()
		shader_material.shader = PALETTE_SHADER
		shader_material.set_shader_parameter("effect_type", effect_type)
		shader_material.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
		material = shader_material
	return {"texture": texture, "material": material}

func _make_engraving_info_line(label_text: String, value_text: String, color: Color) -> Label:
	var label: = Label.new()
	label.text = "%s：%s" % [label_text, value_text]
	label.add_theme_color_override("font_color", color)
	label.add_theme_font_size_override("font_size", 14)
	return label

func _make_engraving_empty_label(message: String) -> Label:
	var label: = Label.new()
	label.text = message
	label.custom_minimum_size.y = 100.0
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.add_theme_color_override("font_color", Color(0.65, 0.72, 0.8, 1.0))
	return label

func _format_engraving_cost(level_data: Dictionary) -> String:
	var costs: Array[String] = []
	for entry in [["HP", "cost_hp"], ["MP", "cost_mp"], ["SP", "cost_sp"]]:
		var amount: = int(level_data.get(entry[1], 0))
		if amount > 0:
			costs.append("%s %d" % [entry[0], amount])
	return " / ".join(costs) if not costs.is_empty() else "無"

func _format_engraving_seconds(seconds: int) -> String:
	if seconds <= 0:
		return "無"
	if seconds >= 60 and seconds % 60 == 0:
		return "%d 分鐘" % int(seconds / 60)
	return "%d 秒" % seconds

func _sort_skill_catalog_entries(left: Dictionary, right: Dictionary) -> bool:
	return int(left.get("id", 0)) < int(right.get("id", 0))

func _build_skill_info_page() -> void :
	skill_info_search.text_changed.connect(_on_skill_search_changed)
	for child in skill_filter_buttons.get_children():
		child.free()
	_skill_filter_button_nodes.clear()
	for job_name in SKILL_FILTER_CLASSES:
		var button: = Button.new()
		button.text = job_name
		button.custom_minimum_size = Vector2(78.0, 34.0)
		button.focus_mode = Control.FOCUS_NONE
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.pressed.connect(_select_skill_class.bind(job_name))
		skill_filter_buttons.add_child(button)
		_skill_filter_button_nodes[job_name] = button

	_skill_card_entries.clear()
	if not _skill_load_error.is_empty():
		_update_skill_filter_button_styles()
		return
	for skill in _skill_catalog:
		var classes: = _get_skill_classes(str(skill.get("job_requirement", "")))
		if classes.is_empty():
			continue
		var effects: Array[String] = []
		var levels: Array[Dictionary] = skill.get("levels", [] as Array[Dictionary])
		for level_data in levels:
			for effect_text in _get_skill_effects(level_data):
				if not effects.has(effect_text):
					effects.append(effect_text)
		var card: = _make_skill_info_card(skill, classes)
		skill_info_rows.add_child(card)
		_skill_card_entries.append({
			"control": card, 
			"classes": classes, 
			"search_text": " ".join([
				str(skill.get("name", "")), 
				str(skill.get("description", "")), 
				" ".join(classes), 
				" ".join(effects)
			]).to_lower()
		})
	_apply_skill_filters()

func _get_skill_classes(job_requirement: String) -> Array[String]:
	var classes: Array[String] = []
	var normalized: = job_requirement.strip_edges()
	if normalized.is_empty() or normalized == "無":
		classes.append("無限制")
		return classes
	for raw_class in normalized.split("、", false):
		var job_name: = raw_class.strip_edges()
		if not job_name.is_empty() and not SKILL_INFO_EXCLUDED_CLASSES.has(job_name) and not classes.has(job_name):
			classes.append(job_name)
	return classes

func _select_skill_class(job_name: String) -> void :
	_selected_skill_class = job_name
	_update_skill_filter_button_styles()
	_apply_skill_filters()

func _on_skill_search_changed(_search_text: String) -> void :
	_apply_skill_filters()

func _update_skill_filter_button_styles() -> void :
	for job_name in _skill_filter_button_nodes:
		var button: Button = _skill_filter_button_nodes[job_name]
		var selected: bool = str(job_name) == _selected_skill_class
		var accent: Color = SKILL_CLASS_COLORS.get(job_name, Color(0.3, 0.45, 0.63, 1.0))
		var normal: = StyleBoxFlat.new()
		normal.bg_color = Color(accent.r, accent.g, accent.b, 0.88 if selected else 0.3)
		normal.border_color = Color(0.92, 0.96, 1.0, 0.85) if selected else Color(accent.r, accent.g, accent.b, 0.58)
		normal.set_border_width_all(2 if selected else 1)
		normal.set_corner_radius_all(14)
		var hover: = normal.duplicate() as StyleBoxFlat
		hover.bg_color = Color(accent.r, accent.g, accent.b, 0.72)
		button.add_theme_stylebox_override("normal", normal)
		button.add_theme_stylebox_override("hover", hover)
		button.add_theme_stylebox_override("pressed", hover)
		button.add_theme_color_override("font_color", Color.WHITE)
		button.add_theme_color_override("font_hover_color", Color.WHITE)

func _apply_skill_filters() -> void :
	if not _skill_load_error.is_empty():
		skill_info_result_count.text = "載入失敗"
		skill_info_status.visible = true
		return
	var query: = skill_info_search.text.strip_edges().to_lower()
	var visible_count: = 0
	for entry in _skill_card_entries:
		var classes: Array[String] = entry.get("classes", [] as Array[String])
		var matches_class: = _selected_skill_class == "全部" or classes.has(_selected_skill_class)
		var matches_query: = query.is_empty() or str(entry.get("search_text", "")).contains(query)
		var card: Control = entry["control"]
		card.visible = matches_class and matches_query
		if card.visible:
			visible_count += 1
	skill_info_result_count.text = "共 %d 項" % visible_count
	skill_info_status.visible = visible_count == 0
	if visible_count == 0:
		skill_info_status.text = "查無符合條件的技能資訊"

func _make_skill_info_card(skill: Dictionary, classes: Array[String]) -> PanelContainer:
	var card: = PanelContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var card_style: = StyleBoxFlat.new()
	card_style.bg_color = Color(0.08, 0.15, 0.25, 0.96)
	card_style.border_color = Color(0.28, 0.42, 0.57, 0.72)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(7)
	card_style.content_margin_left = 12.0
	card_style.content_margin_right = 12.0
	card_style.content_margin_top = 10.0
	card_style.content_margin_bottom = 10.0
	card.add_theme_stylebox_override("panel", card_style)

	var content: = VBoxContainer.new()
	content.add_theme_constant_override("separation", 8)
	card.add_child(content)

	var header: = HBoxContainer.new()
	header.add_theme_constant_override("separation", 10)
	content.add_child(header)
	header.add_child(_make_skill_icon(int(skill.get("icon_id", -1))))

	var summary: = VBoxContainer.new()
	summary.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	summary.add_theme_constant_override("separation", 4)
	header.add_child(summary)
	var title: = Label.new()
	title.text = str(skill.get("name", "未知技能"))
	title.add_theme_color_override("font_color", Color(0.92, 0.96, 1.0, 1.0))
	title.add_theme_font_size_override("font_size", 18)
	summary.add_child(title)
	var badges: = HFlowContainer.new()
	badges.add_theme_constant_override("h_separation", 5)
	badges.add_theme_constant_override("v_separation", 4)
	summary.add_child(badges)
	for job_name in classes:
		badges.add_child(_make_skill_class_badge(job_name))

	var description: = Label.new()
	description.text = str(skill.get("description", "")) if not str(skill.get("description", "")).is_empty() else "無技能描述"
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	description.add_theme_color_override("font_color", Color(0.7, 0.77, 0.84, 1.0))
	description.add_theme_font_size_override("font_size", 14)
	content.add_child(description)
	content.add_child(_make_skill_level_table(skill.get("levels", [] as Array[Dictionary])))
	return card

func _make_skill_icon(icon_id: int) -> PanelContainer:
	var frame: = PanelContainer.new()
	frame.custom_minimum_size = Vector2(54.0, 54.0)
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.04, 0.08, 0.14, 0.92)
	style.border_color = Color(0.4, 0.56, 0.7, 0.7)
	style.set_border_width_all(1)
	style.set_corner_radius_all(5)
	style.content_margin_left = 3.0
	style.content_margin_right = 3.0
	style.content_margin_top = 3.0
	style.content_margin_bottom = 3.0
	frame.add_theme_stylebox_override("panel", style)
	var texture: Texture2D = SkillLoader.get_icon(icon_id) if SkillLoader else null
	if texture:
		var icon: = TextureRect.new()
		icon.texture = texture
		icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
		frame.add_child(icon)
	else:
		var fallback: = Label.new()
		fallback.text = "無圖示"
		fallback.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		fallback.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		fallback.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		fallback.add_theme_color_override("font_color", Color(0.55, 0.62, 0.7, 1.0))
		fallback.add_theme_font_size_override("font_size", 11)
		frame.add_child(fallback)
	return frame

func _make_skill_class_badge(job_name: String) -> PanelContainer:
	var accent: Color = SKILL_CLASS_COLORS.get(job_name, Color(0.55, 0.36, 0.88, 1.0))
	var badge: = PanelContainer.new()
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(accent.r, accent.g, accent.b, 0.78)
	style.set_corner_radius_all(5)
	style.content_margin_left = 7.0
	style.content_margin_right = 7.0
	style.content_margin_top = 2.0
	style.content_margin_bottom = 2.0
	badge.add_theme_stylebox_override("panel", style)
	var label: = Label.new()
	label.text = job_name
	label.add_theme_color_override("font_color", Color.WHITE)
	label.add_theme_font_size_override("font_size", 11)
	badge.add_child(label)
	return badge

func _make_skill_level_table(raw_levels: Variant) -> VBoxContainer:
	var table: = VBoxContainer.new()
	table.add_theme_constant_override("separation", 2)
	var header: = HBoxContainer.new()
	header.add_theme_constant_override("separation", 4)
	header.add_child(_make_skill_table_cell("限制", 52.0, Color(0.3, 0.75, 0.94, 1.0), false, HORIZONTAL_ALIGNMENT_CENTER, true))
	header.add_child(_make_skill_table_cell("消耗", 72.0, Color(0.3, 0.75, 0.94, 1.0), false, HORIZONTAL_ALIGNMENT_CENTER, true))
	header.add_child(_make_skill_table_cell("傷害(加成)", 92.0, Color(0.3, 0.75, 0.94, 1.0), false, HORIZONTAL_ALIGNMENT_CENTER, true))
	header.add_child(_make_skill_table_cell("冷卻 / 持續", 94.0, Color(0.3, 0.75, 0.94, 1.0), false, HORIZONTAL_ALIGNMENT_CENTER, true))
	header.add_child(_make_skill_table_cell("附加屬性 / 效果", 0.0, Color(0.3, 0.75, 0.94, 1.0), true, HORIZONTAL_ALIGNMENT_LEFT, true))
	table.add_child(header)
	if not raw_levels is Array:
		return table
	for raw_level in raw_levels:
		if not raw_level is Dictionary:
			continue
		var row: = HBoxContainer.new()
		row.add_theme_constant_override("separation", 4)
		row.add_child(_make_skill_table_cell("Lv.%d" % int(raw_level.get("required_level", 0)), 52.0, Color(0.9, 0.93, 0.97, 1.0), false, HORIZONTAL_ALIGNMENT_CENTER))
		row.add_child(_make_skill_table_cell(_format_skill_cost(raw_level), 72.0, Color(0.94, 0.78, 0.35, 1.0), false, HORIZONTAL_ALIGNMENT_CENTER))
		row.add_child(_make_skill_table_cell(_format_skill_damage(raw_level), 92.0, Color(0.96, 0.43, 0.43, 1.0), false, HORIZONTAL_ALIGNMENT_CENTER))
		row.add_child(_make_skill_table_cell(_format_skill_time(raw_level), 94.0, Color(0.84, 0.87, 0.91, 1.0), false, HORIZONTAL_ALIGNMENT_CENTER))
		var effects: = _get_skill_effects(raw_level)
		row.add_child(_make_skill_table_cell("、".join(effects) if not effects.is_empty() else "無", 0.0, Color(0.25, 0.82, 0.61, 1.0), true, HORIZONTAL_ALIGNMENT_LEFT))
		table.add_child(row)
	return table

func _make_skill_table_cell(text_value: String, minimum_width: float, color: Color, expand: bool, alignment: HorizontalAlignment, heading: = false) -> Label:
	var label: = Label.new()
	label.text = text_value
	label.custom_minimum_size = Vector2(minimum_width, 28.0 if heading else 34.0)
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL if expand else Control.SIZE_SHRINK_BEGIN
	label.horizontal_alignment = alignment
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.add_theme_color_override("font_color", color)
	label.add_theme_font_size_override("font_size", 12 if heading else 11)
	return label

func _format_skill_cost(level_data: Dictionary) -> String:
	var costs: Array[String] = []
	for entry in [["HP", "cost_hp"], ["MP", "cost_mp"], ["SP", "cost_sp"]]:
		var value: = int(level_data.get(entry[1], 0))
		if value > 0:
			costs.append("%s:%d" % [entry[0], value])
	return "\n".join(costs) if not costs.is_empty() else "-"

func _format_skill_damage(level_data: Dictionary) -> String:
	var base_damage: = int(level_data.get("base_damage", 0))
	var multiplier: = int(level_data.get("damage_multiplier", 0))
	if base_damage <= 0:
		return "-"
	return "%d (+%d%%)" % [base_damage, multiplier] if multiplier > 0 else str(base_damage)

func _format_skill_time(level_data: Dictionary) -> String:
	var cooldown: = int(level_data.get("cooldown", 0))
	var duration: = int(level_data.get("duration", 0))
	return "%s / %s" % ["%ds" % cooldown if cooldown > 0 else "-", "%ds" % duration if duration > 0 else "-"]

func _get_skill_effects(level_data: Dictionary) -> Array[String]:
	var effects: Array[String] = []
	var effect_fields: = [
		["heal_hp", "回復HP"], ["heal_mp", "回復MP"], ["heal_sp", "回復SP"], 
		["max_hp_bonus", "加最大HP"], ["max_mp_bonus", "加最大MP"], ["max_sp_bonus", "加最大SP"], 
		["attack_bonus", "加攻擊"], ["defense_bonus", "加防禦"], ["magic_attack_bonus", "加魔攻"], 
		["magic_defense_bonus", "加魔防"], ["hit_bonus", "加命中"], ["dodge_bonus", "加迴避"], 
		["lucky_bonus", "加幸運"], ["attack_speed_bonus", "加攻速"], ["move_speed_bonus", "加移速"], 
		["crit_rate_bonus", "加暴率"], ["crit_damage_bonus", "加暴傷"], 
		["max_hp_percent", "加最大HP比例"], ["max_mp_percent", "加最大MP比例"], ["max_sp_percent", "加最大SP比例"], 
		["attack_percent", "加攻擊比例"], ["defense_percent", "加防禦比例"], ["magic_attack_percent", "加魔攻比例"], 
		["magic_defense_percent", "加魔防比例"], ["hit_percent", "加命中比例"], ["dodge_percent", "加迴避比例"], 
		["lucky_percent", "加幸運比例"], ["attack_speed_percent", "加攻速比例"], ["move_speed_percent", "加移速比例"], 
		["crit_rate_percent", "加暴率比例"], ["crit_damage_percent", "加暴傷比例"], ["revive_exp_percent", "減免死經比例"]
	]
	for field in effect_fields:
		var value: = int(level_data.get(field[0], 0))
		if value != 0:
			effects.append("%s:%d" % [field[1], value])
	var special_skill: = str(level_data.get("special_skill", "")).strip_edges()
	if not special_skill.is_empty():
		effects.append("特殊:%s" % special_skill)
	return effects

func _load_monster_drop_catalog() -> void :
	_monster_drop_all.clear()
	_monster_drop_filtered.clear()
	_monster_maps_by_name.clear()
	_monster_drop_load_error = ""

	var enemy_data: = _read_game_info_json(MONSTER_DATA_PATH, "怪物資料")
	if enemy_data.is_empty():
		_show_monster_drop_load_error()
		return
	var spawn_data: = _read_game_info_json(MONSTER_SPAWN_DATA_PATH, "怪物出沒地圖資料")
	if spawn_data.is_empty():
		_show_monster_drop_load_error()
		return

	_build_monster_map_index(spawn_data)
	for monster_key in enemy_data:
		var raw_monster: Variant = enemy_data[monster_key]
		if not raw_monster is Dictionary:
			continue
		var monster_data: Dictionary = raw_monster
		var monster_name: = str(monster_data.get("MonsterName", monster_key)).strip_edges()
		if monster_name.is_empty():
			continue
		var drop_names: Array[String] = []
		var raw_drops: Variant = monster_data.get("DropList", [])
		if raw_drops is Array:
			for raw_drop in raw_drops:
				if not raw_drop is Dictionary:
					continue
				var item_name: = str(raw_drop.get("ItemName", "")).strip_edges()
				if not item_name.is_empty():
					drop_names.append(item_name)
		var locations: Array[Dictionary] = []
		if _monster_maps_by_name.has(monster_name):
			for raw_location in _monster_maps_by_name[monster_name]:
				if raw_location is Dictionary:
					locations.append(raw_location)
		_monster_drop_all.append({
			"name": monster_name, 
			"level": int(monster_data.get("Level", 0)), 
			"sprite_name": str(monster_data.get("SpriteName", "")).strip_edges().to_upper(), 
			"color_index": int(monster_data.get("ColorIndex", 0)), 
			"drop_names": drop_names, 
			"locations": locations, 
			"has_fixed_spawn": not locations.is_empty()
		})

	_monster_drop_all.sort_custom(_sort_monster_drop_entries)
	_monster_drop_filtered.assign(_monster_drop_all)
	monster_drop_result_count.text = "共 %d 隻" % _monster_drop_all.size()
	_monster_drop_page = 0
	_render_monster_drop_page()

func _read_game_info_json(path: String, display_name: String) -> Dictionary:
	if not FileAccess.file_exists(path):
		_monster_drop_load_error = "%s不存在：%s" % [display_name, path]
		push_error("[GameInfo] " + _monster_drop_load_error)
		return {}
	var file: = FileAccess.open(path, FileAccess.READ)
	if file == null:
		_monster_drop_load_error = "無法讀取%s：%s" % [display_name, path]
		push_error("[GameInfo] " + _monster_drop_load_error)
		return {}
	var parser: = JSON.new()
	var parse_error: = parser.parse(file.get_as_text())
	if parse_error != OK or not parser.data is Dictionary:
		_monster_drop_load_error = "%s格式錯誤（第 %d 行）：%s" % [display_name, parser.get_error_line(), parser.get_error_message()]
		push_error("[GameInfo] " + _monster_drop_load_error)
		return {}
	return parser.data

func _build_monster_map_index(spawn_data: Dictionary) -> void :
	var map_sets_by_monster: Dictionary = {}
	for raw_map_id in spawn_data:
		var raw_map: Variant = spawn_data[raw_map_id]
		if not raw_map is Dictionary:
			continue
		var map_data: Dictionary = raw_map
		var map_id: = int(str(raw_map_id))
		var map_name: = str(map_data.get("MapName", "")).strip_edges()
		if map_name.is_empty():
			map_name = "Map%02d" % map_id
		var raw_spawners: Variant = map_data.get("Spawners", [])
		if not raw_spawners is Array:
			continue
		for raw_spawner in raw_spawners:
			if not raw_spawner is Dictionary:
				continue
			var monster_name: = str(raw_spawner.get("MonsterName", "")).strip_edges()
			if monster_name.is_empty():
				continue
			if not map_sets_by_monster.has(monster_name):
				map_sets_by_monster[monster_name] = {}
			map_sets_by_monster[monster_name][map_id] = map_name

	for monster_name in map_sets_by_monster:
		var locations: Array[Dictionary] = []
		var maps_for_monster: Dictionary = map_sets_by_monster[monster_name]
		var map_ids: Array = maps_for_monster.keys()
		map_ids.sort()
		for map_id in map_ids:
			locations.append({"map_id": int(map_id), "map_name": str(maps_for_monster[map_id])})
		_monster_maps_by_name[monster_name] = locations

func _sort_monster_drop_entries(left: Dictionary, right: Dictionary) -> bool:
	var left_level: = int(left.get("level", 0))
	var right_level: = int(right.get("level", 0))
	if left_level != right_level:
		return left_level < right_level
	return str(left.get("name", "")).naturalnocasecmp_to(str(right.get("name", ""))) < 0

func _show_monster_drop_load_error() -> void :
	_monster_drop_filtered.clear()
	monster_drop_result_count.text = "載入失敗"
	monster_drop_status.text = _monster_drop_load_error if not _monster_drop_load_error.is_empty() else "怪物資料載入失敗"
	monster_drop_status.visible = true
	monster_drop_previous_page.disabled = true
	monster_drop_next_page.disabled = true
	monster_drop_page_label.text = "第 0 / 0 頁"

func _render_monster_drop_page() -> void :
	_clear_monster_drop_cards()
	var total_count: = _monster_drop_filtered.size()
	if total_count == 0:
		monster_drop_status.text = "查無符合條件的怪物或掉落物" if not monster_drop_search.text.strip_edges().is_empty() else "沒有可顯示的怪物資料"
		monster_drop_status.visible = true
		monster_drop_previous_page.disabled = true
		monster_drop_next_page.disabled = true
		monster_drop_page_label.text = "第 0 / 0 頁"
		return

	var page_count: = ceili(float(total_count) / float(MONSTER_DROP_PAGE_SIZE))
	_monster_drop_page = clampi(_monster_drop_page, 0, page_count - 1)
	var start_index: = _monster_drop_page * MONSTER_DROP_PAGE_SIZE
	var end_index: = mini(start_index + MONSTER_DROP_PAGE_SIZE, total_count)
	for index in range(start_index, end_index):
		monster_drop_rows.add_child(_make_monster_drop_card(_monster_drop_filtered[index]))

	monster_drop_status.visible = false
	monster_drop_previous_page.disabled = _monster_drop_page <= 0
	monster_drop_next_page.disabled = _monster_drop_page >= page_count - 1
	monster_drop_page_label.text = "第 %d / %d 頁" % [_monster_drop_page + 1, page_count]
	monster_drop_scroll.set_deferred("scroll_vertical", 0)

func _clear_monster_drop_cards() -> void :
	for child in monster_drop_rows.get_children():
		if child != monster_drop_status:
			monster_drop_rows.remove_child(child)
			child.queue_free()

func _change_monster_drop_page(direction: int) -> void :
	var page_count: = ceili(float(_monster_drop_filtered.size()) / float(MONSTER_DROP_PAGE_SIZE))
	if page_count <= 0:
		return
	var next_page: = clampi(_monster_drop_page + direction, 0, page_count - 1)
	if next_page == _monster_drop_page:
		return
	_monster_drop_page = next_page
	_render_monster_drop_page()

func _on_monster_drop_search_changed(search_text: String) -> void :
	var query: = search_text.strip_edges().to_lower()
	_monster_drop_filtered.clear()
	if query.is_empty():
		_monster_drop_filtered.assign(_monster_drop_all)
	else:
		for monster in _monster_drop_all:
			if _monster_drop_matches_search(monster, query):
				_monster_drop_filtered.append(monster)
	_monster_drop_page = 0
	if query.is_empty():
		monster_drop_result_count.text = "共 %d 隻" % _monster_drop_all.size()
	else:
		monster_drop_result_count.text = "找到 %d / %d 隻" % [_monster_drop_filtered.size(), _monster_drop_all.size()]
	_render_monster_drop_page()

func _monster_drop_matches_search(monster: Dictionary, query: String) -> bool:
	if query in str(monster.get("name", "")).to_lower():
		return true
	var drop_names: Array = monster.get("drop_names", [])
	for drop_name in drop_names:
		if query in str(drop_name).to_lower():
			return true
	var locations: Array = monster.get("locations", [])
	for location in locations:
		if location is Dictionary and query in str(location.get("map_name", "")).to_lower():
			return true
	return false

func _make_monster_drop_card(monster: Dictionary) -> PanelContainer:
	var card: = PanelContainer.new()
	card.set_meta("monster_drop_card", true)
	card.set_meta("monster_name", str(monster.get("name", "")))
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.075, 0.135, 0.22, 0.94)
	style.border_color = Color(0.24, 0.38, 0.52, 0.8)
	style.set_border_width_all(1)
	style.set_corner_radius_all(7)
	style.content_margin_left = 14.0
	style.content_margin_top = 11.0
	style.content_margin_right = 14.0
	style.content_margin_bottom = 12.0
	card.add_theme_stylebox_override("panel", style)

	var card_layout: = VBoxContainer.new()
	card_layout.name = "CardLayout"
	card_layout.add_theme_constant_override("separation", 10)
	card.add_child(card_layout)

	var summary_section: = PanelContainer.new()
	summary_section.name = "SummarySection"
	summary_section.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var summary_style: = StyleBoxFlat.new()
	summary_style.bg_color = Color(0.045, 0.095, 0.155, 0.78)
	summary_style.set_corner_radius_all(5)
	summary_style.content_margin_left = 8.0
	summary_style.content_margin_top = 8.0
	summary_style.content_margin_right = 10.0
	summary_style.content_margin_bottom = 8.0
	summary_section.add_theme_stylebox_override("panel", summary_style)
	card_layout.add_child(summary_section)

	var summary_row: = HBoxContainer.new()
	summary_row.name = "SummaryRow"
	summary_row.add_theme_constant_override("separation", 12)
	summary_section.add_child(summary_row)
	summary_row.add_child(_make_monster_drop_preview(monster))

	var summary_info: = VBoxContainer.new()
	summary_info.name = "SummaryInfo"
	summary_info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	summary_info.add_theme_constant_override("separation", 8)
	summary_row.add_child(summary_info)

	var name_row: = HBoxContainer.new()
	name_row.name = "NameRow"
	name_row.add_theme_constant_override("separation", 10)
	summary_info.add_child(name_row)
	var name_label: = Label.new()
	name_label.name = "MonsterName"
	name_label.text = str(monster.get("name", "未知怪物"))
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.add_theme_color_override("font_color", Color(0.9, 0.95, 1.0, 1.0))
	name_label.add_theme_font_size_override("font_size", 18)
	name_row.add_child(name_label)
	var level_label: = Label.new()
	level_label.name = "MonsterLevel"
	level_label.text = "Lv.%d" % int(monster.get("level", 0))
	level_label.add_theme_color_override("font_color", Color(1.0, 0.76, 0.25, 1.0))
	level_label.add_theme_font_size_override("font_size", 16)
	name_row.add_child(level_label)

	var locations: Array = monster.get("locations", [])
	var location_names: Array[String] = []
	for location in locations:
		if location is Dictionary:
			var map_name: = str(location.get("map_name", "")).strip_edges()
			if not map_name.is_empty():
				location_names.append(map_name)
	var location_label: = Label.new()
	location_label.name = "MonsterLocations"
	location_label.text = "出沒地圖：%s" % ("、".join(location_names) if not location_names.is_empty() else "未知")
	location_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	location_label.add_theme_color_override("font_color", Color(0.58, 0.8, 0.94, 1.0))
	summary_info.add_child(location_label)

	var drop_names: Array = monster.get("drop_names", [])
	var drop_section: = PanelContainer.new()
	drop_section.name = "DropSection"
	drop_section.custom_minimum_size.y = 120.0
	drop_section.size_flags_vertical = Control.SIZE_EXPAND_FILL
	var drop_style: = StyleBoxFlat.new()
	drop_style.bg_color = Color(0.065, 0.12, 0.195, 0.72)
	drop_style.set_corner_radius_all(5)
	drop_style.content_margin_left = 10.0
	drop_style.content_margin_top = 9.0
	drop_style.content_margin_right = 10.0
	drop_style.content_margin_bottom = 10.0
	drop_section.add_theme_stylebox_override("panel", drop_style)
	card_layout.add_child(drop_section)

	var drop_content: = VBoxContainer.new()
	drop_content.name = "DropContent"
	drop_content.add_theme_constant_override("separation", 7)
	drop_section.add_child(drop_content)
	var drop_title: = Label.new()
	drop_title.name = "DropTitle"
	drop_title.text = "掉落物"
	drop_title.add_theme_color_override("font_color", Color(0.38, 0.82, 0.92, 1.0))
	drop_title.add_theme_font_size_override("font_size", 14)
	drop_content.add_child(drop_title)
	var drop_list: = Label.new()
	drop_list.name = "DropList"
	drop_list.text = "、".join(drop_names) if not drop_names.is_empty() else "無普通掉落物"
	drop_list.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	drop_list.add_theme_color_override("font_color", Color(0.86, 0.88, 0.91, 1.0) if not drop_names.is_empty() else Color(0.58, 0.62, 0.68, 1.0))
	drop_content.add_child(drop_list)
	return card

func _make_monster_drop_preview(monster: Dictionary) -> PanelContainer:
	var preview: = PanelContainer.new()
	preview.name = "MonsterPreview"
	preview.custom_minimum_size = Vector2(96.0, 96.0)
	preview.clip_contents = true
	var preview_style: = StyleBoxFlat.new()
	preview_style.bg_color = Color(0.035, 0.075, 0.125, 0.82)
	preview_style.border_color = Color(0.2, 0.34, 0.48, 0.65)
	preview_style.set_border_width_all(1)
	preview_style.set_corner_radius_all(6)
	preview_style.content_margin_left = 6.0
	preview_style.content_margin_top = 6.0
	preview_style.content_margin_right = 6.0
	preview_style.content_margin_bottom = 6.0
	preview.add_theme_stylebox_override("panel", preview_style)

	var frame_texture: = _load_monster_idle_first_frame(str(monster.get("sprite_name", "")))
	if frame_texture == null:
		var placeholder: = Label.new()
		placeholder.name = "PreviewPlaceholder"
		placeholder.text = "無預覽"
		placeholder.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		placeholder.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		placeholder.add_theme_color_override("font_color", Color(0.5, 0.56, 0.63, 1.0))
		preview.add_child(placeholder)
		return preview

	var texture_rect: = TextureRect.new()
	texture_rect.name = "PreviewTexture"
	texture_rect.texture = frame_texture
	texture_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	texture_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	texture_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var color_index: = int(monster.get("color_index", 0))
	if color_index > 0:
		var material: = ShaderMaterial.new()
		material.shader = PALETTE_SHADER
		material.set_shader_parameter("effect_type", 0)
		material.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
		texture_rect.material = material
	preview.add_child(texture_rect)
	return preview

func _load_monster_idle_first_frame(sprite_name: String) -> AtlasTexture:
	var normalized_name: = sprite_name.strip_edges().to_upper()
	if normalized_name.is_empty():
		return null
	var png_path: = "res://Assets/Monsters/%s.png" % normalized_name
	var json_path: = "res://Assets/Monsters/%s.json" % normalized_name
	if not ResourceLoader.exists(png_path) or not FileAccess.file_exists(json_path):
		return null
	var file: = FileAccess.open(json_path, FileAccess.READ)
	if file == null:
		return null
	var raw_atlas: Variant = JSON.parse_string(file.get_as_text())
	if not raw_atlas is Dictionary:
		return null
	var atlas_data: Dictionary = raw_atlas
	var animations: Variant = atlas_data.get("animations", {})
	var frames: Variant = atlas_data.get("frames", {})
	if not animations is Dictionary or not frames is Dictionary:
		return null
	var idle_frames: Variant = animations.get("IDLE_1", [])
	if not idle_frames is Array or idle_frames.is_empty():
		return null
	var frame_key: = str(idle_frames[0])
	var frame_data: Variant = frames.get(frame_key, {})
	if not frame_data is Dictionary:
		return null
	var width: = int(frame_data.get("w", 0))
	var height: = int(frame_data.get("h", 0))
	if width <= 0 or height <= 0:
		return null
	var atlas: = load(png_path) as Texture2D
	if atlas == null:
		return null
	var frame_texture: = AtlasTexture.new()
	frame_texture.atlas = atlas
	frame_texture.region = Rect2(
		int(frame_data.get("x", 0)), 
		int(frame_data.get("y", 0)), 
		width, 
		height
	)
	frame_texture.set_meta("old_offset", Vector2(
		float(frame_data.get("offset_x", 0.0)), 
		float(frame_data.get("offset_y", 0.0))
	))
	return frame_texture

func _build_pet_guide() -> void :
	pet_combat_mode_rows.add_child(_make_pet_guide_section_title("戰鬥模式", "由系統設定切換，會套用到所有寵物。"))
	for mode_info in PET_COMBAT_MODE_INFOS:
		pet_combat_mode_rows.add_child(_make_pet_guide_card(
			str(mode_info["name"]), 
			str(mode_info["description"]), 
			mode_info["accent"]
		))

	pet_hunger_rows.add_child(_make_pet_guide_section_title("飽食度", "召喚與餵食規則"))
	pet_hunger_rows.add_child(_make_pet_guide_card(
		"召喚消耗", 
		"召喚狀態下每秒扣除 1 點飽食度，飽食度歸零時，寵物將自動反召喚。", 
		Color(0.94, 0.63, 0.3, 1.0)
	))
	pet_hunger_rows.add_child(_make_pet_guide_card(
		"回復藥水皆可餵食", 
		"提升量為道具的 HP、MP、SP 回復值總和。例如回復 200 HP、100 MP 的藥水，可提升 300 點飽食度。", 
		Color(0.42, 0.78, 0.88, 1.0)
	))

	pet_permanent_stat_rows.add_child(_make_pet_guide_section_title("額外永久屬性配點", "餵食特定物品有機率獲得配點，各屬性最多額外 +20 點。"))
	var food_row: = HBoxContainer.new()
	food_row.add_theme_constant_override("separation", 8)
	pet_permanent_stat_rows.add_child(food_row)
	for food_info in PET_PERMANENT_STAT_FOODS:
		food_row.add_child(_make_pet_stat_food_card(
			int(food_info["item_id"]), 
			str(food_info["stat"]), 
			food_info["accent"]
		))

	pet_main_skill_rows.add_child(_make_pet_guide_section_title("設定主要技能", "在寵物視窗中設定"))
	pet_main_skill_rows.add_child(_make_pet_guide_card(
		"點擊技能名稱即可設定", 
		"當寵物擁有多個技能時，可在寵物視窗中點擊技能名稱，將其設定為主要技能。寵物發動技能時會優先使用主要技能，若無法施放，仍可能使用其他技能或普通攻擊。再次點擊可取消設定。", 
		Color(0.76, 0.57, 0.96, 1.0)
	))

func _make_pet_guide_section_title(title_text: String, note_text: String) -> VBoxContainer:
	var heading: = VBoxContainer.new()
	heading.add_theme_constant_override("separation", 2)
	var title: = Label.new()
	title.text = title_text
	title.add_theme_color_override("font_color", Color(0.9, 0.95, 1.0, 1.0))
	title.add_theme_font_size_override("font_size", 19)
	heading.add_child(title)
	var note: = Label.new()
	note.text = note_text
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	note.add_theme_color_override("font_color", Color(0.62, 0.74, 0.84, 1.0))
	note.add_theme_font_size_override("font_size", 13)
	heading.add_child(note)
	return heading

func _make_pet_guide_card(title_text: String, description_text: String, accent_color: Color) -> PanelContainer:
	var card: = _make_pet_guide_panel(accent_color)
	var content: = VBoxContainer.new()
	content.add_theme_constant_override("separation", 5)
	card.add_child(content)
	var title: = Label.new()
	title.text = title_text
	title.add_theme_color_override("font_color", accent_color)
	title.add_theme_font_size_override("font_size", 17)
	content.add_child(title)
	var description: = Label.new()
	description.text = description_text
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	description.add_theme_color_override("font_color", Color(0.86, 0.9, 0.94, 1.0))
	description.add_theme_font_size_override("font_size", 14)
	content.add_child(description)
	return card

func _make_pet_stat_food_card(item_id: int, stat_name: String, accent_color: Color) -> PanelContainer:
	var card: = _make_pet_guide_panel(accent_color)
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var content: = VBoxContainer.new()
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	content.add_theme_constant_override("separation", 4)
	card.add_child(content)

	var visual: = _get_doll_item_visual(item_id)
	var icon: = TextureRect.new()
	icon.custom_minimum_size = Vector2(54.0, 54.0)
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.texture = visual["texture"]
	icon.material = visual["material"]
	content.add_child(icon)

	var item_name: = Label.new()
	item_name.text = str(ItemLoader.get_item_property(item_id, "item_name", "未知物品"))
	item_name.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	item_name.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	item_name.add_theme_color_override("font_color", Color(0.94, 0.96, 1.0, 1.0))
	item_name.add_theme_font_size_override("font_size", 14)
	content.add_child(item_name)

	var effect: = Label.new()
	effect.text = "有機率獲得「%s」配點" % stat_name
	effect.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	effect.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	effect.add_theme_color_override("font_color", accent_color)
	effect.add_theme_font_size_override("font_size", 13)
	content.add_child(effect)
	return card

func _make_pet_guide_panel(accent_color: Color) -> PanelContainer:
	var card: = PanelContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.09, 0.18, 0.29, 0.94)
	style.border_color = Color(accent_color.r, accent_color.g, accent_color.b, 0.58)
	style.set_border_width_all(1)
	style.set_corner_radius_all(7)
	style.content_margin_left = 11.0
	style.content_margin_right = 11.0
	style.content_margin_top = 9.0
	style.content_margin_bottom = 9.0
	card.add_theme_stylebox_override("panel", style)
	return card

func _build_pet_evolution_catalog() -> void :
	for child in pet_family_list.get_children():
		child.queue_free()
	_pet_family_buttons.clear()
	_pet_evolution_graphs.clear()
	_selected_pet_family_id = -1

	var all_configs: Dictionary = PetLoader.get_all_pet_configs()
	var pet_ids: Array[int] = []
	for raw_pet_id in all_configs.keys():
		pet_ids.append(int(raw_pet_id))
	pet_ids.sort()

	var families_since_yield: int = 0
	var batch_started_usec: int = Time.get_ticks_usec()
	for pet_id in pet_ids:
		if not is_inside_tree():
			return
		var pet_config: Dictionary = all_configs.get(pet_id, {})
		var graph: = _build_pet_evolution_graph(pet_id, pet_config)
		_pet_evolution_graphs[pet_id] = graph

		var button: = Button.new()
		button.text = str(pet_config.get("name", "未知寵物（%d）" % pet_id))
		button.custom_minimum_size.y = 36.0
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.focus_mode = Control.FOCUS_NONE
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.pressed.connect(_select_pet_family.bind(pet_id))
		pet_family_list.add_child(button)
		_pet_family_buttons[pet_id] = button
		if _selected_pet_family_id < 0:
			_select_pet_family(pet_id)
		families_since_yield += 1
		if should_yield_pet_evolution_batch(families_since_yield, Time.get_ticks_usec() - batch_started_usec):
			await get_tree().process_frame
			if not is_inside_tree():
				return
			families_since_yield = 0
			batch_started_usec = Time.get_ticks_usec()

	if pet_ids.is_empty():
		pet_family_title.text = "沒有可顯示的寵物"
		pet_stage_count.text = ""
		pet_roadmap_empty_state.text = "Config.json 目前沒有寵物設定"
		pet_roadmap_empty_state.visible = true
		return

	# Retain user-selected family while remaining graphs are built.
	if OS.is_debug_build():
		print("[PET_EVOLUTION_CATALOG_READY] families=%d" % pet_ids.size())

func _build_pet_evolution_graph(pet_id: int, pet_config: Dictionary) -> Dictionary:
	var nodes: Dictionary = {}
	var incoming_counts: Dictionary = {}
	var missing_links: Array[Dictionary] = []
	var raw_evolutions = pet_config.get("evolutions", {})
	if not raw_evolutions is Dictionary:
		raw_evolutions = {}

	for raw_evolution_id in raw_evolutions.keys():
		var evolution_id: = int(raw_evolution_id)
		var raw_config = raw_evolutions[raw_evolution_id]
		if not raw_config is Dictionary:
			continue
		var node_config: Dictionary = raw_config.duplicate(true)
		node_config["evolution_id"] = evolution_id
		node_config["next_ids"] = []
		node_config["parent_ids"] = []
		node_config["display_skills"] = _get_pet_display_skills(node_config.get("skills", []))
		nodes[evolution_id] = node_config
		incoming_counts[evolution_id] = 0

	var evolution_ids: Array[int] = []
	for raw_evolution_id in nodes.keys():
		evolution_ids.append(int(raw_evolution_id))
	evolution_ids.sort()

	for evolution_id in evolution_ids:
		var node_config: Dictionary = nodes[evolution_id]
		var valid_next_ids: Array[int] = []
		for raw_next_id in node_config.get("next_evol_ids", []):
			var next_id: = int(raw_next_id)
			if not nodes.has(next_id):
				missing_links.append({"from": evolution_id, "to": next_id})
				continue
			if next_id not in valid_next_ids:
				valid_next_ids.append(next_id)
				incoming_counts[next_id] = int(incoming_counts.get(next_id, 0)) + 1
				var parent_ids: Array = nodes[next_id]["parent_ids"]
				parent_ids.append(evolution_id)
		valid_next_ids.sort()
		node_config["next_ids"] = valid_next_ids

	var root_ids: Array[int] = []
	for evolution_id in evolution_ids:
		if int(incoming_counts.get(evolution_id, 0)) == 0:
			root_ids.append(evolution_id)

	var depth_by_id: Dictionary = {}
	var pending: Array[Dictionary] = []
	for root_id in root_ids:
		pending.append({"id": root_id, "depth": 0})
	_assign_pet_evolution_depths(nodes, pending, depth_by_id)


	for evolution_id in evolution_ids:
		if depth_by_id.has(evolution_id):
			continue
		root_ids.append(evolution_id)
		_assign_pet_evolution_depths(nodes, [{"id": evolution_id, "depth": 0}], depth_by_id)

	var levels: Dictionary = {}
	for evolution_id in evolution_ids:
		var depth: = int(depth_by_id.get(evolution_id, 0))
		if not levels.has(depth):
			levels[depth] = []
		levels[depth].append(evolution_id)

	return {
		"pet_id": pet_id, 
		"name": str(pet_config.get("name", "未知寵物（%d）" % pet_id)), 
		"nodes": nodes, 
		"root_ids": root_ids, 
		"levels": levels, 
		"missing_links": missing_links
	}

func _assign_pet_evolution_depths(nodes: Dictionary, pending: Array[Dictionary], depth_by_id: Dictionary) -> void :
	while not pending.is_empty():
		var entry: Dictionary = pending.pop_front()
		var evolution_id: = int(entry.get("id", -1))
		var depth: = int(entry.get("depth", 0))
		if not nodes.has(evolution_id):
			continue
		if depth_by_id.has(evolution_id) and int(depth_by_id[evolution_id]) <= depth:
			continue
		depth_by_id[evolution_id] = depth
		for next_id in nodes[evolution_id].get("next_ids", []):
			pending.append({"id": int(next_id), "depth": depth + 1})

func _get_pet_display_skills(raw_skills: Variant) -> Array[Dictionary]:
	var display_skills: Array[Dictionary] = []
	if not raw_skills is Array:
		return display_skills
	for raw_skill in raw_skills:
		if not raw_skill is Dictionary:
			continue
		var skill_id: = int(raw_skill.get("skill_id", 0))
		var skill_level: = int(raw_skill.get("level", 1))
		var skill_name: = "未知技能（%d）" % skill_id
		if skill_id == 9999:
			skill_name = "自動撿拾"
		elif SkillLoader:
			var skill_data = SkillLoader.get_skill_data(skill_id)
			if skill_data:
				skill_name = str(skill_data.skill_name)
		display_skills.append({"skill_id": skill_id, "level": skill_level, "name": skill_name})
	return display_skills

func _select_pet_family(pet_id: int) -> void :
	if not _pet_evolution_graphs.has(pet_id):
		return
	_selected_pet_family_id = pet_id
	for family_id in _pet_family_buttons.keys():
		_apply_button_style(_pet_family_buttons[family_id], int(family_id) == pet_id)

	for child in pet_roadmap_rows.get_children():
		child.queue_free()
	var graph: Dictionary = _pet_evolution_graphs[pet_id]
	var stage_total: = (graph.get("nodes", {}) as Dictionary).size()
	pet_family_title.text = str(graph.get("name", "未知寵物"))
	pet_stage_count.text = "%d 個進化階段" % stage_total
	_render_pet_evolution_graph(graph)

func _render_pet_evolution_graph(graph: Dictionary) -> void :
	var nodes: Dictionary = graph.get("nodes", {})
	var root_ids: Array = graph.get("root_ids", [])
	if nodes.is_empty() or root_ids.is_empty():
		pet_roadmap_empty_state.text = "此寵物目前沒有可顯示的進化資料"
		pet_roadmap_empty_state.visible = true
		return

	pet_roadmap_empty_state.visible = false
	var roots_layout: = HBoxContainer.new()
	roots_layout.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	roots_layout.alignment = BoxContainer.ALIGNMENT_CENTER
	roots_layout.add_theme_constant_override("separation", 14)
	pet_roadmap_rows.add_child(roots_layout)

	var rendered_ids: Dictionary = {}
	for raw_root_id in root_ids:
		var root_id: = int(raw_root_id)
		if rendered_ids.has(root_id) or not nodes.has(root_id):
			continue
		roots_layout.add_child(_make_pet_evolution_branch(root_id, graph, rendered_ids))

func _make_pet_evolution_branch(evolution_id: int, graph: Dictionary, rendered_ids: Dictionary) -> VBoxContainer:
	var branch: = VBoxContainer.new()
	branch.alignment = BoxContainer.ALIGNMENT_BEGIN
	branch.add_theme_constant_override("separation", 7)
	var nodes: Dictionary = graph.get("nodes", {})
	if rendered_ids.has(evolution_id) or not nodes.has(evolution_id):
		return branch

	rendered_ids[evolution_id] = true
	var node_config: Dictionary = nodes[evolution_id]
	var card_center: = CenterContainer.new()
	card_center.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	branch.add_child(card_center)
	card_center.add_child(_make_pet_evolution_card(
		node_config, 
		evolution_id in graph.get("root_ids", [])
	))

	var next_ids: Array = node_config.get("next_ids", [])
	var unrendered_next_ids: Array[int] = []
	for raw_next_id in next_ids:
		var next_id: = int(raw_next_id)
		if nodes.has(next_id) and not rendered_ids.has(next_id):
			unrendered_next_ids.append(next_id)
	if unrendered_next_ids.is_empty():
		return branch

	var connector: = Label.new()
	connector.text = "↓  %s" % ("分支進化" if unrendered_next_ids.size() > 1 else "進化")
	connector.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	connector.add_theme_color_override("font_color", Color(0.55, 0.77, 0.94, 1.0))
	connector.add_theme_font_size_override("font_size", 13)
	branch.add_child(connector)

	var children_layout: = HBoxContainer.new()
	children_layout.alignment = BoxContainer.ALIGNMENT_CENTER
	children_layout.add_theme_constant_override("separation", 12)
	branch.add_child(children_layout)
	for next_id in unrendered_next_ids:
		children_layout.add_child(_make_pet_evolution_branch(next_id, graph, rendered_ids))
	return branch

func _make_pet_evolution_card(node_config: Dictionary, is_initial_stage: bool) -> PanelContainer:
	var card: = PanelContainer.new()
	card.set_meta("pet_evolution_id", int(node_config.get("evolution_id", -1)))
	card.set_meta("pet_outlook", str(node_config.get("outlook", "")))
	card.set_meta("pet_initial_stage", is_initial_stage)
	card.custom_minimum_size = Vector2(218.0, 0.0)
	card.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.08, 0.17, 0.28, 0.97)
	style.border_color = Color(0.35, 0.62, 0.82, 0.72) if not is_initial_stage else Color(0.48, 0.82, 0.63, 0.86)
	style.set_border_width_all(1)
	style.set_corner_radius_all(8)
	style.content_margin_left = 10.0
	style.content_margin_right = 10.0
	style.content_margin_top = 9.0
	style.content_margin_bottom = 10.0
	card.add_theme_stylebox_override("panel", style)

	var content: = VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_theme_constant_override("separation", 6)
	card.add_child(content)

	var preview: = Control.new()
	preview.custom_minimum_size = Vector2(198.0, 92.0)
	preview.clip_contents = true
	preview.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(preview)
	_add_pet_evolution_preview(preview, node_config)

	var stage_name: = str(node_config.get("sub_name", "未知階段"))
	var name_label: = Label.new()
	name_label.text = stage_name if not stage_name.is_empty() else "未知階段"
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.add_theme_color_override("font_color", Color(0.94, 0.97, 1.0, 1.0))
	name_label.add_theme_font_size_override("font_size", 17)
	content.add_child(name_label)

	if is_initial_stage:
		content.add_child(_make_pet_evolution_badge("初始階段", Color(0.48, 0.82, 0.63, 1.0)))
	else:
		content.add_child(_make_pet_evolution_requirements(node_config.get("requirements", {})))

	content.add_child(_make_pet_evolution_skills(node_config.get("display_skills", [])))
	return card

func _add_pet_evolution_preview(preview: Control, node_config: Dictionary) -> void :
	var outlook: = str(node_config.get("outlook", ""))
	if outlook.is_empty() or not MonsterLoader:
		return
	var frames = MonsterLoader.get_monster_frames(outlook)
	if not frames:
		return

	var sprite: = AnimatedSprite2D.new()
	sprite.sprite_frames = frames
	sprite.centered = false
	preview.add_child(sprite)
	var animation_name: = "IDLE_1"
	if not frames.has_animation(animation_name):
		var animation_names: = frames.get_animation_names()
		if animation_names.is_empty():
			return
		animation_name = animation_names[0]
	sprite.play(animation_name)

	var color_index: = int(node_config.get("color", 0))
	if color_index > 0:
		var material: = ShaderMaterial.new()
		material.shader = PALETTE_SHADER
		material.set_shader_parameter("effect_type", 0)
		material.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
		sprite.material = material

	sprite.frame_changed.connect(_update_pet_evolution_preview.bind(sprite, preview))
	preview.resized.connect(_fit_pet_evolution_preview.bind(sprite, preview))
	_update_pet_evolution_preview(sprite, preview)
	_fit_pet_evolution_preview(sprite, preview)

func _update_pet_evolution_preview(sprite: AnimatedSprite2D, preview: Control) -> void :
	if not is_instance_valid(sprite) or not sprite.sprite_frames:
		return
	if not sprite.sprite_frames.has_animation(sprite.animation):
		return
	var texture: = sprite.sprite_frames.get_frame_texture(sprite.animation, sprite.frame)
	if texture:
		var old_offset: Vector2 = texture.get_meta("old_offset") if texture.has_meta("old_offset") else Vector2.ZERO
		sprite.offset = - old_offset
	_fit_pet_evolution_preview(sprite, preview)

func _fit_pet_evolution_preview(sprite: AnimatedSprite2D, preview: Control) -> void :
	if not is_instance_valid(sprite) or not sprite.sprite_frames:
		return
	var animation_name: = sprite.animation
	if not sprite.sprite_frames.has_animation(animation_name):
		return
	var min_corner: = Vector2(INF, INF)
	var max_corner: = Vector2( - INF, - INF)
	for frame_index in sprite.sprite_frames.get_frame_count(animation_name):
		var texture: = sprite.sprite_frames.get_frame_texture(animation_name, frame_index)
		if not texture:
			continue
		var old_offset: Vector2 = texture.get_meta("old_offset") if texture.has_meta("old_offset") else Vector2.ZERO
		var top_left: = - old_offset
		min_corner = min_corner.min(top_left)
		max_corner = max_corner.max(top_left + texture.get_size())
	var bounds_size: = max_corner - min_corner
	if bounds_size.x <= 0.0 or bounds_size.y <= 0.0:
		return
	var available_size: Vector2 = preview.size if preview.size.x > 0.0 and preview.size.y > 0.0 else preview.custom_minimum_size
	var preview_scale: = minf(1.0, minf(available_size.x / bounds_size.x, available_size.y / bounds_size.y))
	sprite.scale = Vector2.ONE * preview_scale
	sprite.position = available_size / 2.0 - (min_corner + bounds_size / 2.0) * preview_scale

func _make_pet_evolution_requirements(raw_requirements: Variant) -> VBoxContainer:
	var requirements: Dictionary = raw_requirements if raw_requirements is Dictionary else {}
	var container: = VBoxContainer.new()
	container.mouse_filter = Control.MOUSE_FILTER_IGNORE
	container.add_theme_constant_override("separation", 3)
	var title: = Label.new()
	title.text = "進化需求"
	title.add_theme_color_override("font_color", Color(0.62, 0.78, 0.9, 1.0))
	title.add_theme_font_size_override("font_size", 12)
	container.add_child(title)

	var chips: = HFlowContainer.new()
	chips.mouse_filter = Control.MOUSE_FILTER_IGNORE
	chips.add_theme_constant_override("h_separation", 4)
	chips.add_theme_constant_override("v_separation", 4)
	container.add_child(chips)
	chips.add_child(_make_pet_evolution_badge("等級 %d" % int(requirements.get("level", 0)), Color(0.95, 0.72, 0.3, 1.0)))
	var stats = requirements.get("stats", {})
	if stats is Dictionary:
		var stat_labels: = {"con": "體質", "str": "力量", "dex": "速度", "wis": "智慧"}
		for stat_key in ["con", "str", "dex", "wis"]:
			var value: = int(stats.get(stat_key, 0))
			if value > 0:
				chips.add_child(_make_pet_evolution_badge("%s %d" % [stat_labels[stat_key], value], Color(0.58, 0.75, 0.94, 1.0)))
	return container

func _make_pet_evolution_skills(raw_skills: Variant) -> VBoxContainer:
	var container: = VBoxContainer.new()
	container.mouse_filter = Control.MOUSE_FILTER_IGNORE
	container.add_theme_constant_override("separation", 3)
	var title: = Label.new()
	title.text = "技能組"
	title.add_theme_color_override("font_color", Color(0.62, 0.78, 0.9, 1.0))
	title.add_theme_font_size_override("font_size", 12)
	container.add_child(title)
	if not raw_skills is Array or raw_skills.is_empty():
		var empty_label: = Label.new()
		empty_label.text = "尚未擁有技能"
		empty_label.add_theme_color_override("font_color", Color(0.58, 0.62, 0.67, 1.0))
		empty_label.add_theme_font_size_override("font_size", 12)
		container.add_child(empty_label)
		return container
	for raw_skill in raw_skills:
		if not raw_skill is Dictionary:
			continue
		var skill_label: = Label.new()
		skill_label.text = "%s　Lv.%d" % [str(raw_skill.get("name", "未知技能")), int(raw_skill.get("level", 1))]
		skill_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		skill_label.add_theme_color_override("font_color", Color(0.84, 0.88, 0.94, 1.0))
		skill_label.add_theme_font_size_override("font_size", 12)
		container.add_child(skill_label)
	return container

func _make_pet_evolution_badge(text_value: String, accent_color: Color) -> PanelContainer:
	var badge: = PanelContainer.new()
	badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(accent_color.r, accent_color.g, accent_color.b, 0.14)
	style.border_color = Color(accent_color.r, accent_color.g, accent_color.b, 0.48)
	style.set_border_width_all(1)
	style.set_corner_radius_all(5)
	style.content_margin_left = 7.0
	style.content_margin_right = 7.0
	style.content_margin_top = 3.0
	style.content_margin_bottom = 3.0
	badge.add_theme_stylebox_override("panel", style)
	var label: = Label.new()
	label.text = text_value
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_color_override("font_color", accent_color)
	label.add_theme_font_size_override("font_size", 12)
	badge.add_child(label)
	return badge

func _build_club_pages() -> void :
	for tab_index in range(club_tab_buttons.size()):
		var button: = club_tab_buttons[tab_index]
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.pressed.connect(_select_club_page.bind(tab_index))

	_build_club_creation_rows()
	_build_club_shop()
	_build_club_research_rows()
	_build_club_command_rows()
	_select_club_page(0)

func _select_club_page(page_index: int) -> void :
	if page_index < 0 or page_index >= club_pages.size():
		return

	for index in range(club_pages.size()):
		club_pages[index].visible = index == page_index
		_apply_button_style(club_tab_buttons[index], index == page_index)
	if page_index != 1:
		_hide_doll_item_tooltip()

	if club_pages[page_index] is ScrollContainer:
		(club_pages[page_index] as ScrollContainer).scroll_vertical = 0

func _build_club_shop() -> void :
	for level_index in range(club_shop_level_buttons.size()):
		var button: = club_shop_level_buttons[level_index]
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.pressed.connect(_select_club_shop_level.bind(level_index))

	if not CLUB_SHOP_LEVELS.is_empty():
		_select_club_shop_level(0)

func _select_club_shop_level(level_index: int) -> void :
	if level_index < 0 or level_index >= CLUB_SHOP_LEVELS.size():
		return

	_hide_doll_item_tooltip()
	for index in range(club_shop_level_buttons.size()):
		_apply_button_style(club_shop_level_buttons[index], index == level_index)

	var shop_level: Dictionary = CLUB_SHOP_LEVELS[level_index]
	var item_ids: Array = shop_level["item_ids"]
	club_shop_title.text = "%d 級戰團可購買商品" % int(shop_level["level"])
	club_shop_item_count.text = "共 %d 項" % item_ids.size()

	for child in club_shop_grid.get_children():
		child.free()
	for item_id_value in item_ids:
		club_shop_grid.add_child(_make_doll_reward_card(int(item_id_value), 1))

	club_shop_scroll.scroll_vertical = 0

func _build_club_creation_rows() -> void :
	club_creation_rows.add_child(_make_club_intro(
		"準備好以下條件後，即可向皇家戰團管理員申請成立戰團。"
	))

	for requirement_index in range(CLUB_CREATION_REQUIREMENTS.size()):
		var requirement: Dictionary = CLUB_CREATION_REQUIREMENTS[requirement_index]
		club_creation_rows.add_child(_make_club_creation_card(
			requirement_index + 1, 
			str(requirement["label"]), 
			str(requirement["value"])
		))

func _make_club_creation_card(step_number: int, label_text: String, value_text: String) -> PanelContainer:
	var card: = _make_club_card(Color(0.37, 0.65, 0.86, 1.0))
	var layout: = HBoxContainer.new()
	layout.add_theme_constant_override("separation", 12)
	card.add_child(layout)

	var badge: = Label.new()
	badge.text = str(step_number)
	badge.custom_minimum_size = Vector2(34.0, 34.0)
	badge.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	badge.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	badge.add_theme_color_override("font_color", Color(0.95, 0.78, 0.38, 1.0))
	badge.add_theme_font_size_override("font_size", 20)
	layout.add_child(badge)

	var content: = VBoxContainer.new()
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 4)
	layout.add_child(content)

	var heading: = Label.new()
	heading.text = label_text
	heading.add_theme_color_override("font_color", Color(0.65, 0.82, 0.95, 1.0))
	heading.add_theme_font_size_override("font_size", 14)
	content.add_child(heading)

	var value: = Label.new()
	value.text = value_text
	value.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	value.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	value.add_theme_font_size_override("font_size", 17)
	content.add_child(value)
	return card

func _build_club_research_rows() -> void :
	club_research_rows.add_child(_make_club_intro(
		"累積足夠的戰團經驗與戰團資金後，團長可以進行戰團科技研發。"
	))
	club_research_rows.add_child(_make_club_section_title("研發效果"))

	for research_info in CLUB_RESEARCH_INFOS:
		club_research_rows.add_child(_make_club_research_card(research_info))

	club_research_rows.add_child(_make_club_section_title("研發需求"))
	for requirement in CLUB_RESEARCH_REQUIREMENTS:
		club_research_rows.add_child(_make_club_requirement_row(requirement))

func _make_club_research_card(research_info: Dictionary) -> PanelContainer:
	var card: = _make_club_card(Color(0.46, 0.69, 0.9, 1.0))
	var layout: = HBoxContainer.new()
	layout.add_theme_constant_override("separation", 12)
	card.add_child(layout)

	var name_label: = Label.new()
	name_label.text = str(research_info["name"])
	name_label.custom_minimum_size.x = 88.0
	name_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	name_label.add_theme_color_override("font_color", Color(0.95, 0.76, 0.36, 1.0))
	name_label.add_theme_font_size_override("font_size", 17)
	layout.add_child(name_label)

	var effect_label: = Label.new()
	effect_label.text = str(research_info["effect"])
	effect_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	effect_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	effect_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	effect_label.add_theme_font_size_override("font_size", 15)
	layout.add_child(effect_label)
	return card

func _make_club_requirement_row(requirement: Dictionary) -> PanelContainer:
	var card: = _make_club_card(Color(0.7, 0.58, 0.3, 1.0))
	var layout: = HBoxContainer.new()
	layout.add_theme_constant_override("separation", 10)
	card.add_child(layout)

	var label: = Label.new()
	label.text = str(requirement["label"])
	label.custom_minimum_size.x = 100.0
	label.add_theme_color_override("font_color", Color(0.95, 0.76, 0.36, 1.0))
	label.add_theme_font_size_override("font_size", 15)
	layout.add_child(label)

	var value: = Label.new()
	value.text = str(requirement["value"])
	value.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	value.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	value.add_theme_font_size_override("font_size", 15)
	layout.add_child(value)
	return card

func _build_club_command_rows() -> void :
	club_commands_rows.add_child(_make_club_intro(
		"在聊天輸入框中輸入以下指令，玩家名稱與職位代號之間請保留空格。"
	))

	for command_info in CLUB_COMMAND_INFOS:
		club_commands_rows.add_child(_make_club_command_card(command_info))

func _make_club_command_card(command_info: Dictionary) -> PanelContainer:
	var card: = _make_club_card(Color(0.49, 0.71, 0.88, 1.0))
	var content: = VBoxContainer.new()
	content.add_theme_constant_override("separation", 7)
	card.add_child(content)

	var command: = Label.new()
	command.text = str(command_info["command"])
	command.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	command.add_theme_color_override("font_color", Color(0.95, 0.76, 0.36, 1.0))
	command.add_theme_font_size_override("font_size", 18)
	content.add_child(command)

	var summary: = Label.new()
	summary.text = str(command_info["summary"])
	summary.add_theme_color_override("font_color", Color(0.88, 0.94, 0.98, 1.0))
	summary.add_theme_font_size_override("font_size", 16)
	content.add_child(summary)

	for detail_text_value in command_info["details"]:
		var detail: = Label.new()
		detail.text = "• %s" % str(detail_text_value)
		detail.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		detail.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		detail.add_theme_color_override("font_color", Color(0.76, 0.83, 0.89, 1.0))
		detail.add_theme_font_size_override("font_size", 14)
		content.add_child(detail)
	return card

func _make_club_intro(intro_text: String) -> Label:
	var intro: = Label.new()
	intro.text = intro_text
	intro.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	intro.add_theme_color_override("font_color", Color(0.72, 0.82, 0.9, 1.0))
	intro.add_theme_font_size_override("font_size", 15)
	return intro

func _make_club_section_title(title_text: String) -> Label:
	var title: = Label.new()
	title.text = title_text
	title.custom_minimum_size.y = 32.0
	title.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	title.add_theme_color_override("font_color", Color(0.82, 0.91, 0.98, 1.0))
	title.add_theme_font_size_override("font_size", 18)
	return title

func _make_club_card(accent_color: Color) -> PanelContainer:
	var card: = PanelContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.1, 0.19, 0.31, 0.94)
	style.border_color = Color(accent_color.r, accent_color.g, accent_color.b, 0.58)
	style.set_border_width_all(1)
	style.set_corner_radius_all(7)
	style.content_margin_left = 12.0
	style.content_margin_right = 12.0
	style.content_margin_top = 10.0
	style.content_margin_bottom = 10.0
	card.add_theme_stylebox_override("panel", style)
	return card

func _build_world_boss_rows() -> void :
	var intro: = Label.new()
	intro.text = "世界王會在固定時段出現於下列其中一個地點。"
	intro.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	intro.add_theme_color_override("font_color", Color(0.72, 0.82, 0.9, 1.0))
	intro.add_theme_font_size_override("font_size", 15)
	world_boss_rows.add_child(intro)

	for boss_info in WORLD_BOSS_INFOS:
		world_boss_rows.add_child(_make_world_boss_card(boss_info))

func _make_world_boss_card(boss_info: Dictionary) -> PanelContainer:
	var card: = PanelContainer.new()
	card.custom_minimum_size.y = 315.0
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var card_style: = StyleBoxFlat.new()
	card_style.bg_color = Color(0.1, 0.19, 0.31, 0.94)
	card_style.border_color = Color(0.45, 0.68, 0.86, 0.62)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(8)
	card_style.content_margin_left = 10.0
	card_style.content_margin_right = 14.0
	card_style.content_margin_top = 8.0
	card_style.content_margin_bottom = 8.0
	card.add_theme_stylebox_override("panel", card_style)

	var layout: = HBoxContainer.new()
	layout.add_theme_constant_override("separation", 14)
	card.add_child(layout)

	var preview_panel: = PanelContainer.new()
	preview_panel.custom_minimum_size = Vector2(170.0, 295.0)
	preview_panel.clip_contents = true
	var preview_style: = StyleBoxFlat.new()
	preview_style.bg_color = Color(0.06, 0.12, 0.21, 0.88)
	preview_style.border_color = Color(0.34, 0.54, 0.72, 0.45)
	preview_style.set_border_width_all(1)
	preview_style.set_corner_radius_all(7)
	preview_panel.add_theme_stylebox_override("panel", preview_style)
	layout.add_child(preview_panel)

	var preview_area: = Control.new()
	preview_area.mouse_filter = Control.MOUSE_FILTER_IGNORE
	preview_panel.add_child(preview_area)

	var monster_preview = UI_MONSTER.new()
	monster_preview.monster_id = str(boss_info["monster_id"])
	monster_preview.default_anim = "IDLE_1"
	monster_preview.anim_speed_scale = 1.0
	monster_preview.position = Vector2(85.0, 202.0)
	monster_preview.scale = Vector2(0.9, 0.9)
	preview_area.add_child(monster_preview)

	var content: = VBoxContainer.new()
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	content.add_theme_constant_override("separation", 10)
	layout.add_child(content)

	var header: = HBoxContainer.new()
	header.add_theme_constant_override("separation", 8)
	content.add_child(header)

	var accent: = ColorRect.new()
	accent.custom_minimum_size = Vector2(4.0, 27.0)
	accent.color = Color(0.95, 0.67, 0.25, 1.0)
	accent.mouse_filter = Control.MOUSE_FILTER_IGNORE
	header.add_child(accent)

	var name_label: = Label.new()
	name_label.text = str(boss_info["name"])
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	name_label.add_theme_color_override("font_color", Color(0.94, 0.96, 1.0, 1.0))
	name_label.add_theme_font_size_override("font_size", 21)
	header.add_child(name_label)

	content.add_child(_make_world_boss_time_badge(str(boss_info["spawn_time"])))

	var location_heading: = Label.new()
	location_heading.text = "出沒地點"
	location_heading.add_theme_color_override("font_color", Color(0.65, 0.82, 0.95, 1.0))
	location_heading.add_theme_font_size_override("font_size", 14)
	content.add_child(location_heading)

	var locations: = HFlowContainer.new()
	locations.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	locations.add_theme_constant_override("h_separation", 7)
	locations.add_theme_constant_override("v_separation", 6)
	content.add_child(locations)
	var location_names: Array = boss_info["locations"]
	for location_index in range(location_names.size()):
		if location_index > 0:
			var or_label: = Label.new()
			or_label.text = "或"
			or_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
			or_label.add_theme_color_override("font_color", Color(0.68, 0.74, 0.8, 1.0))
			or_label.add_theme_font_size_override("font_size", 14)
			locations.add_child(or_label)
		locations.add_child(_make_world_boss_location_chip(str(location_names[location_index])))

	content.add_child(_make_world_boss_rewards(boss_info["rewards"]))
	return card

func _make_world_boss_time_badge(time_text: String) -> PanelContainer:
	var badge: = PanelContainer.new()
	badge.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.29, 0.22, 0.1, 0.76)
	style.border_color = Color(0.9, 0.65, 0.24, 0.62)
	style.set_border_width_all(1)
	style.set_corner_radius_all(6)
	style.content_margin_left = 10.0
	style.content_margin_right = 10.0
	style.content_margin_top = 6.0
	style.content_margin_bottom = 6.0
	badge.add_theme_stylebox_override("panel", style)

	var label: = Label.new()
	label.text = "出沒時間　%s" % time_text
	label.add_theme_color_override("font_color", Color(0.96, 0.8, 0.44, 1.0))
	label.add_theme_font_size_override("font_size", 16)
	badge.add_child(label)
	return badge

func _make_world_boss_location_chip(location_name: String) -> PanelContainer:
	var chip: = PanelContainer.new()
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.14, 0.29, 0.44, 0.94)
	style.border_color = Color(0.42, 0.67, 0.87, 0.68)
	style.set_border_width_all(1)
	style.set_corner_radius_all(10)
	style.content_margin_left = 10.0
	style.content_margin_right = 10.0
	style.content_margin_top = 4.0
	style.content_margin_bottom = 4.0
	chip.add_theme_stylebox_override("panel", style)

	var label: = Label.new()
	label.text = location_name
	label.add_theme_font_size_override("font_size", 14)
	chip.add_child(label)
	return chip

func _make_world_boss_rewards(rewards: Array) -> VBoxContainer:
	var section: = VBoxContainer.new()
	section.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	section.add_theme_constant_override("separation", 5)

	var heading: = Label.new()
	heading.text = "最高傷害獎勵"
	heading.add_theme_color_override("font_color", Color(0.65, 0.82, 0.95, 1.0))
	heading.add_theme_font_size_override("font_size", 13)
	section.add_child(heading)

	for reward_info in rewards:
		section.add_child(_make_world_boss_reward_item(
			int(reward_info["item_id"]), 
			int(reward_info["count"])
		))
	return section

func _make_world_boss_reward_item(item_id: int, display_count: int) -> PanelContainer:
	var reward: = PanelContainer.new()
	reward.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	reward.mouse_filter = Control.MOUSE_FILTER_STOP
	reward.mouse_default_cursor_shape = Control.CURSOR_HELP
	reward.mouse_entered.connect(_on_world_boss_reward_mouse_entered.bind(reward, item_id))
	reward.mouse_exited.connect(_on_world_boss_reward_mouse_exited.bind(reward))
	_apply_world_boss_reward_style(reward, false)

	var layout: = HBoxContainer.new()
	layout.mouse_filter = Control.MOUSE_FILTER_IGNORE
	layout.add_theme_constant_override("separation", 10)
	reward.add_child(layout)

	var visual: = _get_doll_item_visual(item_id)
	var icon: = TextureRect.new()
	icon.custom_minimum_size = Vector2(48.0, 48.0)
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.texture = visual["texture"]
	icon.material = visual["material"]
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	layout.add_child(icon)

	var labels: = VBoxContainer.new()
	labels.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	labels.mouse_filter = Control.MOUSE_FILTER_IGNORE
	layout.add_child(labels)

	var item_row: = HBoxContainer.new()
	item_row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	item_row.add_theme_constant_override("separation", 8)
	labels.add_child(item_row)

	var item_name: = Label.new()
	item_name.text = str(ItemLoader.get_item_property(item_id, "item_name", "未知物品"))
	item_name.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	item_name.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	item_name.add_theme_font_size_override("font_size", 15)
	item_name.mouse_filter = Control.MOUSE_FILTER_IGNORE
	item_row.add_child(item_name)

	var count: = Label.new()
	count.text = "×%d" % display_count
	count.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	count.add_theme_color_override("font_color", Color(0.95, 0.76, 0.36, 1.0))
	count.add_theme_font_size_override("font_size", 16)
	count.mouse_filter = Control.MOUSE_FILTER_IGNORE
	item_row.add_child(count)
	return reward

func _on_world_boss_reward_mouse_entered(reward: PanelContainer, item_id: int) -> void :
	_apply_world_boss_reward_style(reward, true)
	_show_doll_item_tooltip(item_id)

func _on_world_boss_reward_mouse_exited(reward: PanelContainer) -> void :
	_apply_world_boss_reward_style(reward, false)
	_hide_doll_item_tooltip()

func _apply_world_boss_reward_style(reward: PanelContainer, hovered: bool) -> void :
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.2, 0.32, 0.47, 0.96) if hovered else Color(0.13, 0.24, 0.37, 0.94)
	style.border_color = Color(0.62, 0.78, 0.91, 0.86) if hovered else Color(0.39, 0.59, 0.75, 0.56)
	style.set_border_width_all(1)
	style.set_corner_radius_all(6)
	style.content_margin_left = 8.0
	style.content_margin_right = 9.0
	style.content_margin_top = 6.0
	style.content_margin_bottom = 6.0
	reward.add_theme_stylebox_override("panel", style)

func _build_transform_rows() -> void :
	var intro: = Label.new()
	intro.text = "使用變身卷軸後，會依照背包中持有的特殊道具判定外型。"
	intro.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	intro.add_theme_color_override("font_color", Color(0.72, 0.82, 0.9, 1.0))
	intro.add_theme_font_size_override("font_size", 15)
	transform_rows.add_child(intro)
	transform_rows.add_child(_make_transform_featured_item(
		int(TRANSFORM_ITEM_IDS["scroll"]), 
		"使用後依照下方規則判定並套用變身外型。"
	))

	transform_rows.add_child(_make_transform_section_title("道具判定優先順序"))
	var priority_grid: = GridContainer.new()
	priority_grid.columns = 2
	priority_grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	priority_grid.add_theme_constant_override("h_separation", 8)
	priority_grid.add_theme_constant_override("v_separation", 8)
	transform_rows.add_child(priority_grid)
	for priority_index in range(TRANSFORM_PRIORITY.size()):
		priority_grid.add_child(_make_transform_priority_card(priority_index + 1, TRANSFORM_PRIORITY[priority_index]))

	transform_rows.add_child(_make_transform_section_title("一般隨機外型"))
	transform_rows.add_child(_make_transform_note("未持有三種特殊靈石時，會從以下外型隨機選擇一種。"))
	var normal_names: = HFlowContainer.new()
	normal_names.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	normal_names.add_theme_constant_override("h_separation", 7)
	normal_names.add_theme_constant_override("v_separation", 7)
	transform_rows.add_child(normal_names)
	for transform_name in NORMAL_TRANSFORM_NAMES:
		normal_names.add_child(_make_transform_name_chip(str(transform_name)))

	transform_rows.add_child(_make_transform_section_title("持有妖怪靈石"))
	transform_rows.add_child(_make_transform_note("會從以下特殊怪物外型隨機選擇一種，並獲得對應能力加成。"))
	var monster_grid: = GridContainer.new()
	monster_grid.columns = 2
	monster_grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	monster_grid.add_theme_constant_override("h_separation", 8)
	monster_grid.add_theme_constant_override("v_separation", 8)
	transform_rows.add_child(monster_grid)
	for transform_info in MONSTER_STONE_TRANSFORMS:
		monster_grid.add_child(_make_transform_effect_card(transform_info, Color(0.48, 0.68, 0.88, 1.0)))

	transform_rows.add_child(_make_transform_section_title("龍族固定外型"))
	transform_rows.add_child(_make_transform_note("持有對應龍族之石時，變身結果為固定外型。"))
	var dragon_grid: = GridContainer.new()
	dragon_grid.columns = 2
	dragon_grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	dragon_grid.add_theme_constant_override("h_separation", 8)
	dragon_grid.add_theme_constant_override("v_separation", 8)
	transform_rows.add_child(dragon_grid)
	for transform_info in DRAGON_STONE_TRANSFORMS:
		dragon_grid.add_child(_make_transform_effect_card(transform_info, Color(0.91, 0.61, 0.24, 1.0)))

	transform_rows.add_child(_make_transform_section_title("效果與解除方式"))
	for effect_info in TRANSFORM_EFFECT_INFOS:
		transform_rows.add_child(_make_transform_effect_info_card(effect_info))

func _make_transform_section_title(title_text: String) -> Label:
	var title: = Label.new()
	title.text = title_text
	title.custom_minimum_size.y = 34.0
	title.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	title.add_theme_color_override("font_color", Color(0.84, 0.92, 0.98, 1.0))
	title.add_theme_font_size_override("font_size", 19)
	return title

func _make_transform_note(note_text: String) -> Label:
	var note: = Label.new()
	note.text = note_text
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	note.add_theme_color_override("font_color", Color(0.68, 0.78, 0.86, 1.0))
	note.add_theme_font_size_override("font_size", 14)
	return note

func _make_transform_priority_card(order: int, priority_info: Dictionary) -> PanelContainer:
	var card: = _make_transform_card(Color(0.86, 0.65, 0.28, 1.0))
	card.custom_minimum_size.y = 78.0
	var layout: = HBoxContainer.new()
	layout.add_theme_constant_override("separation", 10)
	card.add_child(layout)

	var order_label: = Label.new()
	order_label.text = str(order)
	order_label.custom_minimum_size = Vector2(34.0, 34.0)
	order_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	order_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	order_label.add_theme_color_override("font_color", Color(0.96, 0.77, 0.36, 1.0))
	order_label.add_theme_font_size_override("font_size", 21)
	layout.add_child(order_label)
	var item_id: = int(priority_info["item_id"])
	if item_id > 0:
		layout.add_child(_make_transform_item_icon(item_id))

	var content: = VBoxContainer.new()
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 3)
	layout.add_child(content)

	var item_label: = Label.new()
	item_label.text = str(priority_info["label"])
	item_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	item_label.add_theme_color_override("font_color", Color(0.92, 0.95, 0.98, 1.0))
	item_label.add_theme_font_size_override("font_size", 15)
	content.add_child(item_label)

	var result: = Label.new()
	result.text = str(priority_info["result"])
	result.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	result.add_theme_color_override("font_color", Color(0.66, 0.81, 0.92, 1.0))
	result.add_theme_font_size_override("font_size", 13)
	content.add_child(result)
	return card

func _make_transform_name_chip(transform_name: String) -> PanelContainer:
	var chip: = PanelContainer.new()
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.14, 0.29, 0.44, 0.94)
	style.border_color = Color(0.42, 0.67, 0.87, 0.68)
	style.set_border_width_all(1)
	style.set_corner_radius_all(11)
	style.content_margin_left = 11.0
	style.content_margin_right = 11.0
	style.content_margin_top = 5.0
	style.content_margin_bottom = 5.0
	chip.add_theme_stylebox_override("panel", style)

	var label: = Label.new()
	label.text = transform_name
	label.add_theme_font_size_override("font_size", 14)
	chip.add_child(label)
	return chip

func _make_transform_effect_card(transform_info: Dictionary, accent_color: Color) -> PanelContainer:
	var card: = _make_transform_card(accent_color)
	card.custom_minimum_size.y = 104.0
	var content: = VBoxContainer.new()
	content.add_theme_constant_override("separation", 8)
	card.add_child(content)

	var name_label: = Label.new()
	name_label.text = str(transform_info["name"])
	name_label.add_theme_color_override("font_color", Color(0.93, 0.96, 1.0, 1.0))
	name_label.add_theme_font_size_override("font_size", 17)
	content.add_child(name_label)

	var bonuses: = HFlowContainer.new()
	bonuses.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bonuses.add_theme_constant_override("h_separation", 6)
	bonuses.add_theme_constant_override("v_separation", 6)
	content.add_child(bonuses)
	for bonus in transform_info["bonuses"]:
		bonuses.add_child(_make_transform_bonus_chip(str(bonus["stat"]), int(bonus["value"])))
	return card

func _make_transform_bonus_chip(stat_name: String, bonus_value: int) -> PanelContainer:
	var colors: = {
		"物攻": Color(0.91, 0.39, 0.31, 1.0), 
		"物防": Color(0.82, 0.66, 0.29, 1.0), 
		"魔攻": Color(0.55, 0.47, 0.91, 1.0), 
		"魔防": Color(0.38, 0.69, 0.87, 1.0), 
		"命中": Color(0.4, 0.78, 0.59, 1.0), 
		"迴避": Color(0.45, 0.76, 0.83, 1.0)
	}
	var color: Color = colors.get(stat_name, Color(0.68, 0.72, 0.78, 1.0))
	var chip: = PanelContainer.new()
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(color.r, color.g, color.b, 0.16)
	style.border_color = Color(color.r, color.g, color.b, 0.7)
	style.set_border_width_all(1)
	style.set_corner_radius_all(9)
	style.content_margin_left = 8.0
	style.content_margin_right = 8.0
	style.content_margin_top = 3.0
	style.content_margin_bottom = 3.0
	chip.add_theme_stylebox_override("panel", style)

	var label: = Label.new()
	label.text = "%s + %d" % [stat_name, bonus_value]
	label.add_theme_color_override("font_color", color)
	label.add_theme_font_size_override("font_size", 13)
	chip.add_child(label)
	return chip

func _make_transform_effect_info_card(effect_info: Dictionary) -> PanelContainer:
	var card: = _make_transform_card(Color(0.46, 0.68, 0.86, 1.0))
	var layout: = HBoxContainer.new()
	layout.add_theme_constant_override("separation", 12)
	card.add_child(layout)

	var heading: = Label.new()
	heading.text = str(effect_info["label"])
	heading.custom_minimum_size.x = 92.0
	heading.add_theme_color_override("font_color", Color(0.65, 0.82, 0.95, 1.0))
	heading.add_theme_font_size_override("font_size", 14)
	layout.add_child(heading)
	if str(effect_info["label"]) == "提前解除":
		var restore_item_id: = int(TRANSFORM_ITEM_IDS["restore_scroll"])
		layout.add_child(_make_transform_item_icon(restore_item_id))
		var restore_name: = Label.new()
		restore_name.text = str(ItemLoader.get_item_property(restore_item_id, "item_name", "還原卷軸"))
		restore_name.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		restore_name.add_theme_color_override("font_color", Color(0.9, 0.94, 0.98, 1.0))
		restore_name.add_theme_font_size_override("font_size", 14)
		layout.add_child(restore_name)

	var value: = Label.new()
	value.text = str(effect_info["value"])
	value.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	value.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	value.add_theme_font_size_override("font_size", 15)
	layout.add_child(value)
	return card

func _make_transform_card(accent_color: Color) -> PanelContainer:
	var card: = PanelContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.1, 0.19, 0.31, 0.94)
	style.border_color = Color(accent_color.r, accent_color.g, accent_color.b, 0.58)
	style.set_border_width_all(1)
	style.set_corner_radius_all(7)
	style.content_margin_left = 11.0
	style.content_margin_right = 11.0
	style.content_margin_top = 9.0
	style.content_margin_bottom = 9.0
	card.add_theme_stylebox_override("panel", style)
	return card

func _make_transform_featured_item(item_id: int, description_text: String) -> PanelContainer:
	var item_panel: = PanelContainer.new()
	item_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	item_panel.mouse_filter = Control.MOUSE_FILTER_STOP
	item_panel.mouse_default_cursor_shape = Control.CURSOR_HELP
	item_panel.mouse_entered.connect(_on_transform_item_mouse_entered.bind(item_panel, item_id))
	item_panel.mouse_exited.connect(_on_transform_item_mouse_exited.bind(item_panel))
	_apply_transform_item_style(item_panel, false)

	var layout: = HBoxContainer.new()
	layout.mouse_filter = Control.MOUSE_FILTER_IGNORE
	layout.add_theme_constant_override("separation", 11)
	item_panel.add_child(layout)

	var visual: = _get_doll_item_visual(item_id)
	var icon: = TextureRect.new()
	icon.custom_minimum_size = Vector2(52.0, 52.0)
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.texture = visual["texture"]
	icon.material = visual["material"]
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	layout.add_child(icon)

	var labels: = VBoxContainer.new()
	labels.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	labels.mouse_filter = Control.MOUSE_FILTER_IGNORE
	labels.add_theme_constant_override("separation", 3)
	layout.add_child(labels)

	var item_name: = Label.new()
	item_name.text = str(ItemLoader.get_item_property(item_id, "item_name", "未知物品"))
	item_name.add_theme_color_override("font_color", Color(0.94, 0.96, 1.0, 1.0))
	item_name.add_theme_font_size_override("font_size", 18)
	item_name.mouse_filter = Control.MOUSE_FILTER_IGNORE
	labels.add_child(item_name)

	var description: = Label.new()
	description.text = description_text
	description.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	description.add_theme_color_override("font_color", Color(0.69, 0.8, 0.89, 1.0))
	description.add_theme_font_size_override("font_size", 14)
	description.mouse_filter = Control.MOUSE_FILTER_IGNORE
	labels.add_child(description)
	return item_panel

func _make_transform_item_icon(item_id: int) -> PanelContainer:
	var item_panel: = PanelContainer.new()
	item_panel.custom_minimum_size = Vector2(46.0, 46.0)
	item_panel.mouse_filter = Control.MOUSE_FILTER_STOP
	item_panel.mouse_default_cursor_shape = Control.CURSOR_HELP
	item_panel.mouse_entered.connect(_on_transform_item_mouse_entered.bind(item_panel, item_id))
	item_panel.mouse_exited.connect(_on_transform_item_mouse_exited.bind(item_panel))
	_apply_transform_item_style(item_panel, false)

	var visual: = _get_doll_item_visual(item_id)
	var icon: = TextureRect.new()
	icon.custom_minimum_size = Vector2(38.0, 38.0)
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.texture = visual["texture"]
	icon.material = visual["material"]
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	item_panel.add_child(icon)
	return item_panel

func _on_transform_item_mouse_entered(item_panel: PanelContainer, item_id: int) -> void :
	_apply_transform_item_style(item_panel, true)
	_show_doll_item_tooltip(item_id)

func _on_transform_item_mouse_exited(item_panel: PanelContainer) -> void :
	_apply_transform_item_style(item_panel, false)
	_hide_doll_item_tooltip()

func _apply_transform_item_style(item_panel: PanelContainer, hovered: bool) -> void :
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.2, 0.33, 0.48, 0.96) if hovered else Color(0.13, 0.25, 0.38, 0.94)
	style.border_color = Color(0.63, 0.79, 0.91, 0.88) if hovered else Color(0.39, 0.59, 0.75, 0.58)
	style.set_border_width_all(1)
	style.set_corner_radius_all(6)
	style.content_margin_left = 7.0
	style.content_margin_right = 7.0
	style.content_margin_top = 5.0
	style.content_margin_bottom = 5.0
	item_panel.add_theme_stylebox_override("panel", style)

func _build_royal_territory_war_page() -> void :
	royal_territory_war_rows.add_child(_make_royal_territory_war_hero())
	for section_value in ROYAL_TERRITORY_WAR_SECTIONS:
		var section: Dictionary = section_value
		royal_territory_war_rows.add_child(_make_royal_territory_war_card(section))

func _make_royal_territory_war_hero() -> PanelContainer:
	var hero: = PanelContainer.new()
	hero.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.12, 0.22, 0.36, 0.98)
	style.border_color = Color(0.62, 0.78, 0.94, 0.75)
	style.set_border_width_all(1)
	style.set_corner_radius_all(9)
	style.content_margin_left = 16.0
	style.content_margin_right = 16.0
	style.content_margin_top = 14.0
	style.content_margin_bottom = 14.0
	hero.add_theme_stylebox_override("panel", style)

	var content: = VBoxContainer.new()
	content.add_theme_constant_override("separation", 8)
	hero.add_child(content)

	var title_row: = HBoxContainer.new()
	title_row.add_theme_constant_override("separation", 10)
	content.add_child(title_row)

	var crest: = Label.new()
	crest.text = "◆"
	crest.add_theme_color_override("font_color", Color(0.95, 0.77, 0.31, 1.0))
	crest.add_theme_font_size_override("font_size", 24)
	title_row.add_child(crest)

	var title: = Label.new()
	title.text = "為戰團奪下皇家榮耀"
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	title.add_theme_color_override("font_color", Color(0.94, 0.97, 1.0, 1.0))
	title.add_theme_font_size_override("font_size", 22)
	title_row.add_child(title)

	var description: = Label.new()
	description.text = "集結團員、護送能量石，在一小時的混戰中爭取最高積分。"
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	description.add_theme_color_override("font_color", Color(0.74, 0.83, 0.91, 1.0))
	description.add_theme_font_size_override("font_size", 15)
	content.add_child(description)

	var chips: = HFlowContainer.new()
	chips.add_theme_constant_override("h_separation", 7)
	chips.add_theme_constant_override("v_separation", 7)
	content.add_child(chips)
	chips.add_child(_make_royal_territory_war_chip("週五 20:00～21:00", Color(0.35, 0.7, 0.96, 1.0)))
	chips.add_child(_make_royal_territory_war_chip("最多 4 個戰團", Color(0.71, 0.48, 0.94, 1.0)))
	chips.add_child(_make_royal_territory_war_chip("全區 PvP", Color(0.94, 0.39, 0.36, 1.0)))
	chips.add_child(_make_royal_territory_war_chip("能量石 1 個上限", Color(0.42, 0.84, 0.63, 1.0)))
	return hero

func _make_royal_territory_war_chip(text_value: String, accent: Color) -> PanelContainer:
	var chip: = PanelContainer.new()
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(accent.r, accent.g, accent.b, 0.18)
	style.border_color = Color(accent.r, accent.g, accent.b, 0.68)
	style.set_border_width_all(1)
	style.set_corner_radius_all(12)
	style.content_margin_left = 10.0
	style.content_margin_right = 10.0
	style.content_margin_top = 4.0
	style.content_margin_bottom = 4.0
	chip.add_theme_stylebox_override("panel", style)

	var label: = Label.new()
	label.text = text_value
	label.add_theme_color_override("font_color", Color(0.92, 0.96, 1.0, 1.0))
	label.add_theme_font_size_override("font_size", 14)
	chip.add_child(label)
	return chip

func _make_royal_territory_war_card(section: Dictionary) -> PanelContainer:
	var accent: Color = section["accent"]
	var card: = PanelContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.08, 0.16, 0.27, 0.94)
	style.border_color = Color(accent.r, accent.g, accent.b, 0.48)
	style.set_border_width_all(1)
	style.set_corner_radius_all(8)
	style.content_margin_left = 13.0
	style.content_margin_right = 13.0
	style.content_margin_top = 11.0
	style.content_margin_bottom = 11.0
	card.add_theme_stylebox_override("panel", style)

	var content: = VBoxContainer.new()
	content.add_theme_constant_override("separation", 8)
	card.add_child(content)

	var header: = HBoxContainer.new()
	header.add_theme_constant_override("separation", 9)
	content.add_child(header)

	var accent_bar: = ColorRect.new()
	accent_bar.custom_minimum_size = Vector2(4.0, 25.0)
	accent_bar.color = accent
	accent_bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	header.add_child(accent_bar)

	var title: = Label.new()
	title.text = str(section["title"])
	title.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	title.add_theme_color_override("font_color", Color(0.92, 0.96, 1.0, 1.0))
	title.add_theme_font_size_override("font_size", 19)
	header.add_child(title)

	var separator: = HSeparator.new()
	separator.modulate = Color(accent.r, accent.g, accent.b, 0.38)
	content.add_child(separator)

	for item_value in section["items"]:
		var item: Array = item_value
		content.add_child(_make_royal_territory_war_row(str(item[0]), str(item[1]), accent))
	return card

func _make_royal_territory_war_row(label_text: String, value_text: String, accent: Color) -> HBoxContainer:
	var row: = HBoxContainer.new()
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_theme_constant_override("separation", 10)

	var label: = Label.new()
	label.text = label_text
	label.custom_minimum_size.x = 88.0
	label.add_theme_color_override("font_color", accent)
	label.add_theme_font_size_override("font_size", 14)
	row.add_child(label)

	var value: = Label.new()
	value.text = value_text
	value.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	value.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	value.add_theme_color_override("font_color", Color(0.84, 0.88, 0.92, 1.0))
	value.add_theme_font_size_override("font_size", 15)
	row.add_child(value)
	return row

func _build_shortcut_rows() -> void :
	for shortcut in SHORTCUTS:
		var row: = PanelContainer.new()
		row.custom_minimum_size.y = 44.0
		row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.mouse_filter = Control.MOUSE_FILTER_PASS
		_set_shortcut_row_hover(row, false)
		row.mouse_entered.connect(_set_shortcut_row_hover.bind(row, true))
		row.mouse_exited.connect(_set_shortcut_row_hover.bind(row, false))
		shortcut_rows.add_child(row)

		var layout: = HBoxContainer.new()
		layout.add_theme_constant_override("separation", 12)
		row.add_child(layout)

		var keys: = HBoxContainer.new()
		keys.custom_minimum_size.x = 142.0
		keys.add_theme_constant_override("separation", 5)
		layout.add_child(keys)
		for key_index in range(shortcut[0].size()):
			if key_index > 0:
				var plus: = Label.new()
				plus.text = "+"
				plus.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
				keys.add_child(plus)
			var key_text: String = shortcut[0][key_index]
			keys.add_child(_make_keycap(key_text, 52.0 if key_text in ["Ctrl", "Alt"] else 32.0))

		var description: = Label.new()
		description.text = shortcut[1]
		description.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		description.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		description.add_theme_font_size_override("font_size", 16)
		layout.add_child(description)

func _build_race_rows() -> void :
	var intro: = Label.new()
	intro.text = "同一列代表效果相同，排名由上至下依序降低。"
	intro.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	intro.add_theme_color_override("font_color", Color(0.72, 0.82, 0.9, 1.0))
	intro.add_theme_font_size_override("font_size", 15)
	race_rows.add_child(intro)

	race_rows.add_child(_make_race_section_title("配點加成效果"))
	for ranking in ATTRIBUTE_RANKINGS:
		race_rows.add_child(_make_ranking_card(ranking["name"], ranking["tiers"]))

	race_rows.add_child(_make_race_section_title("基礎戰鬥能力"))
	for ranking in ATTACK_SPEED_RANKINGS:
		race_rows.add_child(_make_ranking_card(ranking["name"], ranking["tiers"]))

func _make_race_section_title(title_text: String) -> Label:
	var label: = Label.new()
	label.text = title_text
	label.custom_minimum_size.y = 32.0
	label.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	label.add_theme_color_override("font_color", Color(0.82, 0.91, 0.98, 1.0))
	label.add_theme_font_size_override("font_size", 18)
	return label

func _make_ranking_card(stat_name: String, tiers: Array) -> PanelContainer:
	var card: = PanelContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var card_style: = StyleBoxFlat.new()
	card_style.bg_color = Color(0.1, 0.19, 0.31, 0.92)
	card_style.border_color = Color(0.37, 0.55, 0.72, 0.55)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(7)
	card_style.content_margin_left = 12.0
	card_style.content_margin_right = 12.0
	card_style.content_margin_top = 10.0
	card_style.content_margin_bottom = 10.0
	card.add_theme_stylebox_override("panel", card_style)

	var card_content: = VBoxContainer.new()
	card_content.add_theme_constant_override("separation", 7)
	card.add_child(card_content)

	var stat_label: = Label.new()
	stat_label.text = stat_name
	stat_label.add_theme_color_override("font_color", Color(0.9, 0.95, 1.0, 1.0))
	stat_label.add_theme_font_size_override("font_size", 17)
	card_content.add_child(stat_label)

	for tier_index in range(tiers.size()):
		card_content.add_child(_make_ranking_tier(tier_index, tiers[tier_index]))

	return card

func _make_ranking_tier(tier_index: int, race_names: Array) -> PanelContainer:
	var tier: = PanelContainer.new()
	tier.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var tier_color: Color = TIER_COLORS[min(tier_index, TIER_COLORS.size() - 1)]
	var tier_style: = StyleBoxFlat.new()
	tier_style.bg_color = Color(tier_color.r, tier_color.g, tier_color.b, 0.12)
	tier_style.border_color = Color(tier_color.r, tier_color.g, tier_color.b, 0.48)
	tier_style.set_border_width_all(1)
	tier_style.set_corner_radius_all(5)
	tier_style.content_margin_left = 7.0
	tier_style.content_margin_right = 9.0
	tier_style.content_margin_top = 5.0
	tier_style.content_margin_bottom = 5.0
	tier.add_theme_stylebox_override("panel", tier_style)

	var layout: = HBoxContainer.new()
	layout.add_theme_constant_override("separation", 10)
	tier.add_child(layout)

	var badge: = Label.new()
	badge.text = TIER_LABELS[min(tier_index, TIER_LABELS.size() - 1)]
	badge.custom_minimum_size.x = 48.0
	badge.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	badge.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	badge.add_theme_color_override("font_color", tier_color)
	badge.add_theme_font_size_override("font_size", 14)
	layout.add_child(badge)

	var races: = Label.new()
	races.text = " ＝ ".join(race_names)
	races.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	races.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	races.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	races.add_theme_font_size_override("font_size", 15)
	layout.add_child(races)

	return tier

func _build_job_rows() -> void :
	var intro: = Label.new()
	intro.text = "各職業的特色、可選種族與建議配點。"
	intro.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	intro.add_theme_color_override("font_color", Color(0.72, 0.82, 0.9, 1.0))
	intro.add_theme_font_size_override("font_size", 15)
	job_rows.add_child(intro)

	for job_info in JOB_INFOS:
		job_rows.add_child(_make_job_card(job_info))

func _make_job_card(job_info: Dictionary) -> PanelContainer:
	var card: = PanelContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var card_style: = StyleBoxFlat.new()
	card_style.bg_color = Color(0.1, 0.19, 0.31, 0.94)
	card_style.border_color = Color(0.37, 0.55, 0.72, 0.55)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(7)
	card_style.content_margin_left = 12.0
	card_style.content_margin_right = 12.0
	card_style.content_margin_top = 10.0
	card_style.content_margin_bottom = 10.0
	card.add_theme_stylebox_override("panel", card_style)

	var content: = VBoxContainer.new()
	content.add_theme_constant_override("separation", 8)
	card.add_child(content)

	var header: = HBoxContainer.new()
	header.add_theme_constant_override("separation", 8)
	content.add_child(header)

	var accent: = ColorRect.new()
	accent.custom_minimum_size = Vector2(4.0, 25.0)
	accent.color = Color(0.42, 0.72, 0.94, 1.0)
	accent.mouse_filter = Control.MOUSE_FILTER_IGNORE
	header.add_child(accent)

	var title: = Label.new()
	title.text = job_info["name"]
	title.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	title.add_theme_color_override("font_color", Color(0.9, 0.95, 1.0, 1.0))
	title.add_theme_font_size_override("font_size", 19)
	header.add_child(title)

	var description: = Label.new()
	description.text = job_info["description"]
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	description.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	description.add_theme_color_override("font_color", Color(0.88, 0.9, 0.93, 1.0))
	description.add_theme_font_size_override("font_size", 15)
	content.add_child(description)

	var separator: = HSeparator.new()
	separator.modulate = Color(0.65, 0.76, 0.86, 0.35)
	content.add_child(separator)

	var race_label: = Label.new()
	race_label.text = "可選種族"
	race_label.add_theme_color_override("font_color", Color(0.65, 0.82, 0.95, 1.0))
	race_label.add_theme_font_size_override("font_size", 14)
	content.add_child(race_label)

	var race_chips: = HFlowContainer.new()
	race_chips.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	race_chips.add_theme_constant_override("h_separation", 6)
	race_chips.add_theme_constant_override("v_separation", 6)
	content.add_child(race_chips)
	for race_name in job_info["races"]:
		race_chips.add_child(_make_job_race_chip(race_name))

	content.add_child(_make_stat_recommendation(job_info["stats"]))
	return card

func _make_job_race_chip(race_name: String) -> PanelContainer:
	var chip: = PanelContainer.new()
	var chip_style: = StyleBoxFlat.new()
	chip_style.bg_color = Color(0.16, 0.31, 0.47, 0.95)
	chip_style.border_color = Color(0.45, 0.68, 0.86, 0.7)
	chip_style.set_border_width_all(1)
	chip_style.set_corner_radius_all(10)
	chip_style.content_margin_left = 9.0
	chip_style.content_margin_right = 9.0
	chip_style.content_margin_top = 3.0
	chip_style.content_margin_bottom = 3.0
	chip.add_theme_stylebox_override("panel", chip_style)

	var label: = Label.new()
	label.text = race_name
	label.add_theme_font_size_override("font_size", 14)
	chip.add_child(label)
	return chip

func _make_stat_recommendation(recommendation: String) -> PanelContainer:
	var panel: = PanelContainer.new()
	panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var panel_style: = StyleBoxFlat.new()
	panel_style.bg_color = Color(0.29, 0.25, 0.12, 0.72)
	panel_style.border_color = Color(0.88, 0.67, 0.25, 0.6)
	panel_style.set_border_width_all(1)
	panel_style.set_corner_radius_all(5)
	panel_style.content_margin_left = 9.0
	panel_style.content_margin_right = 9.0
	panel_style.content_margin_top = 6.0
	panel_style.content_margin_bottom = 6.0
	panel.add_theme_stylebox_override("panel", panel_style)

	var layout: = VBoxContainer.new()
	layout.add_theme_constant_override("separation", 3)
	panel.add_child(layout)

	var heading: = Label.new()
	heading.text = "建議配點"
	heading.add_theme_color_override("font_color", Color(0.95, 0.76, 0.36, 1.0))
	heading.add_theme_font_size_override("font_size", 14)
	layout.add_child(heading)

	var value: = Label.new()
	value.text = recommendation
	value.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	value.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	value.add_theme_font_size_override("font_size", 15)
	layout.add_child(value)
	return panel

func _build_doll_categories() -> void :
	for doll_index in range(DOLL_INFOS.size()):
		var doll_info: Dictionary = DOLL_INFOS[doll_index]
		var item_id: = int(doll_info["item_id"])
		var entry: = _make_doll_category_entry(doll_index, item_id, doll_info["name"])
		doll_category_list.add_child(entry)
		_doll_category_entries.append(entry)

	if not DOLL_INFOS.is_empty():
		_select_doll(0)

func _make_doll_category_entry(doll_index: int, item_id: int, fallback_name: String) -> PanelContainer:
	var entry: = PanelContainer.new()
	entry.custom_minimum_size.y = 66.0
	entry.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	entry.mouse_filter = Control.MOUSE_FILTER_STOP
	entry.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	entry.gui_input.connect(_on_doll_category_input.bind(doll_index))
	entry.mouse_entered.connect(_on_doll_category_mouse_entered.bind(entry, doll_index, item_id))
	entry.mouse_exited.connect(_on_doll_category_mouse_exited.bind(entry, doll_index))
	_apply_doll_category_style(entry, false, false)

	var layout: = HBoxContainer.new()
	layout.mouse_filter = Control.MOUSE_FILTER_IGNORE
	layout.add_theme_constant_override("separation", 7)
	entry.add_child(layout)

	var visual: = _get_doll_item_visual(item_id)
	var icon: = TextureRect.new()
	icon.custom_minimum_size = Vector2(44.0, 44.0)
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.texture = visual["texture"]
	icon.material = visual["material"]
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	layout.add_child(icon)

	var label: = Label.new()
	label.text = str(ItemLoader.get_item_property(item_id, "item_name", fallback_name))
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.add_theme_font_size_override("font_size", 14)
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	layout.add_child(label)
	return entry

func _on_doll_category_input(event: InputEvent, doll_index: int) -> void :
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		_select_doll(doll_index)
		accept_event()

func _on_doll_category_mouse_entered(entry: PanelContainer, doll_index: int, item_id: int) -> void :
	_apply_doll_category_style(entry, doll_index == _selected_doll_index, true)
	_show_doll_item_tooltip(item_id)

func _on_doll_category_mouse_exited(entry: PanelContainer, doll_index: int) -> void :
	_apply_doll_category_style(entry, doll_index == _selected_doll_index, false)
	_hide_doll_item_tooltip()

func _select_doll(doll_index: int) -> void :
	if doll_index < 0 or doll_index >= DOLL_INFOS.size():
		return

	_hide_doll_item_tooltip()
	_selected_doll_index = doll_index
	for index in range(_doll_category_entries.size()):
		_apply_doll_category_style(_doll_category_entries[index], index == doll_index, false)

	var doll_info: Dictionary = DOLL_INFOS[doll_index]
	var rewards: Array = doll_info["rewards"]
	var doll_item_id: = int(doll_info["item_id"])
	doll_title.text = str(ItemLoader.get_item_property(doll_item_id, "item_name", doll_info["name"]))
	doll_reward_count.text = "共 %d 項" % rewards.size()

	for child in doll_reward_grid.get_children():
		child.free()

	for reward_id_value in rewards:
		var reward_id: = int(reward_id_value)
		var display_count: = int(DOLL_REWARD_COUNTS.get(reward_id, 1))
		doll_reward_grid.add_child(_make_doll_reward_card(reward_id, display_count))

	doll_reward_scroll.scroll_vertical = 0

func _make_doll_reward_card(item_id: int, display_count: int) -> PanelContainer:
	var card: = PanelContainer.new()
	card.custom_minimum_size = Vector2(160.0, 132.0)
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	card.mouse_filter = Control.MOUSE_FILTER_STOP
	card.mouse_default_cursor_shape = Control.CURSOR_HELP
	card.mouse_entered.connect(_on_doll_reward_mouse_entered.bind(card, item_id))
	card.mouse_exited.connect(_on_doll_reward_mouse_exited.bind(card))
	_apply_doll_reward_style(card, false)

	var content: = VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	content.add_theme_constant_override("separation", 4)
	card.add_child(content)

	var visual: = _get_doll_item_visual(item_id)
	var icon_center: = CenterContainer.new()
	icon_center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(icon_center)

	var icon: = TextureRect.new()
	icon.custom_minimum_size = Vector2(64.0, 64.0)
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.texture = visual["texture"]
	icon.material = visual["material"]
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon_center.add_child(icon)

	var name_label: = Label.new()
	name_label.text = str(ItemLoader.get_item_property(item_id, "item_name", "未知物品"))
	name_label.custom_minimum_size.y = 38.0
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.add_theme_font_size_override("font_size", 14)
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(name_label)

	if display_count > 1:
		var count_label: = Label.new()
		count_label.text = "×%d" % display_count
		count_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		count_label.add_theme_color_override("font_color", Color(0.95, 0.76, 0.36, 1.0))
		count_label.add_theme_font_size_override("font_size", 14)
		count_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		content.add_child(count_label)

	return card

func _on_doll_reward_mouse_entered(card: PanelContainer, item_id: int) -> void :
	_apply_doll_reward_style(card, true)
	_show_doll_item_tooltip(item_id)

func _on_doll_reward_mouse_exited(card: PanelContainer) -> void :
	_apply_doll_reward_style(card, false)
	_hide_doll_item_tooltip()

func _get_doll_item_visual(item_id: int) -> Dictionary:
	var raw_image_id: = int(ItemLoader.get_item_property(
		item_id, 
		"photo_id", 
		ItemLoader.get_item_property(item_id, "icon_id", 1)
	))
	var texture: = ItemLoader.get_photo(raw_image_id - 1)
	var color_index: = int(ItemLoader.get_item_property(item_id, "color_index", 0))
	var effect_type: = int(ItemLoader.get_item_property(item_id, "effect_type", 0))
	var material: Material = null
	if color_index > 0 or effect_type > 0:
		var shader_material: = ShaderMaterial.new()
		shader_material.shader = PALETTE_SHADER
		shader_material.set_shader_parameter("effect_type", effect_type)
		shader_material.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
		material = shader_material
	return {"texture": texture, "material": material}

func _show_doll_item_tooltip(item_id: int) -> void :
	if not GameManager or not GameManager.current_hud:
		return
	var shared_tooltip = GameManager.current_hud.get("shared_tooltip")
	if not shared_tooltip:
		return
	var item_category: = ItemLoader.get_item_category(item_id)
	var tab_type: = 3 if item_category in [3, 4] else item_category
	if tab_type not in [0, 1, 3]:
		tab_type = 0
	shared_tooltip.show_item(item_id, tab_type, {})

func _hide_doll_item_tooltip() -> void :
	if GameManager and GameManager.current_hud:
		var shared_tooltip = GameManager.current_hud.get("shared_tooltip")
		if shared_tooltip:
			shared_tooltip.hide_tooltip()

func _on_window_visibility_changed() -> void :
	if not visible:
		_hide_doll_item_tooltip()

func _apply_doll_category_style(entry: PanelContainer, selected: bool, hovered: bool) -> void :
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.23, 0.38, 0.58, 0.95) if selected or hovered else Color(0.11, 0.21, 0.34, 0.86)
	style.border_color = Color(0.55, 0.74, 0.9, 0.85) if selected else Color(0.35, 0.53, 0.69, 0.4)
	style.set_border_width_all(1)
	style.set_corner_radius_all(6)
	style.content_margin_left = 7.0
	style.content_margin_right = 7.0
	style.content_margin_top = 6.0
	style.content_margin_bottom = 6.0
	entry.add_theme_stylebox_override("panel", style)

func _apply_doll_reward_style(card: PanelContainer, hovered: bool) -> void :
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.18, 0.3, 0.45, 0.95) if hovered else Color(0.1, 0.19, 0.31, 0.92)
	style.border_color = Color(0.58, 0.75, 0.89, 0.85) if hovered else Color(0.37, 0.55, 0.72, 0.5)
	style.set_border_width_all(1)
	style.set_corner_radius_all(7)
	style.content_margin_left = 7.0
	style.content_margin_right = 7.0
	style.content_margin_top = 7.0
	style.content_margin_bottom = 7.0
	card.add_theme_stylebox_override("panel", style)

func _make_keycap(label_text: String, width: float) -> PanelContainer:
	var keycap: = PanelContainer.new()
	keycap.custom_minimum_size = Vector2(width, 29.0)
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.19, 0.3, 0.45, 1.0)
	style.border_color = Color(0.58, 0.72, 0.85, 0.8)
	style.set_border_width_all(1)
	style.set_corner_radius_all(5)
	keycap.add_theme_stylebox_override("panel", style)
	var label: = Label.new()
	label.text = label_text
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.add_theme_font_size_override("font_size", 15)
	keycap.add_child(label)
	return keycap

func _set_shortcut_row_hover(row: PanelContainer, hovered: bool) -> void :
	var style: = StyleBoxFlat.new()
	style.bg_color = Color(0.18, 0.29, 0.44, 0.9) if hovered else Color(0.12, 0.21, 0.34, 0.8)
	style.content_margin_left = 10.0
	style.content_margin_right = 10.0
	style.content_margin_top = 6.0
	style.content_margin_bottom = 6.0
	style.set_corner_radius_all(5)
	row.add_theme_stylebox_override("panel", style)

func _apply_button_style(button: Button, selected: bool) -> void :
	var normal: = StyleBoxFlat.new()
	normal.bg_color = Color(0.23624733, 0.3779558, 0.57560074, 0.9019608) if selected else Color(0.15013191, 0.2537205, 0.40338945, 0.9019608)
	normal.set_corner_radius_all(5)
	var hover: = normal.duplicate() as StyleBoxFlat
	hover.bg_color = Color(0.23624733, 0.3779558, 0.57560074, 0.9019608)
	button.add_theme_stylebox_override("normal", normal)
	button.add_theme_stylebox_override("hover", hover)
	button.add_theme_stylebox_override("pressed", hover)
