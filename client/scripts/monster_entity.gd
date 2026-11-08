
extends CharacterBody2D
class_name MonsterEntity

var entity_id: int = 0
var monster_level: int = 1
var max_hp: int = 100
var current_hp: int = 100
var require_ctrl_to_attack: bool = false
var current_state: String = "idle"
var current_dir: String = "1"
var is_action_locked: bool = false
var entity_scale: float = 1.0

@onready var sprite: AnimatedSprite2D = $AnimatedSprite2D
var ui_container: Control = null
var name_label: Label = null
var bg_texture_rect: TextureRect = null
var hp_bar: ProgressBar = null
var level_icon: TextureRect = null

var target_indicator: AnimatedSprite2D = null
var skill_use_effect: AnimatedSprite2D = null
var skill_hit_effect: AnimatedSprite2D = null


var collision_shape: CollisionShape2D = null

var move_tween: Tween
var move_idle_generation: int = 0
var network_move_target: Vector2 = Vector2.ZERO
var network_move_speed: float = 0.0
var network_move_active: bool = false
# The latest server-approved waypoint remains safe to finish when the server
# changes state to idle before the renderer has finished interpolation.
var _idle_after_last_waypoint: bool = false
var last_move_packet_ms: int = 0
var pending_move_dir: String = ""
var pending_move_dir_count: int = 0
var last_move_dir_change_ms: int = 0
var last_visual_move_dir: String = ""
var last_visual_move_dir_ms: int = 0
var fallback_action_generation: int = 0
var action_state_started_ms: int = 0
# Server monster movement is broadcast on a 250ms map tick. Keep WALK
# active across one interval of packet jitter, unless an explicit server
# SyncState overrides it.
const MOVE_IDLE_GRACE_SECONDS: float = 0.35
const FALLBACK_ACTION_RELEASE_SECONDS: float = 0.25
const ACTION_STATE_MAX_MS: int = 900
# Short pathfinding/engage steps must not be accelerated beyond their
# authoritative 250ms duration; otherwise they arrive early and pause.
const NETWORK_MOVE_MIN_SPEED: float = 1.0
const NETWORK_MOVE_MAX_SPEED: float = 205.0
const NETWORK_MOVE_SNAP_EPSILON: float = 1.0
# Keep diagnostics bounded even with hundreds of simultaneously chasing mobs.
static var _debug_chase_packet_gap_logs: int = 0
static var _debug_chase_state_idle_logs: int = 0
static var _debug_chase_client_idle_logs: int = 0
static var _debug_authoritative_snap_logs: int = 0
var _debug_last_aggro_move_packet_ms: int = 0
const MOVE_DIR_CONFIRM_COUNT: int = 2
const MOVE_DIR_MAX_HOLD_MS: int = 450
const VISIBILITY_RECT: Rect2 = Rect2(-180.0, -260.0, 360.0, 420.0)
var _is_on_screen: bool = true
var _screen_notifier: VisibleOnScreenNotifier2D = null

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {
	0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0
}

const STATE_MAP = {
	"idle": "IDLE", "walk": "WALK", "attack": "ATTACK", 
	"hit": "HIT", "die": "DEATH", "death": "DEATH"
}

func _resolve_ui_nodes() -> void :
	if not is_instance_valid(ui_container):
		ui_container = get_node_or_null("UIContainer") as Control

	if not is_instance_valid(ui_container):
		ui_container = Control.new()
		ui_container.name = "UIContainer"
		ui_container.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(ui_container)
		push_warning("[MonsterEntity] UIContainer missing; created runtime fallback for EntityID=%d" % entity_id)

	if not is_instance_valid(name_label):
		name_label = ui_container.get_node_or_null("NameLabel") as Label
	if not is_instance_valid(bg_texture_rect):
		bg_texture_rect = ui_container.get_node_or_null("TextureRect") as TextureRect
	if not is_instance_valid(hp_bar):
		hp_bar = ui_container.get_node_or_null("TextureRect/HPBar") as ProgressBar
	if not is_instance_valid(level_icon):
		level_icon = ui_container.get_node_or_null("TextureRect/LevelIcon") as TextureRect


