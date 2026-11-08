"""Regression guard for CS8801 after splitting top-level C# smoke tests.

Executable .NET compilation still must run separately. This protects the
specific source refactor that caused the seven local Release build errors.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parent.parent
SMOKE = ROOT / "tests" / "SGO.Server.SmokeTests"


def read(relative_path: str) -> str:
    return (SMOKE / relative_path).read_text(encoding="utf-8-sig")


class SmokeHelperScopeContractTests(unittest.TestCase):
    def test_packet_tree_helper_lives_only_in_shared_assertions(self):
        shared = read("SmokeAssert.cs")
        program = read("Program.cs")
        self.assertRegex(
            shared,
            r"public\s+static\s+bool\s+PacketTreeContainsString\s*\(",
        )
        self.assertNotRegex(
            program,
            r"\bstatic\s+bool\s+PacketTreeContainsString\s*\(",
            "Top-level Program.cs local functions shadow SmokeAssert in "
            "extracted suite classes and cause CS8801.",
        )
        self.assertIn(
            "using static SGO.Server.SmokeTests.SmokeAssert;",
            program,
        )

    def test_extracted_suites_use_shared_helper_not_top_level_local(self):
        for suite in (
            "Integration/ChoiceRewardSmokeTests.cs",
            "Integration/ItemUseClassificationSmokeTests.cs",
        ):
            with self.subTest(suite=suite):
                source = read(suite)
                self.assertIn(
                    "using static SGO.Server.SmokeTests.SmokeAssert;",
                    source,
                )
                self.assertIn("PacketTreeContainsString(", source)
                self.assertNotRegex(
                    source,
                    r"\bstatic\s+bool\s+PacketTreeContainsString\s*\(",
                )

    def test_ground_snapshot_drop_has_nonnullable_guard(self):
        source = read(
            "Integration/GroundSnapshotPersistenceRaceSmokeTests.cs"
        )
        self.assertIn("?? throw new InvalidOperationException(", source)
        self.assertNotIn("dropped!.EntityId", source)
        self.assertIn("dropped.GroundItemId", source)


if __name__ == "__main__":
    unittest.main()
