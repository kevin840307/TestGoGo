shader_type canvas_item;

// ==========================================
// 🎨 外部控制參數 (Godot Inspector / GDScript)
// ==========================================
// 🌟 擴充：將特效範圍從 0~3 擴充至 0~7
uniform int effect_type : hint_range(0, 7) = 0; 
uniform float hue_shift : hint_range(0.0, 360.0) = 0.0;
uniform float effect_speed : hint_range(0.1, 10.0) = 2.0;
uniform vec4 pulse_color : source_color = vec4(0.0, 0.8, 1.0, 1.0);

// ==========================================
// 🧮 輔助函數：RGB 與 HSL 色彩空間轉換
// ==========================================
vec3 rgb2hsl(vec3 c) {
    float cMin = min(min(c.r, c.g), c.b);
    float cMax = max(max(c.r, c.g), c.b);
    float l = (cMax + cMin) / 2.0;

    float d = cMax - cMin;
    if (d < 0.00001) {
        return vec3(0.0, 0.0, l);
    }

    float s = l > 0.5 ? d / (2.0 - cMax - cMin) : d / (cMax + cMin);
    float h = 0.0; 
    
    if (cMax - c.r < 0.00001) {
        h = (c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0);
    } else if (cMax - c.g < 0.00001) {
        h = (c.b - c.r) / d + 2.0;
    } else {
        h = (c.r - c.g) / d + 4.0;
    }
    h /= 6.0;

    return vec3(h, s, l);
}

float hue2rgb(float p, float q, float t) {
    t = fract(t);
    if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
    if (t < 1.0 / 2.0) return q;
    if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
    return p;
}

vec3 hsl2rgb(vec3 hsl) {
    float h = hsl.x;
    float s = hsl.y;
    float l = hsl.z;

    if (s < 0.00001) {
        return vec3(l, l, l);
    }

    float q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
    float p = 2.0 * l - q;

    return vec3(
        hue2rgb(p, q, h + 1.0 / 3.0),
        hue2rgb(p, q, h),
        hue2rgb(p, q, h - 1.0 / 3.0)
    );
}

float get_luminance(vec3 c) {
    return dot(c, vec3(0.299, 0.587, 0.114));
}