func _ready() -> void :
	_resolve_ui_nodes()

	collision_shape = get_node_or_null("CollisionShape2D")
	if not collision_shape:
		collision_shape = CollisionShape2D.new()
		collision_shape.name = "CollisionShape2D"
		add_child(collision_shape)


	if ui_container:
		if ui_container is Control: ui_container.mouse_filter = Control.MOUSE_FILTER_IGNORE
	if name_label: name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE


	if bg_texture_rect: bg_texture_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE

	if hp_bar:
		hp_bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
		for child in hp_bar.get_children():
			if child is Control: child.mouse_filter = Control.MOUSE_FILTER_IGNORE




	self.collision_layer = 8
	self.collision_mask = 0
	self.input_pickable = true


	if sprite:
		sprite.centered = false
		sprite.frame_changed.connect(_update_sprite_offset)
		sprite.animation_finished.connect(_on_animation_finished)

	mouse_entered.connect(_on_mouse_entered)
	mouse_exited.connect(_on_mouse_exited)

	_screen_notifier = VisibleOnScreenNotifier2D.new()
	_screen_notifier.rect = VISIBILITY_RECT
	add_child(_screen_notifier)
	_screen_notifier.screen_entered.connect(_on_screen_entered)
	_screen_notifier.screen_exited.connect(_on_screen_exited)

	# Most monsters spend the majority of their lifetime idle. Do not keep a
	# per-frame callback alive until movement/combat actually needs it.

func advance_network_runtime(delta: float) -> bool:
	if current_hp <= 0:
		return false

	if current_state in ["attack", "hit"] and action_state_started_ms > 0:
		var action_elapsed_ms: int = Time.get_ticks_msec() - action_state_started_ms
		if action_elapsed_ms >= ACTION_STATE_MAX_MS:
			_release_combat_action_state()

	if not network_move_active:
		if current_state == "walk":
			var idle_elapsed_ms := Time.get_ticks_msec() - last_move_packet_ms
			if idle_elapsed_ms >= int(MOVE_IDLE_GRACE_SECONDS * 1000.0):
				if OS.is_debug_build() and _debug_chase_client_idle_logs < 12 and is_instance_valid(target_indicator) and target_indicator.visible:
					_debug_chase_client_idle_logs += 1
					print("[MONSTER_CHASE_CLIENT_IDLE] id=%d packet_age_ms=%d remaining=%.1f" % [
						entity_id, idle_elapsed_ms, global_position.distance_to(network_move_target)
					])
				current_state = "idle"
				sync_animation()
				return false

	if current_state in ["attack", "hit", "death", "die"]:
		network_move_active = false
		return true

	var remaining := global_position.distance_to(network_move_target)
	if remaining <= NETWORK_MOVE_SNAP_EPSILON:
		global_position = network_move_target
		network_move_active = false
		if _idle_after_last_waypoint:
			_idle_after_last_waypoint = false
			current_state = "idle"
			sync_animation()
			return false
		# Keep the monster in the manager runtime set briefly while WALK settles
		# back to IDLE through the existing grace logic.
		return true

	var step := network_move_speed * delta
	if step <= 0.0:
		return true

	global_position = global_position.move_toward(network_move_target, step)
	if _idle_after_last_waypoint and global_position.distance_to(network_move_target) <= NETWORK_MOVE_SNAP_EPSILON:
		global_position = network_move_target
		network_move_active = false
		_idle_after_last_waypoint = false
		current_state = "idle"
		sync_animation()
		return false
	if current_state != "walk":
		current_state = "walk"
		sync_animation()

	return true

func is_runtime_visible() -> bool:
	return _is_on_screen


func is_finishing_last_move_before_idle() -> bool:
	return _idle_after_last_waypoint and network_move_active

func apply_offscreen_network_move(target_pos: Vector2, new_dir: String = "") -> void :
	# Compatibility entry point for any existing callers outside the main
	# NetworkEntityManager path. Do not restore the old global_position snap:
	# monsters can cross into the viewport between visibility notifications.
	if new_dir != "":
		pending_move_dir = ""
		pending_move_dir_count = 0
	network_move_to(target_pos, 0.25)

func _on_screen_entered() -> void :
	_is_on_screen = true
	if is_instance_valid(sprite):
		if not sprite.is_playing():
			sprite.play()

func _on_screen_exited() -> void :
	_is_on_screen = false
	if is_instance_valid(sprite) and sprite.is_playing():
		sprite.pause()

func _exit_tree() -> void :

	if CursorManager:
		CursorManager.unregister_hover(self)


func _on_mouse_entered() -> void :
	if current_hp <= 0: return

	if bg_texture_rect:
		bg_texture_rect.visible = true

	var player = GameManager.current_player
	if is_instance_valid(player):
		if "data_model" in player and player.data_model.is_shop_open: return
		if player.get("is_interacting_with_npc"): return

	if CursorManager:
		var state_name = "attack"


		if is_instance_valid(player) and player.has_method("can_attack") and not player.can_attack():
			state_name = "normal"

		CursorManager.register_hover(self, state_name)

		if CursorManager.cursor_sprite.animation == "skill":

			if is_instance_valid(player) and player.has_method("can_attack") and not player.can_attack():
				CursorManager.hide_skill_target_tooltip()
				return

			if is_instance_valid(player) and "ready_to_cast_skill_id" in player and player.ready_to_cast_skill_id > 0:
				if SkillLoader:
					var s_res = SkillLoader.get_skill_data(player.ready_to_cast_skill_id)
					if s_res:
						var t_name = name_label.text if name_label else "怪物"
						CursorManager.show_skill_target_tooltip(t_name, s_res.skill_name)

