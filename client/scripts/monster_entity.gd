
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
const GENERIC_MONSTER_CLICK_SIZE := Vector2(112.0, 136.0)
const SIMPLIFIED_ORB_CACHE = preload("res://scripts/simplified_orb_cache.gd")
# Explicit load avoids reliance on the global class_name registry during scene parsing.
const MONSTER_WAYPOINT_SPEED = preload("res://scripts/monster_waypoint_speed.gd")
var _original_sprite_name: String = ""
var _original_color_index: int = 0
var _performance_simplified_visual: bool = false
var _primitive_simplified_visual: bool = false
var _performance_simplified_style: int = ClientPerformanceProfile.SIMPLIFY_PIXEL_A
var _performance_visual_protected: bool = false
var _afk_visual_suspended: bool = false
var _afk_ui_was_visible: bool = true
var _afk_root_was_visible: bool = true

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
# Only visual children may ease attack-entry position corrections. The
# CharacterBody2D global_position stays exactly on the authoritative server
# coordinate for combat, targeting and AOI checks.
var _authoritative_visual_tween: Tween = null
var _sprite_visual_base: Vector2 = Vector2.ZERO
var _ui_visual_base: Vector2 = Vector2.ZERO
var _ui_position_generation: int = 0
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
const MOVE_IDLE_GRACE_SECONDS: float = 0.60
const FALLBACK_ACTION_RELEASE_SECONDS: float = 0.25
const ACTION_STATE_MAX_MS: int = 900
# Short pathfinding/engage steps must not be accelerated beyond their
# authoritative 250ms duration; otherwise they arrive early and pause.
const NETWORK_MOVE_MIN_SPEED: float = 1.0
const NETWORK_MOVE_MAX_SPEED: float = 900.0
# Slightly overlap successive 250ms visual waypoints so normal packet jitter
# does not produce a stop at every server tick. Combat and correction packets
# continue to override movement immediately.
const NETWORK_MOVE_VISUAL_SLACK_SECONDS: float = 0.01
const AUTHORITATIVE_VISUAL_CORRECTION_MIN_DISTANCE: float = 8.0
# Attack-entry position corrections keep the authoritative body exact, while
# the sprite/UI may remain visually continuous across a delayed backlog.
# The server lose-target radius is 700, so anything larger is treated as a
# genuine discontinuity instead of being disguised.
const AUTHORITATIVE_VISUAL_CORRECTION_MAX_DISTANCE: float = 700.0
const NETWORK_MOVE_SNAP_EPSILON: float = 1.0
# Keep diagnostics bounded even with hundreds of simultaneously chasing mobs.
static var _debug_chase_packet_gap_logs: int = 0
static var _debug_chase_state_idle_logs: int = 0
static var _debug_chase_client_idle_logs: int = 0
static var _debug_authoritative_snap_logs: int = 0
static var _debug_first_aggro_move_logs: int = 0
var _debug_last_aggro_move_packet_ms: int = 0
var _debug_aggro_started_ms: int = 0
const MOVE_DIR_CONFIRM_COUNT: int = 2
const MOVE_DIR_MAX_HOLD_MS: int = 450
const VISIBILITY_RECT: Rect2 = Rect2(-180.0, -260.0, 360.0, 420.0)
var _is_on_screen: bool = true
var _screen_notifier: VisibleOnScreenNotifier2D = null

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {
	0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0
}
# Colored mobs share immutable material variants. One map can contain
# hundreds of identical ColorIndex values; their own scene/UI state remains
# independent and the existing Shader/TIME animation stays unchanged.
static var _shared_palette_materials: Dictionary = {}
# Every monster previously allocated a separate RectangleShape2D even when
# thousands of identical sprites have the same click hitbox. The rectangle
# is immutable after setup; share one Shape2D per texture dimensions.
static var _shared_click_shapes: Dictionary = {}


static func get_generic_monster_click_shape() -> RectangleShape2D:
	return get_shared_monster_click_shape(GENERIC_MONSTER_CLICK_SIZE)

static func get_shared_monster_click_shape(texture_size: Vector2) -> RectangleShape2D:
	var key := Vector2i(int(texture_size.x), int(texture_size.y))
	var shape := _shared_click_shapes.get(key) as RectangleShape2D
	if is_instance_valid(shape):
		return shape
	shape = RectangleShape2D.new()
	shape.size = Vector2(key)
	_shared_click_shapes[key] = shape
	return shape


