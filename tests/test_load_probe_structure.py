"""Keep capacity measurement and runtime AI diagnostics wired into validation."""
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent


class LoadProbeStructureTests(unittest.TestCase):
    def test_probe_keeps_10_50_100_and_conservation_checks(self):
        source = (
            ROOT / "tests" / "SGO.Server.LoadProbe" / "Program.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertIn("new[] { 10, 50, 100 }", source)
        self.assertIn("fanout conservation failed", source)
        self.assertIn("SQLite contention probe failed", source)
        self.assertIn("P50", source)
        self.assertIn("P95", source)
        self.assertIn("P99", source)
        self.assertIn("SharedAiElapsedMs", source)
        self.assertIn("CpuUtilizationPercent", source)

    def test_server_health_exposes_runtime_ai_metrics(self):
        ticker = (
            ROOT / "server" / "Application" / "World" /
            "SharedMapAiTicker.cs"
        ).read_text(encoding="utf-8-sig")
        program = (
            ROOT / "server" / "Program.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertIn("SharedMapAiTickDiagnostics", ticker)
        self.assertIn("AssignedMonsterCount", ticker)
        self.assertIn("Stopwatch.GetElapsedTime(started)", ticker)
        self.assertIn("sharedMapAi = sharedMapAiTicker.LastDiagnostics", program)
        self.assertIn("workingSetBytes", program)
        self.assertIn("cpuTotalMilliseconds", program)

    def test_ci_and_local_runner_execute_capacity_probe(self):
        workflow = (
            ROOT / ".github" / "workflows" / "server-build.yml"
        ).read_text(encoding="utf-8-sig")
        runner = (
            ROOT / "tests" / "run_protocol_checks.ps1"
        ).read_text(encoding="utf-8-sig")
        for source in (workflow, runner):
            self.assertIn("SGO.Server.LoadProbe", source)
            self.assertIn("10,50,100", source)


if __name__ == "__main__":
    unittest.main()
