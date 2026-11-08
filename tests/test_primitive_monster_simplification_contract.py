from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")


profile = read("client/scripts/client_performance_profile.gd")
monster = read("client/scripts/monster_entity.gd")

assert 'result["use_primitive_monsters"] = bool(' in profile
assert 'profile["use_primitive_monsters"] = bool(' in profile
assert 'GENERIC_MONSTER_SPRITE' not in monster
assert '_primitive_simplified_visual = simplified' in monster
assert 'var primitive_enabled := enabled' in monster
assert 'sprite.sprite_frames = null' in monster
assert 'func _draw() -> void:' in monster
assert 'if not _primitive_simplified_visual:' in monster
assert 'SIMPLIFIED_ORB_CACHE.monster_texture()' in monster
monster_orb = monster.split('func _draw_simplified_monster(', 1)[1].split('\\n\\nfunc _draw()', 1)[0]
assert monster_orb.count('draw_texture(') == 1
assert 'draw_colored_polygon(' not in monster_orb

print("PASS: every simplified monster uses one shared runtime texture draw without original SpriteFrames")