static func get_shared_monster_click_shape_count() -> int:
	return _shared_click_shapes.size()


static func get_shared_monster_palette_material(color_index: int) -> ShaderMaterial:
	var hue: float = float(COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
	var key: String = "%.4f" % hue
	var material: ShaderMaterial = _shared_palette_materials.get(key, null) as ShaderMaterial
	if is_instance_valid(material):
		return material
	material = ShaderMaterial.new()
	material.shader = PALETTE_SHADER
	material.set_shader_parameter("effect_type", 0)
	material.set_shader_parameter("hue_shift", hue)
	_shared_palette_materials[key] = material
	return material


static func get_shared_monster_palette_material_count() -> int:
	return _shared_palette_materials.size()

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


func prepare_performance_visual(
	simplified: bool,
	style: int = ClientPerformanceProfile.SIMPLIFY_PIXEL_A
) -> void:
	_performance_simplified_visual = simplified
	_primitive_simplified_visual = simplified
	_performance_simplified_style = _normalize_simplified_style(style)

func is_primitive_simplified() -> bool:
	# A simplified mode flag is not the same as an actually rendered primitive.
	return _primitive_simplified_visual and not _afk_visual_suspended


func is_render_visible() -> bool:
	return (
		not _afk_visual_suspended
		and visible
		and _is_on_screen
	)

func _normalize_simplified_style(style: int) -> int:
	var normalized := ClientPerformanceProfile.sanitize_simplify_mode(style)
	if normalized < ClientPerformanceProfile.SIMPLIFY_PIXEL_A:
		return ClientPerformanceProfile.SIMPLIFY_PIXEL_A
	return normalized

func _primitive_direction_vector() -> Vector2:
	match current_dir:
		"1": return Vector2(0.0, 1.0)
		"2": return Vector2(0.707, 0.707)
		"3": return Vector2(1.0, 0.0)
		"4": return Vector2(0.707, -0.707)
		"5": return Vector2(0.0, -1.0)
		"6": return Vector2(-0.707, -0.707)
		"7": return Vector2(-1.0, 0.0)
		"8": return Vector2(-0.707, 0.707)
	return Vector2.DOWN

func _simplified_monster_base_color() -> Color:
	# All simplified ordinary monsters share exactly one pearl-white orb.
	return Color(0.96, 0.985, 1.0, 1.0)


func _draw_simplified_monster(center: Vector2, tint: Color) -> void:
	# One textured quad, not eight triangles plus layered circles and rects.
	# All simplified monsters reuse the SAME runtime-generated ImageTexture.
	# No sprite frames are loaded and no per-entity texture is allocated.
	draw_texture(
		SIMPLIFIED_ORB_CACHE.monster_texture(),
		center - SIMPLIFIED_ORB_CACHE.HALF_SIZE,
		tint
	)

func _draw() -> void:
	# AFK is a presentation shell. Keep the MonsterEntity node alive for
	# authoritative movement/combat/collision, but draw absolutely nothing.
	if _afk_visual_suspended or not _primitive_simplified_visual:
		return

	var state := current_state.to_lower()
	var body_color: Color = _simplified_monster_base_color()
	if state == "hit":
		body_color = body_color.lerp(Color(1.0, 0.28, 0.28, 1.0), 0.62)

	var center := Vector2(0.0, -48.0)
	if state == "attack":
		center += _primitive_direction_vector() * 8.0

	# Death remains in the same built-in visual family; never reload original
	# SpriteFrames merely to play a death animation.
	if state in ["death", "die"]:
		draw_set_transform(center + Vector2(0.0, 18.0), 0.0, Vector2(1.2, 0.34))
		_draw_simplified_monster(Vector2.ZERO, body_color.darkened(0.12))
		draw_set_transform(Vector2.ZERO, 0.0, Vector2.ONE)
		return

	_draw_simplified_monster(center, body_color)

func _ready() -> void :
	_resolve_ui_nodes()
	if is_instance_valid(sprite):
		_sprite_visual_base = sprite.position
	if is_instance_valid(ui_container):
		_ui_visual_base = ui_container.position

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
	return not _afk_visual_suspended and _is_on_screen


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

static func has_resumable_animation(animated_sprite: AnimatedSprite2D) -> bool:
	# Simplification intentionally releases SpriteFrames. A notifier can
	# still fire when that texture-free monster crosses the viewport.
	if not is_instance_valid(animated_sprite):
		return false
	var frames: SpriteFrames = animated_sprite.sprite_frames
	if not is_instance_valid(frames):
		return false
	var anim_name: StringName = animated_sprite.animation
	return frames.has_animation(anim_name) and frames.get_frame_count(anim_name) > 0


func _on_screen_entered() -> void :
	_is_on_screen = true
	if not _primitive_simplified_visual and is_instance_valid(sprite) and not sprite.is_playing():
		if has_resumable_animation(sprite):
			# Resume the last real IDLE/WALK/ATTACK animation, not the
			# default name (which most recovered SPR files do not have).
			sprite.play(sprite.animation)
		elif is_instance_valid(sprite.sprite_frames):
			# When a new frameset replaced the old animation, pick the
			# correct state/direction using the existing fallback logic.
			sync_animation()
	if is_instance_valid(target_indicator) and target_indicator.visible and not target_indicator.is_playing():
		if has_resumable_animation(target_indicator):
			target_indicator.play(target_indicator.animation)

func _on_screen_exited() -> void :
	_is_on_screen = false
	if is_instance_valid(sprite) and sprite.is_playing():
		sprite.pause()
	if is_instance_valid(target_indicator) and target_indicator.is_playing():
		target_indicator.pause()

func _exit_tree() -> void :
	_clear_authoritative_visual_correction()
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
			# Targeting never reloads the original SPR while simplification is
			# enabled. Attack/hit/death are drawn by the same pixel renderer.
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

				if player.has_method("cancel_autorun_tracking"):
					player.cancel_autorun_tracking(true)
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
			# Combat tracking must replace, not compete with, terrain autorun.
			if player.has_method("cancel_autorun_tracking"):
				player.cancel_autorun_tracking(true)

			if "ready_to_cast_skill_id" in player and player.ready_to_cast_skill_id > 0:
				player.begin_targeted_skill(self, player.ready_to_cast_skill_id)
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
	if _afk_visual_suspended:
		return
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
	var is_world_boss := bool(extra_data.get("IsWorldBoss", false))
	_performance_visual_protected = is_world_boss
	if is_world_boss:
		_performance_simplified_visual = false
		_primitive_simplified_visual = false

	if is_world_boss and entity_scale == 1.0: entity_scale = 1.2
	self.scale = Vector2(entity_scale, entity_scale)

	if hp_bar:
		self.monster_level = int(extra_data.get("Level", 1))
		update_level_icon()


		if bg_texture_rect:
			bg_texture_rect.visible = false


	# Delay creating the invisible target icon until the monster actually
	# becomes a target. Most map monsters never need an icon node.

	if extra_data.has("MaxHP") and extra_data.has("CurrentHP"):
		max_hp = extra_data["MaxHP"]
		current_hp = extra_data["CurrentHP"]
		if hp_bar:
			hp_bar.max_value = max_hp
			hp_bar.value = current_hp

	_original_sprite_name = str(extra_data.get("Sprite", ""))
	_original_color_index = int(extra_data.get("ColorIndex", 0))
	_apply_performance_sprite()

	if sprite and not _performance_simplified_visual and _original_color_index > 0:
		sprite.material = get_shared_monster_palette_material(_original_color_index)

	sync_animation()
	_lock_ui_position()
	_update_sprite_offset()
	_generate_dynamic_collision_shape()
	_play_spawn_transition()



func set_afk_visual_suspended(suspended: bool) -> void:
	if _afk_visual_suspended == suspended:
		return
	_afk_visual_suspended = suspended
	if suspended:
		_afk_root_was_visible = visible
		# Canvas visibility is presentation-only: CharacterBody2D collision,
		# authoritative position and fixed/network updates remain alive.
		visible = false
		if is_instance_valid(sprite):
			sprite.stop()
			sprite.visible = false
			sprite.sprite_frames = null
			sprite.material = null
		for visual in [target_indicator, skill_use_effect, skill_hit_effect]:
			if is_instance_valid(visual):
				visual.stop()
				visual.sprite_frames = null
				visual.material = null
				visual.visible = false
		if is_instance_valid(bg_texture_rect):
			bg_texture_rect.texture = null
		if is_instance_valid(ui_container):
			_afk_ui_was_visible = ui_container.visible
			ui_container.visible = false
		queue_redraw()
		return
	visible = _afk_root_was_visible
	if is_instance_valid(ui_container):
		ui_container.visible = _afk_ui_was_visible
	_apply_performance_sprite()
	sync_animation()
	queue_redraw()


func is_afk_visual_suspended() -> bool:
	return _afk_visual_suspended


func set_performance_simplified(
	enabled: bool,
	style: int = ClientPerformanceProfile.SIMPLIFY_PIXEL_A
) -> void:
	# A simplified monster always uses the built-in _draw() pixel path.
	var primitive_enabled := enabled
	var normalized_style := _normalize_simplified_style(style)
	if (
		_performance_simplified_visual == enabled
		and _primitive_simplified_visual == primitive_enabled
		and _performance_simplified_style == normalized_style
	):
		return
	_performance_simplified_visual = enabled
	_primitive_simplified_visual = primitive_enabled
	_performance_simplified_style = normalized_style
	_apply_performance_sprite()
	_lock_ui_position()
	_generate_dynamic_collision_shape()
	sync_animation()
	queue_redraw()

func is_performance_simplified() -> bool:
	return _performance_simplified_visual

func can_use_performance_simplification() -> bool:
	return not _performance_visual_protected

func _apply_performance_sprite() -> void:
	if not is_instance_valid(sprite):
		return
	if _afk_visual_suspended:
		sprite.stop()
		sprite.visible = false
		sprite.sprite_frames = null
		sprite.material = null
		return

	if _primitive_simplified_visual:
		sprite.stop()
		sprite.visible = false
		sprite.sprite_frames = null
		sprite.material = null
		queue_redraw()
		return

	sprite.visible = true
	if not MonsterLoader:
		return
	if _original_sprite_name.is_empty():
		return
	var frames = MonsterLoader.get_monster_frames(_original_sprite_name, true)
	if frames:
		sprite.sprite_frames = frames
	if _original_color_index > 0:
		sprite.material = get_shared_monster_palette_material(_original_color_index)
	else:
		sprite.material = null
	queue_redraw()

func _play_spawn_transition() -> void :
	# Purely visual and intentionally lightweight: one short alpha tween,
	# no particles, shaders or collision/scale changes. Background clients
	# skip it entirely because their render loop is disabled.
	if not RenderingServer.render_loop_enabled:
		modulate.a = 1.0
		return

	modulate.a = 0.0
	var spawn_tween := create_tween()
	spawn_tween.set_trans(Tween.TRANS_QUAD)
	spawn_tween.set_ease(Tween.EASE_OUT)
	spawn_tween.tween_property(self, "modulate:a", 1.0, 0.16)


func _lock_ui_position() -> void :
	# May be called repeatedly while render presets change. A previous call
	# can resume after one frame and must not overwrite the newer layout.
	_ui_position_generation += 1
	var requested_generation: int = _ui_position_generation
	if not is_instance_valid(ui_container):
		return
	if _primitive_simplified_visual:
		ui_container.position = Vector2(-ui_container.size.x / 2.0, -105.0)
		_ui_visual_base = ui_container.position
		return
	if not is_instance_valid(sprite) or not is_instance_valid(sprite.sprite_frames):
		return

	var scene_tree: SceneTree = get_tree()
	if scene_tree == null:
		return
	await scene_tree.process_frame
	if not is_instance_valid(self) or not is_inside_tree():
		return
	if requested_generation != _ui_position_generation or _primitive_simplified_visual:
		return
	if not is_instance_valid(ui_container) or not is_instance_valid(sprite):
		return
	# Simplification intentionally releases SpriteFrames; a frame-level
	# null guard BEFORE await is not enough.
	var frames: SpriteFrames = sprite.sprite_frames
	if not is_instance_valid(frames):
		return

	var fixed_head_y: float = -80.0
	var base_anim: String = "IDLE_1"
	if not frames.has_animation(base_anim):
		var anims: PackedStringArray = frames.get_animation_names()
		if anims.size() > 0:
			base_anim = anims[0]

	if frames.has_animation(base_anim) and frames.get_frame_count(base_anim) > 0:
		var tex: Texture2D = frames.get_frame_texture(base_anim, 0)
		if is_instance_valid(tex):
			var offset: Vector2 = tex.get_meta("old_offset") if tex.has_meta("old_offset") else Vector2.ZERO
			fixed_head_y = -offset.y - 55.0

	ui_container.position = Vector2(-ui_container.size.x / 2.0, fixed_head_y)
	_ui_visual_base = ui_container.position


func _generate_dynamic_collision_shape() -> void :
	if not collision_shape:
		return
	if _performance_simplified_visual:
		collision_shape.shape = get_generic_monster_click_shape()
		collision_shape.position = Vector2(0.0, -GENERIC_MONSTER_CLICK_SIZE.y * 0.5)
		return
	if not sprite or not sprite.sprite_frames:
		return

	var base_anim = "IDLE_1"
	if not sprite.sprite_frames.has_animation(base_anim):
		var anims = sprite.sprite_frames.get_animation_names()
		if anims.size() > 0: base_anim = anims[0]

	if sprite.sprite_frames.has_animation(base_anim):
		var tex = sprite.sprite_frames.get_frame_texture(base_anim, 0)
		if tex:
			var tex_size = tex.get_size()
			var offset = tex.get_meta("old_offset") if tex.has_meta("old_offset") else Vector2.ZERO


			# Identical monsters can reuse the same immutable shape; its
			# position still belongs to each individual CollisionShape2D.
			collision_shape.shape = get_shared_monster_click_shape(tex_size)


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

	if current_state in ["walk", "attack"] or _primitive_simplified_visual:
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
	if new_state in ["death", "die"]:
		_clear_authoritative_visual_correction()
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


func _clear_authoritative_visual_correction() -> void:
	if _authoritative_visual_tween and _authoritative_visual_tween.is_valid():
		_authoritative_visual_tween.kill()
	_authoritative_visual_tween = null
	if is_instance_valid(sprite):
		sprite.position = _sprite_visual_base
	if is_instance_valid(ui_container):
		ui_container.position = _ui_visual_base


static func should_ease_authoritative_correction(distance: float, on_screen: bool, state: String) -> bool:
	return on_screen and state in ["walk", "idle"] and (
		distance >= AUTHORITATIVE_VISUAL_CORRECTION_MIN_DISTANCE
		and distance <= AUTHORITATIVE_VISUAL_CORRECTION_MAX_DISTANCE
	)


# The screen notifier can lag behind Camera2D after zoom or viewport
# resize. Its cached _is_on_screen flag must not turn an on-camera 0ms
# attack correction into a one-frame position teleport at the screen edge.
func _is_near_current_viewport(world_pos: Vector2) -> bool:
	if not is_inside_tree():
		return false
	var viewport: Viewport = get_viewport()
	if not is_instance_valid(viewport) or not is_instance_valid(viewport.get_camera_2d()):
		return false
	var screen_pos: Vector2 = viewport.get_canvas_transform() * world_pos
	return MONSTER_WAYPOINT_SPEED.is_near_viewport(
		viewport.get_visible_rect(), screen_pos, 64.0
	)


func _ease_authoritative_visual_correction(previous_world_pos: Vector2) -> void:
	var distance: float = previous_world_pos.distance_to(global_position)
	# The physics body must move to the server-approved coordinate immediately,
	# but keep its sprite and UI visually continuous after a delayed attack snap.
	# Real map teleports/respawns beyond the bounded correction range are
	# never visually disguised.
	var is_near_screen: bool = _is_on_screen
	if not is_near_screen:
		is_near_screen = (
			_is_near_current_viewport(previous_world_pos)
			or _is_near_current_viewport(global_position)
		)
	if not is_inside_tree() or not should_ease_authoritative_correction(
		distance, is_near_screen, current_state
	):
		_clear_authoritative_visual_correction()
		return

	var displacement: Vector2 = previous_world_pos - global_position
	if _authoritative_visual_tween and _authoritative_visual_tween.is_valid():
		_authoritative_visual_tween.kill()
	_authoritative_visual_tween = null

	var has_visual: bool = false
	if is_instance_valid(sprite):
		sprite.position += displacement
		has_visual = true
	if is_instance_valid(ui_container):
		ui_container.position += displacement
		has_visual = true
	if not has_visual:
		return

	# Ease the render-only offset back to zero. Never move the body itself:
	# server-authoritative collision / skill target positions remain unchanged.
	var correction_seconds: float = MONSTER_WAYPOINT_SPEED.correction_visual_seconds(
		distance
	)
	_authoritative_visual_tween = create_tween()
	_authoritative_visual_tween.set_parallel(true)
	if is_instance_valid(sprite):
		_authoritative_visual_tween.tween_property(
			sprite, "position", _sprite_visual_base,
			correction_seconds
		).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
	if is_instance_valid(ui_container):
		_authoritative_visual_tween.tween_property(
			ui_container, "position", _ui_visual_base,
			correction_seconds
		).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)


