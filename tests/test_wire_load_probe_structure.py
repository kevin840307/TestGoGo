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
        health = (PROBE / "ServerHealthSampler.cs").read_text(encoding="utf-8-sig")

        for marker in ("10,", "50,", "100"):
            self.assertIn(marker, program)

        self.assertIn("ClientProtocol.Encode", client)
        self.assertIn("ClientProtocol.Decode", client)
        self.assertIn('"SyncPosition"', client)
        self.assertIn('"ChatMessage"', client)
        self.assertIn('"MoveEntity"', client)
        self.assertIn('"ChatBroadcast"', client)
        self.assertIn('"AttackEntity"', client)
        self.assertIn('"EntityDeath"', client)
        self.assertIn("CombatDeaths", client)
        self.assertIn("CombatEvents", client)
        self.assertIn("dropCombatNoise", client)
        self.assertIn("DirectionForDelta", client)
        self.assertIn("Dir = direction", client)
        self.assertIn("Dir = Direction", client)
        self.assertNotIn(
            'new { Type = "SyncPosition", X = x, Y = y, Dir = "1" }',
            client,
        )
        self.assertNotIn(
            'new { Type = "SyncState", State = state, Dir = "1" }',
            client,
        )

        self.assertIn("CountDirectedAoiPeers(clients)", scenario)
        self.assertIn("PlayerSpatialAoi.EnterRadius", scenario)
        self.assertNotIn("clients.Length - 1", scenario)
        self.assertIn(
            "clients.Length + CountDirectedAoiPeers(clients)",
            scenario,
        )
        self.assertIn("expectedPerRound *", scenario)
        self.assertIn("movement conservation failed", scenario)
        self.assertIn("chat conservation failed", scenario)
        self.assertIn("combat death conservation failed", scenario)
        self.assertIn("MeasureCombatRaceAsync", scenario)
        self.assertIn("ExpectedDeathDeliveries", scenario)
        self.assertIn("MeasureMixedSoakAsync", scenario)
        self.assertIn("MixedSoak", scenario)
        self.assertIn("ReceivedBytesPerSecond", scenario)
        self.assertIn("P50", scenario)
        self.assertIn("P95", scenario)
        self.assertIn("P99", scenario)

        self.assertIn('Path = "/health"', health)
        self.assertIn("workingSetBytes", health)
        self.assertIn("gcHeapBytes", health)
        self.assertIn("cpuTotalMilliseconds", health)
        self.assertIn("AiTickElapsedMs", health)
        self.assertIn("CpuPercentOfMachine", health)
        self.assertIn("MaxMonsterCount", health)

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
            self.assertIn("--mixed-seconds", source)


if __name__ == "__main__":
    unittest.main()
