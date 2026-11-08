extends SceneTree

# godot --headless --path client --script res://tests/npc_legacy_media_regression.gd
var failures := 0


func _check(condition: bool, description: String) -> void:
    if condition:
        print("[LEGACY_MEDIA_PASS] ", description)
    else:
        failures += 1
        push_error("[LEGACY_MEDIA_FAIL] " + description)


func _initialize() -> void:
    call_deferred("_run")


func _find_button(node: Node) -> Button:
    if node is Button:
        return node as Button
    for child in node.get_children():
        var found := _find_button(child)
        if found != null:
            return found
    return null


func _run() -> void:
    var skin = load("res://scripts/legacy_bubble_skin.gd") as GDScript
    var resolver = load("res://scripts/legacy_media_resolver.gd") as GDScript
    var cinematic = load("res://scripts/legacy_cinematic_panel.gd") as GDScript
    _check(skin != null and resolver != null and cinematic != null,
        "new bubble, media resolver and cinematic scripts parse")
    if skin == null or resolver == null or cinematic == null:
        quit(1)
        return

    for bubble_type in range(7):
        var texture: Texture2D = skin.get_texture(bubble_type)
        _check(texture != null and texture.get_width() > 0 and
            texture.get_height() > 0,
            "bubble %d renders even without local original PNG" % (bubble_type + 1))
        _check(skin.get_texture(bubble_type) == texture,
            "bubble texture %d is cached" % (bubble_type + 1))
    _check(skin.get_texture(-1) == skin.get_texture(0) and
        skin.get_texture(999) == skin.get_texture(6),
        "invalid bubble IDs are clamped safely")

    _check(resolver.normalized_stem("STORY_NAME_00A.spr") ==
        "STORY_NAME_00A", "story SPR converted file stem is recognized")
    _check(resolver.normalized_stem("X_mas_card\\BG_STATION_01.spr") ==
        "X_mas_card/BG_STATION_01", "station subfolders are preserved")
    _check(resolver.normalized_stem("na").is_empty() and
        resolver.normalized_stem("").is_empty(),
        "old empty/na placeholders do not load files")
    _check(resolver.normalized_stem("../secrets.png").is_empty() and
        resolver.normalized_stem("/etc/private.wav").is_empty() and
        resolver.normalized_stem("C:\\private.mp3").is_empty() and
        resolver.normalized_stem("res://other.png").is_empty(),
        "path traversal and absolute media paths are rejected")

    var host := Control.new()
    host.size = Vector2(800, 600)
    get_root().add_child(host)
    var panel = cinematic.new()
    host.add_child(panel)
    await process_frame
    var clicks := [0]
    panel.advance_requested.connect(func() -> void: clicks[0] += 1)
    panel.show_media({
        "ImageA": "STORY_NAME_00A.spr",
        "ImageB": "na",
        "Audio": "030.mp3"
    })
    _check(panel.visible, "cinematic overlay opens for m# scene")
    var button := _find_button(panel)
    _check(button != null, "cinematic has an accessible continue button")
    if button != null:
        button.pressed.emit()
    _check(clicks[0] == 1, "continue button emits one advance event")
    panel.dismiss()
    _check(not panel.visible, "scene dismiss closes the overlay and sound")
    panel.show_media({"ImageA": "na", "ImageB": "na", "Audio": ""})
    _check(panel.visible, "missing original images use a safe fallback UI")
    panel.dismiss()
    host.queue_free()

    var hud_text := FileAccess.get_file_as_string("res://game_hud.gd")
    _check(hud_text.contains("dialog_layout == \"Media\"") and
        hud_text.contains("_legacy_cinematic_panel.advance_requested.connect") and
        hud_text.contains("_hide_legacy_cinematic_panel()"),
        "HUD routes only legacy media packets through cinematic UI")
    print("[LEGACY_MEDIA] ", "ALL PASS" if failures == 0 else
        "%d FAILURES" % failures)
    quit(0 if failures == 0 else 1)
