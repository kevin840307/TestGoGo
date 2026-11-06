
extends Control

@onready var btn_trade = $BtnTrade
@onready var btn_transfer = $BtnTransfer
@onready var btn_shop = $BtnShop
@onready var btn_party = $BtnParty
@onready var btn_clan_invite = $BtnClanInvite

var target_dbid: int = 0
var target_name: String = ""

func _ready() -> void :
	if btn_trade: btn_trade.pressed.connect(_on_btn_trade_pressed)
	if btn_shop: btn_shop.pressed.connect(_on_btn_shop_view_pressed)
	if btn_party: btn_party.pressed.connect(_on_btn_party_invite_pressed)
	if btn_transfer: btn_transfer.pressed.connect(_on_btn_transfer_pressed)
	if btn_clan_invite: btn_clan_invite.pressed.connect(_on_btn_clan_invite_pressed)

func setup(dbid: int, p_name: String) -> void :
	target_dbid = dbid
	target_name = p_name

func _on_btn_trade_pressed() -> void :
	if NetworkManager:
		NetworkManager.send_trade_request(target_dbid)
	queue_free()

func _on_btn_shop_view_pressed() -> void :
	if NetworkManager:
		NetworkManager.send_shop_action("ViewShop", {"TargetDBID": target_dbid})
	queue_free()

func _on_btn_party_invite_pressed() -> void :
	if NetworkManager:
		NetworkManager.send_party_action("Invite", target_dbid)
	queue_free()

func _on_btn_clan_invite_pressed() -> void :
	if NetworkManager and NetworkManager.has_method("send_secure_packet"):
		NetworkManager.send_secure_packet({
			"Type": "ClubAction", 
			"Action": "Invite", 
			"TargetDBID": target_dbid
		})
	queue_free()

func _input(event: InputEvent) -> void :
	if event is InputEventMouseButton and event.pressed:
		var rect = Rect2(global_position, size)
		if not rect.has_point(get_global_mouse_position()):
			queue_free()

func _on_btn_transfer_pressed() -> void :
	if GameManager and is_instance_valid(GameManager.current_hud):
		GameManager.current_hud.show_transfer_input_box(target_dbid, target_name)
	queue_free()
