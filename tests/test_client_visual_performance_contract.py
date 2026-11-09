"""Client-only performance visual LOD / account FPS static integration guards.

These tests complement the Godot behavioral tests (they do not replace them).
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def source(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


class ClientVisualPerformanceContractTests(unittest.TestCase):
    def test_quality_presets_do_not_throttle_fps(self):
        profile = source("client/scripts/client_performance_profile.gd")
        self.assertIn("static func sanitize_fps_limit", profile)
        self.assertIn("static func fps_options()", profile)
        self.assertIn("range(30, 121, 5)", profile)
        self.assertIn('profile["monster_aoi_enter_radius"] = 1250.0', profile)
        self.assertNotIn('profile["monster_aoi_enter_radius"] = 950.0', profile)
        self.assertIn('profile["effect_budget_percent"] = 0', profile)
        self.assertIn('profile["combat_effects_enabled"] = false', profile)
        for cap in ('profile["foreground_fps"] = 45', 'profile["foreground_fps"] = 120'):
            self.assertNotIn(cap, profile)

    def test_enhancement_is_quality_only(self):
        manager = source("client/scripts/game_manager.gd")
        hud = source("client/game_hud.gd")
        scene = source("client/game_hud.tscn")
        self.assertIn(
            "set_world_enhancement_enabled(current_performance_load_mode == ClientPerformanceProfile.LOAD_QUALITY)",
            manager,
        )
        self.assertNotIn("_load_world_enhancement_setting()", manager)
        self.assertNotIn("current_quality_enhancement_enabled", hud)
        self.assertNotIn("option_quality_enhancement", hud)
        self.assertNotIn('name="OptionQualityEnhancement"', scene)
        self.assertIn('popup/item_1/text = "一般"', scene)

    def test_fps_spinbox_has_real_editing_and_step_controls(self):
        profile = source("client/scripts/client_performance_profile.gd")
        scene = source("client/game_hud.tscn")
        hud = source("client/game_hud.gd")
        self.assertIn('name="OptionFpsLimit" type="SpinBox"', scene)
        self.assertIn('min_value = 0.0', scene)
        self.assertIn('max_value = 144.0', scene)
        self.assertIn('step = 1.0', scene)
        self.assertIn('custom_arrow_step = 5.0', scene)
        self.assertIn("static func normalize_fps_spin_step", profile)
        self.assertIn("option_fps_limit.value_changed.connect(", hud)
        self.assertIn("option_fps_limit.editable = true", hud)
        self.assertIn("option_fps_limit.set_value_no_signal(float(selected))", hud)
        self.assertNotIn("option_fps_limit is OptionButton", hud)

    def test_fps_per_account_and_logging(self):
        hud = source("client/game_hud.gd")
        manager = source("client/scripts/game_manager.gd")
        self.assertIn("SETTING_KEY_FOREGROUND_FPS_LIMIT", hud)
        self.assertIn("GameManager.set_foreground_fps_limit(current_fps_limit)", hud)
        self.assertIn('current_performance_profile["foreground_fps"] = current_foreground_fps_limit', manager)
        self.assertIn("cpu_process_time_ms=", manager)
        self.assertIn("monster_aoi_radius=", manager)
        self.assertIn("ground_aoi_radius=", manager)
        self.assertIn("gpu_memory_metric=godot_resources", manager)

    def test_minimized_matches_unfocused_globally_and_mutes_audio(self):
        manager = source("client/scripts/game_manager.gd")
        self.assertIn(
            "static func foreground_from_window_state(focused: bool, minimized: bool) -> bool:",
            manager,
        )
        self.assertIn("return focused and not minimized", manager)
        self.assertIn(
            "DisplayServer.window_get_mode() == DisplayServer.WINDOW_MODE_MINIMIZED",
            manager,
        )
        self.assertIn("var observed_focus: bool = _observe_window_foreground()", manager)
        self.assertIn(
            "static func should_throttle_background_visuals(has_focus: bool, _world_active: bool) -> bool:",
            manager,
        )
        self.assertIn("return not has_focus", manager)
        self.assertIn("AudioServer.set_bus_mute(master_bus_index, true)", manager)
        self.assertIn(
            "_set_window_focus_power_save(_observe_window_foreground())",
            manager,
        )
        self.assertIn("minimized=%s", manager)

    def test_packet_paths_are_preserved(self):
        manager = source("client/scripts/network_entity_manager.gd")
        self.assertIn("_monster_spawn_templates[entity_id] = cached", manager)
        self.assertIn("_ground_spawn_templates[entity_id] = cached_ground", manager)
        self.assertIn("if bool(drop.get_meta(\"is_requesting\", false)):", manager)
        self.assertIn("_is_local_combat_target(monster)", manager)
        self.assertIn("_reveal_combat_monster", manager)
        self.assertIn("resolve_visual_aoi_radii(", manager)
        self.assertIn("_pending_aoi_moves.record_move(", manager)

    def test_fixed_physics_network_and_low_resource_effect_suppression(self):
        manager = source("client/scripts/game_manager.gd")
        network = source("client/scripts/network_manager.gd")
        profile = source("client/scripts/client_performance_profile.gd")
        actor = source("client/scripts/base_entity.gd")
        monster = source("client/scripts/monster_entity.gd")
        floating = source("client/scripts/floating_text_manager.gd")
        self.assertIn('func _physics_process(delta: float)', network)
        self.assertNotIn('func _process(delta: float)', network)
        self.assertIn('physics_ticks_per_second', manager)
        self.assertIn('[PHYSICS_CADENCE]', manager)
        self.assertIn('static func needs_vsync_disabled', manager)
        self.assertIn('DisplayServer.window_set_vsync_mode(preferred_mode)', manager)
        self.assertIn('profile["effect_budget_percent"] = 0', profile)
        self.assertIn('profile["combat_effects_enabled"] = false', profile)
        self.assertIn('func stop_combat_visual_effects()', actor)
        self.assertIn('func stop_combat_visual_effects()', monster)
        self.assertIn('func stop_combat_visual_effects()', floating)
        self.assertIn('not GameManager.should_render_combat_effects()', floating)
        self.assertIn('_try_finish_buff_animation()', actor)

    def test_render_and_focus_savings_never_throttle_network_or_fixed_simulation(self):
        manager = source("client/scripts/game_manager.gd")
        network = source("client/scripts/network_manager.gd")
        profile = source("client/scripts/client_performance_profile.gd")

        # Server<->client delivery is fixed-physics driven, not render-frame driven.
        self.assertIn("func _physics_process(delta: float) -> void :", network)
        self.assertIn("socket.poll()", network)
        self.assertIn("IN_WORLD_PACKET_LIMIT_PER_FRAME", network)
        self.assertIn("IN_WORLD_PACKET_TIME_BUDGET_USEC", network)

        # Rendering/background/AFK policies may touch presentation only.
        self.assertNotIn("NetworkManager.set_process(", manager)
        self.assertNotIn("NetworkManager.set_physics_process(", manager)
        self.assertNotIn("Engine.physics_ticks_per_second =", manager)
        self.assertNotIn("NetworkManager", profile)

        focus_start = manager.index("func _refresh_focus_render_policy() -> void:")
        focus_end = manager.index("\nfunc ", focus_start + 1)
        focus_policy = manager[focus_start:focus_end]
        self.assertNotIn("NetworkManager", focus_policy)
        self.assertNotIn("physics_ticks_per_second =", focus_policy)
        self.assertNotIn("get_tree().paused", focus_policy)
        self.assertIn("RenderingServer.render_loop_enabled", focus_policy)

    def test_removed_last_preset_migrates_to_responsive_low_resource(self):
        profile = source("client/scripts/client_performance_profile.gd")
        scene = source("client/game_hud.tscn")
        manager = source("client/scripts/game_manager.gd")
        self.assertIn("return clampi(value, LOAD_QUALITY, LOAD_LOW)", profile)
        self.assertIn("return clampi(value, COMBAT_NORMAL, COMBAT_RESPONSE)", profile)
        self.assertIn('popup/item_2/text = "低資源"', scene)
        self.assertIn('popup/item_1/text = "反應優先"', scene)
        self.assertNotIn('popup/item_3/text = "多開"', scene)
        self.assertNotIn('popup/item_2/text = "大型戰鬥"', scene)
        self.assertIn('profile["simplify_other_players"] = true', profile)
        self.assertIn('profile["simplify_noncritical_monsters"] = true', profile)
        self.assertIn('profile["effect_budget_percent"] = 0', profile)
        self.assertIn('profile["combat_effects_enabled"] = false', profile)
        self.assertIn('_apply_low_resource_world_visual_policy()', manager)
        self.assertIn('_remove_day_night_overlay()', manager)
        self.assertIn('MonsterLoader.clear_runtime_cache()', manager)
        self.assertIn('EffectLoader.clear_runtime_cache()', manager)

    def test_async_sprite_layout_rechecks_after_frame_await(self):
        monster = source("client/scripts/monster_entity.gd")
        pet = source("client/scripts/pet_entity.gd")
        base = source("client/scripts/base_entity.gd")

        def gd_function_block(text, signature):
            start = text.index(signature)
            end = text.find("\nfunc ", start + len(signature))
            return text[start:] if end < 0 else text[start:end]

        monster_layout = gd_function_block(
            monster, "func _lock_ui_position() -> void :"
        )
        pet_layout = gd_function_block(
            pet, "func _lock_ui_position() -> void :"
        )

        self.assertIn('var _ui_position_generation: int = 0', monster)
        self.assertIn(
            'requested_generation != _ui_position_generation',
            monster_layout,
        )
        self.assertIn(
            'var frames: SpriteFrames = sprite.sprite_frames',
            monster_layout,
        )
        self.assertIn('if not is_instance_valid(frames):', monster_layout)
        self.assertIn('frames.get_frame_count(base_anim) > 0', monster_layout)
        self.assertIn(
            'var frames: SpriteFrames = sprite.sprite_frames',
            pet_layout,
        )
        self.assertIn('if not is_instance_valid(frames):', pet_layout)
        self.assertIn('if data_model.transform_type <= 0 or not is_instance_valid(transform_sprite) or not is_instance_valid(transform_sprite.sprite_frames):', base)
        self.assertNotIn(
            'if not sprite.sprite_frames.has_animation(base_anim):',
            monster_layout,
        )
        self.assertNotIn(
            'if not sprite.sprite_frames.has_animation(base_anim):',
            pet_layout,
        )

    def test_static_map_visual_aoi_streaming_is_presentation_only(self):
        profile = source("client/scripts/client_performance_profile.gd")
        manager = source("client/scripts/game_manager.gd")
        streamer = source("client/scripts/map_visual_aoi_streamer.gd")

        for key in (
            '"map_visual_aoi_enter_radius": 5000.0',
            'profile["map_visual_aoi_enter_radius"] = 3400.0',
            'profile["map_visual_aoi_enter_radius"] = 2400.0',
        ):
            self.assertIn(key, profile)
        self.assertIn("MAP_VISUAL_AOI_STREAMER", manager)
        self.assertIn("_update_map_visual_aoi(delta)", manager)
        self.assertIn("[MAP_VISUAL_AOI]", manager)
        self.assertIn("sprite.texture = null", streamer)
        self.assertIn("ResourceLoader.load_threaded_request(", streamer)
        self.assertIn("ResourceLoader.CACHE_MODE_IGNORE", streamer)
        self.assertIn("sprite.get_script() != null", streamer)
        self.assertIn("_inside_dynamic_container", streamer)
        self.assertIn("FESTIVAL_SWAPPABLE_TOKENS", streamer)
        self.assertNotIn("NetworkManager", streamer)
        self.assertNotIn("set_physics_process", streamer)
        self.assertNotIn("physics_ticks_per_second", streamer)
        self.assertNotIn("Blocking", streamer)

    def test_behavioral_scripts_wired_to_runner(self):
        runner = source("tests/run_protocol_checks.ps1")
        for test in (
            "performance_ui_settings_regression.gd",
            "performance_visual_aoi_regression.gd",
        ):
            self.assertIn(test, runner)
            self.assertTrue((ROOT / "client" / "tests" / test).exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