func _on_mouse_exited() -> void :
	if bg_texture_rect:
		bg_texture_rect.visible = false

	if CursorManager:
		CursorManager.unregister_hover(self)
		if CursorManager.cursor_sprite.animation == "skill":
			CursorManager.hide_skill_target_tooltip()

func _input_event(_viewport: Viewport, event: InputEvent, _shape_idx: int) -> void :
	if event is InputEventMouseButton and event.pressed:
		if current_hp <= 0: return


		if CursorManager and CursorManager.current_hovered_entity != self:
			return




		if event.button_index == MOUSE_BUTTON_LEFT:
			var player = GameManager.current_player
			if not is_instance_valid(player): return

			if "data_model" in player and player.data_model and player.data_model.is_auto_saint_on:
				get_viewport().set_input_as_handled()
				var hud = GameManager.current_hud
				if hud and hud.has_method("show_rejection_message_flash"):
					hud.show_rejection_message_flash("[color=#FFF02D]自動聖靈[/color] 啟動中，禁止攻擊！")
				return


			if require_ctrl_to_attack and not Input.is_key_pressed(KEY_CTRL):
				return

			if player.has_method("can_attack") and not player.can_attack():

				player.target_position = get_global_mouse_position()
				player.is_moving = true
				if not player.is_action_locked:
					player.current_state = "walk"
				get_viewport().set_input_as_handled()
				return

			if player.get("is_interacting_with_npc"): return

			if "data_model" in player and player.data_model.is_shop_open:
				var hud = GameManager.current_hud
				if hud and hud.has_method("show_rejection_message_flash"):
					hud.show_rejection_message_flash("商店營業中，無法進行戰鬥！")
				return

			get_viewport().set_input_as_handled()


			if "ready_to_cast_skill_id" in player and player.ready_to_cast_skill_id > 0:
				player.pending_skill_id = player.ready_to_cast_skill_id
				player.combat_target = self
				player.ready_to_cast_skill_id = -1

				if CursorManager:
					CursorManager.hide_skill_target_tooltip()
					CursorManager.set_cursor_state("attack")
				return


			if "combat_target" in player:
				player.pending_skill_id = -1
				if player.combat_target == self:
					return

				player.combat_target = self




		elif event.button_index == MOUSE_BUTTON_RIGHT:

			if Input.is_key_pressed(KEY_CTRL):
				get_viewport().set_input_as_handled()
				if NetworkManager and NetworkManager.has_method("send_pet_attack_command"):
					NetworkManager.send_pet_attack_command(self.entity_id)


func show_damage_number(damage: int, is_crit: bool = false, hit_delay: float = 0.3, split_count: int = 1, skill_id: int = 0, is_primary: bool = true) -> void :
	if FloatingTextManager:
		FloatingTextManager.show_damage(self, damage, is_crit, hit_delay, split_count, skill_id, is_primary)

func update_hp(new_hp: int, hit_delay: float = 0.3, is_miss: bool = false) -> void :
	if hit_delay > 0:
		if not is_inside_tree():
			return
		var tree: SceneTree = get_tree()
		if tree == null:
			return
		await tree.create_timer(hit_delay).timeout

	if not is_instance_valid(self) or not is_inside_tree():
		return

	var took_damage = new_hp < current_hp
	current_hp = new_hp
	if hp_bar: hp_bar.value = current_hp


	if current_hp > 0 and sprite and took_damage and not is_miss:
		current_state = "hit"
		action_state_started_ms = Time.get_ticks_msec()
		sync_animation()

func _die(hit_delay: float = 0.0) -> void :
	if current_state == "death": return

	if hit_delay > 0:
		if not is_inside_tree():
			return
		var tree: SceneTree = get_tree()
		if tree == null:
			return
		await tree.create_timer(hit_delay).timeout

	if not is_instance_valid(self) or not is_inside_tree():
		return

	current_hp = 0
	if hp_bar: hp_bar.value = 0
	if ui_container: ui_container.visible = false

	current_state = "death"
	sync_animation()

	if is_instance_valid(target_indicator):
		target_indicator.visible = false

	input_pickable = false
	if CursorManager:
		CursorManager.unregister_hover(self)

	var fade_tween = create_tween()
	fade_tween.tween_interval(1.0)
	fade_tween.tween_property(self, "modulate:a", 0.0, 1.0)
	fade_tween.tween_callback(queue_free)


