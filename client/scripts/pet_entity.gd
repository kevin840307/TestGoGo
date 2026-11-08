
extends CharacterBody2D
class_name PetEntity

var entity_id: int = 0
var owner_id: int = 0
var pet_name: String = ""
var max_hp: int = 100
var current_hp: int = 100

var current_state: String = "idle"
var current_dir: String = "1"
var level: int = 1

var target_position: Vector2 = Vector2.ZERO
var move_destination: Vector2 = Vector2.ZERO
var move_path: PackedVector2Array = PackedVector2Array()
var move_path_index: int = 0
const MIN_LERP_WEIGHT: float = 8.0
const MAX_LERP_WEIGHT: float = 20.0
const TELEPORT_DISTANCE: float = 900.0
# Blocking.dat is authoritative for static map obstacles. Avoid adding the
# pet body radius on top of each 16px blocking cell, which causes corner jitter.
const PET_COLLISION_MASK: int = 0
const PET_WAYPOINT_REACH_DISTANCE: float = 8.0
const PET_STUCK_FRAME_LIMIT: int = 30

var network_move_active: bool = false
var network_move_speed: float = 0.0
var stuck_frames: int = 0

@onready var sprite = $AnimatedSprite2D
@onready var ui_container = $UIContainer
@onready var name_label = $UIContainer / NameLabel
@onready var idle_speech_bubble: NinePatchRect = $UIContainer / IdleSpeechBubble
@onready var idle_speech_label: Label = $UIContainer / IdleSpeechBubble / Label
@onready var idle_speech_timer: Timer = $IdleSpeechTimer

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}

const STATE_MAP = {
	"idle": "IDLE", "walk": "WALK", "attack": "ATTACK", 
	"hit": "HIT", "death": "DEATH"
}

var move_tween: Tween
var level_up_effect: AnimatedSprite2D = null
var idle_speech_fade_tween: Tween = null
var _render_visible: bool = true
var _render_notifier: VisibleOnScreenNotifier2D = null
var _afk_visual_suspended: bool = false
var _visual_outlook: String = ""
var _visual_color_index: int = 0
const RENDER_VISIBILITY_RECT: Rect2 = Rect2(-180.0, -220.0, 360.0, 380.0)

func _ready() -> void :
	collision_layer = 8
	collision_mask = PET_COLLISION_MASK
	var body_collision: CollisionShape2D = get_node_or_null("CollisionShape2D") as CollisionShape2D
	if body_collision and body_collision.shape == null:
		var body_shape := CircleShape2D.new()
		body_shape.radius = 7.0
		body_collision.shape = body_shape

	if not sprite.animation_finished.is_connected(_on_animation_finished):
		sprite.animation_finished.connect(_on_animation_finished)

	sprite.centered = false
	if not sprite.frame_changed.is_connected(_update_sprite_offset):
		sprite.frame_changed.connect(_update_sprite_offset)

	_setup_click_area()
	idle_speech_bubble.visible = false
	if not idle_speech_timer.timeout.is_connected(_on_idle_speech_timer_timeout):
		idle_speech_timer.timeout.connect(_on_idle_speech_timer_timeout)

	_render_notifier = VisibleOnScreenNotifier2D.new()
	_render_notifier.rect = RENDER_VISIBILITY_RECT
	add_child(_render_notifier)
	_render_notifier.screen_entered.connect(_on_render_screen_entered)
	_render_notifier.screen_exited.connect(_on_render_screen_exited)

func _on_render_screen_entered() -> void :
	_render_visible = true
	if _afk_visual_suspended:
		return
	if is_instance_valid(sprite) and not sprite.is_playing():
		sprite.play()
	if is_instance_valid(ui_container):
		ui_container.visible = true

func _on_render_screen_exited() -> void :
	_render_visible = false
	if is_instance_valid(sprite) and sprite.is_playing():
		sprite.pause()
	if is_instance_valid(ui_container):
		ui_container.visible = false

func is_render_visible() -> bool:
	return _render_visible

func _exit_tree() -> void :

	if CursorManager:
		CursorManager.unregister_hover(self)

func initialize_pet(data: Dictionary, extra: Dictionary) -> void :
	entity_id = int(data.get("EntityID", 0))
	pet_name = str(data.get("Name", "未知寵物"))
	owner_id = int(extra.get("OwnerID", 0))

	var spawn_pos = Vector2(float(data.get("LocX", 0)), float(data.get("LocY", 0)))
	global_position = spawn_pos
	target_position = spawn_pos
	move_destination = spawn_pos
	move_path = PackedVector2Array()
	move_path_index = 0

	max_hp = int(extra.get("MaxHP", 100))
	current_hp = int(extra.get("CurrentHP", max_hp))
	level = int(extra.get("Level", 1))

	name_label.text = pet_name


	_visual_outlook = str(extra.get("Sprite", ""))
	_visual_color_index = int(extra.get("ColorIndex", 0))
	if GameManager and GameManager.is_afk_mode_active():
		set_afk_visual_suspended(true)
		return

	_restore_visual_resources()
	_lock_ui_position()
	_update_sprite_offset()


