
extends CanvasLayer


@onready var chat_panel: Control = $ChatPanel


@onready var sys_msg_scroll_box: ScrollContainer = $SysMsgScrollBox
@onready var sys_msg_container: VBoxContainer = $SysMsgScrollBox / SysMsgContainer

@onready var minimap_panel: Control = $MiniMapPanel
@onready var map_name_label: Label = $MiniMapPanel / MapNameLabel
@onready var coords_label: Label = $MiniMapPanel / CoordsLabel
@onready var btn_big_map: TextureButton = $MiniMapPanel / BtnBigMap
@onready var btn_world_map: TextureButton = $MiniMapPanel / BtnWorldMap
@onready var minimap_viewport_container: SubViewportContainer = $MiniMapPanel / ViewportContainer
@onready var minimap_viewport: SubViewport = $MiniMapPanel / ViewportContainer / SubViewport
@onready var minimap_camera: Camera2D = $MiniMapPanel / ViewportContainer / SubViewport / MiniMapCamera
@onready var minimap_npc_overlay: Control = $MiniMapPanel / NpcOverlay

@onready var shared_tooltip: Control = $SharedTooltip


@onready var status_panel: Control = $StatusPanel
@onready var lbl_level: Label = $StatusPanel / LblLevel
@onready var lbl_exp_percentage: Label = $StatusPanel / LblExpPercentage
@onready var lbl_hp: Label = $StatusPanel / LblHP
@onready var lbl_mp: Label = $StatusPanel / LblMP
@onready var lbl_will: Label = $StatusPanel / LblWill
@onready var lbl_money: Label = $StatusPanel / LblMoney

@onready var bar_hp: ProgressBar = $StatusPanel / BarHP
@onready var bar_mp: ProgressBar = $StatusPanel / BarMP
@onready var bar_will: ProgressBar = $StatusPanel / BarWill
@onready var bar_exp: ProgressBar = $StatusPanel / BarExp

@onready var emotion_panel: Control = $EmotionPanel
var _emotion_tween: Tween

@onready var menu_panel: Control = $MenuPanel
@onready var btn_character: Button = $MenuPanel / BtnCharacter
@onready var btn_inventory: Button = $MenuPanel / BtnInventory
@onready var btn_skill: Button = $MenuPanel / BtnSkill
@onready var btn_party: Button = $MenuPanel / BtnParty
@onready var btn_clan: Button = $MenuPanel / BtnClan
@onready var btn_shop: Button = $MenuPanel / BtnShop
@onready var btn_quest: Button = $MenuPanel / BtnQuest
@onready var btn_mall: Button = $MenuPanel / BtnMall
@onready var btn_settings: Button = $MenuPanel / BtnSettings
@onready var btn_announcement: TextureButton = $BtnAnnouncement
@onready var btn_game_info: TextureButton = $BtnGameInfo
@onready var btn_craft_dismantle: TextureButton = $BtnCraftDismantle
@onready var btn_afk_mode: Button = $BtnAfkMode
@onready var afk_overlay: Control = $AFKOverlay
@onready var afk_status: Label = $AFKOverlay / MonitorCard / LblStatus
@onready var afk_character: Label = $AFKOverlay / MonitorCard / LblCharacter
@onready var afk_vitals: Label = $AFKOverlay / MonitorCard / LblVitals
@onready var afk_position: Label = $AFKOverlay / MonitorCard / LblPosition
@onready var afk_runtime: Label = $AFKOverlay / MonitorCard / LblRuntime
@onready var afk_resource_stats: Label = $AFKOverlay / MonitorCard / LblResources
@onready var afk_btn_restore: Button = $AFKOverlay / MonitorCard / BtnRestore
@onready var afk_restore_progress: SmoothLoadingBar = $AFKOverlay / MonitorCard / RestoreProgress
@onready var afk_update_timer: Timer = $AFKOverlay / UpdateTimer

@onready var windows_container: Control = $WindowsContainer
@onready var character_window: Control = $WindowsContainer / CharacterWindow
@onready var inventory_window: Control = $WindowsContainer / InventoryWindow
@onready var skill_window: Control = $WindowsContainer / SkillWindow
@onready var party_window: Control = $WindowsContainer / PartyWindow
@onready var clan_window: Control = $WindowsContainer / ClanWindow
@onready var settings_window: Control = $WindowsContainer / SettingsWindow
@onready var option_pet_mode: Control = $WindowsContainer / SettingsWindow / OptionPetMode
@onready var option_volume_bg: Control = $WindowsContainer / SettingsWindow / OptionVolumeBg
@onready var option_volume_effect: Control = $WindowsContainer / SettingsWindow / OptionVolumeEffect
@onready var option_auto_saint: Control = $WindowsContainer / SettingsWindow / OptionAutoSaint
@onready var option_fps_limit: Control = $WindowsContainer / SettingsWindow / OptionFpsLimit
@onready var option_performance_load: Control = $WindowsContainer / SettingsWindow / OptionPerformanceLoad
@onready var option_combat_render: Control = $WindowsContainer / SettingsWindow / OptionCombatRender
@onready var option_simplify_players: Control = $WindowsContainer / SettingsWindow / OptionSimplifyPlayers
@onready var option_simplify_monsters: Control = $WindowsContainer / SettingsWindow / OptionSimplifyMonsters
var mall_window: Control = null
var announcement_window: Control = null
var game_info_window: Control = null


@onready var npc_dialog_panel: Control = $WindowsContainer / NpcDialogPanel
@onready var npc_dialog_text: RichTextLabel = $WindowsContainer / NpcDialogPanel / QuestionLabel
const LEGACY_DIALOG_SEQUENCE = preload("res://scripts/legacy_dialog_sequence.gd")
const LEGACY_CINEMATIC_PANEL = preload("res://scripts/legacy_cinematic_panel.gd")
var _legacy_dialog_sequence = LEGACY_DIALOG_SEQUENCE.new()
var _legacy_dialog_line_waiting: bool = false
var _legacy_cinematic_panel: Variant = null
var _current_npc_entity_id: int = 0
var _current_npc_script_name: String = ""
var _next_dialog_node: String = ""

var _window_map: Dictionary = {}
var _window_tweens: Dictionary = {}

const WINDOW_DRAG_HEADER_HEIGHT: = 30.0

var _draggable_window_defaults: Dictionary = {}
var _dragged_window: Control = null
var _window_drag_pointer_offset: = Vector2.ZERO

@onready var shortcut_bar: Control = $ShortcutBar


@onready var loading_overlay: CanvasLayer = %LoadingOverlay
@onready var loading_bar: SmoothLoadingBar = %LoadingOverlay / LoadingBar
var _map_loading_tween: Tween


const BANK_WINDOW_SCENE = preload("res://bank_window.tscn")
var current_bank_window: Control = null

const REPAIR_WINDOW_SCENE = preload("res://repair_window.tscn")
var current_repair_window: Control = null

const ITEM_CHOICE_WINDOW_SCENE = preload("res://item_choice_window.tscn")
var current_item_choice_window: Control = null


@onready var invite_box: Control = $InviteBox
@onready var lbl_message: Label = $InviteBox / LblMessage
@onready var lbl_timer: Label = $InviteBox / LblTimer
@onready var btn_accept: TextureButton = $InviteBox / BtnAccept
@onready var btn_decline: TextureButton = $InviteBox / BtnDecline


const NPC_SHOP_WINDOW_SCENE = preload("res://npc_shop_window.tscn")
var current_npc_shop_window: Control = null

@onready var pet_status_scroll: ScrollContainer = $WindowsContainer / PetStatusScroll
@onready var pet_status_container: VBoxContainer = $WindowsContainer / PetStatusScroll / PetStatusContainer
const PET_STATUS_ROW_SCENE = preload("res://pet_status_row.tscn")


@onready var message_box: Panel = $MessageBox
@onready var msg_box_label: Label = $MessageBox / LblMessage
@onready var msg_box_btn_confirm: BaseButton = $MessageBox / HBoxContainer / BtnConfirm
@onready var msg_box_btn_cancel: BaseButton = $MessageBox / HBoxContainer / BtnCancel

@onready var input_box: Control = $InputBox
@onready var input_box_label: Label = $InputBox / LblTitle
@onready var input_box_line_edit: LineEdit = $InputBox / InputLineEdit
@onready var input_box_btn_confirm: BaseButton = $InputBox / BtnConfirm
@onready var input_box_btn_cancel: BaseButton = $InputBox / BtnCancel

@onready var spin_input_box: Control = $SpinInputBox
@onready var spin_input_box_label: Label = $SpinInputBox / LblTitle
@onready var spin_input_box_input: SpinBox = $SpinInputBox / InputSpinBox
@onready var spin_input_box_btn_confirm: BaseButton = $SpinInputBox / BtnConfirm
@onready var spin_input_box_btn_cancel: BaseButton = $SpinInputBox / BtnCancel

var quest_window: Control = null
@onready var quest_tracker_panel: Panel = $WindowsContainer / QuestTrackerPanel

var _invite_time_left: float = 0.0
var _invite_timeout_callback: Callable
var _last_invite_display_second: int = -1

const PLAYER_CONTEXT_MENU_SCENE = preload("res://player_context_menu.tscn")
const TRADE_WINDOW_SCENE = preload("res://trade_window.tscn")

var current_trade_window: Control = null
var context_menu_instance: Control = null

const SHOP_WINDOW_SCENE = preload("res://shop_window.tscn")
var current_shop_window: Control = null

@onready var buff_grid: GridContainer = $BuffGrid
const BUFF_ICON_SCENE = preload("res://buff_icon.tscn")

const MINIMAP_UPDATE_INTERVAL: = 0.05
const MINIMAP_WORLD_CULL_MASK: = 1 << 0
# The SubViewport draws only the static map layer; players/NPCs are
# separately drawn by minimap_npc_overlay. Avoid re-rendering that map
# 20 times/sec while the camera is stationary.
const MINIMAP_IDLE_WORLD_REDRAW_INTERVAL: float = 1.0
var _minimap_update_elapsed: = 0.0
var _minimap_world_idle_elapsed: float = 0.0
var _minimap_force_world_redraw: bool = true
var _minimap_world_redraw_requests: int = 0
var _minimap_world_redraw_skips: int = 0


static func should_redraw_minimap_world(camera_changed: bool, forced: bool, idle_seconds: float) -> bool:
	return camera_changed or forced or idle_seconds >= MINIMAP_IDLE_WORLD_REDRAW_INTERVAL


func get_minimap_world_redraw_stats() -> Dictionary:
	return {
		"redraws": _minimap_world_redraw_requests,
		"skipped": _minimap_world_redraw_skips,
	}


const SHORTCUT_SLOT_SCENE = preload("res://shortcut_slot.tscn")

const HORSE_PREVIEW_WINDOW_SCENE = preload("res://horse_preview_window.tscn")
var current_horse_preview_window: Control = null

const PET_WINDOW_SCENE = preload("res://pet_window.tscn")
var current_pet_window: Control = null

const RANK_WINDOW_SCENE = preload("res://rank_window.tscn")
var current_rank_window: Control = null

var craft_dismantle_window: Control = null
var _craft_window_initial_opening: bool = false
var big_map_window: Control = null
var world_map_window: Control = null

const LAZY_WINDOW_PATHS: Dictionary = {
	"quest": "res://quest_window.tscn",
	"mall": "res://mall_window.tscn",
	"announcement": "res://announcement_window.tscn",
	"game_info": "res://game_info_window.tscn",
	"craft": "res://craft_dismantle_window.tscn",
	"big_map": "res://big_map_window.tscn",
	"world_map": "res://world_map_window.tscn",
}
var _lazy_window_scenes: Dictionary = {}
var _lazy_window_requests: Dictionary = {}

const BUBBLE_TEXT_MARGINS = {
	0: {"left": 10, "top": 8, "right": 10, "bottom": 23}, 
	1: {"left": 18, "top": 25, "right": 18, "bottom": 25}, 
	2: {"left": 30, "top": 34, "right": 30, "bottom": 34}, 
	3: {"left": 28, "top": 20, "right": 15, "bottom": 34}, 
	4: {"left": 32, "top": 17, "right": 34, "bottom": 45}, 
	5: {"left": 30, "top": 15, "right": 30, "bottom": 46}, 
	6: {"left": 10, "top": 10, "right": 10, "bottom": 10}
}

const RACE_PREFIX_MAP_ZH = {"Boy": "[騎]", "Girl": "[騎]", "Migu": "[魔]", "Robot": "[機]", "Yas": "[遺]"}

const REAL_MAP_NAMES = {
	1: "黑暗森林", 2: "星刃廢墟", 4: "神團聖殿", 7: "古尼爾森林", 9: "魔法之都．古尼爾", 11: "獅鷲山洞", 
	12: "獅鷲山", 13: "尼西亞台地", 14: "", 15: "首都．尼尼亞皇城", 17: "機械之城．絲蘭", 18: "", 
	19: "尼尼亞盆地", 20: "", 21: "", 22: "騎士之城．格靈", 25: "格靈森林", 27: "流預之河．上游", 28: "星河地洞", 
	29: "流星之河．下游", 31: "星之湖", 32: "星湖迷宮", 33: "地下神殿", 34: "灼熱沙漠", 
	38: "絲蘭森林", 39: "櫻花之城．白雲", 40: "初心者之間", 42: "孤寂山區", 43: "戰爭平原", 44: "饑餓荒地", 
	45: "冰花山谷", 46: "火山島基地", 47: "尼尼亞火車站", 49: "天空島", 51: "遺忘者森林", 52: "死亡峽谷", 
	53: "遺忘者之鎮．艾瑞爾", 65: "死寂仙境", 66: "龍之洞", 67: "皇家領地戰", 68: "星之道", 73: "星震之遺跡—海寧", 74: "", 
	75: "佳雅聖城", 76: "星之門", 77: "地下城．奧哈瑪", 78: "荒塚之地", 79: "詛咒丘陵", 86: "絕望崖", 
	93: "幻境之城", 94: "淨靈公園", 99: "逆寒沙漠", 100: "地下城．艾格斯"
}

var target_player: CharacterBody2D = null
var _last_displayed_coords: Variant = null
var _is_hud_hidden: bool = false
var _hud_tween: Tween
var _sys_msg_tween: Tween
var current_pet_combat_mode: int = 1
var current_fps_limit: int = GameConstants.SETTING_DEFAULT_FOREGROUND_FPS_LIMIT
var current_performance_load_mode: int = GameConstants.SETTING_DEFAULT_PERFORMANCE_LOAD_MODE
var current_combat_render_mode: int = GameConstants.SETTING_DEFAULT_COMBAT_RENDER_MODE
var current_simplify_players_mode: int = GameConstants.SETTING_DEFAULT_SIMPLIFY_PLAYERS_MODE
var current_simplify_monsters_mode: int = GameConstants.SETTING_DEFAULT_SIMPLIFY_MONSTERS_MODE
var _afk_hidden_roots: Array[Dictionary] = []
var _afk_entered_msec: int = 0


