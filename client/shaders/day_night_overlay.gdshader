shader_type canvas_item;
render_mode unshaded;

// 0.0 is daylight; 1.0 is the full nighttime effect.
uniform float night_strength : hint_range(0.0, 1.0) = 0.0;
uniform vec2 player_screen_uv = vec2(0.5, 0.5);
uniform vec2 viewport_size = vec2(1200.0, 900.0);
uniform float fully_lit_radius : hint_range(0.0, 1.0) = 0.12;
uniform float darkness_radius : hint_range(0.0, 2.0) = 0.70;
uniform float outer_brightness : hint_range(0.0, 1.0) = 0.30;


void fragment() {
	vec2 safe_viewport_size = max(viewport_size, vec2(1.0));
	vec2 offset = SCREEN_UV - player_screen_uv;
	offset.x *= safe_viewport_size.x / safe_viewport_size.y;

	float inner_radius = max(fully_lit_radius, 0.0);
	float outer_radius = max(darkness_radius, inner_radius + 0.0001);
	float radial_darkness = smoothstep(inner_radius, outer_radius, length(offset));
	float maximum_opacity = 1.0 - clamp(outer_brightness, 0.0, 1.0);
	float overlay_opacity = clamp(night_strength, 0.0, 1.0) * radial_darkness * maximum_opacity;

	COLOR = vec4(0.0, 0.0, 0.0, overlay_opacity);
}
