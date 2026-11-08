"""Regression contract: one cached runtime texture per simplified entity type.

The old monster/player multi-primitive _draw path must not come back:
it submits many colored polygons/circles per visible entity and can grow
CPU/GPU work with monster count. The replacement builds each image once.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


profile = read("client/scripts/client_performance_profile.gd")
manager = read("client/scripts/network_entity_manager.gd")
player = read("client/network_player.gd")
monster = read("client/scripts/monster_entity.gd")
cache = read("client/scripts/simplified_orb_cache.gd")
hud = read("client/game_hud.tscn")

# Existing saved styles 3/4 still map to Simplified instead of invalid UI.
assert "return SIMPLIFY_SIMPLIFIED" in profile
assert "SIMPLIFY_PIXEL_B := 3" in profile
assert "SIMPLIFY_MINIMAL := 4" in profile
assert "static func default_auto_style(_load_mode: int)" in profile
for needle in ('"player_simplify_style"', '"monster_simplify_style"',
               "prepare_performance_visual"):
    assert needle in manager, needle

# Two separate shapes are rendered from only two lazy, shared ImageTextures.
for needle in (
    "extends RefCounted", "static var _monster_texture: Texture2D",
    "static var _player_texture: Texture2D",
    "static func monster_texture() -> Texture2D:",
    "static func player_texture() -> Texture2D:",
    "if _monster_texture == null:", "if _player_texture == null:",
    "Image.create(SIZE, SIZE, false, Image.FORMAT_RGBA8)",
    "ImageTexture.create_from_image(image)",
    "var spike_count: int = 9 if is_player else 12",
    "var stretch_x: float = 0.84 if is_player else 1.0",
):
    assert needle in cache, needle
assert cache.count("ImageTexture.create_from_image(image)") == 1
assert "ResourceLoader.load" not in cache
assert "preload(" not in cache
assert "set_process(" not in cache

# Monster does ONE quad draw instead of many expensive primitive commands.
monster_draw = monster.split("func _draw_simplified_monster(", 1)[1].split("\n\nfunc _draw()", 1)[0]
assert monster_draw.count("draw_texture(") == 1
assert "SIMPLIFIED_ORB_CACHE.monster_texture()" in monster_draw
for old in ("draw_colored_polygon(", "draw_circle(", "draw_rect(", "for i in range(8):"):
    assert old not in monster_draw, old
for needle in (
    "func _simplified_monster_base_color()",
    "return Color(0.96, 0.985, 1.0, 1.0)",
    'if state in ["death", "die"]:',
    "sprite.sprite_frames = null",
    'preload("res://scripts/simplified_orb_cache.gd")',
):
    assert needle in monster, needle

# Remote player shares one texture for all relationship colors, with the
# original clickable hitbox and combat/death visibility semantics intact.
player_draw = player.split("func _draw_simplified_player_orb(", 1)[1].split("\n\nfunc _draw()", 1)[0]
assert player_draw.count("draw_texture(") == 1
assert "SIMPLIFIED_ORB_CACHE.player_texture()" in player_draw
assert "var tint: Color = relation" in player_draw
for old in ("draw_colored_polygon(", "draw_circle(", "draw_rect(", "draw_line("):
    assert old not in player_draw, old
for needle in (
    "func _simplified_relation_color()",
    'my_party not in ["", "0", "-1"]',
    "var dead := is_dead or state == \"death\"",
    "_draw_simplified_player_orb(Vector2.ZERO",
    "death_effect.visible = false",
    "doll_engine.visible = false",
    "doll_engine.clear_dolls()",
    "SIMPLIFIED_PLAYER_CLICK_SIZE",
):
    assert needle in player, needle
for color in (
    "Color(0.28, 1.0, 0.56, 1.0)",
    "Color(0.32, 0.70, 1.0, 1.0)",
    "Color(1.0, 0.28, 0.30, 1.0)",
    "Color(0.82, 0.88, 0.98, 1.0)",
):
    assert color in player, color
for old in ("func _draw_pixel_human_a", "func _draw_pixel_human_b",
            "func _draw_minimal_human", "GENERIC_PLAYER_RACE"):
    assert old not in player, old

assert "set_performance_simplified(false)" not in player
assert "set_performance_simplified(false)" not in monster
assert "popup/item_3/" not in hud[hud.index('[node name="OptionSimplifyMonsters"'):hud.index('[node name="BtnBackToLogin"')]
print("PASS: shared cached monster/player textured quads, relation tint, no old multi-primitive or sprite fallback")