func _ready() -> void :
	if loading_overlay: loading_overlay.visible = false

	var lbl_sys_msg_node = chat_panel.get_node_or_null("LblSystemMsg") if chat_panel else null
	if lbl_sys_msg_node:
		lbl_sys_msg_node.visible = false
		lbl_sys_msg_node.modulate.a = 0.0

	if invite_box: invite_box.visible = false
	if message_box: message_box.visible = false
	if afk_overlay: afk_overlay.visible = false
	if btn_afk_mode:
		btn_afk_mode.focus_mode = Control.FOCUS_NONE
		btn_afk_mode.pressed.connect(_on_afk_mode_requested)
	if afk_btn_restore:
		afk_btn_restore.pressed.connect(_on_afk_restore_requested)
	if afk_update_timer:
		afk_update_timer.timeout.connect(_refresh_afk_dashboard)
	if input_box:
		input_box.visible = false
		if input_box_line_edit:
			input_box_line_edit.text_submitted.connect( func(_text):
				if input_box.visible and not input_box_btn_confirm.disabled:
					input_box_btn_confirm.pressed.emit()
			)
	if spin_input_box:
		spin_input_box.visible = false
		if spin_input_box_input:
			spin_input_box_input.get_line_edit().text_submitted.connect( func(_text):
				if spin_input_box.visible and not spin_input_box_btn_confirm.disabled:
					spin_input_box_btn_confirm.pressed.emit()
			)


	if chat_panel:
		if chat_panel.has_signal("chat_message_sent"):
			chat_panel.chat_message_sent.connect(_on_chat_panel_message_sent)

		var btn_toggle_emotion = chat_panel.get_node_or_null("BtnToggleEmotion")
		if btn_toggle_emotion:
			btn_toggle_emotion.pressed.connect(_on_btn_toggle_emotion_pressed)
			btn_toggle_emotion.focus_mode = Control.FOCUS_NONE

	if btn_shop:
		btn_shop.pressed.connect(_on_btn_shop_pressed)
		btn_shop.focus_mode = Control.FOCUS_NONE

	if btn_mall:
		btn_mall.pressed.connect(_on_btn_mall_pressed)
		btn_mall.focus_mode = Control.FOCUS_NONE

	if btn_big_map and not btn_big_map.pressed.is_connected(_toggle_big_map):
		btn_big_map.pressed.connect(_toggle_big_map)
	if btn_world_map and not btn_world_map.pressed.is_connected(_toggle_world_map):
		btn_world_map.pressed.connect(_toggle_world_map)

	if emotion_panel:
		emotion_panel.emotion_selected.connect(_on_player_emotion_selected)
		emotion_panel.visible = false

	if npc_dialog_panel:
		npc_dialog_panel.visible = false
		if npc_dialog_panel.has_signal("option_selected") and not npc_dialog_panel.option_selected.is_connected(_on_npc_option_selected):
			npc_dialog_panel.option_selected.connect(_on_npc_option_selected)

	if option_volume_bg:
		option_volume_bg.gui_input.connect(_on_option_gui_input.bind(option_volume_bg))

	if option_volume_effect:
		option_volume_effect.gui_input.connect(_on_option_gui_input.bind(option_volume_effect))

	_setup_window_manager()
	_setup_header_button_hover_signals()
	_setup_menu_tooltips()

	if chat_panel and chat_panel.has_method("focus_input"):
		chat_panel.focus_input()

	_init_shortcut_bar()

	if NetworkManager:
		if NetworkManager.has_signal("change_map_received"):
			if not NetworkManager.change_map_received.is_connected(_on_big_map_change_started):
				NetworkManager.change_map_received.connect(_on_big_map_change_started)
		if NetworkManager.has_signal("inventory_synced"): NetworkManager.inventory_synced.connect(_on_inventory_synced_hud)
		if NetworkManager.has_signal("equip_synced"): NetworkManager.equip_synced.connect(_on_equip_synced_hud)
		if NetworkManager.has_signal("pet_synced"): NetworkManager.pet_synced.connect(_on_pet_synced_hud)
		if NetworkManager.has_signal("horse_synced"): NetworkManager.horse_synced.connect(_on_horse_synced_hud)

		if NetworkManager.has_signal("npc_dialog_received"):
			if not NetworkManager.npc_dialog_received.is_connected(_on_npc_dialog_received):
				NetworkManager.npc_dialog_received.connect(_on_npc_dialog_received)
		if NetworkManager.has_signal("npc_dialog_close_packet"):
			if not NetworkManager.npc_dialog_close_packet.is_connected(_on_npc_dialog_close_requested):
				NetworkManager.npc_dialog_close_packet.connect(_on_npc_dialog_close_requested)
		elif NetworkManager.has_signal("npc_dialog_closed"):
			if not NetworkManager.npc_dialog_closed.is_connected(_on_npc_dialog_close_requested):
				NetworkManager.npc_dialog_closed.connect(_on_npc_dialog_close_requested)

		if NetworkManager.has_signal("open_bank_window_received"):
			if not NetworkManager.open_bank_window_received.is_connected(_on_open_bank_window_received):
				NetworkManager.open_bank_window_received.connect(_on_open_bank_window_received)

		if NetworkManager.has_signal("open_repair_window_received"):
			if not NetworkManager.open_repair_window_received.is_connected(_on_open_repair_window_received):
				NetworkManager.open_repair_window_received.connect(_on_open_repair_window_received)

		if NetworkManager.has_signal("open_rank_window_received"):
			if not NetworkManager.open_rank_window_received.is_connected(_on_open_rank_window_received):
				NetworkManager.open_rank_window_received.connect(_on_open_rank_window_received)
		if not NetworkManager.announcement_list_received.is_connected(_on_announcement_list_received):
			NetworkManager.announcement_list_received.connect(_on_announcement_list_received)

		if NetworkManager:
			NetworkManager.trade_invite_received.connect(_on_trade_invite_received)
			NetworkManager.trade_start_received.connect(_on_trade_start_received)

		if NetworkManager.has_signal("club_invite_received"):
			if not NetworkManager.club_invite_received.is_connected(_on_club_invite_received):
				NetworkManager.club_invite_received.connect(_on_club_invite_received)
		if NetworkManager.has_signal("open_create_club_window_received"):
			NetworkManager.open_create_club_window_received.connect(_on_open_create_club_window_received)

		if NetworkManager.has_signal("shop_view_response_received"):
			NetworkManager.shop_view_response_received.connect(_on_shop_view_response)
		if NetworkManager.has_signal("shop_sync_received"):
			NetworkManager.shop_sync_received.connect(_on_shop_sync_received)
		if NetworkManager.has_signal("npc_shop_view_response_received"):
			NetworkManager.npc_shop_view_response_received.connect(_on_npc_shop_view_response_received)
		if NetworkManager.has_signal("chat_broadcast_received"):
			if not NetworkManager.chat_broadcast_received.is_connected(_on_chat_broadcast_received):
				NetworkManager.chat_broadcast_received.connect(_on_chat_broadcast_received)

		if NetworkManager.has_signal("mall_list_received"):
			NetworkManager.mall_list_received.connect(_on_mall_list_received)
		if NetworkManager.has_signal("mall_points_sync_received"):
			NetworkManager.mall_points_sync_received.connect(_on_mall_points_sync_received)
		if NetworkManager.has_signal("paypal_donation_created_received"):
			NetworkManager.paypal_donation_created_received.connect(_on_paypal_donation_created_received)
		if NetworkManager.has_signal("legacy_point_withdrawal_response_received"):
			NetworkManager.legacy_point_withdrawal_response_received.connect(_on_legacy_point_withdrawal_response_received)

		if NetworkManager.has_signal("system_message_flash_received"):
			if not NetworkManager.system_message_flash_received.is_connected(show_system_message_flash):
				NetworkManager.system_message_flash_received.connect(show_system_message_flash)
		if NetworkManager.has_signal("item_pickup_feedback_received"):
			if not NetworkManager.item_pickup_feedback_received.is_connected(_on_item_pickup_feedback_received):
				NetworkManager.item_pickup_feedback_received.connect(_on_item_pickup_feedback_received)

		if NetworkManager.has_signal("item_choice_required_received"):
			if not NetworkManager.item_choice_required_received.is_connected(_on_item_choice_required_received):
				NetworkManager.item_choice_required_received.connect(_on_item_choice_required_received)

		if NetworkManager.has_signal("system_message_chat_received"):
			if not NetworkManager.system_message_chat_received.is_connected(show_system_message_chat):
				NetworkManager.system_message_chat_received.connect(show_system_message_chat)

		if NetworkManager.has_signal("open_megaphone_window_received"):
			if not NetworkManager.open_megaphone_window_received.is_connected(_on_open_megaphone_window_received):
				NetworkManager.open_megaphone_window_received.connect(_on_open_megaphone_window_received)

		if NetworkManager.has_signal("toggle_auto_saint_response_received"):
			if not NetworkManager.toggle_auto_saint_response_received.is_connected(_on_toggle_auto_saint_response_received):
				NetworkManager.toggle_auto_saint_response_received.connect(_on_toggle_auto_saint_response_received)

		if NetworkManager.has_signal("system_message_broadcast_received"): NetworkManager.system_message_broadcast_received.connect(show_system_message_broadcast)
		if NetworkManager.has_signal("party_invite_received"): NetworkManager.party_invite_received.connect(_on_party_invite_received)
		if NetworkManager.has_signal("skill_synced"): NetworkManager.skill_synced.connect(_on_skill_synced_hud)
		if NetworkManager.has_signal("buffs_synced"): NetworkManager.buffs_synced.connect(_on_sync_buffs_received)
		if NetworkManager.has_signal("quest_synced"): NetworkManager.quest_synced.connect(_on_quest_updated_hud)
		if NetworkManager.has_signal("quest_updated"): NetworkManager.quest_updated.connect(_on_quest_updated_hud)


func _on_chat_panel_message_sent(channel: int, message: String, bubble_type: int) -> void :

	if channel == 0 and not message.begins_with("/"):
		if is_instance_valid(target_player) and target_player.has_method("show_chat_bubble"):
			var margins = BUBBLE_TEXT_MARGINS.get(bubble_type, {"left": 0, "top": 0, "right": 0, "bottom": 0})
			target_player.show_chat_bubble(message, bubble_type, margins)

	if NetworkManager:
		if NetworkManager.has_method("send_secure_packet"):
			NetworkManager.send_secure_packet({"Type": "ChatMessage", "Channel": channel, "Message": message, "BubbleType": bubble_type})


func _on_item_choice_required_received(
	item_uid: int,
	_item_index: int,
	title: String,
	options: Array
) -> void:
	if item_uid <= 0 or options.is_empty():
		show_rejection_message_box("目前沒有可用的獎勵選項。")
		return

	if is_instance_valid(current_item_choice_window):
		current_item_choice_window.queue_free()

	current_item_choice_window = ITEM_CHOICE_WINDOW_SCENE.instantiate()
	if windows_container:
		windows_container.add_child(current_item_choice_window)
	else:
		add_child(current_item_choice_window)

	if current_item_choice_window.has_method("setup"):
		current_item_choice_window.setup(item_uid, title, options)
	current_item_choice_window.move_to_front()
	_play_interface_activation_feedback(current_item_choice_window)


func _on_open_bank_window_received() -> void :
	show_bank_window()

func show_bank_window() -> void :
	if has_node("WindowsContainer/NpcDialogPanel"):
		$WindowsContainer / NpcDialogPanel.visible = false

	if is_instance_valid(current_bank_window):
		return

	current_bank_window = BANK_WINDOW_SCENE.instantiate()
	if windows_container:
		windows_container.add_child(current_bank_window)
	else:
		add_child(current_bank_window)

	if current_bank_window.has_node("BtnClose"):
		current_bank_window.get_node("BtnClose").pressed.connect(_close_bank_window)

	if is_instance_valid(target_player):
		if current_bank_window.has_method("setup"):
			current_bank_window.setup(target_player)
		if target_player.has_method("set_movement_locked"):
			target_player.set_movement_locked(true)
	_play_interface_activation_feedback(current_bank_window)

func _close_bank_window() -> void :
	if is_instance_valid(current_bank_window):
		current_bank_window.queue_free()
		current_bank_window = null

	if is_instance_valid(target_player) and target_player.has_method("set_movement_locked"):
		target_player.set_movement_locked(false)


func show_system_message_broadcast(message_content: String) -> void :
	if message_content == "" or not sys_msg_container:
		return


	var msg_label = RichTextLabel.new()
	msg_label.bbcode_enabled = true
	msg_label.text = message_content


	msg_label.mouse_filter = Control.MOUSE_FILTER_IGNORE


	msg_label.add_theme_color_override("default_color", Color(1.0, 1.0, 1.0, 1.0))
	msg_label.add_theme_color_override("font_outline_color", Color(0.118, 0.118, 0.118, 1.0))
	msg_label.add_theme_constant_override("outline_size", 3)
	msg_label.add_theme_font_size_override("normal_font_size", 15)


	msg_label.fit_content = true
	msg_label.scroll_active = false
	msg_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	msg_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	msg_label.modulate.a = 0.0

	sys_msg_container.add_child(msg_label)

	await get_tree().process_frame
	if sys_msg_scroll_box:
		var v_scroll = sys_msg_scroll_box.get_v_scroll_bar()
		if v_scroll:
			v_scroll.value = v_scroll.max_value

	var fade_in_tween = create_tween()
	fade_in_tween.tween_property(msg_label, "modulate:a", 1.0, 0.3)

	var life_tween = create_tween()
	life_tween.tween_interval(118.0)
	life_tween.tween_property(msg_label, "modulate:a", 0.0, 2.0)
	life_tween.tween_callback( func():
		if is_instance_valid(msg_label):
			msg_label.queue_free()
	)


func _on_inventory_synced_hud(_items: Array, _max_size: int) -> void :
	_refresh_inventory_if_open()
	update_shortcut_amounts()
	_refresh_engraving_requirements_if_visible()

func _on_equip_synced_hud(_equips: Array, _max_size: int) -> void :
	_refresh_inventory_if_open()
	_refresh_engraving_requirements_if_visible()

func _refresh_engraving_requirements_if_visible() -> void :
	if game_info_window and game_info_window.visible and game_info_window.has_method("refresh_engraving_requirements"):
		game_info_window.call("refresh_engraving_requirements")

func _on_pet_synced_hud(_pets: Array, _max_size: int) -> void :
	_refresh_inventory_if_open()
	_load_shortcuts_from_data()
	update_pet_status_panel()

func _on_horse_synced_hud(_horses: Array, _equips: Array, _max_size: int) -> void :
	_refresh_inventory_if_open()

func _on_skill_synced_hud(skills: Array) -> void :
	if is_instance_valid(target_player) and "data_model" in target_player:
		target_player.data_model.update_skills(skills)

	if skill_window and skill_window.visible and skill_window.has_method("update_ui"):
		skill_window.update_ui()

const REJECTION_FEEDBACK_SE_ID: int = 97
# SE0064 is a skill-hit sound. SE0066 is the short, heavier normal-effect cue used for pickup feedback.
const ITEM_PICKUP_SE_ID: int = 97
const PARTY_INVITATION_SE_ID: int = 129

func show_rejection_message_flash(msg: String) -> void :
	show_system_message_flash(msg)
	_play_rejection_feedback_se()

func _on_item_pickup_feedback_received(data: Dictionary) -> void :
	var message: String = str(data.get("Message", ""))
	if not message.is_empty():
		show_system_message_flash(message)

	var play_sound: bool = bool(data.get("PlaySound", true))
	if play_sound and AudioManager:
		AudioManager.play_se(ITEM_PICKUP_SE_ID)

func show_system_message_flash(msg: String) -> void :
	var lbl_sys_msg_node = chat_panel.get_node_or_null("LblSystemMsg") if chat_panel else null
	if not lbl_sys_msg_node: return
	lbl_sys_msg_node.text = msg
	lbl_sys_msg_node.visible = true
	lbl_sys_msg_node.modulate.a = 1.0

	if _sys_msg_tween and _sys_msg_tween.is_running():
		_sys_msg_tween.kill()

	_sys_msg_tween = create_tween()
	_sys_msg_tween.tween_interval(5.0)
	_sys_msg_tween.tween_property(lbl_sys_msg_node, "modulate:a", 0.0, 1.0)
	_sys_msg_tween.tween_callback( func(): lbl_sys_msg_node.visible = false)

func show_system_message_chat(content: String) -> void :
	if chat_panel and chat_panel.has_method("add_system_message"):
		chat_panel.add_system_message(content)

func _refresh_inventory_if_open() -> void :
	if inventory_window and inventory_window.visible:
		if inventory_window.has_method("update_ui"):
			inventory_window.update_ui()

	if is_instance_valid(current_bank_window) and current_bank_window.visible:
		if current_bank_window.has_method("update_ui"):
			current_bank_window.update_ui()

	if is_instance_valid(current_shop_window) and current_shop_window.visible:
		if current_shop_window.has_method("update_ui"):
			current_shop_window.update_ui()

	if is_instance_valid(current_npc_shop_window) and current_npc_shop_window.visible:
		if current_npc_shop_window.has_method("update_ui"):
			current_npc_shop_window.update_ui()


	if is_instance_valid(current_repair_window) and current_repair_window.visible:
		if current_repair_window.has_method("update_ui"):
			current_repair_window.update_ui()

	update_status_panel()

func _on_chat_broadcast_received(entity_id: int, sender_name: String, race_id: int, channel: int, message: String, bubble_type: int) -> void :
	var race_name = "Boy"
	match race_id:
		0: race_name = "Boy"
		1: race_name = "Girl"
		2: race_name = "Migu"
		3: race_name = "Robot"
		4: race_name = "Yas"

	var race_prefix = RACE_PREFIX_MAP_ZH.get(race_name, "[?]")

	if chat_panel and chat_panel.has_method("add_chat_message"):
		chat_panel.add_chat_message(channel, race_prefix + sender_name, message)

	if channel == 0:
		if NetworkEntityManager:
			if "active_entities" in NetworkEntityManager and NetworkEntityManager.active_entities.has(entity_id):
				var entity = NetworkEntityManager.active_entities[entity_id]
				if is_instance_valid(entity) and entity.has_method("show_chat_bubble"):
					var margins = BUBBLE_TEXT_MARGINS.get(bubble_type, BUBBLE_TEXT_MARGINS[0])
					entity.show_chat_bubble(message, bubble_type, margins)

