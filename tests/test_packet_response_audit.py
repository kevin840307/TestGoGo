"""Unit tests for Server -> Client response contract source audit."""
import tempfile
import unittest
from pathlib import Path

from packet_response_audit import scan, REQUIRED


class PacketResponseAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="sgo-response-contract-")
        self.root = Path(self.temp.name)
        self.write("client/scripts/network_manager.gd", "")
        self.write("server/Application/PacketRouter.cs", "")

    def tearDown(self):
        self.temp.cleanup()

    def write(self, path, body):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(body, encoding="utf-8")

    def fixture(self):
        cases = []
        responses = []
        for name, fields in REQUIRED.items():
            cases.append('        "{}":\n            pass'.format(name))
            members = "\n".join(
                "                {} = 1,".format(field) for field in fields)
            responses.append(
                'new\n{{\n    Type = "{}",\n{}\n}}'.format(
                    name, members
                )
            )
        self.write(
            "client/scripts/network_manager.gd",
            "match packet_type:\n" + "\n".join(cases) + "\n"
        )
        self.write(
            "server/Application/PacketRouter.cs",
            "\n".join(responses) + "\n"
        )

    def test_complete_response_contracts(self):
        self.fixture()
        results = scan(self.root)
        self.assertEqual(len(REQUIRED), results["matched_count"])
        self.assertEqual(len(REQUIRED), results["response_declarations_checked"])
        self.assertEqual([], results["issues"])

    def test_detects_missing_server_member_in_one_variant(self):
        self.fixture()
        packet = self.root / "server/Application/PacketRouter.cs"
        with packet.open("a", encoding="utf-8") as stream:
            stream.write(
                '\nnew { Type = "WorldReadyResponse", Success = true };\n'
            )
        results = scan(self.root)
        error = next(x for x in results["issues"]
                     if x["type"] == "WorldReadyResponse")
        self.assertEqual("INCOMPLETE_SERVER_FIELDS", error["status"])
        self.assertEqual(["MapID"], error["violations"][0]["missing"])

    def test_rejects_any_incomplete_world_packet_variant(self):
        self.fixture()
        packet = self.root / "server/Application/PacketRouter.cs"
        with packet.open("a", encoding="utf-8") as stream:
            stream.write(
                '\nnew { Type = "MoveEntity", EntityID = 1, X = 2, '
                'Y = 3, Dir = 4 };\n'
            )
        result = scan(self.root)
        issue = next(x for x in result["issues"]
                     if x["type"] == "MoveEntity")
        self.assertEqual("INCOMPLETE_SERVER_FIELDS", issue["status"])
        self.assertEqual(2, issue["declarations_checked"])
        self.assertEqual(["SpeedTime"], issue["violations"][0]["missing"])

    def test_detects_missing_client_handler(self):
        self.fixture()
        client = self.root / "client/scripts/network_manager.gd"
        client.write_text(
            client.read_text(encoding="utf-8").replace(
                '        "EntityDamage":\n            pass\n', ""
            ),
            encoding="utf-8"
        )
        results = scan(self.root)
        issue = next(x for x in results["issues"]
                     if x["type"] == "EntityDamage")
        self.assertEqual("MISSING_CLIENT_HANDLER", issue["status"])

    def test_detects_missing_server_type(self):
        self.fixture()
        server = self.root / "server/Application/PacketRouter.cs"
        src = server.read_text(encoding="utf-8")
        from_index = src.index('new\n{\n    Type = "PacketError"')
        to_index = src.index("}", from_index) + 1
        server.write_text(
            src[:from_index] + src[to_index:], encoding="utf-8"
        )
        results = scan(self.root)
        issue = next(x for x in results["issues"]
                     if x["type"] == "PacketError")
        self.assertEqual("MISSING_SERVER_PACKET", issue["status"])


if __name__ == "__main__":
    unittest.main()
