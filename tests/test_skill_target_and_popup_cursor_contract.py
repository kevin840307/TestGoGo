from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")


def require(text: str, needle: str, message: str) -> None:
    if needle not in text:
        raise AssertionError(message)


player = read("client/player.gd")
skill_card = read("client/scripts/skill_card.gd")
skill_window = read("client/scripts/skill_window.gd")
cursor = read("client/scripts/cursor_manager.gd")
hud = read("client/game_hud.gd")
combat = read("server/Application/Combat/CombatService.cs")

require(
    player,
    "is_skill_aoe_center_toggle_available",
    "player must classify which learned skills allow AOE center switching",
)
require(
    player,
    "is_skill_aoe_self_center(skill_id)",
    "player must read the persisted per-skill AOE center preference",
)
require(
    player,
    "_can_reuse_combat_target_for_attack_skill",
    "attack skills must reuse a valid selected combat target",
)
require(
    player,
    "preserve_combat_target",
    "reused combat targets must survive a skill cast",
)
require(
    player,
    "int(data_model.db_id),\n\t\t\t\t0",
    "self-centered AOE must send the caster as a player target",
)

require(
    skill_card,
    'text = "自身中心"',
    "AOE skill cards must expose the self-center toggle",
)
require(
    skill_card,
    "aoe_center_mode_changed",
    "skill card must emit AOE center preference changes",
)
require(
    skill_window,
    "show_aoe_center_toggle",
    "skill window must hide the toggle for unsupported/single-target skills",
)
require(
    skill_window,
    "set_skill_aoe_self_center",
    "skill window must persist the per-skill center preference through player",
)

require(
    combat,
    "isSelfCenteredAreaAttack",
    "server must explicitly recognize self-centered damaging AOE casts",
)
require(
    combat,
    "isScreenAreaAttack ||\n                isSelfCenteredAreaAttack",
    "self-centered AOE must share the authoritative caster-center branch",
)

require(
    cursor,
    "GameCursorOverlay",
    "dropdown cursor must render inside the popup viewport",
)
require(
    cursor,
    "_popup_cursor_window.get_mouse_position()",
    "popup game cursor must follow popup-local mouse coordinates",
)
require(
    cursor,
    'popup_animation := "normal"',
    "settings dropdown must use the normal in-game cursor appearance",
)
require(
    hud,
    "show_popup_game_cursor(popup)",
    "system option dropdowns must activate the popup game cursor",
)
require(
    hud,
    "hide_popup_game_cursor(popup)",
    "system option dropdowns must restore the main game cursor on close",
)

print("PASS: skill target reuse, per-AOE center mode, and popup game cursor contracts")