func _setup_window_manager() -> void :
	if windows_container:
		windows_container.visible = true
		windows_container.modulate.a = 1.0

	_window_map = {
		btn_character: character_window,
		btn_inventory: inventory_window,
		btn_skill: skill_window,
		btn_party: party_window,
		btn_clan: clan_window,
		btn_settings: settings_window,
	}

	for btn in _window_map.keys():
		if btn:
			btn.pressed.connect(_toggle_window.bind(_window_map[btn]))
			btn.focus_mode = Control.FOCUS_NONE

	for win in _window_map.values():
		if win:
			win.visible = false
			win.modulate.a = 0.0
			var close_btn = win.get_node_or_null("BtnClose")
			if close_btn is BaseButton:
				close_btn.pressed.connect(_close_window.bind(win))
				close_btn.focus_mode = Control.FOCUS_NONE
			win.gui_input.connect(_on_window_gui_input.bind(win))

	for draggable_window in [
		character_window, 
		inventory_window, 
		skill_window, 
		party_window, 
		clan_window, 
		settings_window
	]:
		_register_draggable_window(draggable_window)

	if inventory_window:
		if inventory_window.has_signal("equip_requested"):
			inventory_window.equip_requested.connect(_on_equip_requested)
		if inventory_window.has_signal("unequip_requested"):
			inventory_window.unequip_requested.connect(_on_unequip_requested)

	if settings_window:
		var btn_back_login = settings_window.get_node_or_null("BtnBackToLogin")
		if btn_back_login is BaseButton:
			btn_back_login.pressed.connect(_on_btn_back_to_login_pressed)
			btn_back_login.focus_mode = Control.FOCUS_NONE

		var btn_quit = settings_window.get_node_or_null("BtnQuit")
		if btn_quit is BaseButton:
			btn_quit.pressed.connect(_on_btn_quit_pressed)
			btn_quit.focus_mode = Control.FOCUS_NONE




		var opt_volume_bg = settings_window.get_node_or_null("OptionVolumeBg")
		if opt_volume_bg is OptionButton:



			var saved_pct = AudioManager.current_bgm_volume_pct if AudioManager else 100.0


			for i in range(opt_volume_bg.item_count):
				var text = opt_volume_bg.get_item_text(i)
				var val_str = text.replace("%", "").strip_edges()
				if val_str.is_valid_float() and val_str.to_float() == saved_pct:

					opt_volume_bg.select(i)
					break


			if not opt_volume_bg.item_selected.is_connected(_on_opt_volume_bg_item_selected):
				opt_volume_bg.item_selected.connect(_on_opt_volume_bg_item_selected)
			opt_volume_bg.focus_mode = Control.FOCUS_NONE

		var opt_volume_effect = settings_window.get_node_or_null("OptionVolumeEffect")
		if opt_volume_effect is OptionButton:
			var saved_se_pct = AudioManager.get_se_volume() if AudioManager else 100.0

			for i in range(opt_volume_effect.item_count):
				var text = opt_volume_effect.get_item_text(i)
				var val_str = text.replace("%", "").strip_edges()
				if val_str.is_valid_float() and val_str.to_float() == saved_se_pct:
					opt_volume_effect.select(i)
					break

			if not opt_volume_effect.item_selected.is_connected(_on_opt_volume_effect_item_selected):
				opt_volume_effect.item_selected.connect(_on_opt_volume_effect_item_selected)
			opt_volume_effect.focus_mode = Control.FOCUS_NONE




		_load_game_settings()

		if option_pet_mode is OptionButton:
			option_pet_mode.select(current_pet_combat_mode)
			if not option_pet_mode.item_selected.is_connected(_on_opt_pet_mode_item_selected):
				option_pet_mode.item_selected.connect(_on_opt_pet_mode_item_selected)
			option_pet_mode.focus_mode = Control.FOCUS_NONE


			if not option_pet_mode.gui_input.is_connected(_on_option_gui_input):
				option_pet_mode.gui_input.connect(_on_option_gui_input.bind(option_pet_mode))

		if option_auto_saint is OptionButton:
			option_auto_saint.select(0)
			if not option_auto_saint.item_selected.is_connected(_on_opt_auto_saint_item_selected):
				option_auto_saint.item_selected.connect(_on_opt_auto_saint_item_selected)
			option_auto_saint.focus_mode = Control.FOCUS_NONE


			if not option_auto_saint.gui_input.is_connected(_on_option_gui_input):
				option_auto_saint.gui_input.connect(_on_option_gui_input.bind(option_auto_saint))

		if option_fps_limit is SpinBox:
			# Editable value + built-in up/down buttons, independent of quality.
			# 0 = unlimited, 30..120 in steps of 5, or 144.
			option_fps_limit.min_value = 0.0
			option_fps_limit.max_value = 144.0
			# Range.step must be 1 so manually entered 144 is representable.
			option_fps_limit.step = 1.0
			option_fps_limit.custom_arrow_step = 5.0
			option_fps_limit.custom_arrow_round = false
			option_fps_limit.rounded = true
			option_fps_limit.value = float(current_fps_limit)
			option_fps_limit.editable = true
			option_fps_limit.select_all_on_focus = true
			if not option_fps_limit.value_changed.is_connected(_on_opt_fps_limit_value_changed):
				option_fps_limit.value_changed.connect(_on_opt_fps_limit_value_changed)

		if option_performance_load is OptionButton:
			option_performance_load.select(current_performance_load_mode)
			if not option_performance_load.item_selected.is_connected(_on_opt_performance_load_item_selected):
				option_performance_load.item_selected.connect(_on_opt_performance_load_item_selected)
			option_performance_load.focus_mode = Control.FOCUS_NONE
			if not option_performance_load.gui_input.is_connected(_on_option_gui_input):
				option_performance_load.gui_input.connect(_on_option_gui_input.bind(option_performance_load))

		if option_combat_render is OptionButton:
			option_combat_render.select(current_combat_render_mode)
			if not option_combat_render.item_selected.is_connected(_on_opt_combat_render_item_selected):
				option_combat_render.item_selected.connect(_on_opt_combat_render_item_selected)
			option_combat_render.focus_mode = Control.FOCUS_NONE
			if not option_combat_render.gui_input.is_connected(_on_option_gui_input):
				option_combat_render.gui_input.connect(_on_option_gui_input.bind(option_combat_render))

		if option_simplify_players is OptionButton:
			option_simplify_players.select(current_simplify_players_mode)
			if not option_simplify_players.item_selected.is_connected(_on_opt_simplify_players_item_selected):
				option_simplify_players.item_selected.connect(_on_opt_simplify_players_item_selected)
			option_simplify_players.focus_mode = Control.FOCUS_NONE
			if not option_simplify_players.gui_input.is_connected(_on_option_gui_input):
				option_simplify_players.gui_input.connect(_on_option_gui_input.bind(option_simplify_players))

		if option_simplify_monsters is OptionButton:
			option_simplify_monsters.select(current_simplify_monsters_mode)
			if not option_simplify_monsters.item_selected.is_connected(_on_opt_simplify_monsters_item_selected):
				option_simplify_monsters.item_selected.connect(_on_opt_simplify_monsters_item_selected)
			option_simplify_monsters.focus_mode = Control.FOCUS_NONE
			if not option_simplify_monsters.gui_input.is_connected(_on_option_gui_input):
				option_simplify_monsters.gui_input.connect(_on_option_gui_input.bind(option_simplify_monsters))

		for option in [
			option_pet_mode,
			option_volume_bg,
			option_volume_effect,
			option_auto_saint,
			option_fps_limit,
			option_performance_load,
			option_combat_render,
			option_simplify_players,
			option_simplify_monsters
		]:
			if option is OptionButton:
				_bind_option_popup_cursor(option)

	if btn_quest and not btn_quest.pressed.is_connected(_toggle_lazy_window.bind("quest")):
		btn_quest.pressed.connect(_toggle_lazy_window.bind("quest"))
	if btn_announcement and not btn_announcement.pressed.is_connected(_toggle_lazy_window.bind("announcement")):
		btn_announcement.pressed.connect(_toggle_lazy_window.bind("announcement"))
	if btn_game_info and not btn_game_info.pressed.is_connected(_toggle_lazy_window.bind("game_info")):
		btn_game_info.pressed.connect(_toggle_lazy_window.bind("game_info"))
	if btn_craft_dismantle and not btn_craft_dismantle.pressed.is_connected(_toggle_lazy_window.bind("craft")):
		btn_craft_dismantle.pressed.connect(_toggle_lazy_window.bind("craft"))
	if btn_mall:
		btn_mall.focus_mode = Control.FOCUS_NONE


func release_afk_visual_preloads() -> int:
	var released: int = 0
	for kind_value in LAZY_WINDOW_PATHS.keys():
		var kind: String = str(kind_value)
		var window: Control = _get_lazy_window(kind)
		if is_instance_valid(window) and not window.visible:
			window.free()
			_assign_lazy_window(kind, null)
			released += 1
	_lazy_window_scenes.clear()
	_lazy_window_requests.clear()
	return released


func _assign_lazy_window(kind: String, window: Control = null) -> void :
	match kind:
		"quest": quest_window = window
		"mall": mall_window = window
		"announcement": announcement_window = window
		"game_info": game_info_window = window
		"craft": craft_dismantle_window = window
		"big_map": big_map_window = window
		"world_map": world_map_window = window

func _get_lazy_window(kind: String) -> Control:
	match kind:
		"quest": return quest_window
		"mall": return mall_window
		"announcement": return announcement_window
		"game_info": return game_info_window
		"craft": return craft_dismantle_window
		"big_map": return big_map_window
		"world_map": return world_map_window
	return null

func _configure_lazy_window(kind: String, window: Control) -> void :
	window.visible = false
	window.modulate.a = 0.0
	_register_draggable_window(window)
	window.gui_input.connect(_on_window_gui_input.bind(window))

	var close_btn := window.get_node_or_null("BtnClose")
	if close_btn is BaseButton:
		close_btn.pressed.connect(_close_window.bind(window))
		close_btn.focus_mode = Control.FOCUS_NONE

	if is_instance_valid(target_player) and window.has_method("setup"):
		window.call("setup", target_player)

	if kind == "big_map":
		if window.has_signal("close_requested"):
			window.close_requested.connect(_close_window.bind(window))
	elif kind == "world_map":
		if window.has_signal("close_requested"):
			window.close_requested.connect(_close_window.bind(window))

func _ensure_lazy_window(kind: String) -> Control:
	var existing := _get_lazy_window(kind)
	if is_instance_valid(existing):
		return existing

	var total_started_usec: int = Time.get_ticks_usec()

	# craft/game_info may already have entered the SceneTree on the login screen.
	# Reuse those exact nodes so their first _ready/layout cost is never paid in
	# the world-entry critical path.
	if GameManager and GameManager.has_method("take_prewarmed_hud_window"):
		var warmed := GameManager.take_prewarmed_hud_window(kind) as Control
		if is_instance_valid(warmed) and is_instance_valid(windows_container):
			var add_started_usec: int = Time.get_ticks_usec()
			windows_container.add_child(warmed)
			var add_child_ms: float = float(Time.get_ticks_usec() - add_started_usec) / 1000.0
			_assign_lazy_window(kind, warmed)
			var setup_started_usec: int = Time.get_ticks_usec()
			_configure_lazy_window(kind, warmed)
			var setup_ms: float = float(Time.get_ticks_usec() - setup_started_usec) / 1000.0
			var total_ms: float = float(Time.get_ticks_usec() - total_started_usec) / 1000.0
			print("[HUD_WINDOW_INIT] kind=%s source=login_cache load_ms=0.00 instantiate_ms=0.00 add_child_ms=%.2f setup_ms=%.2f total_ms=%.2f" % [
				kind,
				add_child_ms,
				setup_ms,
				total_ms,
			])
			return warmed

	var load_started_usec: int = total_started_usec
	var path: String = str(LAZY_WINDOW_PATHS.get(kind, ""))
	if path.is_empty():
		return null

	var scene: PackedScene = _lazy_window_scenes.get(kind, null) as PackedScene
	if scene == null and ResourceLoader.has_cached(path):
		scene = ResourceLoader.load(path) as PackedScene
		if scene != null:
			_lazy_window_scenes[kind] = scene

	if scene == null:
		if not _lazy_window_requests.has(kind):
			var request_error: int = ResourceLoader.load_threaded_request(path)
			if request_error != OK:
				push_error("[HUD] lazy window load request failed: %s" % path)
				return null
			_lazy_window_requests[kind] = true

		while true:
			var progress: Array = []
			var status: int = ResourceLoader.load_threaded_get_status(path, progress)
			if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS:
				await get_tree().process_frame
				continue
			if status == ResourceLoader.THREAD_LOAD_LOADED:
				scene = ResourceLoader.load_threaded_get(path) as PackedScene
				break
			push_error("[HUD] lazy window threaded load failed: %s" % path)
			_lazy_window_requests.erase(kind)
			return null

		_lazy_window_scenes[kind] = scene
		_lazy_window_requests.erase(kind)

	var load_ms: float = float(Time.get_ticks_usec() - load_started_usec) / 1000.0
	var instantiate_started_usec: int = Time.get_ticks_usec()
	var window := scene.instantiate() as Control
	var instantiate_ms: float = float(Time.get_ticks_usec() - instantiate_started_usec) / 1000.0
	if window == null or not is_instance_valid(windows_container):
		return null

	var add_child_started_usec: int = Time.get_ticks_usec()
	windows_container.add_child(window)
	var add_child_ms: float = float(Time.get_ticks_usec() - add_child_started_usec) / 1000.0

	_assign_lazy_window(kind, window)

	var setup_started_usec: int = Time.get_ticks_usec()
	_configure_lazy_window(kind, window)
	var setup_ms: float = float(Time.get_ticks_usec() - setup_started_usec) / 1000.0
	var total_ms: float = float(Time.get_ticks_usec() - total_started_usec) / 1000.0

	print("[HUD_WINDOW_INIT] kind=%s load_ms=%.2f instantiate_ms=%.2f add_child_ms=%.2f setup_ms=%.2f total_ms=%.2f" % [
		kind,
		load_ms,
		instantiate_ms,
		add_child_ms,
		setup_ms,
		total_ms,
	])
	return window

func prepare_lazy_windows_before_reveal(progress_callback: Callable = Callable()) -> void :
	var kinds: Array[String] = [
		"quest",
		"mall",
		"announcement",
		"game_info",
		# Craft uses first-open, time-budgeted recipe construction with a
		# visible in-window progress indicator; not part of map reveal.
		"big_map",
		"world_map",
	]
	var total: int = kinds.size()
	for index: int in range(total):
		if not is_inside_tree():
			return
		var kind: String = kinds[index]
		var window := await _ensure_lazy_window(kind)
		if is_instance_valid(window):
			window.visible = false
			window.modulate.a = 0.0
		if progress_callback.is_valid():
			progress_callback.call(index + 1, total, kind)
		await get_tree().process_frame

func _prewarm_lazy_window_scenes() -> void :
	for kind_value in LAZY_WINDOW_PATHS.keys():
		var kind: String = str(kind_value)
		if _lazy_window_scenes.has(kind) or _lazy_window_requests.has(kind):
			continue
		var path: String = str(LAZY_WINDOW_PATHS[kind])
		var request_error: int = ResourceLoader.load_threaded_request(path)
		if request_error == OK:
			_lazy_window_requests[kind] = true

	while not _lazy_window_requests.is_empty():
		for kind_value in _lazy_window_requests.keys().duplicate():
			var kind: String = str(kind_value)
			var path: String = str(LAZY_WINDOW_PATHS[kind])
			var status: int = ResourceLoader.load_threaded_get_status(path)
			if status == ResourceLoader.THREAD_LOAD_LOADED:
				var scene := ResourceLoader.load_threaded_get(path) as PackedScene
				if scene != null:
					_lazy_window_scenes[kind] = scene
				_lazy_window_requests.erase(kind)
			elif status == ResourceLoader.THREAD_LOAD_FAILED or status == ResourceLoader.THREAD_LOAD_INVALID_RESOURCE:
				_lazy_window_requests.erase(kind)
		await get_tree().process_frame

func _toggle_lazy_window(kind: String) -> void :
	# Optional crafting recipes are intentionally not built at login or map
	# reveal. Open the actual window BEFORE any expensive work, then display
	# first-use progress inside it. A second click must not spawn a second
	# 1453-row Tree build.
	if kind == "craft":
		if _craft_window_initial_opening:
			return
		_craft_window_initial_opening = true
		var crafting_window := await _ensure_lazy_window(kind)
		if not is_instance_valid(crafting_window):
			_craft_window_initial_opening = false
			return
		if crafting_window.has_method("is_initial_prepared") and not bool(crafting_window.is_initial_prepared()):
			_open_window(crafting_window)
			if crafting_window.has_method("prepare_initial_with_progress_async"):
				await crafting_window.prepare_initial_with_progress_async()
			elif crafting_window.has_method("prepare_initial_async"):
				await crafting_window.prepare_initial_async()
			_craft_window_initial_opening = false
			return
		_craft_window_initial_opening = false
		_toggle_window(crafting_window)
		return
	var window := await _ensure_lazy_window(kind)
	if not is_instance_valid(window):
		return
	_toggle_window(window)

func start_post_reveal_background_prewarm() -> void :
	# Dismantle is optional and still costs close to a second of cumulative UI
	# work even when batched. Do not start it automatically after login; build it
	# only if/when the player opens the Dismantle tab.
	return

func _setup_header_button_hover_signals() -> void :
	for button in [btn_announcement, btn_game_info, btn_craft_dismantle]:
		if button:
			button.mouse_entered.connect(_on_header_button_hover_changed.bind(button, true))
			button.mouse_exited.connect(_on_header_button_hover_changed.bind(button, false))

func _on_header_button_hover_changed(button: TextureButton, is_hovered: bool) -> void :
	button.self_modulate = Color(1.3, 1.3, 1.3, 1.0) if is_hovered else Color.WHITE

