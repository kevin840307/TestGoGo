"""Keep the large SGO smoke runner from regaining independent inline suites.

Tests in this file are intentionally source-level; .NET build and real
gameplay smoke still run separately.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent.parent
SMOKE = ROOT / "tests" / "SGO.Server.SmokeTests"
PROGRAM = SMOKE / "Program.cs"

MODULES = {
    "Protocol/RequestSchemaSmokeTests.cs": "RequestSchemaSmokeTests",
    "Protocol/ActionSchemaSmokeTests.cs": "ActionSchemaSmokeTests",
    "Protocol/IdentitySchemaSmokeTests.cs": "IdentitySchemaSmokeTests",
    "Announcements/AnnouncementSmokeTests.cs": "AnnouncementSmokeTests",
}


class SmokeSuiteStructureTests(unittest.TestCase):
    def test_each_domain_suite_is_independent_and_invoked(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        for relative, name in MODULES.items():
            with self.subTest(module=name):
                module = SMOKE / relative
                self.assertTrue(module.is_file(), str(module))
                contents = module.read_text(encoding="utf-8-sig")
                self.assertIn(f"class {name}", contents)
                self.assertIn("public static void Run()", contents)
                self.assertIn(f"{name}.Run();", main)

    def test_protocol_assertions_not_reintroduced_in_monolith(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        self.assertNotIn("PacketSchemaValidator.TryValidate(", main)
        self.assertNotIn("AnnouncementCatalog.Read(", main)
        self.assertNotIn("static JsonDocument Json(", main)
        self.assertNotIn("static void Check(", main)

    def test_common_assertions_stay_central(self):
        helpers = (SMOKE / "SmokeAssert.cs").read_text(encoding="utf-8-sig")
        main = PROGRAM.read_text(encoding="utf-8-sig")
        self.assertIn("public static void Check(", helpers)
        self.assertIn("public static JsonDocument Json(", helpers)
        self.assertIn("using static SGO.Server.SmokeTests.SmokeAssert;", main)


if __name__ == "__main__":
    unittest.main()
