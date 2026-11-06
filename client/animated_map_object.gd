extends AnimatedSprite2D

@export var frame_offsets: PackedVector2Array = PackedVector2Array()


func _ready() -> void :
	frame_changed.connect(_apply_current_frame_offset)
	_apply_current_frame_offset()


func _apply_current_frame_offset() -> void :
	if frame >= 0 and frame < frame_offsets.size():
		offset = frame_offsets[frame]