// ==========================================
// 🖼️ 主渲染邏輯
// ==========================================
void fragment() {
    vec4 tex_color = texture(TEXTURE, UV);
    
    if (tex_color.a <= 0.0) {
        COLOR = tex_color;
    } 
    else {
        if (effect_type == 0 && hue_shift == 0.0) {
            COLOR = tex_color;
        } 
        else {
            vec3 rgb = tex_color.rgb;
            vec3 hsl = rgb2hsl(rgb);
            
            if (effect_type == 0) {
                // 原版：單純色相偏移
                hsl.x = fract(hsl.x + (hue_shift / 360.0));
                rgb = hsl2rgb(hsl);
            } 
            else if (effect_type == 1) {
                // 原版：彩虹流動
                hsl.x = fract(hsl.x + (TIME * effect_speed * 0.2) + (UV.y * 1.5) + (UV.x * 0.5));
                hsl.y = clamp(hsl.y * 1.5, 0.0, 1.0); 
                hsl.z = clamp(hsl.z * 1.2, 0.0, 1.0);
                rgb = hsl2rgb(hsl);
            } 
            else if (effect_type == 2) {
                // 原版：自訂顏色呼吸發光
                hsl.x = fract(hsl.x + (hue_shift / 360.0));
                rgb = hsl2rgb(hsl);
                
                float pulse = (sin(TIME * effect_speed) * 0.5) + 0.5;
                float lum = get_luminance(rgb);
                float glow_mask = smoothstep(0.3, 0.8, lum); 
                vec3 glow = pulse_color.rgb * pulse * glow_mask * 1.5;
                rgb = clamp(rgb + glow, 0.0, 1.0);
            } 
            else if (effect_type == 3) {
                // 原版：高貴黃金流光
                float lum = get_luminance(rgb);
                float contrast_lum = smoothstep(0.1, 0.9, lum);
                
                vec3 dark_gold = vec3(0.3, 0.15, 0.0);
                vec3 bright_gold = vec3(1.0, 0.8, 0.2);
                vec3 pure_white = vec3(1.0, 1.0, 1.0);
                
                if (contrast_lum < 0.7) {
                    rgb = mix(dark_gold, bright_gold, contrast_lum / 0.7);
                } else {
                    rgb = mix(bright_gold, pure_white, (contrast_lum - 0.7) / 0.3);
                }
                
                float sweep = smoothstep(0.95, 1.0, sin(UV.y * 15.0 - TIME * effect_speed * 1.5));
                rgb += vec3(sweep * 0.5);
            }
            else if (effect_type == 4) {
                // 🌟 新增：賽博龐克色散 (Cyberpunk Glitch)
                // 重新採樣周圍像素製造 RGB 通道水平撕裂
                float split_amount = sin(TIME * effect_speed * 5.0 + UV.y * 25.0) * 0.015;
                // 偶爾發生劇烈抖動
                split_amount *= step(0.8, sin(TIME * effect_speed * 2.0)); 
                
                float r_channel = texture(TEXTURE, UV + vec2(split_amount, 0.0)).r;
                float b_channel = texture(TEXTURE, UV - vec2(split_amount, 0.0)).b;
                
                rgb = vec3(r_channel, rgb.g, b_channel);
                // 加上微弱的掃描線
                float scanline = sin(UV.y * 150.0 - TIME * 10.0) * 0.1;
                rgb = clamp(rgb - scanline, 0.0, 1.0);
            }
            else if (effect_type == 5) {
                // 🌟 新增：虛空腐化 (Void Corrupted)
                float lum = get_luminance(rgb);
                // 顛倒明暗，越亮的地方變越黑
                float inv_lum = 1.0 - lum; 
                
                vec3 dark_abyss = vec3(0.05, 0.0, 0.15); // 深邃暗紫
                vec3 void_energy = vec3(0.6, 0.0, 0.8);  // 亮紫紅
                
                // 動態脈動，帶點不規則感
                float throb = (sin(TIME * effect_speed * 1.5) * 0.5 + 0.5) * (sin(TIME * effect_speed * 0.5) * 0.5 + 0.5);
                
                rgb = mix(dark_abyss, void_energy, inv_lum * throb);
                // 讓裝備邊緣 (原始明度較高處) 保留一點銳利感
                rgb += vec3(smoothstep(0.8, 1.0, lum) * 0.3);
            }
            else if (effect_type == 6) {
                // 🌟 新增：冰晶鑽石 (Ice Crystal)
                float lum = get_luminance(rgb);
                // 大幅提升對比，製造水晶切面感
                float crystal_lum = smoothstep(0.3, 0.8, lum);
                
                vec3 deep_ice = vec3(0.0, 0.2, 0.6);
                vec3 bright_ice = vec3(0.6, 0.9, 1.0);
                
                rgb = mix(deep_ice, bright_ice, crystal_lum);
                
                // 快速銳利的斜向反光
                float shine = smoothstep(0.9, 1.0, sin((UV.x + UV.y) * 30.0 - TIME * effect_speed * 4.0));
                rgb = clamp(rgb + vec3(shine), 0.0, 1.0);
            }
            else if (effect_type == 7) {
                // 🌟 新增：熔岩高熱 (Magma Overdrive)
                float lum = get_luminance(rgb);
                
                vec3 coal = vec3(0.2, 0.05, 0.0);
                vec3 magma = vec3(0.9, 0.3, 0.0);
                vec3 plasma = vec3(1.0, 0.9, 0.2);
                
                // 利用 UV 和 Time 創造流動感
                float heat_wave = sin(UV.y * 12.0 - TIME * effect_speed * 3.0) * 0.5 + 0.5;
                float dynamic_lum = clamp(lum * (0.5 + heat_wave * 0.8), 0.0, 1.0);
                
                if (dynamic_lum < 0.4) {
                    rgb = mix(coal, magma, dynamic_lum / 0.4);
                } else {
                    rgb = mix(magma, plasma, (dynamic_lum - 0.4) / 0.6);
                }
            }

            COLOR = vec4(rgb, tex_color.a);
        }
    }
}
