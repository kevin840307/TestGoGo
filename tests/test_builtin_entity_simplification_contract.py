from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")


def require(text: str, needle: str, message: str) -> None:
    if needle not in text:
        raise AssertionError(message)


profile = read("client/scripts/client_performance_profile.gd")
hud_scene = read("client/game_hud.tscn")
monster = read("client/scripts/monster_entity.gd")
network_player = read("client/network_player.gd")
entity_manager = read("client/scripts/network_entity_manager.gd")
skill_card = read("client/scripts/skill_card.gd")

for mode in (
    "SIMPLIFY_PIXEL_A := 2",
    "SIMPLIFY_PIXEL_B := 3",
    "SIMPLIFY_MINIMAL := 4",
):
    require(profile, mode, f"missing simplification mode: {mode}")

for label in (
    '"像素人"',
    '"裝甲人"',
    '"像素魔物"',
    '"像素獸"',
    '"棋子"',
):
    require(
        hud_scene,
        label,
        f"missing compact text-only simplification choice: {label}",
    )

if "開啟（內建圖形）" in hud_scene:
    raise AssertionError(
        "monster selector must stay compact and text-only"
    )

for helper in (
    "func _draw_pixel_monster_a",
    "func _draw_pixel_monster_b",
    "func _draw_minimal_monster",
):
    require(monster, helper, f"missing built-in monster renderer: {helper}")

require(
    monster,
    "sprite.sprite_frames = null",
    "simplified monsters must release original SpriteFrames",
)
if "set_performance_simplified(false)" in monster:
    raise AssertionError(
        "targeting/combat must not restore original monster artwork"
    )
require(
    monster,
    'if state in ["death", "die"]:',
    "simplified monster death must stay on the built-in renderer",
)

for helper in (
    "func _draw_pixel_human_a",
    "func _draw_pixel_human_b",
    "func _draw_minimal_human",
    "func _draw_relation_glow",
):
    require(network_player, helper, f"missing built-in player renderer: {helper}")

if "GENERIC_PLAYER_RACE" in network_player:
    raise AssertionError(
        "simplified players must not construct a generic paper doll"
    )
if "set_performance_simplified(false)" in network_player:
    raise AssertionError(
        "targeting a simplified player must not restore the full paper doll"
    )
require(
    network_player,
    "doll_engine.visible = false",
    "simplified players must hide the layered paper doll",
)
require(
    network_player,
    "doll_engine.clear_dolls()",
    "switching to simplified players must release existing paper-doll layers",
)
for relation in ("party_id", "club_id", "pk_status"):
    require(
        network_player,
        relation,
        f"player simplification must preserve {relation} relationship cue",
    )
require(
    network_player,
    "SIMPLIFIED_PLAYER_CLICK_SIZE",
    "simplified players must keep an enlarged click target",
)
require(
    network_player,
    "func _play_death_effect() -> void:",
    "simplified player death must stay on the procedural renderer",
)

require(
    entity_manager,
    '"player_simplify_style"',
    "entity manager must pass the selected player pixel style",
)
require(
    entity_manager,
    '"monster_simplify_style"',
    "entity manager must pass the selected monster pixel style",
)
require(
    entity_manager,
    "pre_simplify_player",
    "simplified players must be configured before add_child/paper-doll setup",
)

require(
    skill_card,
    'get_node("Margin/Content") as HBoxContainer',
    "SkillCard must use an explicit content node path",
)
if "$Margin / Content" in skill_card:
    raise AssertionError(
        "SkillCard contains the node-path expression that hid the skill list"
    )

print(
    "PASS: built-in pixel monsters/players, relation glow, combat persistence, "
    "compact selectors, and SkillCard load contract"
)