func setup(id: int, m_name: String, extra_data: Dictionary) -> void :
	self.entity_id = id
	_resolve_ui_nodes()
	if name_label: name_label.text = m_name
	require_ctrl_to_attack = bool(extra_data.get("RequireCtrlToAttack", false))
	entity_scale = float(extra_data.get("Scale", 1.0))

	if extra_data.get("IsWorldBoss", false) and entity_scale == 1.0: entity_scale = 1.2
	self.scale = Vector2(entity_scale, entity_scale)

	if hp_bar:
		self.monster_level = int(extra_data.get("Level", 1))
		update_level_icon()


		if bg_texture_rect:
			bg_texture_rect.visible = false


	if not is_instance_valid(target_indicator):
		target_indicator = AnimatedSprite2D.new()
		target_indicator.name = "TargetIndicator"
		target_indicator.visible = false
		target_indicator.z_index = 80
		if is_instance_valid(ui_container):
			ui_container.add_child(target_indicator)

		if EffectLoader:
			var frames = EffectLoader.get_effect("Enemy_Icon/ENEMY_ICON_00")
			if frames:
				target_indicator.sprite_frames = frames
				var anim_name = "default" if frames.has_animation("default") else frames.get_animation_names()[0]
				target_indicator.animation = anim_name
				# This indicator starts invisible. Playing hundreds of invisible
				# AnimatedSprite2Ds wastes per-frame animation work; selection starts
				# playback on demand in set_target_indicator().


	if is_instance_valid(target_indicator):
		target_indicator.centered = true

		if is_instance_valid(ui_container):
			target_indicator.position = Vector2(ui_container.size.x / 2.0, -25.0)

	if extra_data.has("MaxHP") and extra_data.has("CurrentHP"):
		max_hp = extra_data["MaxHP"]
		current_hp = extra_data["CurrentHP"]
		if hp_bar:
			hp_bar.max_value = max_hp
			hp_bar.value = current_hp

	if extra_data.has("Sprite") and MonsterLoader:
		var frames = MonsterLoader.get_monster_frames(extra_data["Sprite"], true)
		if frames and sprite: sprite.sprite_frames = frames

	if extra_data.has("ColorIndex") and sprite:
		var c_idx = int(extra_data["ColorIndex"])
		if c_idx > 0:
			var mat = ShaderMaterial.new()
			mat.shader = PALETTE_SHADER
			mat.set_shader_parameter("effect_type", 0)
			mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(c_idx, 0.0))
			sprite.material = mat

	sync_animation()
	_lock_ui_position()
	_update_sprite_offset()
	_generate_dynamic_collision_shape()


func _lock_ui_position() -> void :
	if not sprite or not sprite.sprite_frames or not ui_container: return

	await get_tree().process_frame
	if not is_instance_valid(self): return

	var fixed_head_y = -80.0

	var base_anim = "IDLE_1"
	if not sprite.sprite_frames.has_animation(base_anim):
		var anims = sprite.sprite_frames.get_animation_names()
		if anims.size() > 0: base_anim = anims[0]

	if sprite.sprite_frames.has_animation(base_anim):
		var tex = sprite.sprite_frames.get_frame_texture(base_anim, 0)
		if tex:
			var offset = tex.get_meta("old_offset") if tex.has_meta("old_offset") else Vector2.ZERO
			fixed_head_y = - offset.y - 55


	ui_container.position = Vector2( - ui_container.size.x / 2.0, fixed_head_y)


func _generate_dynamic_collision_shape() -> void :
	if not collision_shape or not sprite or not sprite.sprite_frames: return

	var base_anim = "IDLE_1"
	if not sprite.sprite_frames.has_animation(base_anim):
		var anims = sprite.sprite_frames.get_animation_names()
		if anims.size() > 0: base_anim = anims[0]

	if sprite.sprite_frames.has_animation(base_anim):
		var tex = sprite.sprite_frames.get_frame_texture(base_anim, 0)
		if tex:
			var tex_size = tex.get_size()
			var offset = tex.get_meta("old_offset") if tex.has_meta("old_offset") else Vector2.ZERO


			var rect = RectangleShape2D.new()
			rect.size = tex_size


			collision_shape.shape = rect


			var center_y = - offset.y + (tex_size.y / 2.0)


			collision_shape.position = Vector2(0, - tex_size.y / 2.0)

func _update_sprite_offset() -> void :
	if not sprite or not sprite.sprite_frames: return
	MonsterLoader.play_confirmed_frame_audio(sprite)
	var anim_name = sprite.animation
	if sprite.sprite_frames.has_animation(anim_name):
		var frame_texture = sprite.sprite_frames.get_frame_texture(anim_name, sprite.frame)
		if frame_texture:
			var offset = frame_texture.get_meta("old_offset") if frame_texture.has_meta("old_offset") else Vector2.ZERO
			var actual_offset_x = frame_texture.get_width() - offset.x if sprite.flip_h else offset.x
			sprite.offset = Vector2( - actual_offset_x, - offset.y)

