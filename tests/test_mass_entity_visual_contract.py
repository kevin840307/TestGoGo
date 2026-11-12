"""Static invariants complement the Godot runtime regressions.

They check that visual hot-path optimizations never delay authoritative
combat, disable gameplay ticks or scan unrelated drop queues per packet.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def script(relative):
    return (ROOT / relative).read_text(encoding="utf-8-sig")


def gd_function(body, signature):
    start = body.index(signature)
    end = body.find("\nfunc ", start + len(signature))
    if end == -1:
        end = len(body)
    return body[start:end]


class MassEntityVisualContracts(unittest.TestCase):
    def test_monster_hp_precedes_visual_hit_delay(self):
        body = gd_function(
            script("client/scripts/monster_entity.gd"),
            "func update_hp(new_hp: int,"
        )
        self.assertLess(body.index("current_hp = new_hp"),
                        body.index("await tree.create_timer(hit_delay).timeout"))
        self.assertLess(body.index("hp_bar.value = current_hp"),
                        body.index("await tree.create_timer(hit_delay).timeout"))
        self.assertIn('if current_state in ["attack", "hit", "death", "die"]:', body)
        self.assertIn("_hit_visual_pending", body)
        self.assertNotIn("current_hp = new_hp\n\tcurrent_state", body)

    def test_idle_monsters_leave_fixed_tick_set(self):
        body = gd_function(
            script("client/scripts/monster_entity.gd"),
            "func advance_network_runtime(delta: float)"
        )
        self.assertIn('elif current_state not in ["attack", "hit", "death", "die"]:', body)
        self.assertIn("return false", body)
        self.assertIn("network_move_active", body)

    def test_despawns_without_queued_visuals_do_not_scan_spawn_array(self):
        body = gd_function(
            script("client/scripts/network_entity_manager.gd"),
            "func _remove_queued_spawn(entity_id: int)"
        )
        self.assertLess(body.index("if not _queued_spawn_ids.has(entity_id):"),
                        body.index("for index: int in range("))
        self.assertIn("_queued_spawn_ids.erase(entity_id)", body)

    def test_many_equal_priority_drops_append_in_constant_time(self):
        body = gd_function(
            script("client/scripts/network_entity_manager.gd"),
            "func _enqueue_spawn_by_priority(data: Dictionary)"
        )
        self.assertIn("_spawn_queue.back()", body)
        self.assertLess(body.index("_spawn_queue.append(data)"),
                        body.index("var low: int = 0"))
        self.assertIn("_spawn_queue.insert(low, data)", body)

    def test_offscreen_numbers_do_not_occupy_visible_combat_pool(self):
        body = gd_function(
            script("client/scripts/floating_text_manager.gd"),
            "func _spawn_bouncing_number_at(base_pos: Vector2,"
        )
        self.assertLess(body.index("viewport.get_visible_rect().grow(200.0)"),
                        body.index("var dmg_lbl = _get_free_label()"))
        self.assertIn("viewport.get_canvas_transform() * base_pos", body)
        self.assertIn("GameManager.should_show_damage_numbers()", script(
            "client/scripts/floating_text_manager.gd"
        ))

    def test_latest_visible_damage_reuses_oldest_bounded_pool_label(self):
        floating = script("client/scripts/floating_text_manager.gd")
        body = gd_function(floating, "func _get_free_label() -> Label:")
        self.assertIn("const POOL_SIZE = 100", floating)
        self.assertIn('lbl.get_meta("damage_allocated_msec", 0)', body)
        self.assertIn("oldest = lbl", body)
        self.assertIn("oldest.set_meta", body)
        self.assertIn("return oldest", body)
        self.assertNotIn("POOL_SIZE = 1000", floating)

    def test_other_players_normal_damage_uses_immediate_authoritative_feedback(self):
        manager = script("client/scripts/network_entity_manager.gd")
        floating = script("client/scripts/floating_text_manager.gd")
        base = script("client/scripts/base_entity.gd")
        policy = script("client/scripts/peer_damage_visual_policy.gd")
        self.assertIn("PEER_DAMAGE_VISUAL_POLICY.is_remote_normal_hit(", manager)
        self.assertIn("FloatingTextManager.show_remote_normal_damage(", manager)
        self.assertIn("target_node.update_hp(current_hp, hit_delay, false)", manager)
        self.assertIn("if is_remote_normal_hit:", manager)
        self.assertIn("target_type == 1", policy)
        self.assertIn("skill_id == 0", policy)
        self.assertIn("damage > 0", policy)
        self.assertIn("attacker_id != local_character_id", policy)
        self.assertIn("_spawn_bouncing_number_at(target.global_position", floating)
        self.assertIn("force_visible_peer: bool = false", floating)
        self.assertIn("if is_riding_mecha():", base)

    def test_original_hit_action_mode_is_honored(self):
        base = script("client/scripts/base_entity.gd")
        hit = gd_function(base, "func get_combat_hit_state() -> String:")
        self.assertIn('"attack_ga", "attack_gb"', hit)
        self.assertIn('"attack_aa", "attack_ab"', hit)
        self.assertIn('get_combat_idle_state()', hit)

    def test_physics_and_network_cadence_still_unthrottled(self):
        network = script("client/scripts/network_manager.gd")
        manager = script("client/scripts/network_entity_manager.gd")
        self.assertIn("func _physics_process(delta: float)", network)
        self.assertIn("socket.poll()", network)
        self.assertIn("_advance_monster_network_motion(delta)", manager)
        self.assertIn("func _on_entity_damage_received(data: Dictionary)", manager)
        self.assertIn("func _on_entity_despawned(data: Dictionary)", manager)


if __name__ == "__main__":
    unittest.main()
