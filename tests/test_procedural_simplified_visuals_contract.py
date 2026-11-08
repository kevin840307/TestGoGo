"""Visual/state contract for painted spiky monster orbs and relation-colored player orbs."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


profile = read("client/scripts/client_performance_profile.gd")
manager = read("client/scripts/network_entity_manager.gd")
player = read("client/network_player.gd")
monster = read("client/scripts/monster_entity.gd")
hud = read("client/game_hud.tscn")

# Existing saved values 3/4 must not select an out-of-range UI index.
assert "return SIMPLIFY_SIMPLIFIED" in profile
assert "SIMPLIFY_PIXEL_B := 3" in profile
assert "SIMPLIFY_MINIMAL := 4" in profile
assert "static func default_auto_style(_load_mode: int)" in profile
assert '"player_simplify_style"' in manager
assert '"monster_simplify_style"' in manager
assert "prepare_performance_visual" in manager

# Monster is ONE pearl-white spiky orb with layered color (cheap fake gradient).
for needle in (
    "func _simplified_monster_base_color()",
    "return Color(0.96, 0.985, 1.0, 1.0)",
    "func _draw_simplified_monster(",
    "for i in range(8):",
    "draw_colored_polygon(",
    "draw_circle(",
    "draw_rect(",
    "sprite.sprite_frames = null",
    'if state in ["death", "die"]:',
):
    assert needle in monster, needle
for old in ("func _draw_pixel_monster_a", "func _draw_pixel_monster_b",
            "func _draw_minimal_monster", "GENERIC_MONSTER_SPRITE"):
    assert old not in monster, f"obsolete monster art path: {old}"

# Remote player is ONE different, elongated orb. Relation tint affects fill.
for needle in (
    "func _draw_simplified_player_orb(",
    "relation.lightened(0.22)",
    "Color(relation.r, relation.g, relation.b, 0.54)",
    "draw_colored_polygon(",
    "draw_circle(",
    "var dead := is_dead or state == \"death\"",
    "_draw_simplified_player_orb(Vector2.ZERO",
    "death_effect.visible = false",
    "doll_engine.visible = false",
    "doll_engine.clear_dolls()",
):
    assert needle in player, needle
for color in (
    "Color(0.28, 1.0, 0.56, 1.0)",  # party
    "Color(0.32, 0.70, 1.0, 1.0)",  # club
    "Color(1.0, 0.28, 0.30, 1.0)",  # attackable
    "Color(0.82, 0.88, 0.98, 1.0)",  # normal
):
    assert color in player, color
for old in ("func _draw_pixel_human_a", "func _draw_pixel_human_b",
            "func _draw_minimal_human", "GENERIC_PLAYER_RACE"):
    assert old not in player, f"obsolete player art path: {old}"

assert "set_performance_simplified(false)" not in player
assert "set_performance_simplified(false)" not in monster
assert "popup/item_3/" not in hud[hud.index('[node name="OptionSimplifyMonsters"'):hud.index('[node name="BtnBackToLogin"')]
print("PASS: single gradient-like orb per entity type, auto relation tint, attack/death stay procedural")
