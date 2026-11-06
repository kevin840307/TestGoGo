
extends Control

@onready var lbl_points: Label = $PointsLabel
@onready var category_vbox: VBoxContainer = $CategoryVBox
@onready var scroll_area: ScrollContainer = $ScrollArea
@onready var item_grid: GridContainer = $ScrollArea / ItemGrid
@onready var btn_close: TextureButton = $CloseButton
@onready var btn_paypal_donate: Button = $PayPalDonateButton
@onready var withdrawal_panel: Panel = $WithdrawalPanel
@onready var legacy_account_input: LineEdit = $WithdrawalPanel / LegacyAccountInput
@onready var legacy_password_input: LineEdit = $WithdrawalPanel / LegacyPasswordInput
@onready var legacy_query_button: Button = $WithdrawalPanel / QueryButton
@onready var legacy_balance_label: Label = $WithdrawalPanel / LegacyBalanceLabel
@onready var withdrawal_amount_input: SpinBox = $WithdrawalPanel / WithdrawalAmountInput
@onready var legacy_withdraw_button: Button = $WithdrawalPanel / WithdrawButton
@onready var legacy_status_label: Label = $WithdrawalPanel / StatusLabel

const MALL_ITEM_ENTRY = preload("res://mall_item_entry.tscn")
const DONATION_REWARD_ENTRY = preload("res://donation_reward_entry.tscn")
const GIFT_CATEGORY: = "贈禮"
const WITHDRAWAL_CATEGORY: = "提領"
const MIN_DONATION_AMOUNT: = 500
const MAX_DONATION_AMOUNT: = 50000
const PAYPAL_ORDER_LIFETIME_SECONDS: = 30 * 60

var _all_items: Array = []
var _all_gifts: Array = []
var _current_category: String = ""
var _current_points: int = 0
var _dynamic_categories: Array[String] = []
var _donation_request_pending: = false
var _active_paypal_url: = ""
var _active_paypal_url_expires_at: = 0
var _legacy_points: = 0
var _legacy_query_valid: = false
var _legacy_request_pending: = false
var _legacy_pending_operation: = ""
var _legacy_queried_login_name: = ""
var _suppress_legacy_credentials_changed: = false

func _ready() -> void :
	if btn_close:
		btn_close.pressed.connect( func():
			if GameManager and GameManager.current_hud and GameManager.current_hud.has_method("_close_window"):
				GameManager.current_hud._close_window(self)
			else:
				hide()
		)
	if btn_paypal_donate:
		btn_paypal_donate.pressed.connect(_on_paypal_donate_pressed)
	if legacy_query_button:
		legacy_query_button.pressed.connect(_on_legacy_query_pressed)
	if legacy_withdraw_button:
		legacy_withdraw_button.pressed.connect(_on_legacy_withdraw_pressed)
	if legacy_account_input:
		legacy_account_input.text_changed.connect(_on_legacy_credentials_changed)
	if legacy_password_input:
		legacy_password_input.text_changed.connect(_on_legacy_credentials_changed)
	visibility_changed.connect(_on_mall_visibility_changed)
	_reset_legacy_withdrawal_state(false)

func _on_paypal_donate_pressed() -> void :
	if _donation_request_pending:
		return

	if not _active_paypal_url.is_empty():
		if Time.get_unix_time_from_system() < _active_paypal_url_expires_at:
			_show_open_paypal_confirmation(_active_paypal_url)
			return
		_clear_active_paypal_order()

	var hud = GameManager.current_hud if GameManager else null
	if not is_instance_valid(hud) or not hud.has_method("show_spin_box"):
		return

	hud.show_spin_box(
		"PayPal贊助", 
		func(amount: int): _validate_and_confirm_donation(amount), 
		Callable(), 
		MIN_DONATION_AMOUNT, 
		MAX_DONATION_AMOUNT, 
		MIN_DONATION_AMOUNT
	)

func _validate_and_confirm_donation(amount: int) -> void :
	var hud = GameManager.current_hud if GameManager else null
	if not is_instance_valid(hud):
		return

	if amount < MIN_DONATION_AMOUNT or amount > MAX_DONATION_AMOUNT:
		hud.show_rejection_message_box(
			"單筆贊助金額必須介於 %d 至 %d 元之間！" % [MIN_DONATION_AMOUNT, MAX_DONATION_AMOUNT]
		)
		return

	hud.show_message_box(
		"本次 PayPal 贊助金額為 %d 元，完成付款後將獲得 %d 點。\n\n確定要建立付款訂單嗎？" % [amount, amount], 
		2, 
		func(): _send_paypal_donation_request(amount), 
		Callable()
	)