func _direction_from_vector(dir_vec: Vector2) -> String:
	if dir_vec.length_squared() <= 0.000001:
		return current_dir

	var angle_deg: float = posmod(
		rad_to_deg(dir_vec.angle()),
		360.0
	)
	var index_raw: int = int(round(angle_deg / 45.0))
	return GameConstants.DIR_STRINGS[index_raw % 8]


func _apply_visual_direction_from_vector(
	dir_vec: Vector2,
	allow_hysteresis: bool = false
) -> void :
	if dir_vec.length_squared() <= 0.000001:
		return

	var visual_dir: String = _direction_from_vector(dir_vec)
	if visual_dir == current_dir:
		last_visual_move_dir = current_dir
		last_visual_move_dir_ms = Time.get_ticks_msec()
		return

	if allow_hysteresis:
		var angle_deg: float = posmod(
			rad_to_deg(dir_vec.angle()),
			360.0
		)
		var dirs: Array = GameConstants.DIR_STRINGS
		var current_index: int = dirs.find(current_dir)
		if current_index >= 0:
			var current_center: float = float(current_index) * 45.0
			var diff: float = absf(angle_deg - current_center)
			diff = minf(diff, 360.0 - diff)
			if diff <= 28.0:
				return

	current_dir = visual_dir
	last_visual_move_dir = visual_dir
	last_visual_move_dir_ms = Time.get_ticks_msec()
	last_move_dir_change_ms = last_visual_move_dir_ms
	pending_move_dir = ""
	pending_move_dir_count = 0

	if current_state in ["walk", "attack"]:
		sync_animation()


func apply_network_move_direction(new_dir: String) -> void :
	# Once combat state owns the monster, stale/late MoveEntity packets from
	# the preceding chase tick must not overwrite the authoritative attack
	# facing supplied by SyncState.
	if current_state in ["attack", "hit", "death", "die"]:
		pending_move_dir = ""
		pending_move_dir_count = 0
		return

	if new_dir == "":
		pending_move_dir = ""
		pending_move_dir_count = 0
		return

	if new_dir == current_dir:
		last_visual_move_dir = current_dir
		last_visual_move_dir_ms = Time.get_ticks_msec()
		pending_move_dir = ""
		pending_move_dir_count = 0
		return

	if pending_move_dir == new_dir:
		pending_move_dir_count += 1
	else:
		pending_move_dir = new_dir
		pending_move_dir_count = 1

	var now_ms := Time.get_ticks_msec()
	var held_too_long := (
		last_move_dir_change_ms <= 0 or
		now_ms - last_move_dir_change_ms >= MOVE_DIR_MAX_HOLD_MS
	)

	# Movement packets arrive about every 250 ms. Requiring confirmation keeps
	# small waypoint angle changes from restarting WALK_1/WALK_8 every packet,
	# while MOVE_DIR_MAX_HOLD_MS still lets a real turn appear promptly.
	if pending_move_dir_count >= MOVE_DIR_CONFIRM_COUNT or held_too_long:
		current_dir = new_dir
		last_visual_move_dir = new_dir
		last_visual_move_dir_ms = now_ms
		last_move_dir_change_ms = now_ms
		pending_move_dir = ""
		pending_move_dir_count = 0
		if current_state == "walk":
			sync_animation()


func apply_attack_visual_direction(target_pos: Vector2) -> void :
	# Use the exact same 8-way mapping as player/mecha movement. Only keep
	# a small hysteresis near sector boundaries to avoid visual flicker.
	_apply_visual_direction_from_vector(
		target_pos - global_position,
		true
	)


func apply_network_state(new_state: String, new_dir: String) -> void :
	# Server combat state is authoritative over movement/state, but the last
	# rendered WALK direction is the best visual truth when the monster has
	# just reached its target. Preserve it on the walk -> attack transition so
	# an attack packet cannot flip a visually-correct chase direction.
	var previous_state := current_state
	var now_ms := Time.get_ticks_msec()
	if OS.is_debug_build() and _debug_chase_state_idle_logs < 12 and new_state == "idle" and previous_state == "walk" and is_instance_valid(target_indicator) and target_indicator.visible:
		_debug_chase_state_idle_logs += 1
		print("[MONSTER_CHASE_SERVER_IDLE] id=%d packet_age_ms=%d remaining=%.1f" % [
			entity_id, now_ms - last_move_packet_ms, global_position.distance_to(network_move_target)
		])
	var preserve_recent_move_facing := (
		new_state == "attack" and
		previous_state != "attack" and
		last_visual_move_dir != "" and
		now_ms - last_visual_move_dir_ms <= 1500
	)

	# Do not discard a final already-authorized MoveEntity waypoint just
	# because SyncState(idle) reached the client before the render position.
	# This is only presentation: new movement/combat packets still override it.
	if (
		new_state == "idle"
		and current_state == "walk"
		and network_move_active
		and global_position.distance_to(network_move_target) > NETWORK_MOVE_SNAP_EPSILON
	):
		_idle_after_last_waypoint = true
		return

	_idle_after_last_waypoint = false
	if new_state != "walk":
		move_idle_generation += 1
		network_move_active = false
		network_move_speed = 0.0
		if move_tween and move_tween.is_valid():
			move_tween.kill()
		move_tween = null

	current_state = new_state
	if new_state in ["attack", "hit"]:
		action_state_started_ms = now_ms
	else:
		action_state_started_ms = 0

	if new_state == "attack" and new_dir != "":
		# Server computes attack facing from monster -> current target.
		# Do not preserve a previous walk sector here.
		current_dir = new_dir
	elif preserve_recent_move_facing:
		current_dir = last_visual_move_dir
	elif new_dir != "":
		current_dir = new_dir

	if new_dir != "" or preserve_recent_move_facing:
		pending_move_dir = ""
		pending_move_dir_count = 0
		last_move_dir_change_ms = now_ms
	sync_animation()


