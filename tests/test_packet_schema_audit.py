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