func _send_paypal_donation_request(amount: int) -> void :
	if not NetworkManager or not NetworkManager.is_connected_to_server:
		var hud = GameManager.current_hud if GameManager else null
		if is_instance_valid(hud):
			hud.show_rejection_message_box("目前未連線至伺服器，無法建立 PayPal 訂單。")
		return

	_set_donation_request_pending(true)
	NetworkManager.send_mall_action("CreatePayPalDonation", {"Amount": amount})

func on_paypal_donation_created(success: bool, message: String, approval_url: String) -> void :
	_set_donation_request_pending(false)
	var hud = GameManager.current_hud if GameManager else null
	if not is_instance_valid(hud):
		return

	if not success:
		hud.show_rejection_message_box(message if not message.is_empty() else "建立 PayPal 訂單失敗，請稍後再試。")
		return

	if not _is_valid_paypal_url(approval_url):
		hud.show_rejection_message_box("伺服器回傳的 PayPal 付款網址無效，為了安全已停止開啟網頁。")
		return

	_active_paypal_url = approval_url
	_active_paypal_url_expires_at = int(Time.get_unix_time_from_system()) + PAYPAL_ORDER_LIFETIME_SECONDS
	if btn_paypal_donate:
		btn_paypal_donate.text = "開啟付款頁"
	_show_open_paypal_confirmation(approval_url, message)

func _show_open_paypal_confirmation(approval_url: String, server_message: String = "") -> void :
	var hud = GameManager.current_hud if GameManager else null
	if not is_instance_valid(hud):
		return

	var display_message: = server_message.strip_edges()
	if display_message.is_empty():
		display_message = "PayPal 訂單已建立。"
	display_message += "\n\n請於 30 分鐘內完成付款。\n按下「確定」後將開啟 PayPal 付款頁面。"
	hud.show_message_box(
		display_message, 
		2, 
		func(): _open_paypal_url(approval_url), 
		Callable()
	)

func _open_paypal_url(url: String) -> void :
	var hud = GameManager.current_hud if GameManager else null
	if not _is_valid_paypal_url(url):
		if is_instance_valid(hud):
			hud.show_rejection_message_box("PayPal 付款網址驗證失敗，無法開啟網頁。")
		return

	var open_error: = OS.shell_open(url)
	if open_error != OK and is_instance_valid(hud):
		hud.show_rejection_message_box("無法開啟系統瀏覽器，請檢查作業系統的預設瀏覽器設定。")

func _is_valid_paypal_url(url: String) -> bool:
	if not url.begins_with("https://"):
		return false
	var authority: = url.substr(8).get_slice("/", 0).get_slice("?", 0).get_slice("#", 0)
	if authority.is_empty() or authority.contains("@"):
		return false
	var host: = authority.get_slice(":", 0).to_lower()
	return host == "paypal.com" or host.ends_with(".paypal.com")

func _set_donation_request_pending(is_pending: bool) -> void :
	_donation_request_pending = is_pending
	if btn_paypal_donate:
		btn_paypal_donate.disabled = is_pending
		if is_pending:
			btn_paypal_donate.text = "建立訂單中..."
		elif _active_paypal_url.is_empty():
			btn_paypal_donate.text = "PayPal贊助"
		else:
			btn_paypal_donate.text = "開啟付款頁"

func _clear_active_paypal_order() -> void :
	_active_paypal_url = ""
	_active_paypal_url_expires_at = 0
	if btn_paypal_donate:
		btn_paypal_donate.text = "PayPal贊助"