func set_afk_visual_suspended(suspended: bool) -> void:
	if _afk_visual_suspended == suspended:
		return
	_afk_visual_suspended = suspended
	if suspended:
		if is_instance_valid(sprite):
			sprite.stop()
			sprite.sprite_frames = null
			sprite.material = null
			sprite.visible = false
		if is_instance_valid(level_up_effect):
			level_up_effect.stop()
			level_up_effect.sprite_frames = null
			level_up_effect.material = null
			level_up_effect.visible = false
		if is_instance_valid(idle_speech_bubble):
			idle_speech_bubble.visible = false
		if is_instance_valid(ui_container):
			ui_container.visible = false
		return
	_restore_visual_resources()


func is_afk_visual_suspended() -> bool:
	return _afk_visual_suspended


func _restore_visual_resources() -> void:
	if not is_instance_valid(sprite):
		return
	sprite.sprite_frames = null
	if MonsterLoader and not _visual_outlook.is_empty():
		var frames: SpriteFrames = MonsterLoader.get_monster_frames(_visual_outlook)
		if frames:
			sprite.sprite_frames = frames
	if _visual_color_index > 0:
		var material := ShaderMaterial.new()
		material.shader = PALETTE_SHADER
		material.set_shader_parameter("effect_type", 0)
		material.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(_visual_color_index, 0.0))
		sprite.material = material
	else:
		sprite.material = null
	sprite.visible = _render_visible
	if is_instance_valid(ui_container):
		ui_container.visible = _render_visible
	sync_animation()


func _lock_ui_position() -> void :
	if not is_instance_valid(sprite) or not is_instance_valid(sprite.sprite_frames) or not is_instance_valid(ui_container):
		return
	var scene_tree: SceneTree = get_tree()
	if scene_tree == null:
		return
	await scene_tree.process_frame
	if not is_instance_valid(self) or not is_inside_tree():
		return
	if not is_instance_valid(sprite) or not is_instance_valid(ui_container):
		return
	# Pet resources can be released/missing while awaiting the next frame.
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

func show_idle_speech_bubble(text: String) -> void :
	if _afk_visual_suspended:
		return
	if not idle_speech_bubble or not idle_speech_label or not idle_speech_timer:
		return

	if idle_speech_fade_tween and idle_speech_fade_tween.is_running():
		idle_speech_fade_tween.kill()

	idle_speech_label.text = text
	idle_speech_bubble.modulate.a = 1.0
	idle_speech_bubble.visible = true
	idle_speech_timer.start(5.0)

func _on_idle_speech_timer_timeout() -> void :
	if not idle_speech_bubble or not idle_speech_bubble.visible:
		return

	if idle_speech_fade_tween and idle_speech_fade_tween.is_running():
		idle_speech_fade_tween.kill()

	idle_speech_fade_tween = create_tween()
	idle_speech_fade_tween.tween_property(idle_speech_bubble, "modulate:a", 0.0, 0.5)
	idle_speech_fade_tween.tween_callback( func(): idle_speech_bubble.visible = false)

func update_state(state: String, dir: String) -> void :
	if current_hp <= 0 and state != "death": return

	current_dir = dir

	if state == "idle" and current_state == "walk" and move_tween and move_tween.is_running():
		return

	current_state = state
	sync_animation()

func network_move_to(new_pos: Vector2, speed_time: float = 1.0) -> void :
	if current_hp <= 0:
		return

	move_destination = new_pos
	var distance: float = global_position.distance_to(move_destination)

	if distance > TELEPORT_DISTANCE or speed_time <= 0.01:
		global_position = move_destination
		target_position = move_destination
		move_path = PackedVector2Array()
		move_path_index = 0
		velocity = Vector2.ZERO
		network_move_active = false
		network_move_speed = 0.0
		stuck_frames = 0
		current_state = "idle"
		sprite.speed_scale = 1.0
		sync_animation()
		if move_tween and move_tween.is_valid():
			move_tween.kill()
		return

	var map_id: int = 0
	if GameManager and is_instance_valid(GameManager.current_player):
		var owner_data = GameManager.current_player.get("data_model")
		if owner_data:
			map_id = int(owner_data.map_id)

	var owner_can_cross_special: bool = false
	if GameManager and is_instance_valid(GameManager.current_player):
		owner_can_cross_special = bool(
			GameManager.current_player.get("can_cross_special_terrain")
		)

	var route: PackedVector2Array = LegacyPathfinder.find_path(
		map_id,
		global_position,
		move_destination,
		owner_can_cross_special
	)

	if route.is_empty():
		_stop_network_move()
		return

	move_path = route
	move_path_index = 0
	target_position = move_path[0]

	if move_tween and move_tween.is_valid():
		move_tween.kill()

	network_move_speed = distance / max(speed_time, 0.01)
	network_move_active = true
	stuck_frames = 0
	current_state = "walk"
	sprite.speed_scale = 1.0
	sync_animation()


