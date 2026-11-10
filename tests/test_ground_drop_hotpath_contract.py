"""Guard display-only ground optimizations without changing simulation cadence."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
MANAGER = (ROOT / "client/scripts/network_entity_manager.gd").read_text(encoding="utf-8")
GROUND = (ROOT / "client/scripts/ground_item_entity.gd").read_text(encoding="utf-8")
INDEX = (ROOT / "client/scripts/ground_drop_spatial_index.gd").read_text(encoding="utf-8")


class GroundDropHotPathContract(unittest.TestCase):
    def test_display_uses_compact_index(self):
        self.assertIn("GROUND_DROP_SPATIAL_INDEX.new()", MANAGER)
        self.assertIn("_ground_visibility_index.upsert(entity_id, cached_ground)", MANAGER)
        self.assertIn("_ground_visibility_index.erase(entity_id)", MANAGER)
        self.assertIn("_ground_visibility_index.find_nearest(center, radius, 5)", MANAGER)
        self.assertIn("_ground_visibility_index.clear()", MANAGER)
        self.assertIn("const CELL_SIZE: float = 512.0", INDEX)

    def test_duplicate_burst_defers_visual_spawns(self):
        start = MANAGER.index("elif entity_type == 3:", MANAGER.index("func _on_entity_spawned("))
        end = MANAGER.index("\n\tif get_active_entity(entity_id) or _queued_spawn_ids.has(entity_id):", start)
        body = MANAGER[start:end]
        self.assertIn("_ground_spawn_templates[entity_id] = cached_ground", body)
        self.assertIn("GROUND_DROPS_LIMIT_DUPLICATES", body)
        self.assertIn("_deferred_ground_spawns[entity_id] = cached_ground", body)
        self.assertIn("_remove_queued_spawn(entity_id)", body)
        self.assertLess(body.index("GROUND_DROPS_LIMIT_DUPLICATES"), body.index("_should_display_ground_drop("))

    def test_single_protection_pulse_replaces_timers(self):
        self.assertNotIn("Timer.new()", GROUND)
        self.assertNotIn("_protection_timer", GROUND)
        self.assertIn('add_to_group("sgo_protected_ground_item")', GROUND)
        self.assertIn('get_nodes_in_group("sgo_protected_ground_item")', MANAGER)
        self.assertIn("_ground_protection_tick_elapsed >= 1.0", MANAGER)

    def test_bounce_does_not_scan_all_active_monsters_per_drop(self):
        start = MANAGER.index("if _should_check_ground_drop_bounce(data):")
        end = MANAGER.index("if is_monster_drop:", start)
        body = MANAGER[start:end]
        self.assertIn("_recent_drop_bounce_delay(spawn_pos)", body)
        self.assertNotIn("for active_entity_id in _active_entity_types", body)
        self.assertIn("_remember_fatal_monster(target_id, target_node", MANAGER)

    def test_packet_and_fps_policy_unchanged(self):
        self.assertIn("func _on_entity_spawned(data: Dictionary)", MANAGER)
        self.assertIn("func _on_entity_despawned(data: Dictionary)", MANAGER)
        self.assertIn("func _on_entity_moved(data: Dictionary)", MANAGER)
        self.assertIn("_update_monster_aoi(delta)", MANAGER)
        self.assertIn("_update_ground_aoi(delta)", MANAGER)
        self.assertNotIn("Engine.max_fps =", INDEX)
        self.assertNotIn("Engine.max_fps =", GROUND)


if __name__ == "__main__":
    unittest.main()
