"""Memory-perf lifecycle contract for SGO Godot client.

Nonrendering structural checks: this suite cannot prove reduced Windows RAM
or animation correctness on the user's Nvidia/Godot installation.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def body(src: str, name: str) -> str:
    m = re.search(r"(?m)^(?:static )?func " + re.escape(name) + r"\b[^\n]*\n", src)
    if not m:
        raise AssertionError(f"missing GDScript function {name}")
    remaining = src[m.end():]
    next_decl = re.search(r"(?m)^(?:static func |func |const |var |class_name )", remaining)
    return remaining[:next_decl.start()] if next_decl else remaining


class ResourceLifetimeContract(unittest.TestCase):
    def setUp(self):
        self.gm = read("client/scripts/game_manager.gd")
        self.map = read("client/scripts/map_visual_aoi_streamer.gd")
        self.doll = read("client/scripts/paper_doll_loader.gd")
        self.monster = read("client/scripts/monster_loader.gd")
        self.hud_scene = read("client/game_hud.tscn")
        self.hud = read("client/game_hud.gd")

    def test_large_map_packed_scene_released_and_detached_before_attach(self):
        initial = body(self.gm, "enter_game_world")
        transition = body(self.gm, "_on_change_map_received")
        # Both first entry and map transition must release intermediate scene
        # dependencies before the new Node enters the SceneTree.
        for phase in (initial, transition):
            self.assertIn("current_map_node = map_resource.instantiate()", phase)
            self.assertIn("map_resource = null", phase)
            self.assertIn("_map_visual_aoi_streamer.detach_far_before_attach(", phase)
            self.assertLess(
                phase.index("_map_visual_aoi_streamer.detach_far_before_attach("),
                phase.index("get_tree().root.add_child(current_map_node)"),
            )
        self.assertIn("_prime_map_visual_aoi(progress_callback)", initial)
        self.assertIn("_resolve_map_scene_path(target_map_id)", initial)

    def test_map_texture_aoi_only_affects_static_visual_nodes(self):
        detach = body(self.map, "detach_far_before_attach")
        self.assertIn("node is Sprite2D", detach)
        self.assertIn("sprite.get_script() != null", detach)
        self.assertIn("_inside_dynamic_container(sprite, root)", detach)
        self.assertIn("_is_festival_swappable(path)", detach)
        self.assertIn("distance_squared_to(player_position)", detach)
        self.assertIn("sprite.set_meta(META_TEXTURE_PATH, path)", detach)
        self.assertIn("sprite.texture = null", detach)
        self.assertNotIn("queue_free()", detach)
        self.assertNotIn("Collision", detach)
        excluded = body(self.map, "_inside_dynamic_container")
        self.assertIn('cursor.is_in_group("Building")', excluded)
        self.assertIn('"Entities"', excluded)
        bind = body(self.map, "bind_map")
        self.assertIn("sprite.has_meta(META_TEXTURE_PATH)", bind)
        self.assertIn('"loaded": loaded', bind)

    def test_offline_base_scene_is_real_loading_source_when_available(self):
        resolver = body(self.gm, "_resolve_map_scene_path")
        self.assertIn("res://maps/streamed/Map%02d_base.scn", resolver)
        self.assertIn("ResourceLoader.exists(streamed_path)", resolver)
        self.assertIn("res://maps/Map%02d.scn", resolver)
        script = read("tools/Prepare-SGO-MapVisuals.ps1")
        self.assertIn("MapIds = @(13, 15)", script)
        self.assertIn("build_map_visual_aoi_base.gd", script)
        self.assertIn("maps/streamed/manifest.json", script)
        self.assertIn("if ($LASTEXITCODE -ne 0)", script)
        builder = read("client/tools/build_map_visual_aoi_base.gd")
        self.assertIn('sprite.texture = null', builder)
        self.assertIn('_inside_dynamic_or_building', builder)
        self.assertIn('_is_festival_swappable(path)', builder)
        self.assertIn('ResourceSaver.save(base, output_path)', builder)

    def test_paper_doll_atlas_frames_shared_by_canonical_frame_key(self):
        self.assertGreaterEqual(self.doll.count("var atlas_frames_by_key: Dictionary = {}"), 2)
        self.assertIn("atlas_frames_by_key[str(frame_key)] = atlas_texture", self.doll)
        self.assertIn("atlas_frames_by_key[str(f_key)] = atlas_tex", self.doll)
        self.assertIn("sprite_frames.add_frame(anim_name, atlas_texture)", self.doll)
        self.assertIn("sf.add_frame(anim_name, atlas_tex)", self.doll)
        self.assertIn('"old_offset"', self.doll)
        self.assertIn('func clear_runtime_cache() -> void', self.doll)

    def test_monster_shares_atlas_but_not_timing_or_gameplay_state(self):
        fetch = body(self.monster, "get_monster_frames")
        self.assertIn('other_key: String', fetch)
        self.assertIn("_atlas_from_sprite_frames", fetch)
        self.assertIn("ResourceLoader.CACHE_MODE_IGNORE", fetch)
        self.assertIn("atlas_frames_by_key[str(f_key)] = atlas_tex", fetch)
        self.assertIn("_apply_act_timing(sf, json_path)", fetch)
        self.assertIn("_cache[cache_key] = sf", fetch)
        factory = body(self.monster, "make_clipped_frame_texture")
        self.assertIn("frame.filter_clip = true", factory)
        self.assertIn('var _cache: Dictionary = {}', self.monster)
        self.assertIn("_cache.clear()", body(self.monster, "clear_runtime_cache"))

    def test_minimap_target_is_small_and_only_renders_when_requested(self):
        match = re.search(
            r'\[node name="SubViewport" type="SubViewport".*?\n\n',
            self.hud_scene, flags=re.S
        )
        self.assertIsNotNone(match)
        viewport = match.group()
        self.assertIn("size = Vector2i(147, 101)", viewport)
        self.assertIn("render_target_update_mode = 0", viewport)
        self.assertIn("SubViewport.UPDATE_ONCE", self.hud)
        self.assertIn("SubViewport.UPDATE_DISABLED", self.hud)
        self.assertNotIn("Engine.physics_ticks_per_second =", self.gm)
        self.assertNotIn("Engine.time_scale =", self.gm)


if __name__ == "__main__":
    unittest.main()
