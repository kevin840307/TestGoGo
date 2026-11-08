"""AFK client runtime: isolate view unloading from world gameplay and packet handling."""

import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


class AfkModeContract(unittest.TestCase):
    def test_header_button_and_confirmations(self):
        scene = read("client/game_hud.tscn")
        hud = read("client/game_hud.gd")
        self.assertIn('[node name="BtnAfkMode" type="Button"', scene)
        self.assertIn('[node name="AFKOverlay" type="ColorRect"', scene)
        self.assertIn('[node name="MonitorCard" type="Panel"', scene)
        self.assertIn('text = "掛機"', scene)
        self.assertIn('text = "返回遊戲"', scene)
        self.assertIn('btn_afk_mode.pressed.connect(_on_afk_mode_requested)', hud)
        self.assertIn('afk_btn_restore.pressed.connect(_on_afk_restore_requested)', hud)
        self.assertIn('func _on_afk_mode_requested()', hud)
        self.assertIn('func _on_afk_restore_requested()', hud)
        self.assertGreaterEqual(hud.count('show_message_box(\n'), 3)
        self.assertIn('_confirm_begin_afk', hud)
        self.assertIn('_confirm_restore_afk', hud)

    def test_afk_view_is_not_a_second_process(self):
        game = read("client/scripts/game_manager.gd")
        hud = read("client/game_hud.gd")
        self.assertIn('const AFK_MONITOR_FPS: int = 20', game)
        self.assertIn('RenderingServer.render_loop_enabled = true', game)
        self.assertIn('Engine.max_fps = AFK_MONITOR_FPS', game)
        self.assertIn('func begin_afk_mode() -> bool:', game)
        self.assertIn('func finish_afk_mode() -> bool:', game)
        self.assertIn('current_map_node.visible = false', game)
        self.assertIn('current_map_node.visible = true', game)
        self.assertIn('AFK_VISUAL_CACHE.new()', game)
        self.assertIn('_clear_visual_runtime_caches()', game)
        self.assertIn('AFKOverlay / UpdateTimer', hud)
        self.assertIn('_refresh_afk_dashboard()', hud)
        self.assertIn('update_timer.start()', hud.replace('afk_update_timer', 'update_timer'))
        self.assertIn('NetworkManager.is_connected_to_server', hud)
        self.assertIn('if GameManager and GameManager.is_afk_mode_active():', hud)
        self.assertIn('loading_overlay.visible = false', hud)
        self.assertIn('HP %d / %d', hud)
        self.assertIn('[AFK_MODE]', game)

    def test_no_character_or_collision_nodes_freed(self):
        cache = read("client/scripts/afk_visual_cache.gd")
        self.assertIn('node is CollisionObject2D', cache)
        self.assertIn('node.name == "Entities"', cache)
        self.assertIn('node.get_script() != null', cache)
        self.assertIn('return "sprite_frames"', cache)
        self.assertIn('res://animated_map_object.gd', cache)
        self.assertIn('node.set(property_name, null)', cache)
        self.assertIn('ResourceLoader.exists(path)', cache)
        self.assertIn('"node": weakref(node)', cache)
        self.assertNotIn('queue_free(', cache)
        self.assertNotIn('remove_child(', cache)
        self.assertIn('var restore_map: Node = tracked_map()', cache)
        self.assertIn('if tracked_map() == restore_map:', cache)
        self.assertIn('_resources = retry', cache)
        self.assertIn('record["playing"]', cache)

    def test_restore_preserves_user_preferences_and_map_change(self):
        game = read("client/scripts/game_manager.gd")
        self.assertIn('var _afk_saved_profile: Dictionary', game)
        self.assertIn('set_performance_modes(', game)
        self.assertIn('set_visual_simplification_modes(', game)
        self.assertIn('_refresh_afk_map_visuals_after_change()', game)
        self.assertIn('call_deferred("_refresh_afk_map_visuals_after_change")', game)
        self.assertIn('is_changing_map or original_map != current_map_node', game)
        self.assertIn('func _reset_afk_mode()', game)
        self.assertIn('func cleanup_and_return_to_login() -> void :\n\t_reset_afk_mode()', game)

    def test_mouse_autorun_and_network_fixed_cadence(self):
        player = read("client/player.gd")
        network = read("client/scripts/network_manager.gd")
        entities = read("client/scripts/network_entity_manager.gd")
        self.assertIn('func begin_afk_navigation_anchor()', player)
        self.assertIn('func end_afk_navigation_anchor()', player)
        self.assertIn('_afk_navigation_anchor if _afk_navigation_anchor_active', player)
        self.assertIn('func _physics_process(delta: float) -> void :', network)
        self.assertIn('func _physics_process(delta: float) -> void:', entities)
        self.assertIn('func _run_entity_runtime(delta: float) -> void :', entities)
        self.assertIn('GameManager.is_afk_mode_active()', entities)
        self.assertNotIn('Engine.time_scale =', read("client/scripts/game_manager.gd"))
        self.assertNotIn('Engine.physics_ticks_per_second =', read("client/scripts/game_manager.gd"))

    def test_afk_memory_logs_explain_why_ram_does_not_drop(self):
        game = read("client/scripts/game_manager.gd")
        cache = read("client/scripts/afk_visual_cache.gd")
        hud = read("client/game_hud.gd")
        self.assertIn("[AFK_MEMORY]", game)
        self.assertIn("metric_scope=godot_not_windows_working_set", game)
        self.assertIn("Performance.MEMORY_STATIC", game)
        self.assertIn("Performance.RENDER_TEXTURE_MEM_USED", game)
        self.assertIn("Performance.RENDER_VIDEO_MEM_USED", game)
        self.assertIn("Performance.OBJECT_RESOURCE_COUNT", game)
        self.assertIn("_log_afk_memory_after_settle", game)
        self.assertIn("_afk_memory_baseline = _afk_memory_sample()", game)
        self.assertIn('"retained_embedded": retained_embedded', cache)
        self.assertIn('"retained_generated": retained_generated', cache)
        self.assertIn('"excluded_scripted": excluded_scripted', cache)
        self.assertIn('"visited_nodes": visited', cache)
        self.assertIn('int(stats.get("retained_embedded", 0))', hud)

    def test_native_mouse_and_silent_afk_audio(self):
        cursor = read("client/scripts/cursor_manager.gd")
        manager = read("client/scripts/game_manager.gd")
        audio = read("client/scripts/audio_manager.gd")
        self.assertIn("func set_afk_native_cursor_enabled(enabled: bool)", cursor)
        self.assertIn("Input.mouse_mode = Input.MOUSE_MODE_VISIBLE", cursor)
        self.assertIn("Input.mouse_mode = Input.MOUSE_MODE_HIDDEN", cursor)
        self.assertIn("CursorManager.set_afk_native_cursor_enabled(_afk_active)", manager)
        self.assertIn("CursorManager.set_process(not paused and not _afk_active)", manager)
        self.assertIn("func set_afk_silent(enabled: bool)", audio)
        self.assertIn("stop_all_se()", audio)
        self.assertIn("stop_bgm()", audio)
        self.assertIn("if _afk_silent:", audio)
        self.assertIn("_enter_afk_audio_silence()", manager)
        self.assertIn("_leave_afk_audio_silence(true)", manager)
        self.assertIn("_afk_active or _master_bus_was_muted_before_focus_loss", manager)

    def test_godot_test_runs_from_ci(self):
        suite = read("client/tests/afk_visual_cache_regression.gd")
        workflow = read(".github/workflows/server-build.yml")
        self.assertIn('afk_visual_cache_regression.gd', workflow)
        self.assertIn('afk_visual_cache_regression.gd', suite)
        self.assertIn('AFK_CACHE_REGRESSION', suite)
        self.assertIn('test_afk_mode_contract.py', workflow)


if __name__ == "__main__":
    unittest.main()
