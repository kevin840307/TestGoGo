"""Static regression tests for Godot network packet ownership.

These tests do not replace a real Godot/WebSocket E2E run. They protect the
recovered client's packet responsibilities so server ACK packets do not slowly
grow duplicate state ownership.
"""
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
NETWORK_MANAGER = ROOT / "client" / "scripts" / "network_manager.gd"
MALL_WINDOW = ROOT / "client" / "scripts" / "mall_window.gd"
CASE = re.compile(r'^\t\t"([A-Za-z][A-Za-z0-9_]*)"\s*:', re.M)
FIELD_READ = re.compile(r'data\.(?:get|has)\(\s*["\']([A-Za-z][A-Za-z0-9_]*)["\']')


def packet_block(source: str, packet_type: str) -> str:
    matches = list(CASE.finditer(source))
    for index, match in enumerate(matches):
        if match.group(1) != packet_type:
            continue
        end = matches[index + 1].start() if index + 1 < len(matches) else len(source)
        return source[match.start():end]
    raise AssertionError(f"Godot handler not found: {packet_type}")


class ClientNetworkContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = NETWORK_MANAGER.read_text(encoding="utf-8-sig")

    def test_drive_mecha_response_is_ack_only(self):
        block = packet_block(self.source, "DriveMechaResponse")
        fields = set(FIELD_READ.findall(block))
        self.assertEqual({"Success", "HorseUID", "Message"}, fields)
        self.assertIn("summoned_horse_uid", block)
        self.assertIn('rider_state["SummonedHorseUID"]', block)

    def test_player_status_sync_owns_effective_combat_stats(self):
        block = packet_block(self.source, "PlayerStatusSync")
        fields = set(FIELD_READ.findall(block))
        for field in (
            "HP", "MP", "SP", "MaxHP", "MaxMP", "MaxSP",
            "Attack", "Defense", "MagicAttack", "MagicDefense",
            "Hit", "Dodge", "AttackRange", "MoveSpeed",
            "CritRate", "CritDamage",
        ):
            self.assertIn(field, fields)

    def test_item_choice_flow_is_server_authoritative(self):
        block = packet_block(self.source, "ItemChoiceRequired")
        fields = set(FIELD_READ.findall(block))
        self.assertTrue({"ItemUID", "ItemIndex", "Title", "Options"} <= fields)
        self.assertIn("item_choice_required_received.emit", block)
        self.assertIn(
            'send_secure_packet({\n\t\t\t"Type": "UseItemChoiceRequest"',
            self.source,
        )
        self.assertIn('"ItemUID": item_uid', self.source)
        self.assertIn('"ItemIndex": item_index', self.source)

        window = (
            ROOT / "client" / "scripts" / "item_choice_window.gd"
        ).read_text(encoding="utf-8-sig")
        self.assertIn(
            "NetworkManager.send_item_choice_request(_item_uid, item_index)",
            window,
        )
        self.assertIn("_selection_sent", window)

    def test_mall_list_consumes_donation_gifts(self):
        block = packet_block(self.source, "MallListResponse")
        fields = set(FIELD_READ.findall(block))
        self.assertTrue({"Points", "Items", "Gifts"} <= fields)

    def test_choice_reward_packet_and_authoritative_reply(self):
        response = packet_block(self.source, "ItemChoiceRequired")
        fields = set(FIELD_READ.findall(response))
        self.assertTrue(
            {"ItemUID", "ItemIndex", "Title", "Options"} <= fields
        )
        self.assertIn("item_choice_required_received.emit(", response)

        self.assertIn(
            'signal item_choice_required_received(',
            self.source,
        )
        self.assertIn(
            'func send_item_choice_request(item_uid: int, item_index: int)',
            self.source,
        )
        self.assertIn('"Type": "UseItemChoiceRequest"', self.source)
        self.assertIn('"ItemUID": item_uid', self.source)
        self.assertIn('"ItemIndex": item_index', self.source)

    def test_choice_reward_ui_attaches_to_hud(self):
        hud = (ROOT / "client" / "game_hud.gd").read_text(
            encoding="utf-8-sig"
        )
        scene = ROOT / "client" / "item_choice_window.tscn"
        script = ROOT / "client" / "scripts" / "item_choice_window.gd"
        headless = ROOT / "client" / "tests" / "item_choice_regression.gd"
        self.assertTrue(scene.is_file())
        self.assertTrue(script.is_file())
        self.assertTrue(headless.is_file())
        self.assertIn(
            'preload("res://item_choice_window.tscn")', hud
        )
        self.assertIn(
            "item_choice_required_received.connect(", hud
        )
        self.assertIn(
            "ITEM_CHOICE_WINDOW_SCENE.instantiate()", hud
        )

        widget = script.read_text(encoding="utf-8-sig")
        self.assertIn("func setup(item_uid: int, title: String, options: Array)", widget)
        self.assertIn("_selection_sent", widget)
        self.assertIn("send_item_choice_request(_item_uid, item_index)", widget)

    def test_router_forwards_external_mall_credentials_and_request_id(self):
        router = (
            ROOT / "server" / "Application" / "PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")
        mall_call = router[
            router.index("return _mall.Handle("):
            router.index("case \"TrainTravelReady\"")
        ]
        self.assertIn('req.Str("LoginName")', mall_call)
        self.assertIn('req.Str("Password")', mall_call)
        self.assertIn('req.Str("RequestID")', mall_call)

    def test_external_mall_mutations_send_idempotency_keys(self):
        source = MALL_WINDOW.read_text(encoding="utf-8-sig")
        self.assertIn(
            '"RequestID": _next_external_request_id("paypal")',
            source,
        )
        self.assertIn(
            '"RequestID": _next_external_request_id("legacy")',
            source,
        )
        self.assertIn(
            "func _next_external_request_id(prefix: String) -> String:",
            source,
        )


if __name__ == "__main__":
    unittest.main()
