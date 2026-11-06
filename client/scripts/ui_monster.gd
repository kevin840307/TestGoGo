
extends AnimatedSprite2D


@export var monster_id: String = ""

@export var default_anim: String = "IDLE_1"

@export var anim_speed_scale: float = 1.0

func _ready() -> void :
	self.centered = false
	self.speed_scale = anim_speed_scale


	self.frame_changed.connect(_on_frame_changed)

	if monster_id != "" and MonsterLoader:
		var frames = MonsterLoader.get_monster_frames(monster_id)
		if frames:
			self.sprite_frames = frames


			var target_anim = default_anim
			if not self.sprite_frames.has_animation(target_anim):
				var anims = self.sprite_frames.get_animation_names()
				if anims.size() > 0:
					target_anim = anims[0]


			self.play(target_anim)
			_on_frame_changed()


func _on_frame_changed() -> void :
	if not self.sprite_frames: return

	var anim_name = self.animation
	if self.sprite_frames.has_animation(anim_name):
		var frame_texture = self.sprite_frames.get_frame_texture(anim_name, self.frame)
		if frame_texture:
			var offset_val = frame_texture.get_meta("old_offset") if frame_texture.has_meta("old_offset") else Vector2.ZERO
			var actual_offset_x = frame_texture.get_width() - offset_val.x if self.flip_h else offset_val.x
			self.offset = Vector2( - actual_offset_x, - offset_val.y)
