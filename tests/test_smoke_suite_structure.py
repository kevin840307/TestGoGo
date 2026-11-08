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
    "Protocol/DynamicActionSchemaCoverageSmokeTests.cs": "DynamicActionSchemaCoverageSmokeTests",
    "Protocol/IdentitySchemaSmokeTests.cs": "IdentitySchemaSmokeTests",
    "Announcements/AnnouncementSmokeTests.cs": "AnnouncementSmokeTests",
    "World/BlockingMapSmokeTests.cs": "BlockingMapSmokeTests",
    "World/MapAiOwnerSpatialIndexSmokeTests.cs": "MapAiOwnerSpatialIndexSmokeTests",
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

    def test_shared_ai_spatial_index_preserves_exact_owner_semantics(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        ticker = (
            ROOT / "server" / "Application" / "World" /
            "SharedMapAiTicker.cs"
        ).read_text(encoding="utf-8-sig")
        index = (
            ROOT / "server" / "Application" / "World" /
            "MapAiOwnerSpatialIndex.cs"
        ).read_text(encoding="utf-8-sig")
        suite = (
            SMOKE / "World" / "MapAiOwnerSpatialIndexSmokeTests.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertIn("new MapAiOwnerSpatialIndex(", ticker)
        self.assertIn("targetOwners.TryGetValue(", ticker)
        self.assertIn("FindNearestOwnerIndex(", ticker)
        self.assertIn("MapAiOwnerSpatialIndexSmokeTests.Run();", main)
        self.assertIn("ReferenceScan(points, x, y)", suite)
        self.assertIn("Parallel.For(0, 1000", suite)
        self.assertIn("delta * delta <= bestDistance", index)

    def test_money_concurrency_suites_use_exact_balances(self):
        fixture = (
            SMOKE / "Fixtures" / "ExactMoneyFixture.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertIn("target - current", fixture)
        self.assertIn("updated[\"Money\"]", fixture)
        for file_name in (
            "EconomyConcurrencySmokeTests.cs",
            "AssetMutationConcurrencySmokeTests.cs",
            "CrossFeatureConcurrencySmokeTests.cs",
        ):
            with self.subTest(suite=file_name):
                suite = (
                    SMOKE / "Integration" / file_name
                ).read_text(encoding="utf-8-sig")
                self.assertIn("ExactMoneyFixture.Set(", suite)
                # An additive seed atop 10,000 starter money makes the
                # low-balance overspend/repair assertions impossible.
                self.assertNotIn("store.AdjustMoney(", suite)

    def test_economy_request_guards_are_in_every_concurrency_run(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        guards = (
            SMOKE / "Integration" / "EconomyRequestGuardSmokeTests.cs"
        ).read_text(encoding="utf-8-sig")
        store = (
            ROOT / "server" / "Infrastructure" / "Persistence" /
            "Sqlite" / "SqliteGameStore.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertEqual(
            main.count("EconomyRequestGuardSmokeTests.Run();"), 2
        )
        self.assertIn("9999, int.MaxValue", guards)
        self.assertIn("itemIndex, int.MaxValue, int.MaxValue", guards)
        self.assertIn("repeated sale of consumed UID", guards)
        self.assertIn("count > maximumCount || unitPrice < 0", store)
        self.assertIn("var total = (long)unitPrice * count", store)

    def test_world_aoi_snapshot_generation_prevents_ghost_ground(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        aoi = (
            ROOT / "server" / "Application" / "Messaging" /
            "WorldEntitySpatialAoi.cs"
        ).read_text(encoding="utf-8-sig")
        hub = (
            ROOT / "server" / "Application" / "Messaging" /
            "ServerBroadcastHub.cs"
        ).read_text(encoding="utf-8-sig")
        router = (
            ROOT / "server" / "Application" / "PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")
        race = (
            SMOKE / "World" / "WorldEntityAoiSnapshotRaceSmokeTests.cs"
        ).read_text(encoding="utf-8-sig")
        stationary = (
            SMOKE / "World" /
            "WorldEntityAoiStationaryRefreshSmokeTests.cs"
        ).read_text(encoding="utf-8-sig")

        self.assertIn("entity.LastMutationGeneration <= snapshotGeneration", aoi)
        self.assertIn("CaptureEntityGeneration()", aoi)
        self.assertIn("entry.Entity.X == entity.X", aoi)
        self.assertIn("CaptureWorldEntityGeneration()", hub)
        self.assertIn("_broadcasts.BeginWorldEntitySnapshot();", router)
        self.assertIn("authoritativeIds,\n                snapshotGeneration)", router)
        self.assertEqual(
            main.count("WorldEntityAoiSnapshotRaceSmokeTests.Run();"), 2
        )
        self.assertEqual(
            main.count("WorldEntityAoiStationaryRefreshSmokeTests.Run();"), 2
        )
        self.assertIn("StaleSnapshotCannotPurgeLaterGroundDrop", race)
        self.assertIn("BroadcastHubPreservesLateDropDuringWorldReadyPrune", race)
        self.assertIn("late observer receives the newest", stationary)
        self.assertIn("same-cell coordinate movement", stationary)

    def test_ground_drop_guard_covers_safe_input_and_pickup(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        store = (
            ROOT / "server" / "Infrastructure" / "Persistence" /
            "Sqlite" / "SqliteGameStore.cs"
        ).read_text(encoding="utf-8-sig")
        router = (
            ROOT / "server" / "Application" / "PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")
        suite = (
            SMOKE / "Integration" / "GroundDropRequestGuardSmokeTests.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertEqual(main.count("GroundDropRequestGuardSmokeTests.Run();"), 2)
        self.assertIn("((tabType is 1 or 3) && count != 1)", store)
        self.assertIn("double.IsFinite(x)", store)
        self.assertIn("var requestedCount = req.Int(\"Count\", 1)", router)
        self.assertIn("requestedCount <= 0", router)
        self.assertIn("duplicate pickup cannot create", suite)
        self.assertIn("equipment pickup restores", suite)

    def test_client_aoi_spawn_replay_uses_latest_movement(self):
        client = (
            ROOT / "client" / "scripts" / "network_entity_manager.gd"
        ).read_text(encoding="utf-8-sig")
        regression = (
            ROOT / "client" / "tests" /
            "aoi_monster_snapshot_order_regression.gd"
        ).read_text(encoding="utf-8-sig")
        runner = (
            ROOT / "tests" / "run_protocol_checks.ps1"
        ).read_text(encoding="utf-8-sig")
        self.assertIn('cached["_aoi_has_move"] = true', client)
        self.assertIn('previous.get("_aoi_has_move", false)', client)
        self.assertIn("repeated SpawnEntity cannot undo newer movement", regression)
        self.assertIn("AOI exit removes both cached and queued", regression)
        self.assertIn("aoi_monster_snapshot_order_regression.gd", runner)

    def test_ground_snapshot_lease_blocks_stale_reappearance(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        aoi = (
            ROOT / "server" / "Application" / "Messaging" /
            "WorldEntitySpatialAoi.cs"
        ).read_text(encoding="utf-8-sig")
        router = (
            ROOT / "server" / "Application" / "PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")
        hub = (
            ROOT / "server" / "Application" / "Messaging" /
            "ServerBroadcastHub.cs"
        ).read_text(encoding="utf-8-sig")
        race = (
            SMOKE / "World" /
            "WorldEntityAoiSnapshotLeaseSmokeTests.cs"
        ).read_text(encoding="utf-8-sig")
        persisted_race = (
            SMOKE / "Integration" /
            "GroundSnapshotPersistenceRaceSmokeTests.cs"
        ).read_text(encoding="utf-8-sig")

        self.assertIn("_removedDuringSnapshots[entityId]", aoi)
        self.assertIn("LastMutationGeneration > snapshot.Generation", aoi)
        self.assertIn("RemoveEntityLocked(", aoi)
        self.assertIn("TryUpsertEntityFromSnapshot(", aoi)
        self.assertIn("BeginWorldEntitySnapshot()", hub)
        self.assertIn("TryUpsertWorldEntityFromSnapshot(", hub)
        self.assertIn("using var worldSnapshot =", router)
        self.assertIn("worldSnapshot,\n                    out var changes", router)
        self.assertIn("PickupBeforeFirstAoiInsertStillBlocksOldSnapshot", race)
        self.assertIn("OverlappingSnapshotsKeepTombstonesUntilLastReader", race)
        self.assertIn("store.PickUpGroundItem(", persisted_race)
        self.assertIn("inventoryCount == 2", persisted_race)
        self.assertEqual(
            main.count("WorldEntityAoiSnapshotLeaseSmokeTests.Run();"), 2
        )
        self.assertEqual(
            main.count("GroundSnapshotPersistenceRaceSmokeTests.Run();"), 2
        )

    def test_windows_local_validation_runs_real_suites_and_load(self):
        batch = (
            ROOT / "tests" / "run_local_verification.bat"
        ).read_text(encoding="utf-8-sig")
        self.assertIn("git pull --ff-only origin feature/local-server-net8", batch)
        self.assertIn("run_protocol_checks.ps1", batch)
        self.assertIn("--suite world", batch)
        self.assertIn("--suite concurrency", batch)
        self.assertIn("--sizes \"%SIZES%\"", batch)
        self.assertIn("--move-pixels 4", batch)
        self.assertIn("sgo-wire-load-", batch)
        self.assertNotIn("git reset --hard", batch)

    def test_dynamic_action_schema_runtime_covers_every_action_rule(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = (
            SMOKE / "Protocol" /
            "DynamicActionSchemaCoverageSmokeTests.cs"
        )
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn('GetField(', source)
        self.assertIn('"ActionRules"', source)
        self.assertIn("ValidValue", source)
        self.assertIn("InvalidValue", source)
        self.assertIn("required-field rejection", source)
        self.assertIn(
            "DynamicActionSchemaCoverageSmokeTests.Run();",
            main,
        )

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

    def test_marketplace_response_contracts_are_modular_and_concurrent(self):
        concurrency = (
            SMOKE / "Integration" / "ConcurrencySmokeTests.cs"
        ).read_text(encoding="utf-8-sig")
        module = (
            SMOKE / "Integration" /
            "MarketplaceResponseContractSmokeTests.cs"
        )
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn(
            "MarketplaceResponseContractSmokeTests.Run(",
            concurrency,
        )
        self.assertIn("TradeSyncUsesSymmetricRuntimeViews", source)
        self.assertIn("PlayerShopViewsPreserveDynamicListings", source)
        self.assertIn("TenBuyerPlayerShopRaceConservesAssetsAndMoney", source)
        self.assertIn("TradeSync exposes the same two offers", source)
        self.assertIn("nested authoritative ExtraData", source)
        self.assertIn("ten buyers racing five player-shop items", source)
        self.assertIn("sold-out player shop closes atomically", source)

    def test_player_shop_lifecycle_race_is_isolated_and_conservative(self):
        concurrency = (
            SMOKE / "Integration" / "ConcurrencySmokeTests.cs"
        ).read_text(encoding="utf-8-sig")
        module = (
            SMOKE / "Integration" /
            "PlayerShopLifecycleConcurrencySmokeTests.cs"
        )
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("RaceBuyAgainstClose", source)
        self.assertIn("buy/close race conserves total item count", source)
        self.assertIn("buy/close race conserves total money", source)
        self.assertIn("closing shop leaves no reserved inventory rows", source)
        self.assertIn(
            "PlayerShopLifecycleConcurrencySmokeTests.Run(",
            concurrency,
        )

    def test_session_lifecycle_suite_covers_position_and_presence_races(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = SMOKE / "Integration" / "SessionLifecycleSmokeTests.cs"
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("disconnect persists the final dirty in-memory position", source)
        self.assertIn(
            "server map reconcile clears old AOI immediately while destination waits for WorldReady",
            source,
        )
        self.assertIn(
            "map transition does not send despawn to an observer outside AOI/not WorldReady",
            source,
        )
        self.assertIn(
            "map transition does not expose player before nearby observer WorldReady",
            source,
        )
        self.assertIn("stale dirty position never overwrites", source)
        self.assertIn("SessionLifecycleSmokeTests.Run(", main)

    def test_session_lifecycle_closes_player_shop_on_transport_disconnect(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        lifecycle = (
            SMOKE / "Integration" / "SessionLifecycleSmokeTests.cs"
        ).read_text(encoding="utf-8-sig")
        router = (
            ROOT / "server" / "Application" / "PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")

        start = router.index("public void DisconnectPresence")
        end = router.index("public IReadOnlyList<object> Handle", start)
        disconnect = router[start:end]

        self.assertIn("_playerShop.Disconnect(", disconnect)
        self.assertIn(
            "transport disconnect closes player shop and releases all reserved rows",
            lifecycle,
        )
        self.assertIn("PlayerShopService playerShop", lifecycle)
        self.assertIn("SessionLifecycleSmokeTests.Run(", main)

    def test_session_identity_cleanup_suite_covers_relogin_and_logout(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = (
            SMOKE / "Integration" /
            "SessionIdentityCleanupSmokeTests.cs"
        )
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        router = (
            ROOT / "server" / "Application" / "PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")

        self.assertIn(
            "same socket can authenticate second account after cleanup",
            source,
        )
        self.assertIn(
            "relogin releases old account player-shop reservations",
            source,
        )
        self.assertIn(
            "LogoutRequest releases player-shop reservations before identity reset",
            source,
        )
        self.assertIn(
            "character bootstrap recovers orphaned player-shop reservations after restart",
            source,
        )
        self.assertIn("RecoverStaleReservations(", router)
        self.assertIn("DisconnectPresence(state);", router)
        self.assertIn("SessionIdentityCleanupSmokeTests.Run(", main)

    def test_player_shop_lifecycle_suite_covers_disconnect_vs_buy(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = (
            SMOKE / "Integration" /
            "PlayerShopLifecycleConcurrencySmokeTests.cs"
        )
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("RaceBuyAgainstDisconnect(", source)
        self.assertIn("buy versus {terminalName} terminates", source)
        self.assertIn("{terminalName} leaves no reserved inventory rows", source)
        self.assertIn("PlayerShopLifecycleConcurrencySmokeTests.Run(", main)

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
        self.assertIn(
            "return-map WorldReady rebuilds persisted ground item snapshot",
            source,
        )
        self.assertIn(
            "owner can pick up lifecycle ground item after returning to map",
            source,
        )
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
        self.assertIn('"RepairItemRequest"', source)
        self.assertIn("repair persists max durability", source)
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

    def test_runtime_dynamic_response_contracts_are_modular(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = (
            SMOKE / "Protocol" /
            "RuntimeResponseContractSmokeTests.cs"
        )
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("AssertLoginResponse(", source)
        self.assertIn("AssertBootstrap(", source)
        self.assertIn("InventorySync.Items", source)
        self.assertIn("HorseSync.HorseEquips", source)
        self.assertIn("WORLD_REQUIRED", source)
        self.assertIn("INVALID_PACKET", source)
        self.assertIn("AUTH_REQUIRED", source)
        self.assertIn("UNHANDLED_PACKET", source)
        self.assertIn(
            "RuntimeResponseContractSmokeTests.AssertLoginResponse(",
            main,
        )
        self.assertIn(
            "RuntimeResponseContractSmokeTests.AssertBootstrap(",
            main,
        )
        self.assertNotIn(
            "var unsupportedPacket = Json(",
            main,
        )

    def test_real_godot_server_network_e2e_is_isolated_and_wired(self):
        script = (
            ROOT / "client" / "tests" /
            "real_server_network_e2e.gd"
        )
        self.assertTrue(script.is_file())
        source = script.read_text(encoding="utf-8-sig")
        for marker in (
            "send_register_account_request(",
            "send_login_request(",
            "send_create_character_request(",
            "send_select_character_request(",
            "send_world_ready(",
            "send_secure_packet({",
            "world_ready_response_received.connect(",
            "_bootstrap_complete()",
            "return-map WorldReady rebuilds ground SpawnEntity in Godot",
            "Godot can pick persisted ground item after leaving and returning",
            "send_pickup_item_request(",
        ):
            self.assertIn(marker, source)

        runner = (
            ROOT / "tests" / "run_protocol_checks.ps1"
        ).read_text(encoding="utf-8-sig")
        workflow = (
            ROOT / ".github" / "workflows" /
            "server-build.yml"
        ).read_text(encoding="utf-8-sig")
        for wiring in (runner, workflow):
            self.assertIn(
                "real_server_network_e2e.gd",
                wiring,
            )
            self.assertIn(
                "SGO_TEST_SERVER_URL",
                wiring,
            )

    def test_dynamic_monster_ids_are_map_unique_for_world_aoi(self):
        combat = (
            ROOT / "server" / "Application" / "Combat" /
            "CombatService.cs"
        ).read_text(encoding="utf-8-sig")

        start = combat.index("public IReadOnlyList<object> SpawnLegacyEnemy")
        end = combat.index("public IReadOnlyList<object> PetAttack", start)
        spawn = combat[start:end]

        self.assertIn("9_000_000 +", spawn)
        self.assertIn("session.CurrentMapId * 10_000", spawn)
        self.assertIn("WorldEntitySpatialAoi uses EntityID", spawn)

    def test_player_companion_aoi_edges_carry_spawn_and_despawn_snapshots(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = (
            SMOKE / "World" /
            "PlayerCompanionAoiSmokeTests.cs"
        )
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        router = (
            ROOT / "server" / "Application" /
            "PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")
        hub = (
            ROOT / "server" / "Application" / "Messaging" /
            "ServerBroadcastHub.cs"
        ).read_text(encoding="utf-8-sig")

        self.assertIn(
            "PlayerCompanionAoiSmokeTests.Run();",
            main,
        )
        self.assertIn(
            "companion snapshot follows latest authoritative movement",
            source,
        )
        self.assertIn(
            "companion despawn removes snapshot",
            source,
        )
        self.assertIn(
            "PlayerCompanionSnapshots(",
            router,
        )
        self.assertIn(
            "PlayerCompanionEntityIds(",
            router,
        )
        self.assertIn(
            "TrackPlayerCompanionPacket(",
            router,
        )
        self.assertIn(
            "TrackPlayerCompanionPacket(",
            hub,
        )
        self.assertIn(
            "ClearPlayerCompanions(",
            router,
        )

    def test_world_entity_aoi_suite_is_modular_and_wired(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = (
            SMOKE / "World" /
            "WorldEntitySpatialAoiSmokeTests.cs"
        )
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        hub = (
            ROOT / "server" / "Application" / "Messaging" /
            "ServerBroadcastHub.cs"
        ).read_text(encoding="utf-8-sig")
        router = (
            ROOT / "server" / "Application" /
            "PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")
        combat = (
            ROOT / "server" / "Application" / "Combat" /
            "CombatService.cs"
        ).read_text(encoding="utf-8-sig")

        self.assertIn(
            "WorldEntitySpatialAoiSmokeTests.Run();",
            main,
        )
        self.assertIn(
            "indexes 1000 distant dynamic entities",
            source,
        )
        self.assertIn(
            "1000-observer grid discovers only nearby viewer",
            source,
        )
        self.assertIn(
            "concurrent monster movement settles all nearby known entities",
            source,
        )
        self.assertIn("_worldAoi", hub)
        self.assertIn("PublishToWorldEntityObservers(", hub)
        self.assertIn("ReconcileWorldAoi(", router)
        self.assertIn("UpsertWorldEntity(", router)
        self.assertIn("RegisterMonsterAoi(", combat)
        self.assertIn("PublishToWorldEntityObservers(", combat)

    def test_high_frequency_public_events_do_not_regress_to_full_map_fanout(self):
        router = (
            ROOT / "server" / "Application" /
            "PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")
        shop = (
            ROOT / "server" / "Application" / "Shops" /
            "PlayerShopService.cs"
        ).read_text(encoding="utf-8-sig")
        party = (
            ROOT / "server" / "Application" / "Party" /
            "PartyService.cs"
        ).read_text(encoding="utf-8-sig")
        combat = (
            ROOT / "server" / "Application" / "Combat" /
            "CombatService.cs"
        ).read_text(encoding="utf-8-sig")

        self.assertIn(
            "_broadcasts.PublishToPlayerObservers(",
            router,
        )
        self.assertIn(
            "_broadcasts.IsPlayerAoiVisible(",
            shop,
        )
        self.assertNotIn(
            "_broadcasts.PublishToMap(",
            shop,
        )
        self.assertIn(
            "_broadcasts.PublishToPlayerObservers(",
            party,
        )

        status_start = combat.index("private void BroadcastPublicStatus")
        status_end = combat.index("private static object Flash", status_start)
        status = combat[status_start:status_end]
        self.assertIn("_broadcasts.FindCharacter(", status)
        self.assertIn("presence.ConnectionId", status)

        world_start = combat.index("private void BroadcastWorldPackets")
        world_end = combat.index("private void RegisterMonsterAoi", world_start)
        world = combat[world_start:world_end]
        self.assertIn('type == "EntityHeal"', world)
        self.assertIn("targetPresence.ConnectionId", world)
        self.assertIn("session.ConnectionId", world)

        origin_start = combat.index(
            "internal static bool IsPlayerOriginAoiPacket"
        )
        origin_end = combat.index("private static void SetAiState", origin_start)
        origin = combat[origin_start:origin_end]
        for packet_type in (
            '"SpawnEntity"',
            '"DespawnEntity"',
            '"SyncState"',
            '"EntityDeath"',
        ):
            self.assertIn(packet_type, origin)
        self.assertIn("entityId >= 4_000_000", origin)
        self.assertIn("entityId < 6_000_000", origin)

    def test_player_shop_aoi_suite_guards_far_packet_mutation(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = (
            SMOKE / "Integration" /
            "PlayerShopAoiSmokeTests.cs"
        )
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")

        self.assertIn(
            "far player cannot view or buy invisible player shop",
            source,
        )
        self.assertIn(
            "far rejected shop packets cannot mutate money or assets",
            source,
        )
        self.assertIn(
            "approaching shop owner creates player AOI spawn edge",
            source,
        )
        self.assertIn(
            "AOI-authorized purchase transfers money and item exactly once",
            source,
        )
        self.assertIn(
            "PlayerShopAoiSmokeTests.Run(repoRoot);",
            main,
        )

    def test_wire_load_probe_has_real_world_aoi_near_far_phase(self):
        scenario = (
            ROOT / "tests" / "SGO.Server.WireLoadProbe" /
            "WireLoadScenario.cs"
        ).read_text(encoding="utf-8-sig")
        client = (
            ROOT / "tests" / "SGO.Server.WireLoadProbe" /
            "WireLoadClient.cs"
        ).read_text(encoding="utf-8-sig")

        self.assertIn("MeasureWorldAoiAsync(", scenario)
        self.assertIn("world AOI leaked target spawn", scenario)
        self.assertIn(
            "distant client did not receive monster spawn after entering range",
            scenario,
        )
        self.assertIn("TeleportAsync(", client)
        self.assertIn("TryWaitForEntityPacketAsync(", client)

    def test_client_aoi_regressions_are_in_normal_runner(self):
        runner = (
            ROOT / "tests" / "run_protocol_checks.ps1"
        ).read_text(encoding="utf-8-sig")

        self.assertIn(
            "ground_item_aoi_regression.gd",
            runner,
        )
        self.assertIn(
            "multiclient_render_dedup_regression.gd",
            runner,
        )

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


    def test_asset_mutation_concurrency_suite_is_modular(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = (
            SMOKE / "Integration" /
            "AssetMutationConcurrencySmokeTests.cs"
        )
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("ConcurrentRepairsCannotUndercharge", source)
        self.assertIn("only one of two 60-cost repairs succeeds", source)
        self.assertIn("failed concurrent repair rolls back", source)
        self.assertIn("ConcurrentDuplicateRepairChargesOnce", source)
        self.assertIn("duplicate same-UID repair charges exactly once", source)
        self.assertIn("AssetMutationConcurrencySmokeTests.Run();", main)

    def test_cross_feature_concurrency_suite_guards_mixed_money_writes(self):
        main = PROGRAM.read_text(encoding="utf-8-sig")
        module = (
            SMOKE / "Integration" /
            "CrossFeatureConcurrencySmokeTests.cs"
        )
        self.assertTrue(module.is_file())
        source = module.read_text(encoding="utf-8-sig")
        self.assertIn("TradeAndDirectTransferConserveMoney", source)
        self.assertIn("trade and direct transfer both commit exactly once", source)
        self.assertIn("TradeAndPlayerShopPurchaseConserveMoney", source)
        self.assertIn("trade and player-shop purchase both commit exactly once", source)
        self.assertIn("CraftAndDirectTransferComposeAtomically", source)
        self.assertIn("craft and direct transfer both commit exactly once", source)
        self.assertIn("mixed multiplayer money operations conserve", source)
        self.assertIn("CrossFeatureConcurrencySmokeTests.Run();", main)


if __name__ == "__main__":
    unittest.main()