func _physics_process(delta: float) -> void :
	if not network_move_active or current_hp <= 0:
		return

	if move_path.is_empty() or move_path_index >= move_path.size():
		_stop_network_move()
		return

	var step_distance: float = network_move_speed * delta
	var stop_threshold: float = maxf(
		PET_WAYPOINT_REACH_DISTANCE,
		step_distance * 1.25
	)
	var map_id: int = 0
	var owner_can_cross_special: bool = false
	if GameManager and is_instance_valid(GameManager.current_player):
		var owner_data = GameManager.current_player.get("data_model")
		if owner_data:
			map_id = int(owner_data.map_id)
		owner_can_cross_special = bool(
			GameManager.current_player.get("can_cross_special_terrain")
		)

	while move_path_index + 1 < move_path.size():
		var next_waypoint: Vector2 = move_path[move_path_index + 1]
		var close_to_current: bool = (
			global_position.distance_to(target_position) <= stop_threshold
		)
		var next_is_visible: bool = LegacyPathfinder.is_segment_walkable(
			map_id,
			global_position,
			next_waypoint,
			owner_can_cross_special
		)

		if not close_to_current and not next_is_visible:
			break

		move_path_index += 1
		target_position = move_path[move_path_index]

	var to_target: Vector2 = target_position - global_position
	var distance: float = to_target.length()

	if move_path_index + 1 >= move_path.size() and distance <= stop_threshold:
		global_position = target_position
		_stop_network_move()
		return

	if distance <= 0.001:
		return

	velocity = to_target.normalized() * network_move_speed
	_update_direction_from_velocity(velocity)

	var old_position: Vector2 = global_position
	move_and_slide()
	if old_position.distance_to(global_position) >= maxf(0.25, step_distance * 0.1):
		stuck_frames = 0
		return

	stuck_frames += 1
	if stuck_frames >= PET_STUCK_FRAME_LIMIT:
		_stop_network_move()


func _stop_network_move() -> void :
	velocity = Vector2.ZERO
	move_path = PackedVector2Array()
	move_path_index = 0
	move_destination = global_position
	target_position = global_position
	network_move_active = false
	network_move_speed = 0.0
	stuck_frames = 0
	if current_state == "walk":
		current_state = "idle"
		sync_animation()


func _update_direction_from_velocity(move_velocity: Vector2) -> void :
	if move_velocity == Vector2.ZERO:
		return
	var angle_deg: float = posmod(rad_to_deg(move_velocity.angle()), 360.0)
	var index_raw: int = int(round(angle_deg / 45.0))
	current_dir = GameConstants.DIR_STRINGS[index_raw % 8]
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
		sprite.play(anim_to_play)
		_update_sprite_offset()

func _update_sprite_offset() -> void :
	if not sprite or not sprite.sprite_frames: return
	var anim_name = sprite.animation
	if sprite.sprite_frames.has_animation(anim_name):
		var frame_texture = sprite.sprite_frames.get_frame_texture(anim_name, sprite.frame)
		if frame_texture:
			var offset = frame_texture.get_meta("old_offset") if frame_texture.has_meta("old_offset") else Vector2.ZERO
			var actual_offset_x = frame_texture.get_width() - offset.x if sprite.flip_h else offset.x
			sprite.offset = Vector2( - actual_offset_x, - offset.y)

func _on_animation_finished() -> void :
	if (sprite.animation.begins_with("ATTACK") or sprite.animation.begins_with("HIT")) and current_hp > 0:
		current_state = "idle"
		sprite.speed_scale = 1.0
		sync_animation()

func update_hp(new_hp: int, hit_delay: float = 0.3, is_miss: bool = false) -> void :
	if hit_delay > 0:
		if not is_instance_valid(self) or not is_inside_tree():
			return
		var tree := get_tree()
		if tree == null:
			return
		var timer := tree.create_timer(hit_delay)
		await timer.timeout

	if not is_instance_valid(self) or not is_inside_tree():
		return

	var took_damage = new_hp < current_hp
	current_hp = new_hp

	if current_hp > 0 and sprite and took_damage and not is_miss:
		current_state = "hit"
		sync_animation()

func show_damage_number(damage: int, is_crit: bool = false, hit_delay: float = 0.3, split_count: int = 1, skill_id: int = 0, is_primary: bool = true) -> void :
	if _afk_visual_suspended:
		return
	if FloatingTextManager:
		FloatingTextManager.show_damage(self, damage, is_crit, hit_delay, split_count, skill_id, is_primary)

