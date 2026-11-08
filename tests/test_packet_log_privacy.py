"""Guard against accidentally logging decrypted account or gameplay packets."""
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent


class PacketLogPrivacyTests(unittest.TestCase):
    def test_transport_logs_never_include_decrypted_json(self):
        code = (
            ROOT / "server/Transport/WebSockets/WebSocketEndpoint.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertNotIn("Payload={Payload}", code)
        self.assertNotIn("rawPayload", code)
        self.assertIn("EncodedLength={EncodedLength}", code)

    def test_unknown_packet_diagnostics_do_not_echo_credentials(self):
        code = (
            ROOT / "server/Application/PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertNotIn("[UNHANDLED_PACKET] Type={Type} Payload=", code)
        self.assertNotIn("TruncatePayload(req.GetRawText())", code)
        self.assertIn("[UNHANDLED_PACKET] Type={Type}", code)


if __name__ == "__main__":
    unittest.main()