func _setup_category_buttons() -> void :

	for child in category_vbox.get_children():
		child.queue_free()


	for cat in _dynamic_categories:
		var btn = Button.new()
		btn.text = cat
		btn.custom_minimum_size = Vector2(120, 50)
		btn.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		btn.pressed.connect(_on_category_selected.bind(cat))
		category_vbox.add_child(btn)

	var gift_button: = Button.new()
	gift_button.text = GIFT_CATEGORY
	gift_button.custom_minimum_size = Vector2(120, 50)
	gift_button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	gift_button.pressed.connect(_on_category_selected.bind(GIFT_CATEGORY))
	category_vbox.add_child(gift_button)

	var withdrawal_button: = Button.new()
	withdrawal_button.text = WITHDRAWAL_CATEGORY
	withdrawal_button.custom_minimum_size = Vector2(120, 50)
	withdrawal_button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	withdrawal_button.pressed.connect(_on_category_selected.bind(WITHDRAWAL_CATEGORY))
	category_vbox.add_child(withdrawal_button)

func _on_category_selected(category: String) -> void :
	_current_category = category
	var is_withdrawal: = category == WITHDRAWAL_CATEGORY
	if scroll_area:
		scroll_area.visible = not is_withdrawal
	if withdrawal_panel:
		withdrawal_panel.visible = is_withdrawal
	if is_withdrawal:
		return
	_refresh_grid()


func on_mall_data_received(points: int, items: Array, gifts: Array = []) -> void :
	var previous_category: = _current_category
	_current_points = points
	_update_points_ui()
	_all_items = items
	_all_gifts = gifts


	_dynamic_categories.clear()
	for item_data in _all_items:
		var cat = str(item_data.get("category", item_data.get("Category", "未分類")))

		if not _dynamic_categories.has(cat):
			_dynamic_categories.append(cat)


	_setup_category_buttons()


	if previous_category == WITHDRAWAL_CATEGORY:
		_on_category_selected(WITHDRAWAL_CATEGORY)
	elif previous_category == GIFT_CATEGORY:
		_on_category_selected(GIFT_CATEGORY)
	elif _dynamic_categories.size() > 0:
		_on_category_selected(_dynamic_categories[0])
	else:
		_on_category_selected(GIFT_CATEGORY)

func on_mall_points_sync(points: int) -> void :
	_current_points = points
	_update_points_ui()

func _update_points_ui() -> void :
	if lbl_points: lbl_points.text = "💷剩餘點數💷  %d 點" % _current_points

func _refresh_grid() -> void :
	if not item_grid: return

	for child in item_grid.get_children():
		child.queue_free()

	if _current_category == GIFT_CATEGORY:
		_refresh_gift_grid()
		return

	for item_data in _all_items:

		if item_data.get("category", item_data.get("Category", "")) == _current_category:
			if item_data.get("is_active", item_data.get("IsActive", true)):
				var entry = MALL_ITEM_ENTRY.instantiate()
				item_grid.add_child(entry)
				entry.setup(item_data)
				entry.buy_requested.connect(_on_buy_requested)


func _on_mall_visibility_changed() -> void :
	if not visible:
		_reset_legacy_withdrawal_state(true)

func _reset_legacy_withdrawal_state(clear_credentials: bool) -> void :
	_legacy_request_pending = false
	_legacy_pending_operation = ""
	_legacy_query_valid = false
	_legacy_points = 0
	_legacy_queried_login_name = ""
	if clear_credentials:
		_suppress_legacy_credentials_changed = true
		if legacy_account_input:
			legacy_account_input.clear()
		if legacy_password_input:
			legacy_password_input.clear()
		_suppress_legacy_credentials_changed = false
	if legacy_balance_label:
		legacy_balance_label.text = "『樂園』點數餘額：尚未查詢"
	if legacy_status_label:
		legacy_status_label.text = ""
	if withdrawal_amount_input:
		withdrawal_amount_input.min_value = 1
		withdrawal_amount_input.max_value = 1
		withdrawal_amount_input.value = 1
	_set_legacy_request_pending(false)

func _on_legacy_credentials_changed(_new_text: String) -> void :
	if _suppress_legacy_credentials_changed or _legacy_request_pending:
		return
	_invalidate_legacy_query()

func _invalidate_legacy_query() -> void :
	_legacy_query_valid = false
	_legacy_points = 0
	_legacy_queried_login_name = ""
	if legacy_balance_label:
		legacy_balance_label.text = "『樂園』點數餘額：尚未查詢"
	if legacy_status_label:
		legacy_status_label.text = "帳號或密碼變更後，請重新查詢。"
	if withdrawal_amount_input:
		withdrawal_amount_input.max_value = 1
		withdrawal_amount_input.value = 1
	if legacy_withdraw_button:
		legacy_withdraw_button.disabled = true