func _setup_menu_tooltips() -> void :
	if btn_character: btn_character.tooltip_text = "角色 (Ctrl+E)"
	if btn_inventory: btn_inventory.tooltip_text = "背包 (Ctrl+W)"
	if btn_skill: btn_skill.tooltip_text = "技能 (Ctrl+S)"
	if btn_party: btn_party.tooltip_text = "組隊 (Ctrl+D)"
	if btn_clan: btn_clan.tooltip_text = "戰團"
	if btn_shop: btn_shop.tooltip_text = "小商店"
	if btn_quest: btn_quest.tooltip_text = "任務 (Ctrl+J)"
	if btn_mall: btn_mall.tooltip_text = "商城"
	if btn_settings: btn_settings.tooltip_text = "系統 (Ctrl+Q)"
	if btn_announcement: btn_announcement.tooltip_text = "最新消息"
	if btn_game_info: btn_game_info.tooltip_text = "遊戲資訊"
	if btn_craft_dismantle: btn_craft_dismantle.tooltip_text = "合成/分解 (Ctrl+G)"
	if btn_afk_mode: btn_afk_mode.tooltip_text = "掛機監控：卸載可還原視覺資源，保留角色運作"

	if chat_panel:
		var btn_channel = chat_panel.get_node_or_null("BtnChannel")
		if btn_channel: btn_channel.tooltip_text = "切換聊天頻道"

		var btn_filter_private = chat_panel.get_node_or_null("BtnFilterPrivate")
		if btn_filter_private: btn_filter_private.tooltip_text = "顯示/隱藏「密語」"

		var btn_filter_general = chat_panel.get_node_or_null("BtnFilterGeneral")
		if btn_filter_general: btn_filter_general.tooltip_text = "顯示/隱藏「附近」頻道"

		var btn_filter_world = chat_panel.get_node_or_null("BtnFilterWorld")
		if btn_filter_world: btn_filter_world.tooltip_text = "顯示/隱藏「陣營」頻道"

		var btn_filter_clan = chat_panel.get_node_or_null("BtnFilterClan")
		if btn_filter_clan: btn_filter_clan.tooltip_text = "顯示/隱藏「戰團」頻道"

		var btn_filter_party = chat_panel.get_node_or_null("BtnFilterParty")
		if btn_filter_party: btn_filter_party.tooltip_text = "顯示/隱藏「隊伍」頻道"

func _toggle_window(target_window: Control) -> void :
	if not target_window: return
	var is_currently_open = target_window.visible and target_window.modulate.a > 0.1
	if is_currently_open:
		_close_window(target_window)
	else:

		if target_window == inventory_window:

			if is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model and target_player.data_model.is_shop_open:
				show_rejection_message_box("商店營業中無法開啟背包！")
				return


			if input_box and input_box.visible and input_box_label.text == "創建小商店":
				show_rejection_message_box("請先完成商店名稱設定！")
				return


			if is_instance_valid(current_shop_window) and current_shop_window.visible:
				var mode = current_shop_window.get("current_mode")
				if mode == 0 or mode == 1:
					show_rejection_message_box("商店設定或營業中無法開啟背包！")
					return

		if target_window == clan_window:
			if is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model.club_id <= 0:
				if NetworkManager and NetworkManager.has_method("send_secure_packet"):
					NetworkManager.send_secure_packet({
						"Type": "ClubAction",
						"Action": "OpenCreate"
					})
				return

		if target_window.has_method("update_ui"): target_window.update_ui()
		_open_window(target_window)
		if target_window == announcement_window:
			announcement_window.call("set_loading")
			NetworkManager.send_announcement_list_request()

func _on_announcement_list_received(data: Dictionary) -> void :
	if announcement_window and announcement_window.visible:
		announcement_window.call("show_response", data)

func _open_window(target_window: Control) -> void :
	if not target_window: return
	_reset_draggable_window_position(target_window)
	var was_visible = target_window.visible
	if _window_tweens.has(target_window) and _window_tweens[target_window].is_running():
		_window_tweens[target_window].kill()

	target_window.visible = true
	_focus_window(target_window)
	var tween = create_tween()
	_window_tweens[target_window] = tween
	tween.set_trans(Tween.TRANS_SINE)
	tween.tween_property(target_window, "modulate:a", 1.0, 0.2)
	if not was_visible:
		_play_interface_activation_feedback(target_window)

func _close_window(target_window: Control) -> void :
	if not target_window: return
	if target_window == _dragged_window:
		_clear_window_drag()
	if _window_tweens.has(target_window) and _window_tweens[target_window].is_running():
		_window_tweens[target_window].kill()

	var tween = create_tween()
	_window_tweens[target_window] = tween
	tween.set_trans(Tween.TRANS_SINE)
	tween.tween_property(target_window, "modulate:a", 0.0, 0.2)
	tween.tween_callback( func(): target_window.visible = false)

func _focus_window(target_window: Control) -> void :
	if target_window and windows_container:
		var last_index = windows_container.get_child_count() - 1
		windows_container.move_child(target_window, last_index)

func _on_window_gui_input(event: InputEvent, target_window: Control) -> void :
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		_focus_window(target_window)

func _register_draggable_window(target_window: Control) -> void :
	if not is_instance_valid(target_window) or _draggable_window_defaults.has(target_window):
		return

	_draggable_window_defaults[target_window] = {
		"left": target_window.offset_left, 
		"top": target_window.offset_top, 
		"right": target_window.offset_right, 
		"bottom": target_window.offset_bottom
	}
	target_window.tree_exiting.connect(_unregister_draggable_window.bind(target_window), CONNECT_ONE_SHOT)

func _unregister_draggable_window(target_window: Control) -> void :
	if target_window == _dragged_window:
		_clear_window_drag()
	_draggable_window_defaults.erase(target_window)

func _reset_draggable_window_position(target_window: Control) -> void :
	if not is_instance_valid(target_window) or not _draggable_window_defaults.has(target_window):
		return

	var default_offsets: Dictionary = _draggable_window_defaults[target_window]
	target_window.offset_left = float(default_offsets["left"])
	target_window.offset_top = float(default_offsets["top"])
	target_window.offset_right = float(default_offsets["right"])
	target_window.offset_bottom = float(default_offsets["bottom"])

func _input(event: InputEvent) -> void :
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_ENTER or event.keycode == KEY_KP_ENTER:
			if message_box and message_box.visible:
				if msg_box_btn_confirm and not msg_box_btn_confirm.disabled:
					msg_box_btn_confirm.pressed.emit()
				get_viewport().set_input_as_handled()
				return

	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			var target_window: = _find_draggable_window_at(event.position)
			if not target_window:
				return
			_dragged_window = target_window
			_window_drag_pointer_offset = event.position - target_window.global_position
			_focus_window(target_window)
			get_viewport().set_input_as_handled()
		elif is_instance_valid(_dragged_window):
			_clear_window_drag()
			get_viewport().set_input_as_handled()
		return

	if event is InputEventMouseMotion and is_instance_valid(_dragged_window):
		_move_dragged_window(event.position)
		get_viewport().set_input_as_handled()

func _find_draggable_window_at(pointer_position: Vector2) -> Control:
	if not is_instance_valid(windows_container):
		return null

	var hovered_control: = get_viewport().gui_get_hovered_control()
	var children: = windows_container.get_children()
	for index in range(children.size() - 1, -1, -1):
		var candidate: = children[index] as Control
		if not is_instance_valid(candidate) or not candidate.visible:
			continue
		if not _draggable_window_defaults.has(candidate):
			continue

		var candidate_rect: = candidate.get_global_rect()
		var header_rect: = Rect2(
			candidate_rect.position, 
			Vector2(candidate_rect.size.x, minf(WINDOW_DRAG_HEADER_HEIGHT, candidate_rect.size.y))
		)
		if not header_rect.has_point(pointer_position):
			continue
		if _is_window_drag_blocked_by_control(hovered_control, candidate):
			return null
		return candidate

	return null

func _is_window_drag_blocked_by_control(hovered_control: Control, target_window: Control) -> bool:
	var current: = hovered_control
	while is_instance_valid(current) and current != target_window:
		if current is BaseButton or current is LineEdit or current is TextEdit:
			return true
		if current is SpinBox or current is Slider or current is ScrollBar:
			return true
		current = current.get_parent() as Control
	return false

func _move_dragged_window(pointer_position: Vector2) -> void :
	if not is_instance_valid(_dragged_window):
		_clear_window_drag()
		return

	var viewport_size: = get_viewport().get_visible_rect().size
	var window_size: = _dragged_window.size
	var maximum_position: = Vector2(
		maxf(viewport_size.x - window_size.x, 0.0), 
		maxf(viewport_size.y - window_size.y, 0.0)
	)
	var target_position: = pointer_position - _window_drag_pointer_offset
	target_position.x = clampf(target_position.x, 0.0, maximum_position.x)
	target_position.y = clampf(target_position.y, 0.0, maximum_position.y)
	_dragged_window.global_position = target_position

func _clear_window_drag() -> void :
	_dragged_window = null
	_window_drag_pointer_offset = Vector2.ZERO

func _on_afk_mode_requested() -> void:
	if not GameManager or GameManager.is_afk_mode_active():
		return
	if GameManager.is_changing_map:
		show_rejection_message_box("正在切換地圖，請完成後再進入掛機模式。")
		return
	if not is_instance_valid(GameManager.current_player):
		show_rejection_message_box("必須先進入遊戲才能啟用掛機模式。")
		return
	show_message_box(
		"確定進入掛機監控模式？\n將關閉遊戲畫面、卸載可安全回收的貼圖，但會繼續自動移動、打怪與拾取。\n如需恢復畫面可按「返回遊戲」。",
		2,
		_confirm_begin_afk
	)


func _confirm_begin_afk() -> void:
	if not GameManager or not GameManager.begin_afk_mode():
		show_rejection_message_box("掛機模式啟動失敗，請確認地圖已完成載入。")
		return
	_afk_entered_msec = Time.get_ticks_msec()
	set_background_visuals_paused(true)
	_afk_hidden_roots.clear()
	_afk_hide_nonessential_roots()
	if afk_btn_restore:
		afk_btn_restore.disabled = false
	if afk_overlay:
		afk_overlay.visible = true
		afk_overlay.move_to_front()
	# Focus may have changed during AFK setup; do not start a monitor
	# timer in a hidden window. The next focus-in refreshes it immediately.
	set_afk_dashboard_updates_enabled(GameManager.is_game_window_focused())


func _afk_hide_nonessential_roots() -> void:
	# Keep the existing MessageBox for a real confirm/cancel interaction.
	# HUD state remains alive for packet-driven inventory/quest updates.
	for root_child in get_children():
		if root_child == afk_overlay or root_child == message_box:
			continue
		if not (root_child is CanvasItem or root_child is CanvasLayer):
			continue
		var already_tracked: bool = false
		for snapshot in _afk_hidden_roots:
			var ref: WeakRef = snapshot["node"]
			if ref.get_ref() == root_child:
				already_tracked = true
				break
		if not already_tracked:
			_afk_hidden_roots.append({"node": weakref(root_child), "visible": root_child.visible})
		root_child.visible = false


func _on_afk_restore_requested() -> void:
	if not GameManager or not GameManager.is_afk_mode_active():
		return
	if GameManager.is_changing_map or GameManager.is_afk_mode_restoring():
		_refresh_afk_dashboard()
		return
	show_message_box(
		"確定結束掛機監控並返回遊戲？\n地圖貼圖將分批重新載入，角色不會登出。",
		2,
		_confirm_restore_afk
	)


func _on_afk_restore_progress(percent: float, stage: String) -> void:
	if not is_inside_tree():
		return
	if afk_restore_progress and afk_restore_progress.visible:
		afk_restore_progress.set_target_progress(percent)
	if afk_resource_stats:
		afk_resource_stats.text = "還原進度 %d%%｜%s" % [int(percent), stage]


func _confirm_restore_afk() -> void:
	if not GameManager or not GameManager.is_afk_mode_active():
		return
	if afk_btn_restore:
		afk_btn_restore.disabled = true
	if afk_restore_progress:
		afk_restore_progress.visible = true
		afk_restore_progress.reset_progress()
	if afk_status:
		afk_status.text = "正在分批還原遊戲資源…"
	var restored: bool = await GameManager.finish_afk_mode(
		Callable(self, "_on_afk_restore_progress")
	)
	if not is_inside_tree():
		return
	if not restored:
		if afk_btn_restore:
			afk_btn_restore.disabled = false
		if afk_restore_progress:
			afk_restore_progress.visible = false
		_refresh_afk_dashboard()
		return
	if afk_restore_progress:
		afk_restore_progress.finish_progress()
		await get_tree().create_timer(0.3).timeout
		if not is_inside_tree():
			return
		afk_restore_progress.visible = false
	if afk_update_timer:
		afk_update_timer.stop()
	if afk_overlay:
		afk_overlay.visible = false
	for snapshot in _afk_hidden_roots:
		var ref: WeakRef = snapshot["node"]
		var node: Node = ref.get_ref()
		if is_instance_valid(node) and (node is CanvasItem or node is CanvasLayer):
			node.visible = bool(snapshot["visible"])
	_afk_hidden_roots.clear()
	# GameManager already restored the correct foreground/background focus policy.
	# Re-applying the AFK-enter pause state would strand the minimap disabled.
	if is_instance_valid(GameManager.current_map_node):
		GameManager.current_map_node.visible = true


func set_afk_dashboard_updates_enabled(enabled: bool) -> void:
	if not afk_update_timer or not afk_overlay or not afk_overlay.visible:
		return
	if enabled and GameManager and GameManager.is_afk_mode_active():
		if afk_update_timer.is_stopped():
			afk_update_timer.start()
			_refresh_afk_dashboard()
	else:
		afk_update_timer.stop()


func _refresh_afk_dashboard() -> void:
	# The SceneTree may still execute fixed gameplay while backgrounded.
	# Do not update visual labels or traverse the HUD until refocused.
	if not GameManager or not GameManager.is_afk_mode_active():
		return
	if not GameManager.is_game_window_focused():
		return
	_afk_hide_nonessential_roots()
	var player: Node = GameManager.current_player
	var connected: bool = NetworkManager and NetworkManager.is_connected_to_server
	if afk_character:
		afk_character.text = "角色｜等待同步"
	if afk_vitals:
		afk_vitals.text = "生命 / 魔力｜—"
	if afk_position:
		afk_position.text = "位置｜—"
	if is_instance_valid(player) and "data_model" in player and player.data_model:
		var data: PlayerData = player.data_model
		if afk_character:
			afk_character.text = "角色｜%s    Lv.%d" % [data.character_name, data.level]
		if afk_vitals:
			afk_vitals.text = "HP %d / %d    MP %d / %d" % [data.hp, data.max_hp, data.mp, data.max_mp]
		if afk_position:
			afk_position.text = "地圖 %d    X %d · Y %d" % [data.map_id, int(player.global_position.x), int(player.global_position.y)]
	if afk_status:
		var state: String = "待命"
		if not connected:
			state = "連線中斷"
		elif GameManager.is_changing_map:
			state = "切換地圖中"
		elif GameManager.is_afk_mode_restoring():
			state = "還原資源中"
		elif is_instance_valid(player):
			if bool(player.get("is_dead")):
				state = "角色死亡"
			elif is_instance_valid(player.get("combat_target")):
				state = "自動戰鬥中"
			elif str(player.get("current_state")).begins_with("attack"):
				state = "攻擊中"
			elif bool(player.get("is_moving")):
				state = "移動中"
			elif "data_model" in player and player.data_model and player.data_model.is_auto_saint_on:
				state = "自動聖靈中"
		afk_status.text = "● %s  ｜  %s" % ["已連線" if connected else "未連線", state]
	if afk_runtime:
		var elapsed: int = maxi(0, int((Time.get_ticks_msec() - _afk_entered_msec) / 1000))
		afk_runtime.text = "掛機時間｜%02d:%02d:%02d" % [int(elapsed / 3600), int(elapsed / 60) % 60, elapsed % 60]
	if afk_resource_stats and not GameManager.is_afk_mode_restoring():
		var stats: Dictionary = GameManager.afk_visual_stats()
		var failed: int = int(stats.get("restore_missing", 0))
		if failed > 0:
			afk_resource_stats.text = "有 %d 筆貼圖無法還原，請再次點擊返回遊戲重試" % failed
		else:
			afk_resource_stats.text = "卸載 %d 筆 · 保留子資源 %d 筆 · 監控 20 FPS · 失焦不渲染" % [
				int(stats.get("released", 0)),
				int(stats.get("retained_embedded", 0))
			]
	if afk_btn_restore:
		afk_btn_restore.disabled = GameManager.is_changing_map or GameManager.is_afk_mode_restoring()


func _on_btn_back_to_login_pressed() -> void :
	var btn_back = get_node_or_null("WindowsContainer/SettingsWindow/BtnBackToLogin") as BaseButton
	if btn_back: btn_back.disabled = true
	if NetworkManager:
		if NetworkManager.has_method("send_logout_request"):
			NetworkManager.send_logout_request("login")

