extends RefCounted
## One procedurally generated ImageTexture per entity TYPE, shared by all
## simplified entities. No PNG files, per-entity textures or runtime shaders.
## The 96x96 pixels are computed only on the first request. All later
## CanvasItem draws reuse one cached GPU texture.
const SIZE := 96
const HALF_SIZE := Vector2(48.0, 48.0)

static var _monster_texture: Texture2D = null
static var _player_texture: Texture2D = null


static func monster_texture() -> Texture2D:
	if _monster_texture == null:
		_monster_texture = _build_texture(false)
	return _monster_texture


static func player_texture() -> Texture2D:
	if _player_texture == null:
		_player_texture = _build_texture(true)
	return _player_texture


static func _build_texture(is_player: bool) -> Texture2D:
	var image := Image.create(SIZE, SIZE, false, Image.FORMAT_RGBA8)
	var spike_count: int = 9 if is_player else 12
	var sector: float = TAU / float(spike_count)
	# A tall, subtly narrower player silhouette distinguishes players from
	# the wider, more heavily spiked monster while reusing the same render path.
	var stretch_x: float = 0.84 if is_player else 1.0
	var stretch_y: float = 1.10 if is_player else 1.0
	var base_radius: float = 22.0 if is_player else 23.0
	var rim := Color(0.62, 0.75, 0.88, 1.0) if is_player else Color(0.64, 0.81, 0.97, 1.0)

	for y in range(SIZE):
		var dy: float = float(y) + 0.5 - HALF_SIZE.y
		for x in range(SIZE):
			var dx: float = float(x) + 0.5 - HALF_SIZE.x
			var px: float = dx / stretch_x
			var py: float = dy / stretch_y
			var radius: float = sqrt(px * px + py * py)
			var angle: float = atan2(py, px)
			# Narrow high-power lobes give sharp, irregular-looking star tips.
			# The long and short tips alternate, resembling a hand-drawn orb.
			var spike_wave: float = pow(maxf(0.0, cos(angle * float(spike_count))), 8.0)
			var spike_index: int = int(round(angle / sector))
			var tip_length: float = (17.0 if absi(spike_index) % 2 == 0 else 12.0)
			var edge: float = base_radius + tip_length * spike_wave
			var solid_alpha: float = clampf(edge + 0.65 - radius, 0.0, 1.0)

			# Very faint edge glow is stored IN the same texture. It adds no
			# extra draw command, particles or overlaid CanvasItems per entity.
			var glow_radius: float = sqrt(dx * dx + dy * dy)
			var glow: float = 0.09 * pow(
				clampf((46.0 - glow_radius) / 13.0, 0.0, 1.0), 2.0
			)
			var alpha: float = maxf(solid_alpha, glow)
			if alpha < 0.004:
				continue

			# Baked pearl gradient and an upper-left specular highlight.
			# Player relation color is applied later as a single draw modulate.
			var inner: float = 1.0 - pow(clampf(radius / 41.0, 0.0, 1.0), 1.35)
			var shine_x: float = dx + 10.0
			var shine_y: float = dy + 11.0
			var shine: float = clampf(
				1.0 - sqrt(shine_x * shine_x + shine_y * shine_y) / 19.0,
				0.0, 1.0
			)
			var color: Color = rim.lerp(
				Color.WHITE, clampf(inner * 0.68 + shine * 0.45, 0.0, 1.0)
			)
			image.set_pixel(x, y, Color(color.r, color.g, color.b, alpha))

	return ImageTexture.create_from_image(image)
