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

    def test_mall_list_consumes_donation_gifts(self):
        block = packet_block(self.source, "MallListResponse")
        fields = set(FIELD_READ.findall(block))
        self.assertTrue({"Points", "Items", "Gifts"} <= fields)


if __name__ == "__main__":
    unittest.main()
