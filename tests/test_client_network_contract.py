"""Static regression tests for Godot network packet ownership.

These tests do not replace a real Godot/WebSocket E2E run. They protect the
recovered client's packet responsibilities so server ACK packets do not slowly
grow duplicate state ownership.
"""
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
NETWORK_MANAGER = ROOT / "client" / "scripts" / "network_manager.gd"
PLAYER = ROOT / "client" / "player.gd"
NETWORK_PLAYER = ROOT / "client" / "network_player.gd"
PACKET_ROUTER = ROOT / "server" / "Application" / "PacketRouter.cs"
MALL_WINDOW = ROOT / "client" / "scripts" / "mall_window.gd"
BANK_WINDOW = ROOT / "client" / "scripts" / "bank_window.gd"
PLAYER_SHOP_WINDOW = ROOT / "client" / "scripts" / "shop_window.gd"
CASE = re.compile(r'^\t\t"([A-Za-z][A-Za-z0-9_]*)"\s*:', re.M)
FIELD_READ = re.compile(r'data\.(?:get|has)\(\s*["\']([A-Za-z][A-Za-z0-9_]*)["\']')


def packet_block(source: str, packet_type: str) -> str:
    matches = list(CASE.finditer(source))
    for index, match in enumerate(matches):
        if match.group(1) != packet_type:
            continue
        end = matches[index + 1].start() if index + 1 < len(matches) else len(source)
        return source[match.start():end]
    raise AssertionError(f"Godot handler not found: {packet_type}")


class ClientNetworkContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = NETWORK_MANAGER.read_text(encoding="utf-8-sig")

    def test_login_reconstructs_authoritative_pvp_name_color(self):
        player = PLAYER.read_text(encoding="utf-8-sig")
        start = player.index("func parse_login_server_data(json_packet: Dictionary) -> void :")
        end = player.find("\nfunc ", start + 5)
        block = player[start:end if end >= 0 else None]

        # Reconnect must honor persisted RedMan / PKPinkUntilUtcMs instead of
        # collapsing every non-red player to the base white/blue state.
        self.assertIn("_apply_initial_name_color_state()", block)
        self.assertNotIn(
            "_apply_name_color(1 if data_model.red_man > 0 else 0)",
            block,
        )

        base_entity = (
            ROOT / "client" / "scripts" / "base_entity.gd"
        ).read_text(encoding="utf-8-sig")
        self.assertIn("data_model.pk_pink_until_utc_ms", base_entity)
        self.assertIn(
            "_apply_server_name_color_state(color_state, remaining)",
            base_entity,
        )

    def test_sync_position_preserves_real_player_direction(self):
        manager = self.source
        player = PLAYER.read_text(encoding="utf-8-sig")
        router = PACKET_ROUTER.read_text(encoding="utf-8-sig")

        start = manager.index("func send_sync_position(")
        end = manager.find("\nfunc ", start + 5)
        sync_helper = manager[start:end if end >= 0 else None]
        self.assertIn('packet["Dir"] = dir', sync_helper)
        self.assertIn("dir in GameConstants.DIR_STRINGS", sync_helper)

        # Both periodic walking snapshots and the final stop endpoint carry
        # the player's current 8-way facing.
        self.assertGreaterEqual(
            player.count(
                "NetworkManager.send_sync_position("
            ),
            2,
        )
        self.assertGreaterEqual(
            player.count("current_dir\n\t\t)"),
            1,
        )
        self.assertIn(
            "current_dir\n\t\t\t)",
            player,
        )

        sync_start = router.index('case "SyncPosition":')
        sync_end = router.index('case "UpdateShortcut":', sync_start)
        sync_case = router[sync_start:sync_end]
        self.assertIn('var moveDir = req.Str("Dir", "")', sync_case)
        self.assertIn("Dir = moveDir", sync_case)
        self.assertNotIn('req.Str("Dir", "1")', sync_case)

    def test_sync_state_never_invents_missing_direction(self):
        manager = self.source
        router = PACKET_ROUTER.read_text(encoding="utf-8-sig")
        entity_manager = (
            ROOT / "client" / "scripts" / "network_entity_manager.gd"
        ).read_text(encoding="utf-8-sig")

        self.assertIn(
            'data.get("Dir", ""))',
            manager,
        )
        self.assertNotIn(
            '"SyncState": entity_state_changed.emit(int(data.get("EntityID", 0)), data.get("State", "idle"), data.get("Dir", "1"))',
            manager,
        )

        sync_start = router.index('case "SyncState":')
        sync_end = router.index('case "InteractRequest":', sync_start)
        sync_case = router[sync_start:sync_end]
        self.assertIn('var stateDir = req.Str("Dir", "")', sync_case)
        self.assertIn("Dir = stateDir", sync_case)
        self.assertNotIn('req.Str("Dir", "1")', sync_case)

        self.assertIn(
            'if "current_dir" in target_node and not new_dir.is_empty():',
            entity_manager,
        )

    def test_entity_damage_does_not_replay_owned_remote_attack(self):
        player = NETWORK_PLAYER.read_text(encoding="utf-8-sig")
        entity_manager = (
            ROOT / "client" / "scripts" / "network_entity_manager.gd"
        ).read_text(encoding="utf-8-sig")
        policy = (
            ROOT / "client" / "scripts" / "remote_player_state_policy.gd"
        ).read_text(encoding="utf-8-sig")

        self.assertIn(
            "func should_trigger_attack_visual_from_damage(skill_id: int)",
            player,
        )
        self.assertIn(
            "damage_should_start_attack_visual",
            policy,
        )
        self.assertIn(
            'attacker_node.has_method("should_trigger_attack_visual_from_damage")',
            entity_manager,
        )
        self.assertIn(
            "should_start_damage_attack_visual",
            entity_manager,
        )
        self.assertIn(
            "if should_start_damage_attack_visual and is_instance_valid(target_node):",
            entity_manager,
        )
        direction_gate = entity_manager.index(
            "if should_start_damage_attack_visual and is_instance_valid(target_node):"
        )
        direction_update = entity_manager.index(
            "attacker_node.update_direction_8way(",
            direction_gate,
        )
        attack_trigger = entity_manager.index(
            "attacker_node.trigger_combat_action(",
            direction_gate,
        )
        self.assertLess(direction_gate, direction_update)
        self.assertLess(direction_update, attack_trigger)
        self.assertIn(
            "NETWORK_ATTACK_DAMAGE_OWNER_WINDOW_MSEC",
            player,
        )
        self.assertIn(
            "skill_id == 0\n\t\tand _network_attack_damage_owner_until_msec >= now_msec",
            player,
        )
        self.assertIn(
            "skill_id == 0 and explicit_network_attack_pending",
            player,
        )
        self.assertIn(
            "is_action_locked = true",
            player,
        )
        self.assertIn(
            "_sync_physics_animation_if_changed()",
            player,
        )
        self.assertIn(
            "network_state_is_one_shot",
            policy,
        )

    def test_stationary_remote_network_state_renders_immediately(self):
        player = NETWORK_PLAYER.read_text(encoding="utf-8-sig")
        start = player.index(
            "func apply_network_state(new_state: String, new_dir: String)"
        )
        end = player.index("\nfunc _physics_process", start)
        block = player[start:end]

        self.assertIn(
            "REMOTE_STATE_POLICY.network_state_is_one_shot",
            block,
        )
        self.assertIn("is_action_locked = true", block)
        self.assertIn("_sync_physics_animation_if_changed()", block)
        self.assertIn(
            "_network_attack_damage_owner_until_msec",
            block,
        )

    def test_pending_idle_settle_cannot_cut_locked_remote_action(self):
        player = NETWORK_PLAYER.read_text(encoding="utf-8-sig")
        start = player.index("func _handle_movement(delta: float)")
        end = player.index("\nfunc update_direction_8way", start)
        block = player[start:end]

        settle = block.index(
            "if global_position.distance_squared_to(target_position) <= 4.0:"
        )
        guard = block.index(
            "REMOTE_STATE_POLICY.movement_may_replace_animation(",
            settle,
        )
        idle_write = block.index('current_state = "idle"', guard)
        self.assertLess(settle, guard)
        self.assertLess(guard, idle_write)

    def test_world_ready_ack_is_observable(self):
        block = packet_block(self.source, "WorldReadyResponse")
        fields = set(FIELD_READ.findall(block))
        self.assertTrue(
            {"Success", "MapID", "Message"} <= fields
        )
        self.assertIn(
            "signal world_ready_response_received(",
            self.source,
        )
        self.assertIn(
            "world_ready_response_received.emit(",
            block,
        )

    def test_drive_mecha_response_is_ack_plus_authoritative_rebase_only(self):
        block = packet_block(self.source, "DriveMechaResponse")
        fields = set(FIELD_READ.findall(block))
        self.assertEqual(
            {"Success", "HorseUID", "MapID", "X", "Y", "Message"},
            fields,
        )
        self.assertIn("summoned_horse_uid", block)
        self.assertIn('rider_state["SummonedHorseUID"]', block)
        self.assertIn("apply_position_correction", block)

    def test_player_status_sync_owns_effective_combat_stats(self):
        block = packet_block(self.source, "PlayerStatusSync")
        fields = set(FIELD_READ.findall(block))
        for field in (
            "HP", "MP", "SP", "MaxHP", "MaxMP", "MaxSP",
            "Attack", "Defense", "MagicAttack", "MagicDefense",
            "Hit", "Dodge", "AttackRange", "MoveSpeed",
            "CritRate", "CritDamage",
        ):
            self.assertIn(field, fields)

    def test_item_choice_flow_is_server_authoritative(self):
        block = packet_block(self.source, "ItemChoiceRequired")
        fields = set(FIELD_READ.findall(block))
        self.assertTrue({"ItemUID", "ItemIndex", "Title", "Options"} <= fields)
        self.assertIn("item_choice_required_received.emit", block)
        self.assertIn(
            'send_secure_packet({\n\t\t\t"Type": "UseItemChoiceRequest"',
            self.source,
        )
        self.assertIn('"ItemUID": item_uid', self.source)
        self.assertIn('"ItemIndex": item_index', self.source)

        window = (
            ROOT / "client" / "scripts" / "item_choice_window.gd"
        ).read_text(encoding="utf-8-sig")
        self.assertIn(
            "NetworkManager.send_item_choice_request(_item_uid, item_index)",
            window,
        )
        self.assertIn("_selection_sent", window)

    def test_mall_list_consumes_donation_gifts(self):
        block = packet_block(self.source, "MallListResponse")
        fields = set(FIELD_READ.findall(block))
        self.assertTrue({"Points", "Items", "Gifts"} <= fields)

    def test_dynamic_packet_builders_preserve_type_and_action(self):
        # Literal Type scanners alone cannot prove computed Trade and
        # Action helper packets still retain the expected wire envelope.
        def method(name):
            start = self.source.index("func " + name + "(")
            end = self.source.find("\nfunc ", start + 5)
            return self.source[start:end if end >= 0 else None]

        for helper, expected in {
            "send_trade_action": (
                '"Type": action_type',
                "packet.merge(extra_data)",
                "send_secure_packet(packet)",
            ),
            "send_shop_action": (
                '"Type": "ShopAction"',
                '"Action": action_type',
                "packet.merge(extra_data)",
            ),
            "send_pet_action": (
                '"Type": "PetAction"',
                '"Action": action',
                '"PetUID": pet_uid',
                "packet.merge(extra_data)",
            ),
            "send_mall_action": (
                '"Type": "MallAction"',
                '"Action": action',
                "packet.merge(args)",
            ),
            "send_party_action": (
                '"Type": "PartyAction"',
                '"Action": action',
                '"TargetDBID": target_dbid',
            ),
        }.items():
            with self.subTest(helper=helper):
                body = method(helper)
                for fragment in expected:
                    self.assertIn(fragment, body)
                self.assertIn("send_secure_packet(", body)

    def test_economy_request_builders_keep_server_schema(self):
        bank = BANK_WINDOW.read_text(encoding="utf-8-sig")
        for fragment in (
            '"Type": "BankTransfer"',
            '"UID": item_uid',
            '"ToBank": to_bank',
            '"Amount": amount',
            '"TabType": SharedInventoryPanel.TabType.GENERAL',
        ):
            self.assertIn(fragment, bank)

        player_shop = PLAYER_SHOP_WINDOW.read_text(encoding="utf-8-sig")
        for fragment in (
            'send_shop_action("OpenShop", {"ShopName": s_name})',
            'send_shop_action("AddItem", {"UID": uid, "Count": count, "Price": price, "TabType": tab})',
            'send_shop_action("RemoveItem", {"UID": uid, "TabType": tab})',
            'send_shop_action("BuyItem", {"TargetDBID": shop_owner_dbid, "UID": uid, "Count": buy_count})',
        ):
            self.assertIn(fragment, player_shop)

        for fragment in (
            '"Type": "CraftItemRequest"',
            '"RecipeID": recipe_id',
            '"BatchCount": amount',
            '"Type": "DismantleItemRequest"',
            '"ItemUIDs": target_uids',
            '"ItemType": item_type',
            '"Type": "RepairItemRequest"',
            '"ItemType": item_type',
            '"UID": uid',
        ):
            self.assertIn(fragment, self.source)

    def test_choice_reward_packet_and_authoritative_reply(self):
        response = packet_block(self.source, "ItemChoiceRequired")
        fields = set(FIELD_READ.findall(response))
        self.assertTrue(
            {"ItemUID", "ItemIndex", "Title", "Options"} <= fields
        )
        self.assertIn("item_choice_required_received.emit(", response)

        self.assertIn(
            'signal item_choice_required_received(',
            self.source,
        )
        self.assertIn(
            'func send_item_choice_request(item_uid: int, item_index: int)',
            self.source,
        )
        self.assertIn('"Type": "UseItemChoiceRequest"', self.source)
        self.assertIn('"ItemUID": item_uid', self.source)
        self.assertIn('"ItemIndex": item_index', self.source)

    def test_choice_reward_ui_attaches_to_hud(self):
        hud = (ROOT / "client" / "game_hud.gd").read_text(
            encoding="utf-8-sig"
        )
        scene = ROOT / "client" / "item_choice_window.tscn"
        script = ROOT / "client" / "scripts" / "item_choice_window.gd"
        headless = ROOT / "client" / "tests" / "item_choice_regression.gd"
        self.assertTrue(scene.is_file())
        self.assertTrue(script.is_file())
        self.assertTrue(headless.is_file())
        self.assertIn(
            'preload("res://item_choice_window.tscn")', hud
        )
        self.assertIn(
            "item_choice_required_received.connect(", hud
        )
        self.assertIn(
            "ITEM_CHOICE_WINDOW_SCENE.instantiate()", hud
        )

        widget = script.read_text(encoding="utf-8-sig")
        self.assertIn("func setup(item_uid: int, title: String, options: Array)", widget)
        self.assertIn("_selection_sent", widget)
        self.assertIn("send_item_choice_request(_item_uid, item_index)", widget)

    def test_router_forwards_external_mall_credentials_and_request_id(self):
        router = (
            ROOT / "server" / "Application" / "PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")
        mall_call = router[
            router.index("return _mall.Handle("):
            router.index("case \"TrainTravelReady\"")
        ]
        self.assertIn('req.Str("LoginName")', mall_call)
        self.assertIn('req.Str("Password")', mall_call)
        self.assertIn('req.Str("RequestID")', mall_call)

    def test_external_mall_mutations_send_idempotency_keys(self):
        source = MALL_WINDOW.read_text(encoding="utf-8-sig")
        self.assertIn(
            '"RequestID": _next_external_request_id("paypal")',
            source,
        )
        self.assertIn(
            '"RequestID": _next_external_request_id("legacy")',
            source,
        )
        self.assertIn(
            "func _next_external_request_id(prefix: String) -> String:",
            source,
        )


if __name__ == "__main__":
    unittest.main()
