shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
uniform float sharpen_strength : hint_range(0.0, 1.0) = 0.35;
uniform float edge_threshold : hint_range(0.0, 0.2) = 0.025;
uniform float local_contrast : hint_range(0.0, 0.5) = 0.08;
uniform float vibrance : hint_range(0.0, 0.5) = 0.06;
uniform float tonal_curve : hint_range(0.0, 0.5) = 0.08;
uniform float anti_ringing_margin : hint_range(0.0, 0.5) = 0.12;


float luminance(vec3 color) {
	return dot(color, vec3(0.299, 0.587, 0.114));
}


void fragment() {
	vec2 texel_size = 1.0 / vec2(textureSize(screen_texture, 0));
	vec4 center = textureLod(screen_texture, SCREEN_UV, 0.0);
	vec3 north = textureLod(screen_texture, SCREEN_UV + vec2(0.0, -texel_size.y), 0.0).rgb;
	vec3 south = textureLod(screen_texture, SCREEN_UV + vec2(0.0, texel_size.y), 0.0).rgb;
	vec3 west = textureLod(screen_texture, SCREEN_UV + vec2(-texel_size.x, 0.0), 0.0).rgb;
	vec3 east = textureLod(screen_texture, SCREEN_UV + vec2(texel_size.x, 0.0), 0.0).rgb;
	vec3 neighbor_average = (north + south + west + east) * 0.25;

	float center_luma = luminance(center.rgb);
	float north_luma = luminance(north);
	float south_luma = luminance(south);
	float west_luma = luminance(west);
	float east_luma = luminance(east);
	float neighbor_luma = luminance(neighbor_average);
	float local_min = min(center_luma, min(min(north_luma, south_luma), min(west_luma, east_luma)));
	float local_max = max(center_luma, max(max(north_luma, south_luma), max(west_luma, east_luma)));
	float local_range = max(local_max - local_min, 0.00001);

	float adaptive_weight = smoothstep(edge_threshold, edge_threshold * 3.0, local_range);
	float shadow_protection = smoothstep(0.02, 0.16, center_luma);
	float highlight_protection = 1.0 - smoothstep(0.82, 0.98, center_luma);
	float tonal_protection = shadow_protection * highlight_protection;
	float luma_detail = center_luma - neighbor_luma;
	float enhanced_luma = center_luma + luma_detail * (sharpen_strength * adaptive_weight + local_contrast) * tonal_protection;

	float ringing_allowance = local_range * anti_ringing_margin;
	enhanced_luma = clamp(enhanced_luma, local_min - ringing_allowance, local_max + ringing_allowance);
	vec3 color = center.rgb + vec3(enhanced_luma - center_luma);

	float color_min = min(min(color.r, color.g), color.b);
	float color_max = max(max(color.r, color.g), color.b);
	float saturation = clamp(color_max - color_min, 0.0, 1.0);
	float enhanced_color_luma = luminance(color);
	float vibrance_highlight_protection = 1.0 - smoothstep(0.72, 0.96, enhanced_color_luma);
	float vibrance_weight = (1.0 - saturation) * vibrance_highlight_protection;
	vec3 gray = vec3(enhanced_color_luma);
	color = mix(gray, color, 1.0 + vibrance * vibrance_weight);

	float curve_protection = smoothstep(0.02, 0.18, enhanced_color_luma) * (1.0 - smoothstep(0.82, 0.98, enhanced_color_luma));
	color += tonal_curve * curve_protection * color * (1.0 - color) * (color - 0.5) * 2.0;
	COLOR = vec4(clamp(color, 0.0, 1.0), center.a);
}