func _die(hit_delay: float = 0.0) -> void :
	if current_state == "death": return

	if hit_delay > 0:
		await get_tree().create_timer(hit_delay).timeout

	if not is_instance_valid(self) or not is_inside_tree():
		return

	current_hp = 0
	if ui_container: ui_container.visible = false

	current_state = "death"
	sprite.speed_scale = 1.0
	sync_animation()
	if CursorManager:
		CursorManager.unregister_hover(self)

	var fade_tween = create_tween()
	fade_tween.tween_interval(1.0)
	fade_tween.tween_property(self, "modulate:a", 0.0, 1.0)
	fade_tween.tween_callback(queue_free)

func show_level_up(is_mecha: bool = false) -> void :
	if _afk_visual_suspended:
		return
	if not is_instance_valid(level_up_effect):
		level_up_effect = AnimatedSprite2D.new()
		level_up_effect.name = "LevelUpEffect"
		level_up_effect.visible = false
		level_up_effect.z_index = 60
		level_up_effect.position = Vector2.ZERO
		level_up_effect.centered = false
		add_child(level_up_effect)

		if EffectLoader:
			level_up_effect.sprite_frames = EffectLoader.get_effect("Normal_Effect/Normal_Effect08")

		level_up_effect.animation_finished.connect( func():
			level_up_effect.visible = false
			level_up_effect.stop()
		)
		if not level_up_effect.frame_changed.is_connected(_on_effect_frame_changed.bind(level_up_effect)):
			level_up_effect.frame_changed.connect(_on_effect_frame_changed.bind(level_up_effect))

	if is_instance_valid(level_up_effect) and level_up_effect.sprite_frames:
		var target_anim = current_dir if level_up_effect.sprite_frames.has_animation(current_dir) else "default"
		if not level_up_effect.sprite_frames.has_animation(target_anim):
			var anims = level_up_effect.sprite_frames.get_animation_names()
			if anims.size() > 0: target_anim = anims[0]
			else: return

		level_up_effect.visible = true
		level_up_effect.animation = target_anim
		level_up_effect.frame = 0
		level_up_effect.play(target_anim)
		_on_effect_frame_changed(level_up_effect)

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

func _setup_click_area() -> void :
	var click_area = Area2D.new()
	click_area.name = "ClickArea"
	var click_shape = CollisionShape2D.new()
	var rect = RectangleShape2D.new()
	rect.size = Vector2(60, 60)
	click_shape.shape = rect
	click_shape.position = Vector2(0, -30)
	click_area.add_child(click_shape)
	add_child(click_area)

	click_area.input_event.connect(_on_click_area_input_event)
	click_area.mouse_entered.connect(_on_mouse_entered)
	click_area.mouse_exited.connect(_on_mouse_exited)

func _on_mouse_entered() -> void :
	if current_hp <= 0: return
	if CursorManager:
		var local_player = GameManager.current_player if GameManager else null
		var hover_state = "skill_target"


		if is_instance_valid(local_player) and "data_model" in local_player:
			if owner_id != local_player.data_model.db_id and local_player.data_model.pk_status == 1:
				hover_state = "attack"

		CursorManager.register_hover(self, hover_state)

		if CursorManager.cursor_sprite.animation == "skill":
			if is_instance_valid(local_player) and "ready_to_cast_skill_id" in local_player and local_player.ready_to_cast_skill_id > 0:
				if SkillLoader:
					var s_res = SkillLoader.get_skill_data(local_player.ready_to_cast_skill_id)
					if s_res:
						CursorManager.show_skill_target_tooltip(pet_name, s_res.skill_name)
			return

func _on_mouse_exited() -> void :
	if CursorManager:
		CursorManager.unregister_hover(self)
		if CursorManager.cursor_sprite.animation == "skill":
			CursorManager.hide_skill_target_tooltip()

func _on_click_area_input_event(_viewport: Node, event: InputEvent, _shape_idx: int) -> void :
	if current_hp <= 0: return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if CursorManager and CursorManager.current_hovered_entity != self:
			return

		var local_player = GameManager.current_player if GameManager else null
		if is_instance_valid(local_player):

			if "ready_to_cast_skill_id" in local_player and local_player.ready_to_cast_skill_id > 0:
				local_player.pending_skill_id = local_player.ready_to_cast_skill_id
				local_player.combat_target = self
				local_player.ready_to_cast_skill_id = -1
				if CursorManager:
					CursorManager.cancel_skill_state()
				get_viewport().set_input_as_handled()
				return


			if owner_id != local_player.data_model.db_id:
				if local_player.data_model.pk_status == 1:
					local_player.pending_skill_id = -1
					local_player.combat_target = self
					get_viewport().set_input_as_handled()
