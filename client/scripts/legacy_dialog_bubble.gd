extends RefCounted

# An optional RichTextLabel over the existing bubble Label. Only trusted
# NPC quest markup uses this path; normal player chat stays plain text.
static func display(bubble: Control, fallback_label: Label, text: String, rich_text: bool) -> void:
    var rendered: RichTextLabel = bubble.get_node_or_null("LegacyDialogRichText") as RichTextLabel
    var has_markup := rich_text and (
        text.contains("[color=") or text.contains("[b]") or
        text.contains("[i]") or text.contains("[font_size=") or
        text.contains("[u]")
    )

    if not has_markup:
        fallback_label.visible = true
        fallback_label.text = text
        if is_instance_valid(rendered):
            rendered.visible = false
        return

    if not is_instance_valid(rendered):
        rendered = RichTextLabel.new()
        rendered.name = "LegacyDialogRichText"
        rendered.bbcode_enabled = true
        rendered.mouse_filter = Control.MOUSE_FILTER_IGNORE
        rendered.scroll_active = false
        rendered.fit_content = false
        rendered.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
        rendered.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
        rendered.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
        rendered.add_theme_color_override("default_color", Color.BLACK)
        rendered.add_theme_font_size_override("normal_font_size", 13)
        bubble.add_child(rendered)

    rendered.position = fallback_label.position
    rendered.size = fallback_label.size
    rendered.text = text
    rendered.visible = true
    fallback_label.visible = false