func _on_btn_quit_pressed() -> void :
	if NetworkManager: NetworkManager.log_client_exit("quit_button_pressed")
	var btn_quit = settings_window.get_node_or_null("BtnQuit") as BaseButton
	if btn_quit: btn_quit.disabled = true
	show_system_message_chat("正在儲存角色資料並安全登出，請稍候...")
	if NetworkManager:
		if NetworkManager.has_method("send_logout_request"): NetworkManager.send_logout_request()
		elif NetworkManager.has_method("send_secure_packet"): NetworkManager.send_secure_packet({"Type": "LogoutRequest"})
		else:
			NetworkManager.log_client_exit("quit_button_fallback")
			get_tree().quit()
	else:

		get_tree().quit()

	get_tree().create_timer(3.0).timeout.connect( func():
		if NetworkManager: NetworkManager.log_client_exit("quit_button_timeout")
		get_tree().quit()
	)

func execute_safe_quit() -> void :
	if NetworkManager: NetworkManager.log_client_exit("execute_safe_quit")
	get_tree().quit()

func _on_equip_requested(item_uid: int, target_slot: int) -> void :
	if NetworkManager:
		if NetworkManager.has_method("send_secure_packet"):
			NetworkManager.send_secure_packet({"Type": "EquipRequest", "ItemUID": item_uid, "TargetSlot": target_slot})

func _on_unequip_requested(target_slot: int) -> void :
	if NetworkManager:
		if NetworkManager.has_method("send_secure_packet"):
			NetworkManager.send_secure_packet({"Type": "UnequipRequest", "TargetSlot": target_slot})

var _initial_hud_ready: bool = false

func initialize_hud(player: CharacterBody2D) -> void :
	var hud_started_usec := Time.get_ticks_usec()
	target_player = player
	_last_displayed_coords = null
	if character_window and character_window.has_method("setup"): character_window.setup(player)
	if inventory_window and inventory_window.has_method("setup"): inventory_window.setup(player)
	if skill_window and skill_window.has_method("setup"):
		var skill_setup_started_usec: int = Time.get_ticks_usec()
		skill_window.setup(player)
		var skill_setup_ms: float = float(Time.get_ticks_usec() - skill_setup_started_usec) / 1000.0
		print("[HUD_SKILL_INIT] setup_ms=%.2f" % skill_setup_ms)
	if clan_window and clan_window.has_method("setup"): clan_window.setup(player)
	if quest_tracker_panel and quest_tracker_panel.has_method("setup"): quest_tracker_panel.setup(player)

	if minimap_viewport and get_tree().root.is_node_ready():
		minimap_viewport.world_2d = get_tree().root.get_viewport().world_2d
		minimap_viewport.canvas_cull_mask = MINIMAP_WORLD_CULL_MASK
		minimap_viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	if minimap_camera: minimap_camera.zoom = Vector2(0.05, 0.05)
	if minimap_npc_overlay and minimap_npc_overlay.has_method("setup"):
		minimap_npc_overlay.setup(minimap_camera, minimap_viewport_container)

	if GameManager.current_map_node:
		var raw_node_name = GameManager.current_map_node.name
		var map_id = target_player.data_model.map_id if target_player and target_player.data_model else raw_node_name.to_lower().replace("map", "").to_int()
		update_map_info(map_id, raw_node_name)

	update_status_panel()

	# Shortcut restoration may synchronously load skill .tres resources. Keep it
	# outside the initial world-entry critical path and let the first frame render.
	call_deferred("_initialize_hud_deferred")

	var hud_sync_ms := float(Time.get_ticks_usec() - hud_started_usec) / 1000.0
	if hud_sync_ms >= 25.0:
		print("[HUD_INIT_TIMING] synchronous_ms=%.2f" % hud_sync_ms)

func _initialize_hud_deferred() -> void :
	await get_tree().process_frame
	if not is_inside_tree():
		return

	var shortcuts_started_usec := Time.get_ticks_usec()
	_load_shortcuts_from_data()
	var shortcuts_ms := float(Time.get_ticks_usec() - shortcuts_started_usec) / 1000.0
	if shortcuts_ms >= 25.0:
		print("[HUD_INIT_TIMING] shortcuts_ms=%.2f" % shortcuts_ms)

	if NetworkManager and NetworkManager.has_method("send_set_global_pet_mode"):
		NetworkManager.send_set_global_pet_mode(current_pet_combat_mode)

	_initial_hud_ready = true
	call_deferred("_open_initial_announcement_lazy")

func _open_initial_announcement_lazy() -> void :
	await get_tree().process_frame
	if not is_inside_tree():
		return
	var window := await _ensure_lazy_window("announcement")
	if not is_instance_valid(window):
		return
	_open_window(window)
	if window.has_method("set_loading"):
		window.call("set_loading")
	if NetworkManager:
		NetworkManager.send_announcement_list_request()

func is_initial_hud_ready() -> bool:
	return _initial_hud_ready

func update_map_info(map_id: int, raw_node_name: String) -> void :
	_last_displayed_coords = null
	_minimap_force_world_redraw = true
	if map_name_label:
		map_name_label.text = REAL_MAP_NAMES.get(map_id, "")
	if is_instance_valid(minimap_npc_overlay):
		if minimap_npc_overlay.has_method("set_active"):
			minimap_npc_overlay.set_active( not _is_hud_hidden)
		if minimap_npc_overlay.has_method("setup"):
			minimap_npc_overlay.setup(minimap_camera, minimap_viewport_container)
	if is_instance_valid(big_map_window):
		call_deferred("_refresh_big_map_context")

func _on_big_map_change_started(_map_id: int, _target_x: float, _target_y: float) -> void :
	if is_instance_valid(minimap_npc_overlay) and minimap_npc_overlay.has_method("clear_context"):
		minimap_npc_overlay.clear_context()
	if is_instance_valid(big_map_window) and big_map_window.has_method("prepare_for_map_change"):
		big_map_window.prepare_for_map_change()

func _exit_tree() -> void :
	if CursorManager and CursorManager.has_method("set_game_window_hovered"):
		CursorManager.set_game_window_hovered(false)
	if is_instance_valid(minimap_npc_overlay) and minimap_npc_overlay.has_method("clear_context"):
		minimap_npc_overlay.clear_context()
	if is_instance_valid(big_map_window) and big_map_window.has_method("clear_context"):
		big_map_window.clear_context()

func update_status_panel() -> void :
	if not is_instance_valid(target_player) or not "data_model" in target_player: return
	var data = target_player.data_model
	if not data: return

	if lbl_level: lbl_level.text = "Lv.%d" % data.level
	if lbl_money: lbl_money.text = str(data.money)

	if lbl_hp: lbl_hp.text = "%d / %d" % [data.hp, data.max_hp]
	if lbl_mp: lbl_mp.text = "%d / %d" % [data.mp, data.max_mp]
	if lbl_will: lbl_will.text = "%d / %d" % [data.sp, data.max_sp]

	if bar_hp: bar_hp.max_value = data.max_hp;bar_hp.value = data.hp
	if bar_mp: bar_mp.max_value = data.max_mp;bar_mp.value = data.mp
	if bar_will: bar_will.max_value = data.max_sp;bar_will.value = data.sp

	if data.max_exp > 0:
		var pct = (float(data.exp_acc) / float(data.max_exp)) * 100.0
		if lbl_exp_percentage: lbl_exp_percentage.text = "%.2f %%" % pct
		if bar_exp:
			bar_exp.max_value = data.max_exp
			bar_exp.value = data.exp_acc
			bar_exp.tooltip_text = "%d / %d" % [data.exp_acc, data.max_exp]
	else:
		if lbl_exp_percentage: lbl_exp_percentage.text = "0.00 %"
		if bar_exp:
			bar_exp.value = 0
			bar_exp.tooltip_text = "%d / 0" % data.exp_acc

	update_pet_status_panel()


func update_pet_status_panel() -> void :
	if not pet_status_container: return
	if not is_instance_valid(target_player) or not "data_model" in target_player: return

	var summoned_pets = []

	for p in target_player.data_model.pet_list:
		var state = int(p.get("state", p.get("State", 0)))
		if state == 1:
			summoned_pets.append(p)


	var existing_rows = pet_status_container.get_children()
	var used_rows = 0


	for i in range(summoned_pets.size()):
		var p_data = summoned_pets[i]
		var row_node = null


		if i < existing_rows.size():
			row_node = existing_rows[i]
		else:
			row_node = PET_STATUS_ROW_SCENE.instantiate()
			pet_status_container.add_child(row_node)

		if row_node.has_method("update_data"):
			row_node.update_data(p_data)

		row_node.visible = true
		used_rows += 1


	for i in range(used_rows, existing_rows.size()):
		existing_rows[i].queue_free()

var _background_visuals_paused: bool = false

func set_background_visuals_paused(paused: bool) -> void :
	_background_visuals_paused = paused
	if minimap_viewport:
		minimap_viewport.render_target_update_mode = (
			SubViewport.UPDATE_DISABLED if paused else SubViewport.UPDATE_ONCE
		)
	if is_instance_valid(minimap_npc_overlay) and minimap_npc_overlay.has_method("set_active"):
		minimap_npc_overlay.set_active(not paused and not _is_hud_hidden)
	if not paused:
		_minimap_update_elapsed = MINIMAP_UPDATE_INTERVAL
		_minimap_force_world_redraw = true
		_cursor_scan_elapsed = CURSOR_SCAN_IDLE_INTERVAL

var _cursor_scan_elapsed: float = 0.0
var _last_cursor_scan_position: Vector2 = Vector2(-99999.0, -99999.0)
const CURSOR_SCAN_IDLE_INTERVAL: float = 0.10

func _process(delta: float) -> void :
	if not _background_visuals_paused:
		_cursor_scan_elapsed += delta
		var mouse_position := get_viewport().get_mouse_position()
		if mouse_position != _last_cursor_scan_position or _cursor_scan_elapsed >= CURSOR_SCAN_IDLE_INTERVAL:
			_last_cursor_scan_position = mouse_position
			_cursor_scan_elapsed = 0.0
			_update_game_window_cursor_override()

		if not _is_hud_hidden and is_instance_valid(target_player):
			_minimap_world_idle_elapsed += delta
		_minimap_update_elapsed += delta
		if _minimap_update_elapsed >= MINIMAP_UPDATE_INTERVAL:
			_minimap_update_elapsed = fmod(_minimap_update_elapsed, MINIMAP_UPDATE_INTERVAL)
			_update_minimap()

	if invite_box and invite_box.visible and _invite_time_left > 0:
		_invite_time_left -= delta
		var displayed_second: = int(ceil(_invite_time_left))
		if lbl_timer and displayed_second != _last_invite_display_second:
			_last_invite_display_second = displayed_second
			lbl_timer.text = str(displayed_second)

		if _invite_time_left <= 0:
			invite_box.visible = false
			if _invite_timeout_callback.is_valid():
				_invite_timeout_callback.call()

func _update_game_window_cursor_override() -> void :
	if not CursorManager or not CursorManager.has_method("set_game_window_hovered"):
		return

	var mouse_position: = get_viewport().get_mouse_position()
	var is_over_game_window: = false

	if is_instance_valid(windows_container):
		for child in windows_container.get_children():
			var window: = child as Control
			if not is_instance_valid(window) or not window.is_visible_in_tree():
				continue
			if window == pet_status_scroll or window == quest_tracker_panel:
				continue
			if window.get_global_rect().has_point(mouse_position):
				is_over_game_window = true
				break

	if not is_over_game_window:
		for modal in [message_box, input_box, spin_input_box]:
			if is_instance_valid(modal) and modal.is_visible_in_tree() and modal.get_global_rect().has_point(mouse_position):
				is_over_game_window = true
				break

	CursorManager.set_game_window_hovered(is_over_game_window)

func _update_minimap() -> void :
	if _is_hud_hidden or not is_instance_valid(target_player):
		return

	var player_position: = target_player.global_position
	var camera_changed: bool = false
	if minimap_camera and minimap_camera.global_position != player_position:
		minimap_camera.global_position = player_position
		camera_changed = true
	if minimap_viewport:
		if should_redraw_minimap_world(camera_changed, _minimap_force_world_redraw, _minimap_world_idle_elapsed):
			minimap_viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
			_minimap_world_redraw_requests += 1
			_minimap_world_idle_elapsed = 0.0
			_minimap_force_world_redraw = false
		else:
			_minimap_world_redraw_skips += 1
	if coords_label:
		var displayed_coords: = Vector2i(int(player_position.x), int(player_position.y))
		if displayed_coords != _last_displayed_coords:
			_last_displayed_coords = displayed_coords
			coords_label.text = "X：%d , Y：%d" % [displayed_coords.x, displayed_coords.y]

func _unhandled_input(event: InputEvent) -> void :
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode >= KEY_F1 and event.keycode <= KEY_F12:

			if is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model:
				if target_player.data_model.is_shop_open:
					show_rejection_message_box("商店營業中，無法使用快捷鍵！")
					get_viewport().set_input_as_handled()
					return

			var slot_idx = event.keycode - KEY_F1
			var slots = _get_all_slots()
			if slot_idx >= 0 and slot_idx < slots.size():
				slots[slot_idx].trigger_slot()
			get_viewport().set_input_as_handled()
			return


		if event.ctrl_pressed:
			match event.keycode:
				KEY_H: _toggle_hud_visibility()
				KEY_E: if character_window: _toggle_window(character_window)
				KEY_W: if inventory_window: _toggle_window(inventory_window)
				KEY_S: if skill_window: _toggle_window(skill_window)
				KEY_D: if party_window: _toggle_window(party_window)
				KEY_J: _toggle_lazy_window("quest")
				KEY_Q: if settings_window: _toggle_window(settings_window)
				KEY_G: _toggle_lazy_window("craft")
				KEY_F:
					toggle_fullscreen()
					get_viewport().set_input_as_handled()
					return

func _toggle_big_map() -> void :
	if not is_instance_valid(big_map_window):
		big_map_window = await _ensure_lazy_window("big_map")
	if not is_instance_valid(big_map_window):
		return

	var is_open: = big_map_window.visible and big_map_window.modulate.a > 0.1
	if is_open:
		_close_window(big_map_window)
		return

	_refresh_big_map_context()
	_open_window(big_map_window)

func _toggle_world_map() -> void :
	if not is_instance_valid(world_map_window):
		world_map_window = await _ensure_lazy_window("world_map")
	if not is_instance_valid(world_map_window):
		return

	var is_open: = world_map_window.visible and world_map_window.modulate.a > 0.1
	if is_open:
		_close_window(world_map_window)
		return

	var map_id: = 0
	if is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model:
		map_id = int(target_player.data_model.map_id)
	if world_map_window.has_method("set_current_map"):
		world_map_window.set_current_map(map_id)
	_open_window(world_map_window)

func _refresh_big_map_context() -> void :
	if not is_instance_valid(big_map_window):
		return
	if not is_instance_valid(target_player) or not is_instance_valid(GameManager.current_map_node):
		return

	var map_id: = 0
	if "data_model" in target_player and target_player.data_model:
		map_id = int(target_player.data_model.map_id)
	var display_name: = str(REAL_MAP_NAMES.get(map_id, "")).strip_edges()
	if display_name.is_empty():
		display_name = "未知地圖"

	if big_map_window.has_method("set_player_context"):
		big_map_window.set_player_context(target_player)
	if big_map_window.has_method("set_map_context"):
		big_map_window.set_map_context(
			map_id, 
			display_name, 
			GameManager.current_map_node, 
			get_viewport().world_2d
		)

func _toggle_hud_visibility() -> void :
	var panels = [chat_panel, minimap_panel, status_panel, emotion_panel, menu_panel, btn_announcement, btn_game_info, btn_craft_dismantle, windows_container, shortcut_bar]
	if _hud_tween and _hud_tween.is_running(): _hud_tween.kill()
	_hud_tween = create_tween().set_parallel(true).set_trans(Tween.TRANS_SINE)
	_is_hud_hidden = not _is_hud_hidden
	if _is_hud_hidden:
		if is_instance_valid(minimap_npc_overlay) and minimap_npc_overlay.has_method("set_active"):
			minimap_npc_overlay.set_active(false)
		for panel in panels:
			if panel and panel.visible: _hud_tween.tween_property(panel, "modulate:a", 0.0, 0.2)
		_hud_tween.chain().tween_callback( func(): for panel in panels: if panel: panel.visible = false)
	else:
		if is_instance_valid(minimap_npc_overlay) and minimap_npc_overlay.has_method("set_active"):
			minimap_npc_overlay.set_active(true)
		_minimap_force_world_redraw = true
		_minimap_update_elapsed = MINIMAP_UPDATE_INTERVAL
		for panel in panels:
			if panel: panel.visible = true;_hud_tween.tween_property(panel, "modulate:a", 1.0, 0.2)