func _apply_network_target_direction(target_pos: Vector2) -> void :
	_apply_visual_direction_from_vector(
		target_pos - global_position
	)


func network_move_to(target_pos: Vector2, speed_time: float = 1.0) -> void :
	if current_hp <= 0:
		return
	_idle_after_last_waypoint = false

	# A late MoveEntity can arrive after SyncState/EntityDamage. While combat
	# owns the animation, movement may update the buffered target/position but
	# must never overwrite the attack-facing direction.
	if current_state not in ["attack", "hit", "death", "die"]:
		_apply_network_target_direction(target_pos)

	move_idle_generation += 1
	var now_ms: int = Time.get_ticks_msec()
	if OS.is_debug_build() and is_instance_valid(target_indicator) and target_indicator.visible:
		# Ignore the first move after aggro: the previous packet may have been
		# a normal wander step several seconds earlier, not a missed chase tick.
		if _debug_last_aggro_move_packet_ms > 0:
			var aggro_gap_ms: int = now_ms - _debug_last_aggro_move_packet_ms
			if _debug_chase_packet_gap_logs < 16 and aggro_gap_ms > 375:
				_debug_chase_packet_gap_logs += 1
				print("[MONSTER_CHASE_PACKET_GAP] id=%d gap_ms=%d state=%s distance=%.1f speed_time=%.3f" % [
					entity_id, aggro_gap_ms, current_state,
					global_position.distance_to(target_pos), speed_time
				])
		_debug_last_aggro_move_packet_ms = now_ms
	last_move_packet_ms = now_ms
	network_move_target = target_pos

	# Server uses SpeedTime <= 0.01 as an authoritative correction before
	# entering attack state. Snap first so the following SyncState("attack")
	# cannot freeze the monster at an older visual position.
	if speed_time <= 0.01:
		# Server attack-position corrections remain instantaneous for
		# authoritative combat. Report sizeable on-screen jumps so we can
		# distinguish them from viewport-edge snapping in Debug builds.
		if OS.is_debug_build() and _debug_authoritative_snap_logs < 20:
			var snap_distance: float = global_position.distance_to(target_pos)
			if snap_distance >= 16.0 and _is_on_screen:
				_debug_authoritative_snap_logs += 1
				print("[MONSTER_AUTHORITATIVE_SNAP] id=%d distance=%.1f state=%s from=%s to=%s" % [
					entity_id, snap_distance, current_state,
					str(global_position), str(target_pos)
				])
		global_position = target_pos
		network_move_active = false
		network_move_speed = 0.0
		return

	var remaining: float = global_position.distance_to(target_pos)
	if remaining <= NETWORK_MOVE_SNAP_EPSILON:
		network_move_active = false
		return

	# Keep each short waypoint paced to SpeedTime (250ms in server AI),
	# while capping extreme catch-up speed for delayed/lost packets.
	# A large minimum speed made short steps complete early, then appear idle
	# until the next server packet.
	var requested_time: float = maxf(speed_time, 0.01)
	var requested_speed: float = remaining / requested_time
	network_move_speed = clampf(
		requested_speed,
		NETWORK_MOVE_MIN_SPEED,
		NETWORK_MOVE_MAX_SPEED
	)

	# Combat animation owns the body while it is playing, but keep the newest
	# movement target buffered. When ATTACK/HIT finishes, movement resumes
	# smoothly instead of having discarded one or more server waypoints.
	if current_state in ["attack", "hit", "death", "die"]:
		network_move_active = false
		return

	network_move_active = true
	set_process(true)
	if current_state != "walk":
		current_state = "walk"
		sync_animation()

