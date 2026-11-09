"""Regression guards for the compact, texture-free entity simplification path."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def require(source: str, needle: str) -> None:
    assert needle in source, f"missing contract: {needle}"


profile = read("client/scripts/client_performance_profile.gd")
hud = read("client/game_hud.tscn")
monster = read("client/scripts/monster_entity.gd")
player = read("client/network_player.gd")
manager = read("client/scripts/network_entity_manager.gd")
skill_card = read("client/scripts/skill_card.gd")

for name in ("SIMPLIFY_AUTO", "SIMPLIFY_NORMAL", "SIMPLIFY_SIMPLIFIED"):
    require(profile, name)
require(profile, "return SIMPLIFY_SIMPLIFIED")
require(profile, "static func default_auto_style(_load_mode: int)")
require(profile, 'result["use_primitive_monsters"] = bool(')

for option, values in (
    ("OptionSimplifyPlayers", ("全顯示", "只顯示敵人", "只顯示隊友", "都不顯示")),
    ("OptionSimplifyMonsters", ("顯示", "不顯示")),
):
    match = re.search(
        rf'\\[node name="{option}" type="OptionButton".*?(?=\\n\\[node |\\Z)',
        hud, re.S,
    )
    assert match, f"missing selector {option}"
    widget = match.group()
    require(widget, f"item_count = {len(values)}")
    for idx, name in enumerate(values):
        require(widget, f'popup/item_{idx}/text = "{name}"')
    assert f"popup/item_{len(values)}/" not in widget


require(monster, "func _draw_simplified_monster")
require(monster, "SIMPLIFIED_ORB_CACHE.monster_texture()")
require(monster, "draw_texture(")
require(monster, "sprite.sprite_frames = null")
require(monster, 'if state in ["death", "die"]:')
require(monster, "var primitive_enabled := enabled")
assert "GENERIC_MONSTER_SPRITE" not in monster
assert "func _draw_pixel_monster_" not in monster

require(player, "func _draw_simplified_player_orb")
require(player, "func _simplified_relation_color")
require(player, "SIMPLIFIED_ORB_CACHE.player_texture()")
require(player, "draw_texture(")
require(player, "SIMPLIFIED_PLAYER_CLICK_SIZE")
require(player, "doll_engine.visible = false")
require(player, "doll_engine.clear_dolls()")
require(player, "func _play_death_effect() -> void:")
require(player, "func play_mecha_transform() -> void:")
for relation in ("party_id", "club_id", "pk_status"):
    require(player, relation)
assert "func _draw_pixel_human_" not in player

for name in ('"player_simplify_style"', '"monster_simplify_style"',
             "pre_simplify_player", "prepare_performance_visual"):
    require(manager, name)

require(skill_card, 'get_node("Margin/Content") as HBoxContainer')
assert "$Margin / Content" not in skill_card

cache = read("client/scripts/simplified_orb_cache.gd")
require(cache, "static var _monster_texture: Texture2D")
require(cache, "static var _player_texture: Texture2D")
require(cache, "ImageTexture.create_from_image(image)")
assert cache.count("ImageTexture.create_from_image(image)") == 1
print("PASS: fixed 4/2 visibility options, existing cached orb path and unchanged hitboxes")
