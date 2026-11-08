extends Control

signal advance_requested

const MEDIA_RESOLVER = preload("res://scripts/legacy_media_resolver.gd")

var _picture_a: TextureRect
var _picture_b: TextureRect
var _missing_label: Label
var _audio: AudioStreamPlayer


func _ready() -> void:
    set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    mouse_filter = Control.MOUSE_FILTER_STOP
    z_index = 220

    var dim := ColorRect.new()
    dim.color = Color(0.02, 0.04, 0.10, 0.84)
    dim.mouse_filter = Control.MOUSE_FILTER_IGNORE
    add_child(dim)
    dim.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

    var center := CenterContainer.new()
    center.mouse_filter = Control.MOUSE_FILTER_IGNORE
    add_child(center)
    center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

    var panel := PanelContainer.new()
    panel.custom_minimum_size = Vector2(590, 350)
    var panel_style := StyleBoxFlat.new()
    panel_style.bg_color = Color(0.09, 0.13, 0.22, 0.98)
    panel_style.border_color = Color(0.48, 0.60, 0.76, 1.0)
    panel_style.set_border_width_all(2)
    panel_style.set_corner_radius_all(12)
    panel.add_theme_stylebox_override("panel", panel_style)
    center.add_child(panel)

    var content := VBoxContainer.new()
    content.add_theme_constant_override("separation", 12)
    panel.add_child(content)

    var title := Label.new()
    title.text = "劇情演出"
    title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    title.add_theme_font_size_override("font_size", 21)
    title.add_theme_color_override("font_color", Color.WHITE)
    content.add_child(title)

    var visuals := HBoxContainer.new()
    visuals.alignment = BoxContainer.ALIGNMENT_CENTER
    visuals.add_theme_constant_override("separation", 10)
    visuals.custom_minimum_size = Vector2(560, 240)
    content.add_child(visuals)

    _picture_a = _new_picture(visuals)
    _picture_b = _new_picture(visuals)

    _missing_label = Label.new()
    _missing_label.text = "原版演出素材尚未匯入"
    _missing_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    _missing_label.add_theme_color_override(
        "font_color", Color(0.78, 0.83, 0.91))
    content.add_child(_missing_label)

    var next_button := Button.new()
    next_button.text = "繼續 ▶"
    next_button.custom_minimum_size = Vector2(0, 42)
    next_button.pressed.connect(func() -> void: advance_requested.emit())
    content.add_child(next_button)

    _audio = AudioStreamPlayer.new()
    _audio.bus = "Master"
    add_child(_audio)
    hide()


func _new_picture(parent: HBoxContainer) -> TextureRect:
    var picture := TextureRect.new()
    picture.custom_minimum_size = Vector2(255, 210)
    picture.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    picture.size_flags_vertical = Control.SIZE_EXPAND_FILL
    picture.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
    picture.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
    picture.mouse_filter = Control.MOUSE_FILTER_IGNORE
    parent.add_child(picture)
    return picture


func show_media(media: Dictionary) -> void:
    _audio.stop()
    _audio.stream = null
    var a_path: String = MEDIA_RESOLVER.find_texture_path(
        str(media.get("ImageA", "")))
    var b_path: String = MEDIA_RESOLVER.find_texture_path(
        str(media.get("ImageB", "")))

    _picture_a.texture = null
    _picture_b.texture = null
    if not a_path.is_empty():
        _picture_a.texture = load(a_path) as Texture2D
    if not b_path.is_empty():
        _picture_b.texture = load(b_path) as Texture2D
    _picture_a.visible = _picture_a.texture != null
    _picture_b.visible = _picture_b.texture != null
    _missing_label.visible = not _picture_a.visible and not _picture_b.visible

    var audio_path: String = MEDIA_RESOLVER.find_audio_path(
        str(media.get("Audio", "")))
    if not audio_path.is_empty():
        _audio.stream = load(audio_path) as AudioStream
        if _audio.stream != null:
            var percent := 100.0
            if AudioManager:
                percent = float(AudioManager.current_se_volume_pct)
            var volume := clampf(percent / 100.0, 0.0, 1.0) * 0.25
            _audio.volume_db = linear_to_db(volume) if volume > 0.0 else -80.0
            _audio.play()
    show()


func dismiss() -> void:
    if is_instance_valid(_audio):
        _audio.stop()
        _audio.stream = null
    hide()