func sync_animation() -> void :
	if not sprite or not sprite.sprite_frames: return
	var mapped_prefix = STATE_MAP.get(current_state.to_lower(), "IDLE")
	var dir_int = int(current_dir)
	var anim_suffix = str(dir_int)
	var should_flip = false

	if current_state.to_lower() == "idle":
		match dir_int:
			8, 1: anim_suffix = "1";should_flip = false
			2, 3: anim_suffix = "3";should_flip = false
			4, 5: anim_suffix = "3";should_flip = true
			6, 7: anim_suffix = "1";should_flip = true
	else:
		match dir_int:
			1, 2, 3, 4, 8: anim_suffix = str(dir_int);should_flip = false
			5: anim_suffix = "3";should_flip = true
			6: anim_suffix = "2";should_flip = true
			7: anim_suffix = "1";should_flip = true

	sprite.flip_h = should_flip
	var target_anim = mapped_prefix + "_" + anim_suffix
	var fallback_anim = mapped_prefix + "_1"
	var idle_fallback = "IDLE_1"
	var anim_to_play = ""

	if sprite.sprite_frames.has_animation(target_anim): anim_to_play = target_anim
	elif sprite.sprite_frames.has_animation(fallback_anim): anim_to_play = fallback_anim
	elif sprite.sprite_frames.has_animation(idle_fallback): anim_to_play = idle_fallback
	else:
		var all_anims = sprite.sprite_frames.get_animation_names()
		if all_anims.size() > 0: anim_to_play = all_anims[0]

	if anim_to_play != "":
		var requested_state: String = current_state.to_lower()
		var requested_prefix: String = str(mapped_prefix) + "_"
		var has_requested_animation: bool = anim_to_play.begins_with(requested_prefix)

		if sprite.animation != anim_to_play or not sprite.is_playing():
			MonsterLoader.begin_audio_sequence(sprite)
			sprite.play(anim_to_play)
		_update_sprite_offset()

		# Some recovered monster sprites do not contain ATTACK/HIT animations.
		# In that case we fall back to IDLE visually, but must not leave the
		# logical state stuck in "attack"/"hit" forever because IDLE loops and
		# never emits animation_finished.
		if requested_state in ["attack", "hit"] and not has_requested_animation:
			fallback_action_generation += 1
			var generation := fallback_action_generation
			var tree: SceneTree = get_tree()
			if tree == null:
				return
			tree.create_timer(FALLBACK_ACTION_RELEASE_SECONDS).timeout.connect(func():
				if not is_instance_valid(self) or not is_inside_tree() or current_hp <= 0:
					return
				if generation != fallback_action_generation:
					return
				if current_state not in ["attack", "hit"]:
					return

				_release_combat_action_state()
			)

func _release_combat_action_state() -> void :
	action_state_started_ms = 0
	fallback_action_generation += 1
	var has_recent_move: bool = (
		last_move_packet_ms > 0 and
		Time.get_ticks_msec() - last_move_packet_ms <= 700 and
		global_position.distance_to(network_move_target) > NETWORK_MOVE_SNAP_EPSILON
	)
	if has_recent_move:
		network_move_active = true
		current_state = "walk"
	else:
		network_move_active = false
		current_state = "idle"
		set_process(false)
	sync_animation()


func _on_animation_finished() -> void :
	if (sprite.animation.begins_with("HIT") or sprite.animation.begins_with("ATTACK")) and current_hp > 0:
		_release_combat_action_state()

func set_target_indicator(has_target: bool) -> void :
	if is_instance_valid(target_indicator):
		if OS.is_debug_build() and target_indicator.visible != has_target:
			# Reset the chase-packet baseline on aggro changes.
			_debug_last_aggro_move_packet_ms = 0
		target_indicator.visible = has_target
		if has_target:
			if not target_indicator.is_playing():
				target_indicator.play()
		elif target_indicator.is_playing():
			# A deselected monster must not animate an invisible target marker.
			target_indicator.stop()


func update_level_icon() -> void :
	if not is_instance_valid(level_icon): return

	var p_level = 1
	if GameManager and is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player:
		p_level = GameManager.current_player.data_model.level

	var diff = p_level - monster_level
	var tex_path = "res://ui/B_ENEMY2.png"

	if diff >= 7:
		tex_path = "res://ui/B_ENEMY4.png"
	elif diff >= 4 and diff <= 6:
		tex_path = "res://ui/B_ENEMY3.png"
	elif diff >= 0 and diff <= 3:
		tex_path = "res://ui/B_ENEMY2.png"
	elif diff >= -3 and diff <= -1:
		tex_path = "res://ui/B_ENEMY1.png"
	else:
		tex_path = "res://ui/B_ENEMY0.png"

	if ResourceLoader.exists(tex_path):
		level_icon.texture = load(tex_path)

func _ensure_skill_use_effect() -> void :
	if not is_instance_valid(skill_use_effect):
		skill_use_effect = AnimatedSprite2D.new()
		skill_use_effect.name = "SkillUseEffect"
		skill_use_effect.visible = false
		skill_use_effect.z_index = 55
		skill_use_effect.position = Vector2.ZERO
		skill_use_effect.centered = false
		add_child(skill_use_effect)
		skill_use_effect.animation_finished.connect( func():
			skill_use_effect.visible = false
			skill_use_effect.stop()
		)
		if not skill_use_effect.frame_changed.is_connected(_on_effect_frame_changed.bind(skill_use_effect)):
			skill_use_effect.frame_changed.connect(_on_effect_frame_changed.bind(skill_use_effect))


