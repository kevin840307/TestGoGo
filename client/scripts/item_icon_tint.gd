extends RefCounted

const COLOR_INDEX_TO_DEGREES: = {
	0: 0.0, 
	1: 90.0, 
	2: 180.0, 
	3: 270.0, 
	4: 45.0, 
	5: 135.0, 
	6: 225.0, 
	7: 315.0
}

static var _texture_cache: Dictionary = {}

static func get_tinted_icon(source_texture: Texture2D, color_index: int) -> Texture2D:
	if source_texture == null or color_index == 0 or not COLOR_INDEX_TO_DEGREES.has(color_index):
		return source_texture

	var cache_key: = "%d:%d" % [source_texture.get_rid().get_id(), color_index]
	if _texture_cache.has(cache_key):
		return _texture_cache[cache_key]

	var source_image: = source_texture.get_image()
	if source_image == null or source_image.is_empty():
		return source_texture
	if source_image.is_compressed() and source_image.decompress() != OK:
		return source_texture

	var tinted_image: = source_image.duplicate() as Image
	if tinted_image == null:
		return source_texture

	var hue_offset: float = float(COLOR_INDEX_TO_DEGREES[color_index]) / 360.0
	for y in range(tinted_image.get_height()):
		for x in range(tinted_image.get_width()):
			var source_color: = tinted_image.get_pixel(x, y)
			if source_color.a <= 0.0:
				continue
			var hsl: = _rgb_to_hsl(source_color)
			hsl.x = fposmod(hsl.x + hue_offset, 1.0)
			var tinted_color: = _hsl_to_rgb(hsl)
			tinted_color.a = source_color.a
			tinted_image.set_pixel(x, y, tinted_color)

	var tinted_texture: = ImageTexture.create_from_image(tinted_image)
	_texture_cache[cache_key] = tinted_texture
	return tinted_texture

static func _rgb_to_hsl(color: Color) -> Vector3:
	var minimum: = minf(minf(color.r, color.g), color.b)
	var maximum: = maxf(maxf(color.r, color.g), color.b)
	var lightness: = (maximum + minimum) / 2.0
	var difference: = maximum - minimum
	if difference < 1e-05:
		return Vector3(0.0, 0.0, lightness)

	var saturation: = difference / (2.0 - maximum - minimum) if lightness > 0.5 else difference / (maximum + minimum)
	var hue: = 0.0
	if maximum - color.r < 1e-05:
		hue = (color.g - color.b) / difference + (6.0 if color.g < color.b else 0.0)
	elif maximum - color.g < 1e-05:
		hue = (color.b - color.r) / difference + 2.0
	else:
		hue = (color.r - color.g) / difference + 4.0

	return Vector3(hue / 6.0, saturation, lightness)

static func _hsl_to_rgb(hsl: Vector3) -> Color:
	if hsl.y < 1e-05:
		return Color(hsl.z, hsl.z, hsl.z)

	var q: = hsl.z * (1.0 + hsl.y) if hsl.z < 0.5 else hsl.z + hsl.y - hsl.z * hsl.y
	var p: = 2.0 * hsl.z - q
	return Color(
		_hue_to_rgb(p, q, hsl.x + 1.0 / 3.0), 
		_hue_to_rgb(p, q, hsl.x), 
		_hue_to_rgb(p, q, hsl.x - 1.0 / 3.0)
	)

static func _hue_to_rgb(p: float, q: float, hue: float) -> float:
	hue = fposmod(hue, 1.0)
	if hue < 1.0 / 6.0:
		return p + (q - p) * 6.0 * hue
	if hue < 1.0 / 2.0:
		return q
	if hue < 2.0 / 3.0:
		return p + (q - p) * (2.0 / 3.0 - hue) * 6.0
	return p