func _on_btn_toggle_emotion_pressed() -> void :
	if not emotion_panel: return
	if _emotion_tween and _emotion_tween.is_running(): _emotion_tween.kill()
	_emotion_tween = create_tween().set_trans(Tween.TRANS_SINE)

	if emotion_panel.visible and emotion_panel.modulate.a > 0.1:
		_emotion_tween.tween_property(emotion_panel, "modulate:a", 0.0, 0.2)
		_emotion_tween.tween_callback( func(): emotion_panel.visible = false)
	else:
		emotion_panel.visible = true;emotion_panel.modulate.a = 0.0
		_emotion_tween.tween_property(emotion_panel, "modulate:a", 1.0, 0.2)

func _on_player_emotion_selected(anim_name: String) -> void :
	if is_instance_valid(target_player) and target_player.has_method("play_emotion"):
		target_player.play_emotion(anim_name)




func _on_npc_dialog_received(data: Dictionary, from_queue: bool = false) -> void :
	# Old quest scripts can emit multiple Say() calls before an Ask().
	# Sequence only old Lua packets; other dialogs preserve their original path.
	if not from_queue:
		if data.has("LegacyPrefix") and data.has("DialogLayout"):
			if not _legacy_dialog_sequence.push_or_begin(data):
				return
		else:
			_legacy_dialog_sequence.reset()
			_legacy_dialog_line_waiting = false
	if CursorManager:
		CursorManager.set_cursor_state("normal")

	_current_npc_entity_id = int(data.get("EntityID", 0))
	_current_npc_script_name = str(data.get("ScriptName", ""))

	var dialog_type = str(data.get("DialogType", "Dialog"))
	var text = str(data.get("Text", ""))
	var bubble_type = int(data.get("BubbleType", 0))
	var speaker = str(data.get("Speaker", "NPC"))
	var options = data.get("Options", [])
	var legacy_prefix = str(data.get("LegacyPrefix", ""))
	var dialog_layout = str(data.get("DialogLayout", "Default"))
	var bubble_text = text
	# Legacy n7/p7 are narrative events rather than ordinary spoken lines.
	if dialog_layout == "Narration":
		bubble_text = "[i][color=#596777]" + text + "[/color][/i]"


	if not from_queue and is_instance_valid(target_player):
		target_player.set("is_interacting_with_npc", true)
		if target_player.has_method("_stop_movement"):
			target_player._stop_movement()

	if dialog_type == "Close":
		_on_npc_dialog_closed()
		return

	if dialog_type == "ActionDialog":
		_legacy_dialog_sequence.note_presented(data)
		_legacy_dialog_line_waiting = from_queue or data.has("LegacyPrefix")
		_next_dialog_node = str(data.get("NextNode", ""))

		if dialog_layout == "Media":
			_hide_current_dialog_bubbles()
			_ensure_legacy_cinematic_panel()
			if is_instance_valid(_legacy_cinematic_panel):
				var media: Dictionary = data.get("LegacyMedia", {}) if data.get("LegacyMedia", {}) is Dictionary else {}
				_legacy_cinematic_panel.show_media(media)
		else:
			_hide_legacy_cinematic_panel()
			if text != "":
				_trigger_npc_bubble(_current_npc_entity_id, bubble_text, bubble_type, speaker, true)
		if npc_dialog_panel: npc_dialog_panel.visible = false
		return

	_hide_legacy_cinematic_panel()
	_legacy_dialog_line_waiting = false
	_next_dialog_node = ""
	if options.is_empty():

		if text != "": _trigger_npc_bubble(_current_npc_entity_id, bubble_text, bubble_type, speaker, true)
		if npc_dialog_panel: npc_dialog_panel.visible = false
		return

	if is_instance_valid(npc_dialog_panel) and npc_dialog_panel.has_method("show_dialog"):
		_legacy_dialog_sequence.note_presented(data)
		var was_visible = npc_dialog_panel.visible
		npc_dialog_panel.show_dialog(text, options, legacy_prefix, dialog_layout)
		if not was_visible:
			_play_interface_activation_feedback(npc_dialog_panel)

func has_legacy_dialog_line_waiting() -> bool:
	return _legacy_dialog_line_waiting


func _on_npc_dialog_close_requested(packet: Dictionary = {}) -> void:
	# A late close for an older Lua execution must not dismiss a newer batch.
	if _legacy_dialog_sequence.is_stale_close(packet):
		return
	# Original quests may call QuestEnd() directly after Ask(). Keep its options
	# clickable; option handlers already close/reset the dialogue when selected.
	if _legacy_dialog_sequence.is_awaiting_choice():
		return
	# Otherwise preserve every queued Say before respecting script termination.
	if _legacy_dialog_line_waiting and _legacy_dialog_sequence.defer_close():
		return
	_on_npc_dialog_closed()


func request_next_dialog_sentence() -> void :
	_hide_legacy_cinematic_panel()
	# A terminal QuestEnd() must not swallow an explicit Say(..., "Next")
	# continuation. Queued actual Say/Ask messages still take precedence.
	var resume_explicit_node := _next_dialog_node != "" and (
		_legacy_dialog_sequence.only_terminal_close_pending()
	)
	if _legacy_dialog_sequence.has_pending() and not resume_explicit_node:
		var next_packet: Dictionary = _legacy_dialog_sequence.take_next()
		if str(next_packet.get("DialogType", "")) == "Close":
			_on_npc_dialog_closed()
		else:
			_hide_current_dialog_bubbles()
			_on_npc_dialog_received(next_packet, true)
		return
	if _next_dialog_node == "":
		_on_npc_dialog_closed()
		return

	var target_node = _next_dialog_node
	_next_dialog_node = ""
	# A server continuation starts a fresh batch, not a queued old line.
	_legacy_dialog_line_waiting = false
	_legacy_dialog_sequence.reset()

	if NetworkManager:
		if NetworkManager.has_method("send_secure_packet"):
			NetworkManager.send_secure_packet({"Type": "NpcOptionRequest", "EntityID": _current_npc_entity_id, "ScriptName": _current_npc_script_name, "TargetNode": target_node})

func _on_npc_option_selected(target_node: String) -> void :

	var req_entity_id = _current_npc_entity_id
	var req_script_name = _current_npc_script_name


	_on_npc_dialog_closed()


	if NetworkManager:
		if NetworkManager.has_method("send_secure_packet"):
			NetworkManager.send_secure_packet({
				"Type": "NpcOptionRequest", 
				"EntityID": req_entity_id, 
				"ScriptName": req_script_name, 
				"TargetNode": target_node
			})

func _on_npc_dialog_closed() -> void :
	_hide_legacy_cinematic_panel()
	_legacy_dialog_line_waiting = false
	_legacy_dialog_sequence.reset()
	if npc_dialog_panel: npc_dialog_panel.visible = false


	_hide_current_dialog_bubbles()


	_current_npc_entity_id = 0
	_current_npc_script_name = ""
	_next_dialog_node = ""


	if is_instance_valid(target_player):
		target_player.set("is_interacting_with_npc", false)

func _ensure_legacy_cinematic_panel() -> void:
	if is_instance_valid(_legacy_cinematic_panel):
		return
	if not is_instance_valid(windows_container):
		return
	_legacy_cinematic_panel = LEGACY_CINEMATIC_PANEL.new()
	_legacy_cinematic_panel.name = "LegacyCinematicPanel"
	windows_container.add_child(_legacy_cinematic_panel)
	_legacy_cinematic_panel.advance_requested.connect(request_next_dialog_sentence)


func _hide_legacy_cinematic_panel() -> void:
	if is_instance_valid(_legacy_cinematic_panel):
		_legacy_cinematic_panel.dismiss()


func _hide_current_dialog_bubbles() -> void:
	if _current_npc_entity_id != 0 and NetworkEntityManager:
		if "active_entities" in NetworkEntityManager and NetworkEntityManager.active_entities.has(_current_npc_entity_id):
			var entity = NetworkEntityManager.active_entities[_current_npc_entity_id]
			if is_instance_valid(entity) and entity.has_method("hide_chat_bubble"):
				entity.hide_chat_bubble()
	if is_instance_valid(target_player) and target_player.has_method("hide_chat_bubble"):
		target_player.hide_chat_bubble()


func _trigger_npc_bubble(entity_id: int, text: String, bubble_type: int, speaker: String = "NPC", rich_text: bool = false) -> void :
	var margins = BUBBLE_TEXT_MARGINS.get(bubble_type, BUBBLE_TEXT_MARGINS[0])


	if speaker == "Player":
		if is_instance_valid(target_player) and target_player.has_method("show_chat_bubble"):
			target_player.show_chat_bubble(text, bubble_type, margins, rich_text)
		return


	if NetworkEntityManager:
		if "active_entities" in NetworkEntityManager and NetworkEntityManager.active_entities.has(entity_id):
			var entity = NetworkEntityManager.active_entities[entity_id]
			if is_instance_valid(entity) and entity.has_method("show_chat_bubble"):
				entity.show_chat_bubble(text, bubble_type, margins, rich_text)




func set_map_loading_progress(progress: float) -> void:
	if is_instance_valid(loading_bar):
		loading_bar.set_target_progress(progress)


func show_map_loading(duration: float = 0.3) -> void :
	_hide_legacy_cinematic_panel()
	if GameManager and GameManager.is_afk_mode_active():
		# The monitor is the ONLY visible window during AFK map changes.
		# The existing hide_map_loading still completes WorldReady handshake.
		if loading_overlay:
			loading_overlay.visible = false
		return
	if not loading_overlay or not loading_bar: return

	if _map_loading_tween and _map_loading_tween.is_running():
		_map_loading_tween.kill()

	loading_overlay.visible = true
	loading_bar.reset_progress()

	_map_loading_tween = create_tween()
	_map_loading_tween.set_parallel(true)


	for child in loading_overlay.get_children():
		if child is CanvasItem:
			child.modulate.a = 0.0
			_map_loading_tween.tween_property(child, "modulate:a", 1.0, duration).set_trans(Tween.TRANS_SINE)

func hide_map_loading() -> void :
	if _map_loading_tween and _map_loading_tween.is_running():
		_map_loading_tween.kill()

	if loading_bar: loading_bar.finish_progress()

	# Cancel a previous fade-out when a new map change starts, so an old
	# finish callback cannot clear the newer overlay or send stale WorldReady.
	_map_loading_tween = create_tween().set_parallel(true)
	var fade_tween: Tween = _map_loading_tween
	var has_fade_target = false


	for child in loading_overlay.get_children():
		if child is CanvasItem:
			fade_tween.tween_property(child, "modulate:a", 0.0, 0.4).set_trans(Tween.TRANS_SINE)
			has_fade_target = true

	if has_fade_target:
		fade_tween.chain().tween_callback(_finish_map_loading)
	else:
		_finish_map_loading()

func _finish_map_loading() -> void :
	if loading_overlay:
		loading_overlay.visible = false
	# Keep the 0.4-second loading fade drawable; switching maps must not
	# pause the render loop until this callback has hidden the overlay.
	if GameManager and GameManager.has_method("notify_world_loading_overlay_hidden"):
		GameManager.notify_world_loading_overlay_hidden()
	if NetworkManager and NetworkManager.has_method("send_world_ready"):
		var map_id := 0
		if is_instance_valid(target_player) and target_player.get("data_model"):
			map_id = int(target_player.data_model.map_id)
		elif GameManager and is_instance_valid(GameManager.current_player) and GameManager.current_player.get("data_model"):
			map_id = int(GameManager.current_player.data_model.map_id)
		NetworkManager.send_world_ready(map_id)


func show_player_context_menu(target_dbid: int, target_name: String) -> void :
	if is_instance_valid(context_menu_instance):
		context_menu_instance.queue_free()

	context_menu_instance = PLAYER_CONTEXT_MENU_SCENE.instantiate()
	add_child(context_menu_instance)
	context_menu_instance.global_position = get_viewport().get_mouse_position()
	context_menu_instance.setup(target_dbid, target_name)
	_play_interface_activation_feedback(context_menu_instance)

func _on_trade_invite_received(requester_dbid: int, requester_name: String) -> void :
	_show_invite_box(
		requester_name + "：你願意與我交易嗎？", 
		func(): NetworkManager.send_trade_reply(requester_dbid, true), 
		func(): NetworkManager.send_trade_reply(requester_dbid, false)
	)

func _on_party_invite_received(inviter_dbid: int, inviter_name: String) -> void :
	if not invite_box:
		return
	_show_invite_box(
		inviter_name + "：你願意加入我的隊伍嗎？", 
		func():
			if NetworkManager:
				NetworkManager.send_party_action("Accept", inviter_dbid), 
		func():
			if NetworkManager:
				NetworkManager.send_party_action("Decline", inviter_dbid)
	)
	if AudioManager:
		AudioManager.play_se(PARTY_INVITATION_SE_ID)


func _show_invite_box(message: String, on_accept: Callable, on_decline: Callable) -> void :
	if not invite_box: return
	var was_visible = invite_box.visible

	lbl_message.text = message
	invite_box.visible = true
	invite_box.move_to_front()
	if not was_visible:
		_play_interface_activation_feedback(invite_box)

	_invite_time_left = 30.0
	_invite_timeout_callback = on_decline
	_last_invite_display_second = 30
	if lbl_timer: lbl_timer.text = str(_last_invite_display_second)

	for conn in btn_accept.pressed.get_connections():
		btn_accept.pressed.disconnect(conn.callable)
	for conn in btn_decline.pressed.get_connections():
		btn_decline.pressed.disconnect(conn.callable)

	btn_accept.pressed.connect( func():
		_invite_time_left = 0.0
		on_accept.call()
		invite_box.visible = false
	)

	btn_decline.pressed.connect( func():
		_invite_time_left = 0.0
		on_decline.call()
		invite_box.visible = false
	)


func _on_trade_start_received(session_id: String, target_name: String) -> void :
	if is_instance_valid(current_trade_window):
		current_trade_window.queue_free()

	current_trade_window = TRADE_WINDOW_SCENE.instantiate()
	windows_container.add_child(current_trade_window)
	current_trade_window.setup(target_player, target_name)
	_play_interface_activation_feedback(current_trade_window)

	if inventory_window: inventory_window.visible = false


func _on_btn_shop_pressed() -> void :
	if not is_instance_valid(target_player) or not "data_model" in target_player: return

	if target_player.is_action_locked:
		show_rejection_message_flash("目前狀態無法開啟商店！")
		return

	if inventory_window and inventory_window.visible and inventory_window.modulate.a > 0.1:
		_close_window(inventory_window)

	if "is_shop_open" in target_player.data_model and target_player.data_model.is_shop_open:
		var s_name = target_player.data_model.shop_name if "shop_name" in target_player.data_model else ""
		_open_shop_window(s_name, true)
	else:

		var default_shop_name: = "%s的小商店" % target_player.data_model.character_name
		show_input_box(
			"創建小商店", 
			func(shop_name: String):
				if shop_name.strip_edges() == "":
					show_rejection_message_box("小商店名稱不可為空！")
					return
				_open_shop_window(shop_name.strip_edges(), false), 
			Callable(), 
			"請輸入小商店名稱", 
			max(12, default_shop_name.length())
		)
		input_box_line_edit.text = default_shop_name
		input_box_line_edit.caret_column = default_shop_name.length()

func _open_shop_window(shop_name: String, is_already_open: bool) -> void :
	if is_instance_valid(current_shop_window): current_shop_window.queue_free()
	current_shop_window = SHOP_WINDOW_SCENE.instantiate()
	windows_container.add_child(current_shop_window)
	current_shop_window.setup_as_owner(target_player, shop_name, is_already_open)
	if current_shop_window.has_method("update_ui"): current_shop_window.update_ui()
	_play_interface_activation_feedback(current_shop_window)


	if NetworkManager:
		NetworkManager.send_shop_action("SyncMyShop", {})

func _on_shop_sync_received(items: Array = []) -> void :

	if is_instance_valid(current_shop_window) and current_shop_window.has_method("sync_shop_items"):
		current_shop_window.sync_shop_items(items)

func _on_shop_view_response(shop_name: String, owner_dbid: int, items: Array) -> void :
	if is_instance_valid(current_shop_window): current_shop_window.queue_free()
	current_shop_window = SHOP_WINDOW_SCENE.instantiate()
	windows_container.add_child(current_shop_window)
	current_shop_window.setup_as_customer(shop_name, owner_dbid, items)
	_play_interface_activation_feedback(current_shop_window)

func show_horse_preview(horse_data: Dictionary) -> void :

	if is_instance_valid(current_horse_preview_window):
		current_horse_preview_window.queue_free()

	current_horse_preview_window = HORSE_PREVIEW_WINDOW_SCENE.instantiate()
	windows_container.add_child(current_horse_preview_window)
	_register_draggable_window(current_horse_preview_window)
	current_horse_preview_window.setup(target_player, horse_data)
	_play_interface_activation_feedback(current_horse_preview_window)

func show_pet_window(pet_uid: int) -> void :

	if is_instance_valid(current_pet_window):
		current_pet_window.queue_free()


	current_pet_window = PET_WINDOW_SCENE.instantiate()
	windows_container.add_child(current_pet_window)
	_register_draggable_window(current_pet_window)


	if current_pet_window.has_method("open_pet"):
		current_pet_window.open_pet(pet_uid)
	elif current_pet_window.has_method("set_selected_pet"):
		current_pet_window.set_selected_pet(pet_uid)
	_play_interface_activation_feedback(current_pet_window)







