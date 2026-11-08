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
        self.assertIn('RenderingServer.render_loop_enabled = false', game)
        self.assertIn('func request_afk_dashboard_frame() -> bool:', game)
        self.assertIn('Engine.max_fps = AFK_MONITOR_FPS', game)
        self.assertIn('RenderingServer.render_loop_enabled = true', game)
        self.assertIn('var desired_fps: int = AFK_MONITOR_FPS if _afk_active else', game)
        self.assertIn('func begin_afk_mode(progress_callback: Callable = Callable()) -> bool:', game)
        self.assertIn('func finish_afk_mode(progress_callback: Callable = Callable()) -> bool:', game)
        self.assertIn('current_map_node.visible = false', game)
        self.assertIn('current_map_node.visible = true', game)
        self.assertIn('AFK_VISUAL_CACHE.new()', game)
        self.assertIn('AFK_RENDER_SHELL.new()', game)
        self.assertIn('_enter_afk_render_shell()', game)
        self.assertIn('await _restore_afk_render_shell(progress_callback)', game)
        self.assertIn('_set_afk_dynamic_visuals_suspended(true)', game)
        self.assertIn('_clear_visual_runtime_caches()', game)
        self.assertIn('await GameManager.begin_afk_mode(', hud)
        self.assertIn('_set_afk_dynamic_visuals_suspended_batched', game)
        self.assertIn('_clear_visual_runtime_caches_batched', game)
        self.assertIn('clear_runtime_cache_batched', read("client/scripts/paper_doll_loader.gd"))
        self.assertIn('AFKOverlay / UpdateTimer', hud)
        self.assertIn('_refresh_afk_dashboard()', hud)
        self.assertIn('update_timer.start()', hud.replace('afk_update_timer', 'update_timer'))
        self.assertIn('NetworkManager.is_connected_to_server', hud)
        self.assertIn('if GameManager and GameManager.is_afk_mode_active():', hud)
        self.assertIn('loading_overlay.visible = false', hud)
        self.assertIn('HP %d / %d', hud)
        self.assertIn('[AFK_MODE]', game)

    def test_unfocused_afk_stops_gpu_and_visual_updates_only(self):
        game = read("client/scripts/game_manager.gd")
        hud = read("client/game_hud.gd")
        focus_test = read("client/tests/afk_focus_render_regression.gd")
        self.assertIn('static func should_draw_afk_dashboard(has_focus: bool) -> bool:', game)
        self.assertIn('return has_focus', game)
        self.assertIn('var desired_rendering: bool = should_draw_afk_dashboard(_window_has_focus) if _afk_active else not should_pause', game)
        self.assertIn('RenderingServer.render_loop_enabled = false', game)
        self.assertIn('Engine.max_fps = AFK_MONITOR_FPS', game)
        self.assertIn('AFK focus-out keeps the main loop capped at 20 FPS', focus_test)
        self.assertIn('return _afk_active and _window_has_focus', game)
        self.assertNotIn('RenderingServer.force_draw(true)', game)
        self.assertIn('current_hud.set_background_visuals_paused(paused or _afk_active)', game)
        self.assertIn('current_hud.set_afk_dashboard_updates_enabled(_afk_active and _window_has_focus)', game)
        self.assertIn('func set_afk_dashboard_updates_enabled(enabled: bool)', hud)
        self.assertIn('afk_update_timer.stop()', hud)
        self.assertIn('afk_update_timer.start()', hud)
        self.assertIn('if not GameManager.is_game_window_focused():', hud)
        self.assertIn('監控 20 FPS · 失焦不渲染', hud)
        self.assertIn('AFK_FOCUS_RENDER_REGRESSION', focus_test)
        self.assertNotIn('if _afk_active:\\n\\t\\t# AFK status dashboard must remain visible even when unfocused.', game)

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
        self.assertIn('if is_changing_map or not bool(restore_result.get("ok", false)):', game)
        self.assertIn('func _reset_afk_mode()', game)
        cleanup = game[game.index("func cleanup_and_return_to_login() -> void :"):]
        self.assertIn("_reset_afk_mode()", cleanup)
        self.assertIn("_set_afk_dynamic_visuals_suspended(true)", cleanup)
        self.assertIn("LegacyPathfinder.clear_runtime_cache()", cleanup)
        self.assertIn("FloatingTextManager.release_world_runtime_for_login()", cleanup)
        self.assertIn("DropItemManager.release_world_runtime_for_login()", cleanup)
        self.assertIn("_skip_next_login_instance_prewarm = true", cleanup)
        entry = read("client/entry_manager.gd")
        login = read("client/login_screen.gd")
        self.assertIn("consume_skip_login_instance_prewarm", entry)
        self.assertIn("release_background_video_resources", entry)
        self.assertIn("bg_video.stream = null", login)

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

    def test_afk_releases_rebuildable_hud_rows_and_inventory_is_lazy(self):
        hud = read("client/game_hud.gd")
        inventory = read("client/scripts/inventory_window.gd")
        shared = read("client/scripts/shared_inventory_panel.gd")

        setup_start = inventory.index("func setup(player: CharacterBody2D) -> void :")
        setup_end = inventory.index("\nfunc ", setup_start + 1)
        setup = inventory[setup_start:setup_end]
        self.assertIn("setup_player(player)", setup)
        self.assertNotIn("update_ui()", setup)

        self.assertIn("func release_rebuildable_visual_state_batched", shared)
        self.assertIn("_item_list_render_generation += 1", shared)
        self.assertIn("container.remove_child(child)", shared)
        self.assertIn("child.free()", shared)
        self.assertIn('get_node_or_null("EquipPreview")', shared)
        self.assertIn("CACHE_MODE_IGNORE", shared)

        self.assertIn("func release_afk_rebuildable_ui_batched", hud)
        self.assertIn("release_rebuildable_visual_state_batched", hud)
        game = read("client/scripts/game_manager.gd")
        self.assertIn('current_hud.has_method("release_afk_rebuildable_ui_batched")', game)
        self.assertIn("[AFK_UI_RELEASE]", game)
        self.assertIn('get_node_or_null("ContentScroll/SkillGrid")', hud)
        self.assertIn('get_node_or_null("GridContainer")', hud)
        self.assertIn("SubViewport.UPDATE_DISABLED", hud)
        self.assertIn("func _restore_afk_rebuildable_ui()", hud)


    def test_afk_memory_audit_and_disposable_resource_ownership(self):
        game = read("client/scripts/game_manager.gd")
        audit = read("client/scripts/afk_memory_audit.gd")
        effect = read("client/scripts/effect_loader.gd")
        monster_loader = read("client/scripts/monster_loader.gd")
        doll_loader = read("client/scripts/paper_doll_loader.gd")
        item_loader = read("client/scripts/item_loader.gd")
        skill_loader = read("client/scripts/skill_loader.gd")
        audio = read("client/scripts/audio_manager.gd")
        pet = read("client/scripts/pet_entity.gd")
        ground = read("client/scripts/ground_item_entity.gd")
        npc = read("client/scripts/npc_entity.gd")
        render_test = read("client/tests/render_resource_regression.gd")

        self.assertIn("AFK_MEMORY_AUDIT", game)
        self.assertIn("[AFK_MEMORY_AUDIT]", game)
        self.assertNotIn('_log_afk_memory_audit("afk_enter_before")', game)
        self.assertNotIn('_log_afk_memory_audit("afk_enter_after")', game)
        self.assertIn('_log_afk_memory_audit("afk_settled_3s")', game)
        self.assertIn('_log_afk_memory_audit("logout_settled")', game)
        self.assertIn("get_reference_count()", audit)
        self.assertIn("if value is Node:", audit)
        self.assertIn("reference_sources", audit)
        self.assertIn("estimated_texture_bytes", audit)
        self.assertIn("SpriteFrames", audit)
        self.assertIn("AtlasTexture", audit)

        # Map scenes and effect .tres files own large external visual
        # dependencies, so their disposable graphs must bypass the global
        # ResourceLoader cache recursively.
        self.assertGreaterEqual(game.count("CACHE_MODE_IGNORE_DEEP"), 3)
        self.assertIn("CACHE_MODE_IGNORE_DEEP", effect)
        for source in (monster_loader, doll_loader, item_loader, skill_loader, audio):
            self.assertIn("CACHE_MODE_IGNORE", source)

        # All dynamic entity categories preserved by the AFK shell must expose
        # a visual-only suspend path; no collision/gameplay node is freed.
        for source in (pet, ground, npc):
            self.assertIn("func set_afk_visual_suspended(suspended: bool)", source)
            self.assertNotIn("set_physics_process(false)", source)
        self.assertIn("AFK strips pet SpriteFrames and UI", render_test)
        self.assertIn("AFK strips ground-item texture and labels", render_test)
        self.assertIn("AFK strips NPC texture and visual UI", render_test)

        # Unfocused remains a warm/light mode; only optional hidden UI and
        # unreferenced loader cache entries are reclaimed.
        trim_start = game.index("func _trim_unfocused_runtime_caches() -> int:")
        trim_end = game.index("\nfunc ", trim_start + 1)
        trim = game[trim_start:trim_end]
        self.assertIn("_release_background_optional_ui()", trim)
        self.assertNotIn("_set_afk_dynamic_visuals_suspended", trim)
        self.assertNotIn("_enter_afk_render_shell", trim)


    def test_afk_low_memory_shell_is_render_only(self):
        shell = read("client/scripts/afk_render_shell.gd")
        game = read("client/scripts/game_manager.gd")
        entities = read("client/scripts/network_entity_manager.gd")
        base = read("client/scripts/base_entity.gd")
        monster = read("client/scripts/monster_entity.gd")
        self.assertIn("map_node.queue_free()", shell)
        self.assertIn("entities.reparent(shell, true)", shell)
        self.assertIn("var _entities: Node = null", shell)
        self.assertIn("_entities.reparent(restored_map, true)", shell)
        self.assertNotIn("NetworkManager", shell)
        self.assertNotIn("send_secure_packet", shell)
        self.assertNotIn("send_world_ready", shell)
        begin = game[
            game.index("func begin_afk_mode(progress_callback: Callable = Callable())"):
            game.index("func finish_afk_mode(progress_callback: Callable = Callable())")
        ]
        self.assertNotIn("clear_map_entities()", begin)
        # A read-only debug check may verify that network physics stayed alive;
        # AFK itself must never pause, disconnect, or mutate network processing.
        self.assertIn("NetworkManager.is_physics_processing()", begin)
        self.assertNotIn("NetworkManager.set_process(", begin)
        self.assertNotIn("NetworkManager.set_physics_process(", begin)
        self.assertNotIn("NetworkManager.disconnect", begin)
        self.assertNotIn("NetworkManager.close", begin)
        self.assertIn("current_player.reparent(container, true)", game)
        self.assertNotIn('current_map_node.get_node_or_null("ObjectContainer")', game)
        self.assertIn("NetworkEntityManager.active_entities.values()", game)
        self.assertIn("set_afk_visual_suspended", base)
        self.assertIn("set_afk_visual_suspended", monster)
        self.assertIn("GameManager.is_afk_mode_active()", entities)


    def test_native_mouse_and_silent_afk_audio(self):
        cursor = read("client/scripts/cursor_manager.gd")
        manager = read("client/scripts/game_manager.gd")
        audio = read("client/scripts/audio_manager.gd")
        self.assertIn("func set_afk_custom_cursor_enabled(enabled: bool)", cursor)
        self.assertIn("Keep the main", cursor)
        self.assertIn("cursor_sprite.visible = true", cursor)
        self.assertIn("Input.mouse_mode = Input.MOUSE_MODE_HIDDEN", cursor)
        self.assertIn("if _afk_custom_cursor_enabled:", cursor)
        self.assertIn("CursorManager.set_afk_custom_cursor_enabled(_afk_active)", manager)
        self.assertIn("CursorManager.set_process(_afk_active or not paused)", manager)
        self.assertNotIn("set_afk_native_cursor_enabled", cursor)
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
        self.assertIn('afk_focus_render_regression.gd', workflow)
        self.assertIn('afk_visual_cache_regression.gd', workflow)
        self.assertIn('afk_cursor_audio_regression.gd', workflow)
        self.assertIn('AFK_CURSOR_AUDIO_REGRESSION', read("client/tests/afk_cursor_audio_regression.gd"))
        self.assertIn('afk_visual_cache_regression.gd', suite)
        self.assertIn('AFK_CACHE_REGRESSION', suite)
        self.assertIn('test_afk_mode_contract.py', workflow)


if __name__ == "__main__":
    unittest.main()
