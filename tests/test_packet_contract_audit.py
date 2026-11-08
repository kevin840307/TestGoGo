"""Regression tests for packet_contract_audit.py (stdlib-only)."""
import tempfile
import unittest
from pathlib import Path

from packet_contract_audit import scan


class PacketContractAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="sgo-contract-")
        self.root = Path(self.temp.name)
        self.write(
            "client/scripts/network_manager.gd",
            '\tmatch packet_type:\n'
            '\t\t"SkillCast":\n\t\t\tpass\n'
            '\t\t"PacketError":\n\t\t\tpass\n'
            'func send_skill():\n'
            '\tsend_secure_packet({"Type": "UseSkill", "SkillID": 1})\n'
            'func send_trade_action(action_type):\n'
            '\tsend_secure_packet({"Type": action_type})\n',
        )
        self.write(
            "client/scripts/shop_window.gd",
            'func open_shop():\n'
            '\tNetworkManager.send_shop_action("OpenShop")\n'
            '\tNetworkManager.send_trade_action("TradeLock")\n',
        )
        self.write(
            "server/Application/PacketRouter.cs",
            'if (type == "LoginRequest") {}\n'
            'switch (type) {\n'
            '            case "UseSkill":\n'
            '            case "ShopAction":\n'
            '            case "TradeLock":\n'
            '}\n'
            'var result = new { Type = "SkillCast" };\n'
            'var error = new { Type = "PacketError" };\n',
        )
        self.write(
            "server/Application/Shops/PlayerShopService.cs",
            'return action switch {\n  "OpenShop" => Open(),\n  _ => [] };\n',
        )
        self.write(
            "server/Application/Mall/MallService.cs",
            'switch (action) { case "OpenMall": return []; }\n',
        )
        self.write(
            "server/Application/Party/PartyService.cs",
            'return action switch { "Create" => Create(), _ => [] };\n',
        )
        self.write(
            "server/Application/Bank/BankPacketHandler.cs",
            'public string PacketType => "BankTransfer";\n',
        )

    def tearDown(self):
        self.temp.cleanup()

    def write(self, name, contents):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(contents, encoding="utf-8")

    def test_matching_types_and_dynamic_actions(self):
        report = scan(self.root)
        self.assertEqual([], report["unhandled_client_requests"])
        self.assertEqual([], report["unhandled_server_events"])
        self.assertEqual({}, report["unhandled_dynamic_actions"])
        self.assertIn("TradeLock", report["client_requests"])
        self.assertIn("UseSkill", report["client_requests"])
        self.assertIn("SkillCast", report["server_events"])

    def test_flags_unhandled_both_directions_and_actions(self):
        with (self.root / "client/scripts/network_manager.gd").open(
            "a", encoding="utf-8"
        ) as file:
            file.write('send_secure_packet({"Type": "NotOnServer"})\n')
        with (self.root / "server/Application/PacketRouter.cs").open(
            "a", encoding="utf-8"
        ) as file:
            file.write('new { Type = "NotOnClient" };\n')
        with (self.root / "client/scripts/shop_window.gd").open(
            "a", encoding="utf-8"
        ) as file:
            file.write('NetworkManager.send_shop_action("MissingShopAction")\n')

        report = scan(self.root)
        self.assertIn("NotOnServer", report["unhandled_client_requests"])
        self.assertIn("NotOnClient", report["unhandled_server_events"])
        self.assertEqual(
            ["MissingShopAction"],
            report["unhandled_dynamic_actions"]["shop"],
        )

    def test_module_handler_contributes_to_dispatch(self):
        self.write(
            "client/scripts/bank_window.gd",
            'NetworkManager.send_secure_packet({"Type": "BankTransfer"})\n',
        )
        report = scan(self.root)
        self.assertNotIn("BankTransfer", report["unhandled_client_requests"])


if __name__ == "__main__":
    unittest.main()