func _apply_network_target_direction(target_pos: Vector2) -> void :
	_apply_visual_direction_from_vector(
		target_pos - global_position
	)


# The original client advances an entity by small local steps between
# destination updates. Keep that pacing for presentation, while all server
# corrections, targeting and combat packets retain their normal handling.
static func calculate_network_move_speed(distance: float, duration: float) -> float:
	return MONSTER_WAYPOINT_SPEED.calculate(
		distance, duration,
		NETWORK_MOVE_VISUAL_SLACK_SECONDS,
		NETWORK_MOVE_MIN_SPEED,
		NETWORK_MOVE_MAX_SPEED,
		NETWORK_MOVE_SNAP_EPSILON
	)


func network_move_to(
	target_pos: Vector2,
	speed_time: float = 1.0,
	snapshot_span_seconds: float = 0.0,
	server_move_speed: float = 0.0
) -> void :
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
		# The first move after acquiring aggro is not a packet-gap event.
		if (
			_debug_last_aggro_move_packet_ms == 0
			and _debug_aggro_started_ms > 0
			and _debug_first_aggro_move_logs < 20
		):
			_debug_first_aggro_move_logs += 1
			print("[MONSTER_AGGRO_FIRST_MOVE] id=%d delay_ms=%d state=%s speed_time=%.3f" % [
				entity_id, now_ms - _debug_aggro_started_ms, current_state, speed_time
			])
		if _debug_last_aggro_move_packet_ms > 0:
			var aggro_gap_ms: int = now_ms - _debug_last_aggro_move_packet_ms
			if _debug_chase_packet_gap_logs < 16 and aggro_gap_ms > 375:
				_debug_chase_packet_gap_logs += 1
				var focus_changed: bool = GameManager.did_focus_change_during_interval(
					_debug_last_aggro_move_packet_ms, now_ms
				)
				# Large packet intervals after Alt+Tab may include idle time
				# or background rendering suspension; never call them a
				# server/network defect without additional packet evidence.
				print("[MONSTER_CHASE_PACKET_GAP] id=%d gap_ms=%d state=%s distance=%.1f speed_time=%.3f focus_changed_in_gap=%s %s" % [
					entity_id, aggro_gap_ms, current_state,
					global_position.distance_to(target_pos), speed_time,
					str(focus_changed), GameManager._focus_perf_context(),
				])
		_debug_last_aggro_move_packet_ms = now_ms
	last_move_packet_ms = now_ms
	network_move_target = target_pos

	# Server uses SpeedTime <= 0.01 as an authoritative correction before
	# entering attack state. Snap first so the following SyncState("attack")
	# cannot freeze the monster at an older visual position.
	if speed_time <= 0.01:
		var visual_position_before_correction: Vector2 = global_position
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
		_ease_authoritative_visual_correction(visual_position_before_correction)
		network_move_active = false
		network_move_speed = 0.0
		return

	var remaining: float = global_position.distance_to(target_pos)
	if remaining <= NETWORK_MOVE_SNAP_EPSILON:
		network_move_active = false
		return

	# Match the original client's destination + entity-speed model. A normal
	# packet follows the server's movement speed; coalesced historical
	# waypoints may catch up only at a bounded multiple of that speed.
	network_move_speed = MONSTER_WAYPOINT_SPEED.calculate_server_driven(
		remaining,
		speed_time,
		server_move_speed,
		snapshot_span_seconds,
		NETWORK_MOVE_VISUAL_SLACK_SECONDS,
		NETWORK_MOVE_MIN_SPEED,
		NETWORK_MOVE_MAX_SPEED,
		NETWORK_MOVE_SNAP_EPSILON
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
	if _primitive_simplified_visual or _afk_visual_suspended:
		queue_redraw()
		if current_state.to_lower() in ["attack", "hit"]:
			fallback_action_generation += 1
			var generation := fallback_action_generation
			var tree: SceneTree = get_tree()
			if tree != null:
				tree.create_timer(FALLBACK_ACTION_RELEASE_SECONDS).timeout.connect(func():
					if not is_instance_valid(self) or not is_inside_tree() or current_hp <= 0:
						return
					if generation != fallback_action_generation:
						return
					if current_state not in ["attack", "hit"]:
						return
					_release_combat_action_state()
				)
		return
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

	if anim_to_play != "" and sprite.sprite_frames.get_frame_count(anim_to_play) > 0:
		var requested_state: String = current_state.to_lower()
		var requested_prefix: String = str(mapped_prefix) + "_"
		var has_requested_animation: bool = anim_to_play.begins_with(requested_prefix)

		if sprite.animation != anim_to_play or not sprite.is_playing():
			MonsterLoader.begin_audio_sequence(sprite)
			sprite.play(anim_to_play)
			# The authoritative map ticker sends many movement updates in
			# the same 250ms window. Do not make all idle/walk sprite loops
			# restart on the same animation frame; give each monster its own
			# stable visual phase. Attack/hit/death animations stay exact.
			if requested_state in ["idle", "walk"]:
				var loop_frames: int = sprite.sprite_frames.get_frame_count(anim_to_play)
				if loop_frames > 1:
					var offset: int = posmod(
						entity_id * 31 + (7 if requested_state == "walk" else 0),
						loop_frames
					)
					sprite.set_frame_and_progress(offset, 0.0)
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

func _ensure_target_indicator() -> void:
	if is_instance_valid(target_indicator) or not is_instance_valid(ui_container):
		return
	target_indicator = AnimatedSprite2D.new()
	target_indicator.name = "TargetIndicator"
	target_indicator.visible = false
	target_indicator.z_index = 80
	target_indicator.centered = true
	target_indicator.position = Vector2(ui_container.size.x / 2.0, -25.0)
	ui_container.add_child(target_indicator)
	if EffectLoader:
		var frames = EffectLoader.get_effect("Enemy_Icon/ENEMY_ICON_00")
		if frames:
			var names: PackedStringArray = frames.get_animation_names()
			if not names.is_empty():
				var anim_name: StringName = &"default" if frames.has_animation(&"default") else StringName(names[0])
				if frames.get_frame_count(anim_name) > 0:
					target_indicator.sprite_frames = frames
					target_indicator.animation = anim_name


func set_target_indicator(has_target: bool) -> void :
	if _afk_visual_suspended:
		if is_instance_valid(target_indicator):
			target_indicator.stop()
			target_indicator.visible = false
		return
	if has_target and not is_instance_valid(target_indicator):
		_ensure_target_indicator()
	if is_instance_valid(target_indicator):
		if OS.is_debug_build() and target_indicator.visible != has_target:
			# Keep the first-move latency distinct from a chase packet gap.
			_debug_last_aggro_move_packet_ms = 0
			_debug_aggro_started_ms = Time.get_ticks_msec() if has_target else 0
		target_indicator.visible = has_target
		if has_target:
			if _is_on_screen and not target_indicator.is_playing() and has_resumable_animation(target_indicator):
				target_indicator.play(target_indicator.animation)
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


func stop_combat_visual_effects() -> void:
	for node in [skill_use_effect, skill_hit_effect]:
		if is_instance_valid(node):
			node.stop()
			node.visible = false


func execute_skill_visuals(skill_id: int, _skill_level: int, target_node: Node2D = null, aoe_targets: Array = []) -> void :
	if is_instance_valid(target_node):
		_apply_visual_direction_from_vector(
			target_node.global_position - global_position
		)

	current_state = "attack"
	action_state_started_ms = Time.get_ticks_msec()
	sync_animation()
	# The attack state still runs and releases normally, but no FX resource is
	# loaded and no FX node is created for Mass mode.
	if GameManager and not GameManager.should_render_combat_effects():
		return

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
	if GameManager and not GameManager.should_render_combat_effects():
		return
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
