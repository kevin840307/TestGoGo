"""Ensure documented incomplete gameplay follows protocol audit PARTIAL routes."""
from pathlib import Path
import unittest

from packet_contract_audit import scan

ROOT = Path(__file__).resolve().parent.parent
TRACKER = ROOT / "server" / "FEATURE_GAPS.md"


class FeatureGapTrackerTests(unittest.TestCase):
    def test_partial_wire_routes_are_documented(self):
        report = scan(ROOT)
        text = TRACKER.read_text(encoding="utf-8-sig")
        partial = {
            route["type"] for route in report["routes"]
            if route["status"] == "PARTIAL"
        }
        self.assertTrue(partial, "Expected at least one known partial feature")
        for packet_type in sorted(partial):
            with self.subTest(packet_type=packet_type):
                self.assertIn(
                    "`" + packet_type + "`", text,
                    "Undocumented PARTIAL wire route: " + packet_type
                )

    def test_backlog_explicitly_distinguishes_unverified_from_complete(self):
        text = TRACKER.read_text(encoding="utf-8-sig")
        for marker in (
            "PARTIAL", "BLOCKED_EXTERNAL", "IMPLEMENTED_UNVERIFIED",
            "TEST_GAP", "P0-01", "P0-02", "P0-03"
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, text)


if __name__ == "__main__":
    unittest.main()
