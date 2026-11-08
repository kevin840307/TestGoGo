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
    "World/BlockingMapSmokeTests.cs": "BlockingMapSmokeTests",
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

    def test_current_client_catalog_suite_has_explicit_catalog_context(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "GameData" / "CurrentClientCatalogSmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn(
            "public static void Run(CurrentClientDataCatalog catalog)",
            source,
        )
        self.assertIn(
            "CurrentClientCatalogSmokeTests.Run(realCurrentCatalog);",
            main,
        )
        self.assertNotIn("var festivalRewards =", main)
        self.assertIn("ResolveExperienceCard(2460)", source)
        self.assertIn("ResolveSkillBook(3701)", source)

    def test_prelogin_route_suite_has_explicit_context(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Protocol" / "PreloginPacketSmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn(
            "public static void Run(PacketRouter router, ClientSession session)",
            source,
        )
        self.assertIn("PreloginPacketSmokeTests.Run(router, session);", main)
        self.assertIn('AnnouncementListRequest', source)
        self.assertIn('LoginRequest', source)

    def test_protocol_assertions_not_reintroduced_in_monolith(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        self.assertNotIn("PacketSchemaValidator.TryValidate(", main)
        self.assertNotIn("AnnouncementCatalog.Read(", main)
        self.assertNotIn("var blockingTestRoot =", main)
        self.assertNotIn("MonsterNavigationService(blocking)", main)
        self.assertNotIn("static JsonDocument Json(", main)
        self.assertNotIn("static void Check(", main)

    def test_fast_domain_suites_are_selectable(self):
        source = PROGRAM.read_text(encoding="utf-8-sig")
        for suite in ("protocol", "announcements", "world", "all"):
            with self.subTest(suite=suite):
                self.assertIn(f'case "{suite}":', source)
        self.assertIn('args[0] != "--suite"', source)

    def test_common_assertions_stay_central(self):
        helpers = (SMOKE / "SmokeAssert.cs").read_text(encoding="utf-8-sig")
        main = PROGRAM.read_text(encoding="utf-8-sig")
        self.assertIn("public static void Check(", helpers)
        self.assertIn("public static JsonDocument Json(", helpers)
        self.assertIn("using static SGO.Server.SmokeTests.SmokeAssert;", main)


if __name__ == "__main__":
    unittest.main()
