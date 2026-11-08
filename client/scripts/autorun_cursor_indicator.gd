extends Node2D

# Drawn once by CanvasItem; no new texture, sprite sheet, or process loop.
# Keep the recovered animated cursor intact and add a distinct, small
# double-chevron "continuous walk" icon only while the latch is active.
func _draw() -> void:
	var background := Rect2(Vector2(0.0, 0.0), Vector2(34.0, 30.0))
	draw_rect(background, Color(0.025, 0.10, 0.16, 0.92), true)
	draw_rect(background, Color(0.35, 0.88, 0.90, 0.95), false, 1.5)
	var glow := Color(0.32, 0.98, 0.80, 1.0)
	draw_colored_polygon(PackedVector2Array([
		Vector2(5, 5), Vector2(15, 15), Vector2(5, 25),
		Vector2(5, 19), Vector2(9, 15), Vector2(5, 11)
	]), glow)
	draw_colored_polygon(PackedVector2Array([
		Vector2(16, 5), Vector2(26, 15), Vector2(16, 25),
		Vector2(16, 19), Vector2(20, 15), Vector2(16, 11)
	]), glow)