func _on_legacy_query_pressed() -> void :
	if _legacy_request_pending:
		return
	var login_name: = legacy_account_input.text.strip_edges() if legacy_account_input else ""
	var password: = legacy_password_input.text if legacy_password_input else ""
	if login_name.is_empty() or password.is_empty():
		_show_legacy_error("請輸入『樂園』遊戲帳號與密碼。")
		return
	if not NetworkManager or not NetworkManager.is_connected_to_server:
		_show_legacy_error("目前未連線至伺服器，無法查詢『樂園』剩餘點數。")
		return
	if legacy_account_input and legacy_account_input.text != login_name:
		_suppress_legacy_credentials_changed = true
		legacy_account_input.text = login_name
		_suppress_legacy_credentials_changed = false
	_legacy_pending_operation = "Query"
	_set_legacy_request_pending(true)
	NetworkManager.send_mall_action("QueryLegacyPoints", {
		"LoginName": login_name, 
		"Password": password
	})

func _on_legacy_withdraw_pressed() -> void :
	if _legacy_request_pending or not _legacy_query_valid:
		return
	var amount: = int(withdrawal_amount_input.value) if withdrawal_amount_input else 0
	if amount <= 0 or amount > _legacy_points:
		_show_legacy_error("提領金額必須介於 1 至 %d 點之間。" % _legacy_points)
		return
	var hud = GameManager.current_hud if GameManager else null
	if not is_instance_valid(hud):
		return
	hud.show_message_box(
		"確定要將『樂園』遊戲帳號『%s』的 %d 點轉入目前遊戲帳號嗎？" % [_legacy_queried_login_name, amount], 
		2, 
		func(): _send_legacy_withdraw_request(amount), 
		Callable()
	)

func _send_legacy_withdraw_request(amount: int) -> void :
	if _legacy_request_pending or not _legacy_query_valid:
		return
	var login_name: = legacy_account_input.text.strip_edges() if legacy_account_input else ""
	var password: = legacy_password_input.text if legacy_password_input else ""
	if login_name != _legacy_queried_login_name or password.is_empty() or amount <= 0 or amount > _legacy_points:
		_invalidate_legacy_query()
		_show_legacy_error("帳號資料或點數餘額已變更，請重新查詢。")
		return
	if not NetworkManager or not NetworkManager.is_connected_to_server:
		_show_legacy_error("目前未連線至伺服器，無法提領點數。")
		return
	_legacy_pending_operation = "Withdraw"
	_set_legacy_request_pending(true)
	NetworkManager.send_mall_action("WithdrawLegacyPoints", {
		"LoginName": login_name, 
		"Password": password, 
		"Amount": amount
	})

func on_legacy_point_withdrawal_response(success: bool, operation: String, message: String, legacy_points: int) -> void :
	if not _legacy_request_pending or operation != _legacy_pending_operation:
		return
	_legacy_pending_operation = ""
	if not success:
		_invalidate_legacy_query()
		_set_legacy_request_pending(false)
		_show_legacy_error(message if not message.is_empty() else "『樂園』點數提領失敗，請稍後再試。")
		return

	_legacy_points = maxi(legacy_points, 0)
	if operation == "Query":
		_legacy_query_valid = true
		_legacy_queried_login_name = legacy_account_input.text.strip_edges() if legacy_account_input else ""
		_update_legacy_balance_ui()
		_set_legacy_request_pending(false)
		if legacy_status_label:
			legacy_status_label.text = message if not message.is_empty() else "查詢成功。"
		return

	if operation == "Withdraw":
		_legacy_query_valid = false
		_legacy_queried_login_name = ""
		_suppress_legacy_credentials_changed = true
		if legacy_password_input:
			legacy_password_input.clear()
		_suppress_legacy_credentials_changed = false
		_update_legacy_balance_ui()
		_set_legacy_request_pending(false)
		if legacy_status_label:
			legacy_status_label.text = message if not message.is_empty() else "點數提領成功。"
		var hud = GameManager.current_hud if GameManager else null
		if is_instance_valid(hud):
			hud.show_message_box(message if not message.is_empty() else "點數提領成功。", 1)

