extends RefCounted

# Existing recovered client expects BG_BUBBLE_01..07, but PNG assets may be
# outside the repository. Use the original image whenever installed; otherwise
# create a small cached substitute instead of leaving speech invisible.
const BUBBLE_PATH = "res://ui/BG_BUBBLE_%02d.png"
const ACCENTS = [
    Color(0.34, 0.60, 0.87),
    Color(0.35, 0.71, 0.52),
    Color(0.91, 0.66, 0.30),
    Color(0.66, 0.51, 0.85),
    Color(0.32, 0.74, 0.78),
    Color(0.87, 0.45, 0.49),
    Color(0.48, 0.55, 0.68)
]

static var _textures: Dictionary = {}


static func get_texture(bubble_type: int) -> Texture2D:
    var safe_type := clampi(bubble_type, 0, 6)
    if _textures.has(safe_type):
        return _textures[safe_type] as Texture2D

    var original_path := BUBBLE_PATH % (safe_type + 1)
    var texture: Texture2D = null
    if ResourceLoader.exists(original_path):
        texture = load(original_path) as Texture2D

    if texture == null:
        texture = _make_fallback(safe_type)
    _textures[safe_type] = texture
    return texture


static func _make_fallback(bubble_type: int) -> Texture2D:
    # No texture generation in _process; at most seven tiny cached images.
    # This is an accessibility fallback, not a claim of original art accuracy.
    var image := Image.create(176, 100, false, Image.FORMAT_RGBA8)
    image.fill(Color.TRANSPARENT)
    var border: Color = ACCENTS[bubble_type]
    var fill := Color(1.0, 1.0, 1.0, 0.96)

    # Rounded body (screen-space margin 5) and a small bottom speech tail.
    for y in range(5, 87):
        for x in range(5, 171):
            var center_x := clampi(x, 16, 159)
            var center_y := clampi(y, 16, 75)
            var dx := x - center_x
            var dy := y - center_y
            if dx * dx + dy * dy > 121:
                continue
            var edge := x < 8 or x > 167 or y < 8 or y > 83
            image.set_pixel(x, y, border if edge else fill)

    for y in range(84, 98):
        var half_width := maxi(0, 11 - (y - 84))
        for x in range(88 - half_width, 89 + half_width):
            if x < 0 or x >= image.get_width():
                continue
            var edge := (x <= 88 - half_width + 1 or
                x >= 88 + half_width - 1)
            image.set_pixel(x, y, border if edge else fill)

    return ImageTexture.create_from_image(image)
