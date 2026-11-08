"""SGO offline performance report regression tests; Python stdlib only."""
import csv
import importlib.util
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "client" / "tests" / "performance" / "generate_report.py"
SPEC = importlib.util.spec_from_file_location("sgo_client_perf_report", SOURCE)
REPORT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(REPORT)


def write_csv(path, rows):
    if not rows:
        return
    with path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)


class ClientPerfReportTests(unittest.TestCase):
    def make_session(self, parent, run_id="demo", scenario="multi", count=2):
        d = parent / run_id
        d.mkdir()
        (d / "session.json").write_text(json.dumps({
            "run_id": run_id, "scenario": scenario, "target_count": count,
        }), encoding="utf-8")
        return d

    @staticmethod
    def hw(stage, idx, pid, cpu, ram, gpu="", vram=""):
        return {
            "stage": stage, "sample_index": str(idx), "pid": str(pid),
            "cpu_percent": cpu, "ram_working_mb": ram,
            "ram_private_mb": ram, "gpu_3d_percent": gpu,
            "gpu_vram_dedicated_mb": vram,
        }

    @staticmethod
    def event(stage, kind, fps="", calls="", focus="foreground",
              valid="true", mode="balanced", simp="on", monsters="100"):
        return {
            "stage": stage, "kind": kind, "focus": focus,
            "render_stats_valid": valid, "mode": mode,
            "monster_simplify": simp,
            "fps": fps, "draw_calls": calls, "canvas_nodes": "4000",
            "visible_monsters": monsters, "process_ms": "8.0",
        }

    def test_hardware_aggregate_per_tick_then_median(self):
        rows = [
            self.hw("x", 1, 11, "4", "100"),
            self.hw("x", 1, 22, "5", "200"),
            self.hw("x", 2, 11, "8", "120"),
            self.hw("x", 2, 22, "7", "210"),
        ]
        result = REPORT.aggregate_hardware(rows)
        self.assertEqual(result["cpu_percent"], 12.0)
        self.assertEqual(result["ram_working_mb"], 315.0)
        self.assertIsNone(result["gpu_3d_percent"])
        self.assertIsNone(result["gpu_vram_dedicated_mb"])

    def test_missing_gpu_is_not_zero(self):
        data = [self.hw("x", 1, 1, "12", "400", "", "")]
        result = REPORT.aggregate_hardware(data)
        self.assertIsNone(result["gpu_3d_percent"])
        self.assertIsNone(result["gpu_vram_dedicated_mb"])
        self.assertEqual(result["cpu_percent"], 12.0)

    def test_valid_foreground_render_only(self):
        rows = [
            self.event("balanced-on", "render", "59", "400"),
            self.event("balanced-on", "render", "5", "3000", focus="background"),
            self.event("balanced-on", "render", "60", "410"),
            self.event("balanced-on", "render", "7", "2500", valid="false"),
            self.event("balanced-on", "process"),
        ]
        result = REPORT.aggregate_events(rows)
        self.assertEqual(result["fps"], 59.5)
        self.assertEqual(result["draw_calls"], 405.0)
        self.assertEqual(result["render_samples"], 2)
        self.assertEqual(result["process_samples"], 1)

    def test_stage_isolation_and_warning_if_no_godot_log(self):
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            session = self.make_session(root)
            write_csv(session / "hardware.csv", [
                self.hw("balanced-on", 1, 1, "10", "200"),
                self.hw("low-on", 1, 1, "20", "220"),
            ])
            write_csv(session / "godot_events.csv", [
                self.event("balanced-on", "render", "60", "400"),
            ])
            rows = REPORT.summarize(REPORT.load_sessions(root))
            self.assertEqual(len(rows), 2)
            self.assertEqual(rows[0]["draw_calls"], 400)
            self.assertEqual(rows[1]["cpu_percent"], 20)
            self.assertIsNone(rows[1]["draw_calls"])
            self.assertIn("無 Godot", rows[1]["warnings"])

    def test_report_is_standalone_and_summary_has_empty_gpu_cells(self):
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            session = self.make_session(root, run_id="test", scenario="single", count=1)
            write_csv(session / "hardware.csv", [self.hw("balanced-on", 1, 1, "5", "300")])
            write_csv(session / "godot_events.csv", [
                self.event("balanced-on", "render", "60", "400")])
            output, rows = REPORT.generate(root)
            self.assertTrue(output.exists())
            self.assertEqual(len(rows), 1)
            text = output.read_text(encoding="utf-8")
            self.assertIn("<svg", text)
            self.assertIn("CPU", text)
            self.assertIn("Draw Calls", text)
            self.assertNotIn("<script src=", text)
            data = REPORT.read_csv(root / "summary.csv")
            self.assertEqual(data[0]["gpu_3d_percent"], "")
            self.assertEqual(data[0]["gpu_vram_dedicated_mb"], "")

    def test_invalid_or_missing_metadata_skips_session(self):
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            broken = root / "broken"
            broken.mkdir()
            (broken / "session.json").write_text("bad-json", encoding="utf-8")
            self.assertEqual(REPORT.load_sessions(root), [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
