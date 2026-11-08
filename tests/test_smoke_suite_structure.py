"""Keep the large SGO smoke runner from regaining independent inline suites.

Tests in this file are intentionally source-level; .NET build and real
gameplay smoke still run separately.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent.parent
SMOKE = ROOT / "tests" / "SGO.Server.SmokeTests"
PROGRAM = SMOKE / "Program.cs"
WIRE_SMOKE = ROOT / "tests" / "SGO.Server.WireSmokeTests" / "Program.cs"

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

    def test_trade_terminal_races_are_separate_and_deterministic(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        concurrency = (
            SMOKE / "Integration" / "ConcurrencySmokeTests.cs"
        ).read_text(encoding="utf-8-sig")
        terminal = (
            SMOKE / "Integration" / "TradeTerminalRaceSmokeTests.cs"
        ).read_text(encoding="utf-8-sig")
        trade = (
            ROOT / "server" / "Application" / "Trade" / "TradeService.cs"
        ).read_text(encoding="utf-8-sig")

        self.assertIn("ConcurrencySmokeTests.Run(", main)
        self.assertIn("TradeTerminalRaceSmokeTests.Run(", concurrency)
        self.assertIn("BlockingTradeCommitProxy", terminal)
        self.assertIn("CommitEntered.Wait(", terminal)
        self.assertIn("cancel and disconnect during commit", terminal)
        self.assertIn("trade.CommitStarted && !force", trade)
        self.assertIn("trade.CommitStarted || self.Locked", trade)

    def test_concurrency_suite_has_explicit_isolated_context(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "ConcurrencySmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("public static void Run(string repoRoot)", source)
        self.assertIn("ConcurrentGroundPickup(", source)
        self.assertIn("ConcurrentTradeFinalConfirm(", source)
        self.assertIn("ConcurrencySmokeTests.Run(", main)
        self.assertIn('case "concurrency":', main)

    def test_booster_suite_uses_explicit_router_context(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "BoosterSmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("class BoosterSmokeTests", source)
        self.assertIn("PacketRouter router", source)
        self.assertIn("CurrentClientDataCatalog current", source)
        self.assertIn("BoosterSmokeTests.Run(", main)
        self.assertIn("ItemEffect_14", source)
        self.assertIn("expired booster rows", source)

    def test_choice_reward_suite_is_modular_and_replay_safe(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "ChoiceRewardSmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("forged choice reward IDs are rejected", source)
        self.assertIn("replaying a consumed choice UID cannot duplicate", source)
        self.assertIn("ten concurrent choice requests consume one box", source)
        self.assertIn("ChoiceRewardSmokeTests.Run(", main)

    def test_recovered_box_suite_guards_safe_multi_reward_behavior(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "RecoveredBoxSmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("RecoveredBoxCatalog.Resolve(", source)
        self.assertIn("2256", source)
        self.assertIn("15230", source)
        self.assertIn("15243", source)
        self.assertIn("2209", source)
        self.assertIn("2250", source)
        self.assertIn("RecoveredBoxSmokeTests.Run(", main)

    def test_shared_map_ai_suite_covers_ten_player_scheduler_arbitration(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "SharedMapAiConcurrencySmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("ten players on one map still produce exactly one shared map tick", source)
        self.assertIn("nearest-player arbitration assigns synthetic monster", source)
        self.assertIn("second shared pass advances each monster once", source)
        self.assertIn("SharedMapAiConcurrencySmokeTests.Run(", main)

    def test_monster_concurrency_suite_covers_shared_kill_race(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "MonsterConcurrencySmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("ten sessions reference one shared monster runtime", source)
        self.assertIn("EntityDeath", source)
        self.assertIn("目標已死亡", source)
        self.assertIn("MonsterConcurrencySmokeTests.Run(", main)

    def test_item_integration_modules_are_invoked_and_domain_focused(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        modules = {
            "Integration/ItemUseClassificationSmokeTests.cs":
                ("ItemUseClassificationSmokeTests", "製作材料／藍圖"),
            "Integration/WaspPackageSmokeTests.cs":
                ("WaspPackageSmokeTests", "50340"),
            "Integration/CosmeticTransformationSmokeTests.cs":
                ("CosmeticTransformationSmokeTests", "effective combat stats"),
        }

        for relative, (name, evidence) in modules.items():
            with self.subTest(module=name):
                module = SMOKE / relative
                self.assertTrue(module.is_file(), str(module))
                source = module.read_text(encoding="utf-8-sig")
                self.assertIn(f"class {name}", source)
                self.assertIn(f"{name}.Run(", main)
                self.assertIn(evidence, source)

    def test_session_lifecycle_suite_covers_position_and_presence_races(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "SessionLifecycleSmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("disconnect persists the final dirty in-memory position", source)
        self.assertIn("map transition despawns player from the old map", source)
        self.assertIn("stale dirty position never overwrites", source)
        self.assertIn("SessionLifecycleSmokeTests.Run(", main)

    def test_wire_smoke_keeps_real_two_client_party_and_trade_flow(self):
        source = WIRE_SMOKE.read_text(encoding="utf-8-sig")
        self.assertIn("using var peerSocket = new ClientWebSocket();", source)
        self.assertIn('"PartyInvite"', source)
        self.assertIn('"PartySync"', source)
        self.assertIn('"TradeInvite"', source)
        self.assertIn('"TradeStart"', source)
        self.assertIn('"TradeFinalConfirmReq"', source)
        self.assertIn('"TradeSuccess"', source)
        self.assertIn('"ItemChoiceRequired"', source)
        self.assertIn('"UseItemChoiceRequest"', source)
        self.assertIn("forged choice is rejected without reward", source)
        self.assertIn("replayed choice UID cannot grant a second reward", source)
        self.assertIn('"MoveEntity"', source)
        self.assertIn('"ChatBroadcast"', source)
        self.assertIn('"DropItemRequest"', source)
        self.assertIn('"PickUpItemRequest"', source)
        self.assertIn('"PetAction"', source)
        self.assertIn('"CommandAttack"', source)
        self.assertIn('"DriveMechaRequest"', source)
        self.assertIn('"AttackEntity"', source)
        self.assertIn('"EntityDeath"', source)
        self.assertIn('"UseSkill"', source)
        self.assertIn('"SkillCast"', source)
        self.assertIn('"SyncBuffs"', source)
        self.assertIn('"ChangeMap"', source)
        self.assertIn('"BankTransfer"', source)
        self.assertIn('"DirectTransfer"', source)
        self.assertIn('"CraftItemRequest"', source)
        self.assertIn('"DismantleItemRequest"', source)
        self.assertIn('Action = "BuyNpcItem"', source)
        self.assertIn("CraftItemRequest consumes deterministic Recipe 1291", source)
        self.assertIn("DismantleItemRequest executes deterministic Recipe 1292", source)
        self.assertIn('Action = "AddItem"', source)
        self.assertIn('Action = "OpenShop"', source)
        self.assertIn('Action = "ViewShop"', source)
        self.assertIn('Action = "BuyItem"', source)
        self.assertIn("player-shop purchase credits seller exactly once", source)
        self.assertIn("destination map WorldReady completes", source)
        self.assertIn("peer despawns player immediately", source)
        self.assertIn("peer respawns returning player", source)
        self.assertIn("authoritative same-map movement", source)
        self.assertIn("nearby chat", source)
        self.assertIn("production defaults GM commands off", source)
        self.assertIn("allowInterleaved: true", source)
        self.assertIn("AwaitType(", source)

    def test_mall_external_suite_guards_provider_and_idempotent_settlement(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "MallExternalIntegrationSmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("PayPal order creation forwards request id", source)
        self.assertIn("PayPal settlement is idempotent", source)
        self.assertIn("legacy query forwards exact credentials", source)
        self.assertIn("replayed legacy withdrawal", source)
        self.assertIn("null external providers fail closed", source)
        self.assertIn("MallExternalIntegrationSmokeTests.Run();", main)

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
        for suite in ("protocol", "announcements", "world", "concurrency", "all"):
            with self.subTest(suite=suite):
                self.assertIn(f'case "{suite}":', source)
        self.assertIn('args[0] != "--suite"', source)

    def test_common_assertions_stay_central(self):
        helpers = (SMOKE / "SmokeAssert.cs").read_text(encoding="utf-8-sig")
        main = PROGRAM.read_text(encoding="utf-8-sig")
        self.assertIn("public static void Check(", helpers)
        self.assertIn("public static JsonDocument Json(", helpers)
        self.assertIn("using static SGO.Server.SmokeTests.SmokeAssert;", main)


    def test_lantern_transform_suite_is_isolated_and_replay_safe(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "LanternTransformationSmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("lantern transform consumes source while preserving", source)
        self.assertIn("replaying consumed lantern source cannot duplicate", source)
        self.assertIn("LanternTransformationSmokeTests.Run(", main)

    def test_doll_reward_suite_guards_full_recovered_pools(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "DollRewardSmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("all nine recovered doll source IDs 2126..2134", source)
        self.assertIn("zodiac doll atomically consumes one source", source)
        self.assertIn("replaying a consumed doll UID cannot grant", source)
        self.assertIn("DollRewardSmokeTests.Run(", main)

    def test_economy_concurrency_suite_covers_shared_money_and_asset_races(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "EconomyConcurrencySmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("ten concurrent NPC purchases cannot overspend", source)
        self.assertIn("ten concurrent sells of one UID pay exactly once", source)
        self.assertIn("ten concurrent bank moves conserve", source)
        self.assertIn("ten buyers racing one five-count player-shop listing", source)
        self.assertIn("ten concurrent direct transfers never overspend", source)
        self.assertIn("EconomyConcurrencySmokeTests.Run();", main)


if __name__ == "__main__":
    unittest.main()