func execute_skill_visuals(skill_id: int, _skill_level: int, target_node: Node2D = null, aoe_targets: Array = []) -> void :
	if is_instance_valid(target_node):
		_apply_visual_direction_from_vector(
			target_node.global_position - global_position
		)

	current_state = "attack"
	action_state_started_ms = Time.get_ticks_msec()
	sync_animation()

	if SkillLoader:
		var s_res = SkillLoader.get_skill_data(skill_id)
		if s_res and s_res.fx_use != "":
			_ensure_skill_use_effect()
			var effect_name = "Skill_Effect/" + s_res.fx_use
			var loaded_res = EffectLoader.get_effect(effect_name)
			if not loaded_res:
				loaded_res = EffectLoader.get_effect("Hit_Effect/" + s_res.fx_use)

			if loaded_res:
				if skill_use_effect.sprite_frames != loaded_res:
					skill_use_effect.sprite_frames = loaded_res

				var anim_to_play = current_dir if loaded_res.has_animation(current_dir) else "default"
				if not loaded_res.has_animation(anim_to_play):
					var anims = loaded_res.get_animation_names()
					if anims.size() > 0: anim_to_play = anims[0]

				skill_use_effect.visible = true
				skill_use_effect.animation = anim_to_play
				skill_use_effect.frame = 0
				skill_use_effect.play(anim_to_play)
				_on_effect_frame_changed(skill_use_effect)


func _on_effect_frame_changed(effect_node: AnimatedSprite2D) -> void :
	if not is_instance_valid(effect_node) or not effect_node.sprite_frames: return
	EffectLoader.play_confirmed_frame_audio(effect_node)

	var anim = effect_node.animation
	if not effect_node.sprite_frames.has_animation(anim): return
	var total_frames = effect_node.sprite_frames.get_frame_count(anim)
	var safe_frame = clampi(effect_node.frame, 0, total_frames - 1)
	var tex = effect_node.sprite_frames.get_frame_texture(anim, safe_frame)

	if not tex: return

	var offset_val = Vector2.ZERO
	if EffectLoader:
		offset_val = EffectLoader.get_offset(tex)

	var tex_w = tex.get_width()
	if tex is AtlasTexture:
		tex_w = tex.region.size.x

	var actual_offset_x = tex_w - offset_val.x if effect_node.flip_h else offset_val.x
	effect_node.offset = Vector2( - actual_offset_x, - offset_val.y)

func _ensure_skill_hit_effect() -> void :
	if not is_instance_valid(skill_hit_effect):
		skill_hit_effect = AnimatedSprite2D.new()
		skill_hit_effect.name = "SkillHitEffect"
		skill_hit_effect.visible = false
		skill_hit_effect.z_index = 60
		skill_hit_effect.position = Vector2.ZERO
		skill_hit_effect.centered = false
		add_child(skill_hit_effect)
		skill_hit_effect.animation_finished.connect( func():
			skill_hit_effect.visible = false
			skill_hit_effect.stop()
		)
		if not skill_hit_effect.frame_changed.is_connected(_on_effect_frame_changed.bind(skill_hit_effect)):
			skill_hit_effect.frame_changed.connect(_on_effect_frame_changed.bind(skill_hit_effect))


func _play_skill_hit(fx_hit: String) -> void :
	if fx_hit == "": return
	_ensure_skill_hit_effect()
	var effect_name = ""
	if fx_hit.begins_with("Normal"):
		effect_name = "Normal_Effect/%s" % fx_hit
	elif fx_hit.begins_with("Skill"):
		effect_name = "Skill_Effect/%s" % fx_hit
	else:
		effect_name = "Skill_Effect/%s" % fx_hit

	var loaded_res = EffectLoader.get_effect(effect_name)

	if not loaded_res:
		loaded_res = EffectLoader.get_effect("Hit_Effect/%s" % fx_hit)

	if loaded_res:
		if skill_hit_effect.sprite_frames != loaded_res:
			skill_hit_effect.sprite_frames = loaded_res

		var anim_to_play = current_dir if loaded_res.has_animation(current_dir) else "default"
		if not loaded_res.has_animation(anim_to_play):
			var anims = loaded_res.get_animation_names()
			if anims.size() > 0: anim_to_play = anims[0]

		skill_hit_effect.visible = true
		skill_hit_effect.animation = anim_to_play
		skill_hit_effect.frame = 0
		skill_hit_effect.play(anim_to_play)
		_on_effect_frame_changed(skill_hit_effect)