func show_message_box(message: String, type: int = 1, on_confirm: Callable = Callable(), on_cancel: Callable = Callable()) -> void :
	if not message_box: return
	var was_visible = message_box.visible

	msg_box_label.text = message
	message_box.visible = true
	message_box.move_to_front()
	if not was_visible:
		_play_interface_activation_feedback(message_box)


	if msg_box_btn_cancel:
		msg_box_btn_cancel.visible = (type == 2)


	if msg_box_btn_confirm:
		for conn in msg_box_btn_confirm.pressed.get_connections():
			msg_box_btn_confirm.pressed.disconnect(conn.callable)

	if msg_box_btn_cancel:
		for conn in msg_box_btn_cancel.pressed.get_connections():
			msg_box_btn_cancel.pressed.disconnect(conn.callable)


	if msg_box_btn_confirm:
		msg_box_btn_confirm.pressed.connect( func():
			message_box.visible = false
			if on_confirm.is_valid():
				on_confirm.call()
		)


	if msg_box_btn_cancel:
		msg_box_btn_cancel.pressed.connect( func():
			message_box.visible = false
			if on_cancel.is_valid():
				on_cancel.call()
		)

func show_rejection_message_box(message: String) -> void :
	show_message_box(message, 1)
	_play_rejection_feedback_se()

func _play_rejection_feedback_se() -> void :
	if AudioManager:
		AudioManager.play_se(REJECTION_FEEDBACK_SE_ID)








func show_input_box(title: String, on_confirm: Callable, on_cancel: Callable = Callable(), placeholder: String = "", max_length: int = 0) -> void :
	if not input_box: return
	var was_visible = input_box.visible
	if spin_input_box:
		spin_input_box.visible = false

	input_box_label.text = title
	input_box_line_edit.text = ""


	input_box_line_edit.placeholder_text = placeholder
	input_box_line_edit.max_length = max_length

	input_box.visible = true
	input_box.move_to_front()
	if not was_visible:
		_play_interface_activation_feedback(input_box)
	input_box_line_edit.grab_focus()


	if input_box_btn_confirm:
		for conn in input_box_btn_confirm.pressed.get_connections():
			input_box_btn_confirm.pressed.disconnect(conn.callable)

	if input_box_btn_cancel:
		for conn in input_box_btn_cancel.pressed.get_connections():
			input_box_btn_cancel.pressed.disconnect(conn.callable)


	if input_box_btn_confirm:
		input_box_btn_confirm.pressed.connect( func():
			var input_text = input_box_line_edit.text.strip_edges()
			input_box.visible = false
			if on_confirm.is_valid():
				on_confirm.call(input_text)
		)


	if input_box_btn_cancel:
		input_box_btn_cancel.pressed.connect( func():
			input_box.visible = false
			if on_cancel.is_valid():
				on_cancel.call()
		)



func show_spin_box(title: String, on_confirm: Callable, on_cancel: Callable = Callable(), min_value: int = 0, max_value: int = 100, initial_value: int = 0) -> void :
	if not spin_input_box or not spin_input_box_input:
		return

	var normalized_max: = maxi(max_value, min_value)
	var normalized_initial: = clampi(initial_value, min_value, normalized_max)
	var was_visible: = spin_input_box.visible

	if input_box:
		input_box.visible = false

	spin_input_box_label.text = title
	spin_input_box_input.min_value = min_value
	spin_input_box_input.max_value = normalized_max
	spin_input_box_input.step = 1.0
	spin_input_box_input.rounded = true
	spin_input_box_input.allow_lesser = false
	spin_input_box_input.allow_greater = false
	spin_input_box_input.value = normalized_initial

	spin_input_box.visible = true
	spin_input_box.move_to_front()
	if not was_visible:
		_play_interface_activation_feedback(spin_input_box)
	spin_input_box_input.get_line_edit().grab_focus()
	spin_input_box_input.get_line_edit().select_all()

	if spin_input_box_btn_confirm:
		for conn in spin_input_box_btn_confirm.pressed.get_connections():
			spin_input_box_btn_confirm.pressed.disconnect(conn.callable)

	if spin_input_box_btn_cancel:
		for conn in spin_input_box_btn_cancel.pressed.get_connections():
			spin_input_box_btn_cancel.pressed.disconnect(conn.callable)

	if spin_input_box_btn_confirm:
		spin_input_box_btn_confirm.pressed.connect( func():
			var input_value: = clampi(int(spin_input_box_input.value), min_value, normalized_max)
			spin_input_box.visible = false
			if on_confirm.is_valid():
				on_confirm.call(input_value)
		)

	if spin_input_box_btn_cancel:
		spin_input_box_btn_cancel.pressed.connect( func():
			spin_input_box.visible = false
			if on_cancel.is_valid():
				on_cancel.call()
		)


func _init_shortcut_bar() -> void :
	if not shortcut_bar: return


	var container = shortcut_bar.get_node_or_null("GridContainer")


	for child in container.get_children():
		child.queue_free()


	for i in range(12):
		var slot = SHORTCUT_SLOT_SCENE.instantiate()
		container.add_child(slot)

		slot.setup(i, "F%d" % (i + 1))
		slot.shortcut_triggered.connect(_on_shortcut_triggered)


		slot.slot_mouse_entered.connect(_show_shortcut_tooltip)
		slot.slot_mouse_exited.connect(_hide_shortcut_tooltip)


func _on_shortcut_triggered(slot_index: int, type: String, target_id: int) -> void :
	if type == "SKILL":
		if is_instance_valid(target_player) and target_player.has_method("prepare_skill"):
			target_player.prepare_skill(target_id)
	elif type == "ITEM":
		if is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model:
			var inventory = target_player.data_model.inventory
			var item_to_use = null
			for item in inventory:
				if item.get("in_bank", 0) == 0 and item.get("in_shop", 0) == 0:
					if item.get("item_index", 0) == target_id and item.get("count", 0) > 0:
						item_to_use = item
						break
			if item_to_use:
				var item_uid = int(item_to_use.get("item_uid", 0))
				if NetworkManager:
					NetworkManager.send_use_item_request(item_uid)
			else:
				show_rejection_message_flash("數量不足！")
	elif type == "PET":
		if is_instance_valid(current_pet_window) and current_pet_window.visible and current_pet_window.get("selected_pet_uid") == target_id:

			current_pet_window.queue_free()
			current_pet_window = null
		else:

			show_pet_window(target_id)
	elif type == "MECHA":
		if is_instance_valid(current_horse_preview_window) and current_horse_preview_window.visible and current_horse_preview_window.current_horse_data.get("horse_uid", -1) == target_id:

			current_horse_preview_window.queue_free()
			current_horse_preview_window = null
		else:
			if is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model:
				var horse_data = null
				for h in target_player.data_model.horse_list:
					if int(h.get("horse_uid", h.get("HorseUID", -1))) == target_id:
						horse_data = h
						break
				if horse_data:
					show_horse_preview(horse_data)

func _get_all_slots() -> Array:
	var slots = []
	if shortcut_bar:

		var container = shortcut_bar.get_node_or_null("GridContainer")
		if container:
			for child in container.get_children():
				if child.has_method("trigger_slot"): slots.append(child)
	return slots

func _load_shortcuts_from_data() -> void :
	if not is_instance_valid(target_player) or not "data_model" in target_player: return
	var p_data = target_player.data_model

	var shortcuts = []
	if "shortcuts" in p_data: shortcuts = p_data.shortcuts
	elif p_data.has_meta("shortcuts"): shortcuts = p_data.get_meta("shortcuts")

	var slots = _get_all_slots()

	for s_data in shortcuts:
		var s_idx = int(s_data.get("SlotIndex", -1))
		var s_type = int(s_data.get("ShortcutType", 0))
		var t_id = int(s_data.get("TargetID", 0))

		if s_idx >= 0 and s_idx < slots.size():
			var slot = slots[s_idx]
			if s_type == 1:
				if ItemLoader:
					var icon_id = ItemLoader.get_item_property(t_id, "icon_id", 1) - 1
					var tex = ItemLoader.get_photo(icon_id)

					var color_index = ItemLoader.get_item_property(t_id, "color_index", 0)
					var effect_type = int(ItemLoader.get_item_property(t_id, "effect_type", 0))
					var mat = null
					if color_index > 0 or effect_type > 0:
						mat = ShaderMaterial.new()
						mat.shader = preload("res://palette_swap.gdshader")
						mat.set_shader_parameter("effect_type", effect_type)
						var COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
						mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
					slot.set_slot("ITEM", t_id, tex, mat, -1, false)

			elif s_type == 2:
				if SkillLoader:
					var s_res = SkillLoader.get_skill_data(t_id)
					if s_res:
						var tex = SkillLoader.get_icon(s_res.icon_id)
						slot.set_slot("SKILL", t_id, tex, null, -1, false)

			elif s_type == 3:
				var pet_data = null
				for p in p_data.pet_list:
					if int(p.get("uid", p.get("UID", 0))) == t_id:
						pet_data = p
						break

				if pet_data:
					var pet_id = int(pet_data.get("pet_id", pet_data.get("PETID", 0)))
					var evol_id = int(pet_data.get("evol_id", pet_data.get("EvolID", 0)))
					var tex = null
					var mat = null

					if PetLoader:
						var config = PetLoader.get_pet_config(pet_id)
						if config and config.has("evolutions"):
							var evol_key = str(evol_id)
							if config.evolutions.has(evol_key):
								var evol_info = config.evolutions[evol_key]
								var color_index = int(evol_info.get("color", 0))
								if color_index > 0:
									mat = ShaderMaterial.new()
									mat.shader = preload("res://palette_swap.gdshader")
									mat.set_shader_parameter("effect_type", 0)
									var COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
									mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))

								var outlook = str(evol_info.get("outlook", ""))
								if outlook != "":
									var photo_path = "res://Assets/Pets/Photo/" + outlook + ".png"
									if ResourceLoader.exists(photo_path):
										tex = load(photo_path)

					if tex == null and ItemLoader:
						tex = ItemLoader.get_icon(375)

					slot.set_slot("PET", t_id, tex, mat, -1, false)

			elif s_type == 4:
				var horse_data = null
				for h in p_data.horse_list:
					if int(h.get("horse_uid", h.get("HorseUID", -1))) == t_id:
						horse_data = h
						break

				if horse_data:
					var h_idx = int(horse_data.get("horse_idx", horse_data.get("HorseIdx", 0)))
					var tex = null
					var mat = null

					if ItemLoader:

						var raw_id = int(ItemLoader.get_item_property(h_idx, "photo_id", ItemLoader.get_item_property(h_idx, "icon_id", 1)))
						var target_img_id = raw_id - 1


						tex = ItemLoader.get_photo(target_img_id)

						var color_index = int(horse_data.get("Color", horse_data.get("color", ItemLoader.get_item_property(h_idx, "color_index", 0))))
						var effect_type = int(horse_data.get("EffectType", horse_data.get("effect_type", ItemLoader.get_item_property(h_idx, "effect_type", 0))))
						if color_index > 0 or effect_type > 0:
							mat = ShaderMaterial.new()
							mat.shader = preload("res://palette_swap.gdshader")
							mat.set_shader_parameter("effect_type", effect_type)
							var COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
							mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))

					slot.set_slot("MECHA", t_id, tex, mat, -1, false)

	update_shortcut_amounts()


func _show_shortcut_tooltip(type: String, target_id: int, tex: Texture2D, mat: Material = null) -> void :
	if shared_tooltip: shared_tooltip.show_shortcut(type, target_id, tex, mat)

func _hide_shortcut_tooltip() -> void :
	if shared_tooltip: shared_tooltip.hide_tooltip()


func _show_buff_tooltip(type: String, target_id: int, tex: Texture2D, mat: Material = null) -> void :
	if shared_tooltip: shared_tooltip.show_buff(type, target_id, tex, mat)

func _hide_buff_tooltip() -> void :
	if shared_tooltip: shared_tooltip.hide_tooltip()

func update_shortcut_amounts() -> void :
	if not is_instance_valid(target_player) or not "data_model" in target_player: return
	var inventory = []
	if "inventory" in target_player.data_model:
		inventory = target_player.data_model.inventory


	var item_counts = {}
	for item in inventory:
		if item.get("in_bank", 0) == 0:
			var idx = int(item.get("item_index", item.get("item_idx", 0)))
			item_counts[idx] = item_counts.get(idx, 0) + int(item.get("count", 1))


	var slots = _get_all_slots()
	for slot in slots:
		if slot.shortcut_type == "ITEM":
			slot.update_amount(item_counts.get(slot.target_id, 0))


func _on_sync_buffs_received(buffs: Array) -> void :
	if not buff_grid: return

	var current_buff_names = []
	for b in buffs:
		var b_name = str(b.get("BuffName", ""))
		current_buff_names.append(b_name)

		var source_type = int(b.get("SourceType", 0))
		var value = int(b.get("Value", 0))
		var rem_time = float(b.get("RemainingTime", 0.0))
		var tot_time = float(b.get("TotalTime", rem_time))

		var existing_icon = buff_grid.get_node_or_null(b_name)
		if existing_icon:
			existing_icon.update_time(rem_time, tot_time)
		else:
			var new_icon = BUFF_ICON_SCENE.instantiate()
			new_icon.name = b_name
			buff_grid.add_child(new_icon)

			var tex: Texture2D = null
			var mat: Material = null
			var tip_type: String = ""

			if source_type == 1 and ItemLoader:
				var icon_id = ItemLoader.get_item_property(value, "icon_id", 1) - 1
				tex = ItemLoader.get_photo(icon_id)

				var color_index = ItemLoader.get_item_property(value, "color_index", 0)
				var effect_type = int(ItemLoader.get_item_property(value, "effect_type", 0))
				if color_index > 0 or effect_type > 0:
					mat = ShaderMaterial.new()
					mat.shader = preload("res://palette_swap.gdshader")
					mat.set_shader_parameter("effect_type", effect_type)
					var COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
					mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))

				tip_type = "ITEM"

			elif source_type == 2 and SkillLoader:
				var s_res = SkillLoader.get_skill_data(value)
				if s_res:
					tex = SkillLoader.get_icon(s_res.icon_id)
				tip_type = "SKILL"


			elif (source_type == 3 or b_name.begins_with("Transform")) and ItemLoader:

				var item_id = value if value > 1000 else 2802

				var icon_id = ItemLoader.get_item_property(item_id, "icon_id", 1) - 1
				tex = ItemLoader.get_photo(icon_id)


				if tex == null:
					tex = ItemLoader.get_icon(icon_id)

				var color_index = ItemLoader.get_item_property(item_id, "color_index", 0)
				var effect_type = int(ItemLoader.get_item_property(item_id, "effect_type", 0))
				if color_index > 0 or effect_type > 0:
					mat = ShaderMaterial.new()
					mat.shader = preload("res://palette_swap.gdshader")
					mat.set_shader_parameter("effect_type", effect_type)
					var COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
					mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))

				tip_type = "ITEM"
				value = item_id

			var display_minutes = b_name.begins_with("ExperienceBoost_")
			new_icon.setup(b_name, tex, mat, rem_time, tot_time, display_minutes)


			if tip_type != "":
				new_icon.mouse_entered.connect(_show_buff_tooltip.bind(tip_type, value, tex, mat))
				new_icon.mouse_exited.connect(_hide_buff_tooltip)

	for child in buff_grid.get_children():
		if child.name not in current_buff_names:
			child.queue_free()


func _on_npc_shop_view_response_received(shop_id: int, shop_name: String, items: Array) -> void :

	if npc_dialog_panel and npc_dialog_panel.visible:
		npc_dialog_panel.visible = false


	if is_instance_valid(current_npc_shop_window):
		current_npc_shop_window.queue_free()


	current_npc_shop_window = NPC_SHOP_WINDOW_SCENE.instantiate()
	windows_container.add_child(current_npc_shop_window)
	current_npc_shop_window.setup(shop_id, shop_name, items)
	_play_interface_activation_feedback(current_npc_shop_window)


func _on_club_invite_received(inviter_dbid: int, inviter_name: String, club_name: String) -> void :
	_show_invite_box(
		inviter_name + " 邀請你加入 " + club_name, 
		func():
			if NetworkManager and NetworkManager.has_method("send_secure_packet"):
				NetworkManager.send_secure_packet({"Type": "ClubAction", "Action": "Accept", "TargetDBID": inviter_dbid}), 
		func(): pass
	)

func _on_open_create_club_window_received() -> void :

	if npc_dialog_panel and npc_dialog_panel.visible:
		npc_dialog_panel.visible = false


	show_input_box(
		"創建戰團", 
		func(club_name: String):
			if club_name.is_empty():
				show_rejection_message_box("戰團名稱不可為空白！")
				return


			if NetworkManager and NetworkManager.has_method("send_secure_packet"):
				NetworkManager.send_secure_packet({
					"Type": "ClubAction", 
					"Action": "Create", 
					"ClubName": club_name
				}), 
		Callable(), 
		"請輸入戰團名稱", 
		8
	)


