"""Keep real AES/WebSocket 10/50/100 load coverage wired into validation."""
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
PROBE = ROOT / "tests" / "SGO.Server.WireLoadProbe"


class WireLoadProbeStructureTests(unittest.TestCase):
    def test_probe_uses_real_client_protocol_and_multiplayer_conservation(self):
        program = (PROBE / "Program.cs").read_text(encoding="utf-8-sig")
        client = (PROBE / "WireLoadClient.cs").read_text(encoding="utf-8-sig")
        scenario = (PROBE / "WireLoadScenario.cs").read_text(encoding="utf-8-sig")

        for marker in ("10,", "50,", "100"):
            self.assertIn(marker, program)

        self.assertIn("ClientProtocol.Encode", client)
        self.assertIn("ClientProtocol.Decode", client)
        self.assertIn('"SyncPosition"', client)
        self.assertIn('"ChatMessage"', client)
        self.assertIn('"MoveEntity"', client)
        self.assertIn('"ChatBroadcast"', client)

        self.assertIn("clients.Length - 1", scenario)
        self.assertIn("clients.Length *", scenario)
        self.assertIn("movement conservation failed", scenario)
        self.assertIn("chat conservation failed", scenario)
        self.assertIn("ReceivedBytesPerSecond", scenario)
        self.assertIn("P50", scenario)
        self.assertIn("P95", scenario)
        self.assertIn("P99", scenario)

    def test_ci_and_local_runner_execute_wire_load_probe(self):
        workflow = (
            ROOT / ".github" / "workflows" / "server-build.yml"
        ).read_text(encoding="utf-8-sig")
        runner = (
            ROOT / "tests" / "run_protocol_checks.ps1"
        ).read_text(encoding="utf-8-sig")

        for source in (workflow, runner):
            self.assertIn("SGO.Server.WireLoadProbe", source)
            self.assertIn("10,50,100", source)


if __name__ == "__main__":
    unittest.main()