func _update_legacy_balance_ui() -> void :
	if legacy_balance_label:
		legacy_balance_label.text = "『樂園』點數餘額：%d 點" % _legacy_points
	if withdrawal_amount_input:
		withdrawal_amount_input.max_value = maxi(_legacy_points, 1)
		withdrawal_amount_input.value = 1
	if legacy_withdraw_button:
		legacy_withdraw_button.disabled = not _legacy_query_valid or _legacy_points <= 0 or _legacy_request_pending

func _set_legacy_request_pending(is_pending: bool) -> void :
	_legacy_request_pending = is_pending
	if legacy_account_input:
		legacy_account_input.editable = not is_pending
	if legacy_password_input:
		legacy_password_input.editable = not is_pending
	if legacy_query_button:
		legacy_query_button.disabled = is_pending
		legacy_query_button.text = "查詢中..." if is_pending and _legacy_pending_operation == "Query" else "查詢"
	if withdrawal_amount_input:
		withdrawal_amount_input.editable = not is_pending and _legacy_query_valid and _legacy_points > 0
	if legacy_withdraw_button:
		legacy_withdraw_button.disabled = is_pending or not _legacy_query_valid or _legacy_points <= 0
		legacy_withdraw_button.text = "提領中..." if is_pending and _legacy_pending_operation == "Withdraw" else "確認提領"

func _show_legacy_error(message: String) -> void :
	if legacy_status_label:
		legacy_status_label.text = message
	var hud = GameManager.current_hud if GameManager else null
	if is_instance_valid(hud):
		hud.show_rejection_message_box(message)

func _refresh_gift_grid() -> void :
	if _all_gifts.is_empty():
		var empty_label: = Label.new()
		empty_label.custom_minimum_size = Vector2(610, 80)
		empty_label.text = "目前沒有已達成的贊助滿額禮。"
		empty_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		empty_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		item_grid.add_child(empty_label)
		return

	for reward_data in _all_gifts:
		if not reward_data is Dictionary:
			continue
		var entry = DONATION_REWARD_ENTRY.instantiate()
		item_grid.add_child(entry)
		entry.setup(reward_data)
		entry.claim_requested.connect(_on_donation_reward_claim_requested)

func _on_donation_reward_claim_requested(reward_data: Dictionary) -> void :
	var reward_id: = int(reward_data.get("RewardID", 0))
	if reward_id <= 0:
		return
	var item_name: = str(reward_data.get("Name", "未知物品"))
	var count: = int(reward_data.get("Count", 1))
	var character_name: = "目前角色"
	if GameManager and is_instance_valid(GameManager.current_player):
		var data_model = GameManager.current_player.get("data_model")
		if data_model:
			character_name = str(data_model.character_name)
	var hud = GameManager.current_hud if GameManager else null
	if not is_instance_valid(hud):
		return
	hud.show_message_box(
		"確定要由角色「%s」領取「%s x %d」嗎？\n\n每個帳號只能領取一次。" % [character_name, item_name, count], 
		2, 
		func(): request_donation_gift_claim(reward_id), 
		Callable()
	)


func _on_buy_requested(item_data: Dictionary, quantity: int) -> void :
	var price = int(item_data.get("price", item_data.get("Price", 0)))
	var total_cost = price * quantity
	var remaining = _current_points - total_cost

	var item_name = str(item_data.get("name", item_data.get("Name", "未知商品")))
	var count_per_pack = int(item_data.get("count", item_data.get("Count", 1)))
	var total_gain = count_per_pack * quantity

	if remaining < 0:
		if GameManager.current_hud:
			GameManager.current_hud.show_rejection_message_box("點數不足，無法購買！")
		return

	var msg = "確定要購買以下商品嗎？\n\n商品：%s x %d\n總金額：%d 點" % [item_name, quantity, total_cost]


	if GameManager.current_hud:
		var mall_id = int(item_data.get("mall_id", item_data.get("MallID", 0)))
		GameManager.current_hud.show_message_box(
			msg, 
			2, 
			func(): _send_buy_request(mall_id, quantity), 
			Callable()
		)

func _send_buy_request(mall_id: int, quantity: int) -> void :
	if NetworkManager and NetworkManager.has_method("send_mall_action"):
		NetworkManager.send_mall_action("BuyItem", {
			"MallID": mall_id, 
			"Count": quantity
		})

func request_donation_gift_claim(reward_id: int) -> void :
	if NetworkManager and NetworkManager.has_method("send_claim_donation_gift"):
		NetworkManager.send_claim_donation_gift(reward_id)