func _on_opt_volume_bg_item_selected(index: int) -> void :
	if not is_instance_valid(settings_window): return

	var opt = settings_window.get_node_or_null("OptionVolumeBg") as OptionButton
	if opt:

		var text = opt.get_item_text(index)

		var val_str = text.replace("%", "").strip_edges()


		if val_str.is_valid_float() or val_str.is_valid_int():
			var percentage = val_str.to_float()
			if AudioManager and AudioManager.has_method("set_bgm_volume"):
				AudioManager.set_bgm_volume(percentage)

func _on_opt_volume_effect_item_selected(index: int) -> void :
	if not is_instance_valid(settings_window): return

	var opt = settings_window.get_node_or_null("OptionVolumeEffect") as OptionButton
	if opt:
		var text = opt.get_item_text(index)
		var val_str = text.replace("%", "").strip_edges()

		if val_str.is_valid_float() or val_str.is_valid_int():
			var percentage = val_str.to_float()
			if AudioManager and AudioManager.has_method("set_se_volume"):
				AudioManager.set_se_volume(percentage)





func _on_option_gui_input(event: InputEvent, option_btn: OptionButton) -> void :
	if event is InputEventMouseButton and event.pressed:
		match event.button_index:
			MOUSE_BUTTON_WHEEL_UP:
				_select_previous_option(option_btn)
				option_btn.accept_event()
			MOUSE_BUTTON_WHEEL_DOWN:
				_select_next_option(option_btn)
				option_btn.accept_event()

func _select_previous_option(option_btn: OptionButton) -> void :

	if option_btn.item_count <= 0 or option_btn.disabled: return

	var idx = option_btn.selected
	while idx > 0:
		idx -= 1

		if not option_btn.is_item_disabled(idx) and not option_btn.is_item_separator(idx):
			option_btn.select(idx)
			option_btn.item_selected.emit(idx)
			return

func _select_next_option(option_btn: OptionButton) -> void :
	if option_btn.item_count <= 0 or option_btn.disabled: return

	var idx = option_btn.selected
	while idx < option_btn.item_count - 1:
		idx += 1
		if not option_btn.is_item_disabled(idx) and not option_btn.is_item_separator(idx):
			option_btn.select(idx)
			option_btn.item_selected.emit(idx)
			return


func show_transfer_input_box(target_dbid: int, p_target_name: String) -> void :
	var available_money: = 0
	if is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model:
		available_money = int(target_player.data_model.money)

	if available_money <= 0:
		show_rejection_message_box("身上金錢不足，無法轉帳！")
		return

	var max_transfer_amount: = mini(available_money, 9999999999)
	show_spin_box(
		"轉帳", 
		func(amount: int):
			if amount <= 0:
				show_rejection_message_box("轉帳金額必須大於 0！")
				return


			if is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model:
				if target_player.data_model.money < amount:
					show_rejection_message_box("身上金錢不足，無法轉帳！")
					return


			if NetworkManager:
				NetworkManager.send_direct_transfer(target_dbid, amount), 
		Callable(), 
		1, 
		max_transfer_amount, 
		1
	)


func _bind_option_popup_cursor(option_button: OptionButton) -> void:
	if option_button == null:
		return
	var popup := option_button.get_popup()
	if popup == null:
		return
	if not popup.about_to_popup.is_connected(_on_system_option_popup_opened.bind(popup)):
		popup.about_to_popup.connect(_on_system_option_popup_opened.bind(popup))
	if not popup.popup_hide.is_connected(_on_system_option_popup_closed.bind(popup)):
		popup.popup_hide.connect(_on_system_option_popup_closed.bind(popup))

func _on_system_option_popup_opened(popup: PopupMenu) -> void:
	if CursorManager and CursorManager.has_method("show_popup_game_cursor"):
		CursorManager.show_popup_game_cursor(popup)

func _on_system_option_popup_closed(popup: PopupMenu) -> void:
	if CursorManager and CursorManager.has_method("hide_popup_game_cursor"):
		CursorManager.hide_popup_game_cursor(popup)


func _game_settings_section() -> String:
	var account := ""
	if GlobalTempData:
		account = str(GlobalTempData.login_user).strip_edges().to_lower()
	if account.is_empty():
		return GameConstants.SETTING_SECTION_GAME
	return "%s:%s" % [
		GameConstants.SETTING_SECTION_GAME,
		account
	]

func _load_game_settings() -> void :
	var config = ConfigFile.new()
	var err = config.load(GameConstants.CLIENT_SETTINGS_PATH)
	var section := _game_settings_section()
	var fallback_section := GameConstants.SETTING_SECTION_GAME

	if err == OK:
		current_pet_combat_mode = int(config.get_value(
			section,
			GameConstants.SETTING_KEY_PET_COMBAT_MODE,
			config.get_value(
				fallback_section,
				GameConstants.SETTING_KEY_PET_COMBAT_MODE,
				GameConstants.SETTING_DEFAULT_PET_COMBAT_MODE
			)
		))
		current_fps_limit = ClientPerformanceProfile.sanitize_fps_limit(int(config.get_value(
			section,
			GameConstants.SETTING_KEY_FOREGROUND_FPS_LIMIT,
			config.get_value(
				fallback_section,
				GameConstants.SETTING_KEY_FOREGROUND_FPS_LIMIT,
				GameConstants.SETTING_DEFAULT_FOREGROUND_FPS_LIMIT
			)
		)))
		current_performance_load_mode = ClientPerformanceProfile.sanitize_load_mode(int(config.get_value(
			section,
			GameConstants.SETTING_KEY_PERFORMANCE_LOAD_MODE,
			config.get_value(
				fallback_section,
				GameConstants.SETTING_KEY_PERFORMANCE_LOAD_MODE,
				GameConstants.SETTING_DEFAULT_PERFORMANCE_LOAD_MODE
			)
		)))
		current_combat_render_mode = ClientPerformanceProfile.sanitize_combat_mode(int(config.get_value(
			section,
			GameConstants.SETTING_KEY_COMBAT_RENDER_MODE,
			config.get_value(
				fallback_section,
				GameConstants.SETTING_KEY_COMBAT_RENDER_MODE,
				GameConstants.SETTING_DEFAULT_COMBAT_RENDER_MODE
			)
		)))
		current_simplify_players_mode = ClientPerformanceProfile.sanitize_simplify_mode(int(config.get_value(
			section,
			GameConstants.SETTING_KEY_SIMPLIFY_PLAYERS_MODE,
			config.get_value(
				fallback_section,
				GameConstants.SETTING_KEY_SIMPLIFY_PLAYERS_MODE,
				GameConstants.SETTING_DEFAULT_SIMPLIFY_PLAYERS_MODE
			)
		)))
		current_simplify_monsters_mode = ClientPerformanceProfile.sanitize_simplify_mode(int(config.get_value(
			section,
			GameConstants.SETTING_KEY_SIMPLIFY_MONSTERS_MODE,
			config.get_value(
				fallback_section,
				GameConstants.SETTING_KEY_SIMPLIFY_MONSTERS_MODE,
				GameConstants.SETTING_DEFAULT_SIMPLIFY_MONSTERS_MODE
			)
		)))
	else:
		current_pet_combat_mode = GameConstants.SETTING_DEFAULT_PET_COMBAT_MODE
		current_fps_limit = GameConstants.SETTING_DEFAULT_FOREGROUND_FPS_LIMIT
		current_performance_load_mode = GameConstants.SETTING_DEFAULT_PERFORMANCE_LOAD_MODE
		current_combat_render_mode = GameConstants.SETTING_DEFAULT_COMBAT_RENDER_MODE
		current_simplify_players_mode = GameConstants.SETTING_DEFAULT_SIMPLIFY_PLAYERS_MODE
		current_simplify_monsters_mode = GameConstants.SETTING_DEFAULT_SIMPLIFY_MONSTERS_MODE

	if GameManager:
		GameManager.set_foreground_fps_limit(current_fps_limit)
		GameManager.set_performance_modes(
			current_performance_load_mode,
			current_combat_render_mode
		)
		GameManager.set_visual_simplification_modes(
			current_simplify_players_mode,
			current_simplify_monsters_mode
		)

func _save_game_settings() -> void :
	var config = ConfigFile.new()
	config.load(GameConstants.CLIENT_SETTINGS_PATH)
	var section := _game_settings_section()
	config.set_value(
		section,
		GameConstants.SETTING_KEY_PET_COMBAT_MODE,
		current_pet_combat_mode
	)
	config.set_value(section, GameConstants.SETTING_KEY_FOREGROUND_FPS_LIMIT, current_fps_limit)
	config.set_value(
		section,
		GameConstants.SETTING_KEY_PERFORMANCE_LOAD_MODE,
		current_performance_load_mode
	)
	config.set_value(
		section,
		GameConstants.SETTING_KEY_COMBAT_RENDER_MODE,
		current_combat_render_mode
	)
	config.set_value(
		section,
		GameConstants.SETTING_KEY_SIMPLIFY_PLAYERS_MODE,
		current_simplify_players_mode
	)
	config.set_value(
		section,
		GameConstants.SETTING_KEY_SIMPLIFY_MONSTERS_MODE,
		current_simplify_monsters_mode
	)
	config.save(GameConstants.CLIENT_SETTINGS_PATH)


func _on_opt_pet_mode_item_selected(index: int) -> void :
	current_pet_combat_mode = index
	_save_game_settings()


	if NetworkManager and NetworkManager.has_method("send_set_global_pet_mode"):
		NetworkManager.send_set_global_pet_mode(current_pet_combat_mode)

func _on_opt_fps_limit_value_changed(raw_value: float) -> void:
	if not option_fps_limit is SpinBox:
		return
	var selected: int = ClientPerformanceProfile.normalize_fps_spin_step(
		current_fps_limit, int(round(raw_value))
	)
	# Reflect normalized values back to the numeric editor without recursion.
	if int(round(option_fps_limit.value)) != selected:
		option_fps_limit.set_value_no_signal(float(selected))
		# Prevent a stale typed text value from restoring an invalid number
		# when the LineEdit loses keyboard focus.
		option_fps_limit.get_line_edit().text = str(selected)
	if selected == current_fps_limit:
		return
	current_fps_limit = selected
	_save_game_settings()
	if GameManager:
		GameManager.set_foreground_fps_limit(current_fps_limit)

func _on_opt_performance_load_item_selected(index: int) -> void:
	current_performance_load_mode = ClientPerformanceProfile.sanitize_load_mode(index)
	_save_game_settings()
	if GameManager:
		GameManager.set_performance_modes(
			current_performance_load_mode,
			current_combat_render_mode
		)

func _on_opt_combat_render_item_selected(index: int) -> void:
	current_combat_render_mode = ClientPerformanceProfile.sanitize_combat_mode(index)
	_save_game_settings()
	if GameManager:
		GameManager.set_performance_modes(
			current_performance_load_mode,
			current_combat_render_mode
		)

func _on_opt_simplify_players_item_selected(index: int) -> void:
	current_simplify_players_mode = ClientPerformanceProfile.sanitize_simplify_mode(index)
	_save_game_settings()
	if GameManager:
		GameManager.set_visual_simplification_modes(
			current_simplify_players_mode,
			current_simplify_monsters_mode
		)

func _on_opt_simplify_monsters_item_selected(index: int) -> void:
	current_simplify_monsters_mode = ClientPerformanceProfile.sanitize_simplify_mode(index)
	_save_game_settings()
	if GameManager:
		GameManager.set_visual_simplification_modes(
			current_simplify_players_mode,
			current_simplify_monsters_mode
		)


func show_revive_prompt(caster_name: String, skill_id: int, skill_level: int, hp_pct: int, mp_pct: int) -> void :
	var msg = "「%s」嘗試將你復活\n(恢復HP %d%% / MP %d%%)\n\n你想要現在復活嗎？" % [caster_name, hp_pct, mp_pct]

	show_message_box(
		msg, 
		2, 
		func():
			if NetworkManager and NetworkManager.has_method("send_revive_accept"):
				NetworkManager.send_revive_accept(skill_id, skill_level), 
		Callable()
	)

func _on_quest_updated_hud(_data = null) -> void :

	if quest_window and quest_window.visible and quest_window.has_method("update_ui"):
		quest_window.update_ui()


	if quest_tracker_panel and quest_tracker_panel.has_method("update_tracker"):
		quest_tracker_panel.update_tracker()


	if NetworkEntityManager:
		for entity in NetworkEntityManager.active_entities.values():
			if is_instance_valid(entity) and entity.has_method("update_quest_indicator"):
				entity.update_quest_indicator()



func _on_open_repair_window_received(repair_type: int = 1) -> void :
	if has_node("WindowsContainer/NpcDialogPanel"):
		$WindowsContainer / NpcDialogPanel.visible = false

	if is_instance_valid(current_repair_window):
		current_repair_window.queue_free()

	current_repair_window = REPAIR_WINDOW_SCENE.instantiate()
	windows_container.add_child(current_repair_window)

	if is_instance_valid(target_player):
		current_repair_window.setup(target_player, repair_type)
	_play_interface_activation_feedback(current_repair_window)

func _on_open_megaphone_window_received(item_uid: int) -> void :
	show_input_box(
		"大聲公", 
		func(input_text: String):
			if input_text.strip_edges() == "":
				show_rejection_message_box("訊息內容不可為空！")
				return
			if NetworkManager:
				NetworkManager.send_megaphone_message(item_uid, input_text.strip_edges()), 
		Callable(), 
		"請輸入訊息內容", 
		50
	)


func _on_btn_mall_pressed() -> void :
	if is_instance_valid(mall_window) and mall_window.visible and mall_window.modulate.a > 0.1:
		_close_window(mall_window)
		return

	mall_window = await _ensure_lazy_window("mall")
	if not is_instance_valid(mall_window):
		return
	if NetworkManager and NetworkManager.has_method("send_mall_action"):
		NetworkManager.send_mall_action("OpenMall")

func _on_mall_list_received(points: int, items: Array, gifts: Array) -> void :
	if is_instance_valid(mall_window):
		if mall_window.has_method("on_mall_data_received"):
			mall_window.on_mall_data_received(points, items, gifts)


		if not mall_window.visible or mall_window.modulate.a < 1.0:
			_open_window(mall_window)

func _on_mall_points_sync_received(points: int) -> void :
	if is_instance_valid(mall_window) and mall_window.has_method("on_mall_points_sync"):
		mall_window.on_mall_points_sync(points)

func _on_paypal_donation_created_received(success: bool, message: String, approval_url: String) -> void :
	if is_instance_valid(mall_window) and mall_window.has_method("on_paypal_donation_created"):
		mall_window.on_paypal_donation_created(success, message, approval_url)
	elif not success:
		show_rejection_message_box(message if not message.is_empty() else "建立 PayPal 訂單失敗，請稍後再試。")

func _on_legacy_point_withdrawal_response_received(success: bool, operation: String, message: String, legacy_points: int) -> void :
	if is_instance_valid(mall_window) and mall_window.has_method("on_legacy_point_withdrawal_response"):
		mall_window.on_legacy_point_withdrawal_response(success, operation, message, legacy_points)
	elif not success:
		show_rejection_message_box(message if not message.is_empty() else "舊遊戲點數操作失敗，請稍後再試。")


func _on_open_rank_window_received(rank_type: int) -> void :

	if has_node("WindowsContainer/NpcDialogPanel"):
		$WindowsContainer / NpcDialogPanel.visible = false


	if is_instance_valid(current_rank_window):
		current_rank_window.queue_free()


	current_rank_window = RANK_WINDOW_SCENE.instantiate()



	current_rank_window.current_rank_type = rank_type


	windows_container.add_child(current_rank_window)


	var btn_close = current_rank_window.get_node_or_null("BtnClose")
	if btn_close and btn_close is BaseButton:
		btn_close.pressed.connect( func(): current_rank_window.queue_free())
	_play_interface_activation_feedback(current_rank_window)

func _play_interface_activation_feedback(interface: CanvasItem) -> void :
	if AudioManager and AudioManager.has_method("play_interface_activation_feedback"):
		AudioManager.play_interface_activation_feedback(interface)


func _on_opt_auto_saint_item_selected(index: int) -> void :
	var is_on = (index == 1)
	if NetworkManager and NetworkManager.has_method("send_toggle_auto_saint"):

		NetworkManager.send_toggle_auto_saint(is_on, 30, 30)

func _on_toggle_auto_saint_response_received(is_on: bool) -> void :

	if option_auto_saint is OptionButton:
		var target_idx = 1 if is_on else 0
		if option_auto_saint.selected != target_idx:
			option_auto_saint.select(target_idx)


	if is_instance_valid(target_player) and "data_model" in target_player:
		target_player.data_model.is_auto_saint_on = is_on
		if is_on:
			target_player.combat_target = null
			target_player.pending_skill_id = -1
			target_player.ready_to_cast_skill_id = -1
			if CursorManager and CursorManager.has_method("cancel_skill_state"):
				CursorManager.cancel_skill_state()
			if target_player.has_method("_stop_movement"):
				target_player._stop_movement()


func toggle_fullscreen() -> void :
	var current_mode = DisplayServer.window_get_mode()


	if current_mode != DisplayServer.WINDOW_MODE_FULLSCREEN and current_mode != DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN)
	else:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
