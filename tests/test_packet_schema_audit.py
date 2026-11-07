"""Contract schema audit fixtures (Python stdlib only)."""
import tempfile
import unittest
from pathlib import Path

from packet_schema_audit import dictionary_literals, scan


class PacketSchemaAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="sgo-schema-")
        self.root = Path(self.temp.name)
        self.write(
            "server/Protocol/PacketSchemaValidator.cs",
            """
            static readonly object Rules = new Dictionary<string, object> {
                ["SyncPosition"] = [
                    new("X", FieldType.Integer),
                    new("Y", FieldType.Integer)
                ],
                ["TradeReply"] = [
                    new("RequesterDBID", FieldType.Integer),
                    new("Accept", FieldType.Boolean)
                ]
            };
            """,
        )
        self.write(
            "client/scripts/network_manager.gd",
            """
            func send_sync_position(x: int, y: int) -> void:
                send_secure_packet({"Type": "SyncPosition", "X": x, "Y": y})
            func send_trade_reply(dbid: int, accept: bool) -> void:
                send_secure_packet({
                    "Type": "TradeReply",
                    "RequesterDBID": dbid,
                    "Accept": accept
                })
            """,
        )

    def tearDown(self):
        self.temp.cleanup()

    def write(self, name, value):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value, encoding="utf-8")

    def test_schema_matches_client_payload_fields(self):
        result = scan(self.root)
        self.assertEqual(2, result["schema_type_count"])
        self.assertEqual(2, result["matched_count"])
        self.assertEqual([], result["issues"])

    def test_missing_field_is_reported(self):
        self.write(
            "client/scripts/network_manager.gd",
            'send_secure_packet({"Type":"SyncPosition","X":1})\n'
            'send_secure_packet({"Type":"TradeReply","RequesterDBID":7,"Accept":false})\n',
        )
        result = scan(self.root)
        self.assertEqual(1, result["matched_count"])
        self.assertEqual("FIELD_MISMATCH", result["issues"][0]["status"])
        self.assertEqual(["Y"], result["issues"][0]["missing_fields"])

    def test_dynamic_trade_and_shop_fields(self):
        with (self.root / "server/Protocol/PacketSchemaValidator.cs").open(
            "a", encoding="utf-8"
        ) as file:
            file.write(
                '\\n["TradeSetMoney"] = [new("Money", FieldType.LongInteger)],\\n'
                '["ShopAction:AddItem"] = ['
                'new("UID", FieldType.LongInteger),'
                'new("Price", FieldType.LongInteger),'
                'new("Count", FieldType.Integer),'
                'new("TabType", FieldType.Integer)],\\n'
            )
        self.write(
            "client/scripts/trade_window.gd",
            'NetworkManager.send_trade_action("TradeSetMoney", {"Money": amount})\\n',
        )
        self.write(
            "client/scripts/shop_window.gd",
            'NetworkManager.send_shop_action("AddItem", '
            '{"UID": uid, "Price": price, "Count": count, "TabType": tab})\\n',
        )
        result = scan(self.root)
        self.assertEqual([], result["issues"])
        self.assertEqual(3, result["matched_count"])
        self.assertEqual(1, result["action_matched_count"])

        self.write(
            "client/scripts/shop_window.gd",
            'NetworkManager.send_shop_action("AddItem", '
            '{"UID": uid, "Count": count, "TabType": tab})\\n',
        )
        result = scan(self.root)
        self.assertEqual(
            ["Price"], result["issues"][0]["missing_fields"]
        )
        self.assertEqual(
            "ShopAction:AddItem", result["issues"][0]["type"]
        )

    def test_literal_parser_skips_quoted_braces_and_comments(self):
        source = (
            'var note = "{not a dictionary}"\n'
            '# {"Type": "Ignored"}\n'
            'send_secure_packet({"Type":"SyncPosition",'
            '"X": 10, "Y": 20, "Note": "{ok}"})\n'
        )
        literals = list(dictionary_literals(source))
        self.assertEqual(1, len(literals))
        self.assertIn('"Y": 20', literals[0])


if __name__ == "__main__":
    unittest.main()
