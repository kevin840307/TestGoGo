from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")


def require(text: str, needle: str, message: str) -> None:
    if needle not in text:
        raise AssertionError(message)


profile = read("client/scripts/client_performance_profile.gd")
manager = read("client/scripts/network_entity_manager.gd")
player = read("client/network_player.gd")
monster = read("client/scripts/monster_entity.gd")
hud = read("client/game_hud.tscn")

for name in ("SIMPLIFY_PIXEL_A", "SIMPLIFY_PIXEL_B", "SIMPLIFY_MINIMAL"):
    require(profile, name, f"missing simplify style {name}")

require(profile, '"player_simplify_style"', "profile must carry player procedural style")
require(profile, '"monster_simplify_style"', "profile must carry monster procedural style")
require(manager, '"player_simplify_style"', "manager must pass player style")
require(manager, '"monster_simplify_style"', "manager must pass monster style")
require(manager, "prepare_performance_visual", "entities must be simplified before scene setup")

if "GENERIC_PLAYER_RACE" in player or "GENERIC_PLAYER_HAIR" in player:
    raise AssertionError("simplified player must not rebuild a generic paper doll")
if "set_performance_simplified(false)" in player:
    raise AssertionError("targeting a simplified player must not restore full artwork")
if "set_performance_simplified(false)" in monster:
    raise AssertionError("targeting a simplified monster must not restore full artwork")

for needle in (
    "func _draw_pixel_human_a",
    "func _draw_pixel_human_b",
    "func _draw_minimal_human",
    "func _draw_relation_glow",
    "SIMPLIFIED_PLAYER_CLICK_SIZE",
    'data_model.party_id = str(data.get("PartyID"',
):
    require(player, needle, f"missing procedural player contract: {needle}")

for color in (
    "Color(0.28, 1.0, 0.56, 1.0)",
    "Color(0.32, 0.70, 1.0, 1.0)",
    "Color(1.0, 0.28, 0.30, 1.0)",
    "Color(0.82, 0.88, 0.98, 1.0)",
):
    require(player, color, "party/club/attackable/normal relation cue is incomplete")

require(player, "func _play_death_effect() -> void:", "simplified player must override death visuals")
require(player, "death_effect.visible = false", "simplified death must hide original death SpriteFrames")
require(player, "doll_engine.visible = false", "simplified player must hide layered paper doll")
require(player, "func play_mecha_transform() -> void:", "simplified player must skip full mecha transition")

for needle in (
    "func _draw_pixel_monster_a",
    "func _draw_pixel_monster_b",
    "func _draw_minimal_monster",
    "func _simplified_monster_base_color",
    'state in ["death", "die"]',
    "sprite.sprite_frames = null",
):
    require(monster, needle, f"missing procedural monster contract: {needle}")

if "FAST_MONSTER_COLORS" in monster:
    raise AssertionError("one simplified monster style must use one shared visual palette")

for label in (
    'popup/item_2/text = "像素人"',
    'popup/item_3/text = "裝甲人"',
    'popup/item_2/text = "像素魔物"',
    'popup/item_3/text = "像素獸"',
    'popup/item_4/text = "棋子"',
):
    require(hud, label, f"missing compact text-only simplify option: {label}")

print("PASS: procedural player/monster simplification stays texture-free across targeting, relation and death")
