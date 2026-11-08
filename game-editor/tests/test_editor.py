from __future__ import annotations

import base64
import re
import struct
import tempfile
import unittest
import sys
from pathlib import Path

GAME_EDITOR_ROOT = Path(__file__).resolve().parents[1]
if str(GAME_EDITOR_ROOT) not in sys.path:
    sys.path.insert(0, str(GAME_EDITOR_ROOT))

from sgo_editor.asset_repo import AssetRepository
from sgo_editor.enemy_repo import EnemyRepository
from sgo_editor.map_repo import MapRepository
from sgo_editor.paths import ProjectPaths
from sgo_editor.quest_repo import QuestRepository
from sgo_editor.skill_repo import SkillRepository
from sgo_editor.storage import BackupManager, UnsafePathError, safe_child
from editor import EditorHandler


class EditorFixture(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / "client" / "Skills").mkdir(parents=True)
        (self.root / "client" / "Assets" / "Monsters").mkdir(parents=True)
        (self.root / "client" / "Assets" / "GameInfo").mkdir(parents=True)
        (self.root / "client" / "maps").mkdir(parents=True)
        (self.root / "server").mkdir()
        dat = self.root / "Old" / "服務端" / "Dat"
        (dat / "Enemy").mkdir(parents=True)
        (dat / "Map" / "Map13").mkdir(parents=True)
        (dat / "Quest").mkdir(parents=True)
        (self.root / "game-editor").mkdir()
        self.paths = ProjectPaths.resolve(self.root)
        self.backups = BackupManager(self.paths)

    def tearDown(self) -> None:
        self.temp.cleanup()


class PathTests(EditorFixture):
    def test_project_paths_resolve_and_traversal_is_rejected(self) -> None:
        self.assertEqual(self.paths.root, self.root.resolve())
        with self.assertRaises(UnsafePathError):
            safe_child(self.paths.assets, "../server/appsettings.json")


class SkillRepositoryTests(EditorFixture):
    SAMPLE = '''[gd_resource type="Resource" script_class="SkillData" format=3]\n\n[ext_resource type="Script" path="res://scripts/skill_level_data.gd" id="1_level"]\n[ext_resource type="Script" path="res://scripts/skill_data.gd" id="2_skill"]\n\n[sub_resource type="Resource" id="Resource_a"]\nscript = ExtResource("1_level")\ncast_range = 150\nbase_damage = 55\ncustom_magic = 9\n\n[resource]\nscript = ExtResource("2_skill")\nid = 20101\nskill_name = "赤色光彈"\ndescription = "測試"\ndamage_type = 1\nlegacy_note = "keep"\nlevels = Array[ExtResource("1_level")]([SubResource("Resource_a")])\n'''

    def test_skill_crud_preserves_unknown_properties(self) -> None:
        path = self.paths.skills / "20101.tres"
        path.write_text(self.SAMPLE, encoding="utf-8")
        repo = SkillRepository(self.paths, self.backups)
        doc = repo.get(20101)
        self.assertEqual(doc["name"], "赤色光彈")
        self.assertEqual(doc["levels"][0]["level"], 1)
        doc["levels"][0]["baseDamage"] = 777
        doc["levels"].append({**doc["levels"][0], "level": 2, "baseDamage": 888})
        saved = repo.save(20101, doc)
        self.assertEqual(saved["levels"][0]["baseDamage"], 777)
        self.assertEqual(saved["levels"][1]["baseDamage"], 888)
        text = path.read_text(encoding="utf-8")
        self.assertIn('legacy_note = "keep"', text)
        self.assertIn("custom_magic = 9", text)
        self.assertTrue(any(self.paths.backups.rglob("20101.tres")))
        repo.delete(20101)
        self.assertFalse(path.exists())

    def test_new_skill_round_trip(self) -> None:
        repo = SkillRepository(self.paths, self.backups)
        doc = {
            "name": "星界測試", "description": "demo", "skillType": 7, "damageType": 1,
            "iconId": 42, "fxUse": "Skill_Use_100", "fxHit": "Skill_Hit_100", "fxExtra": "", "specialSkill": "AOE",
            "levels": [{"level": 1, "castRange": 0, "aoeRadius": 900, "costMp": 1, "baseDamage": 1000, "damageMultiplier": 100, "tickCount": 1}],
        }
        saved = repo.save(99002, doc)
        self.assertEqual(saved["id"], 99002)
        self.assertEqual(saved["levels"][0]["aoeRadius"], 900)


class EnemyRepositoryTests(EditorFixture):
    def test_enemy_update_preserves_other_columns_and_cp950(self) -> None:
        cols = [""] * 180
        cols[0] = "1"; cols[1] = "1"; cols[2] = "波奇鳥"; cols[3] = "1"; cols[6] = "EN010A"; cols[9] = "40"; cols[50] = "KEEP_ME"
        self.paths.enemy_csv.write_text(",".join(cols) + "\r\n", encoding="cp950")
        repo = EnemyRepository(self.paths, self.backups)
        saved = repo.save(1, {"name": "新波奇鳥", "level": 9, "sprite": "EN010B", "maxHp": 1234})
        self.assertEqual(saved["name"], "新波奇鳥")
        raw = self.paths.enemy_csv.read_text(encoding="cp950").splitlines()[0].split(",")
        self.assertEqual(raw[50], "KEEP_ME")
        self.assertEqual(raw[9], "1234")

    def test_enemy_create_from_clone(self) -> None:
        cols = [""] * 180
        cols[0] = cols[1] = "1"; cols[2] = "舊怪"; cols[3] = "2"; cols[6] = "EN001"; cols[9] = "50"; cols[75] = "template"
        self.paths.enemy_csv.write_text(",".join(cols) + "\r\n", encoding="cp950")
        repo = EnemyRepository(self.paths, self.backups)
        repo.save(2, {"name": "新怪", "level": 3, "sprite": "EN002", "maxHp": 60, "cloneFromId": 1})
        rows = self.paths.enemy_csv.read_text(encoding="cp950").splitlines()
        self.assertEqual(rows[1].split(",")[75], "template")


class MapRepositoryTests(EditorFixture):
    def _write_enemy_dat(self) -> None:
        payload = bytearray(struct.pack("<i", 2))
        for flags, enemy_id, x, y, fill in [(11, 1, 100, 200, 0xAA), (22, 2, 300, 400, 0xBB)]:
            payload += struct.pack("<i", flags) + str(enemy_id).encode() + b"\x00" + struct.pack("<ii", x, y) + bytes([fill]) * 207
        (self.paths.maps / "Map13" / "Enemy.dat").write_bytes(payload)

    def _write_npc_dat(self) -> None:
        name = "治療師".encode("cp950")
        payload = struct.pack("<i", 1) + struct.pack("<iiiB", 7, 10, 20, 9) + b"Cure.txt\x00" + name + b"\x00"
        (self.paths.maps / "Map13" / "NPC.dat").write_bytes(payload)

    def test_enemy_round_trip_preserves_207_byte_tail(self) -> None:
        self._write_enemy_dat()
        repo = MapRepository(self.paths, self.backups)
        rows = repo.get_enemy_spawns(13)
        rows[0]["x"] = 999
        repo.save_enemy_spawns(13, rows)
        raw = (self.paths.maps / "Map13" / "Enemy.dat").read_bytes()
        # First tail begins after count + flags + '1\0' + x/y.
        first_tail = 4 + 4 + 2 + 8
        self.assertEqual(raw[first_tail:first_tail+207], bytes([0xAA]) * 207)
        self.assertEqual(repo.get_enemy_spawns(13)[0]["x"], 999)

    def test_npc_round_trip_preserves_reserved_and_updates_script(self) -> None:
        self._write_npc_dat()
        repo = MapRepository(self.paths, self.backups)
        rows = repo.get_npc_spawns(13)
        self.assertEqual(rows[0]["name"], "治療師")
        rows[0]["script"] = "Cure2.txt"
        repo.save_npc_spawns(13, rows)
        raw = (self.paths.maps / "Map13" / "NPC.dat").read_bytes()
        self.assertEqual(raw[16], 9)  # count(4) + 3 ints(12)
        self.assertEqual(repo.get_npc_spawns(13)[0]["script"], "Cure2.txt")

    def test_map_editor_meta_blocking_overlay_and_snap_target(self) -> None:
        (self.root / "client" / "maps" / "map_bounds.json").write_text(
            '{"13":{"min_x":17,"min_y":17,"max_x":95,"max_y":79}}',
            encoding="utf-8",
        )
        (self.root / "client" / "Assets" / "GameInfo" / "MapSpawnConfig.json").write_text(
            '{"13":{"MapName":"測試森林","Spawners":[{"ZoneName":"波奇鳥區","MonsterName":"波奇鳥","MaxCount":3,"RespawnTime":10,"Rect":{"X1":32,"Y1":32,"X2":64,"Y2":64}}]}}',
            encoding="utf-8",
        )
        width, height = 8, 6
        cells = bytearray(width * height)
        cells[1] = 1
        cells[2] = 3
        (self.paths.maps / "Map13" / "Blocking.dat").write_bytes(
            struct.pack("<ii", width, height) + bytes(cells)
        )

        repo = MapRepository(self.paths, self.backups)
        meta = repo.get_editor_meta(13)
        self.assertEqual(meta["name"], "測試森林")
        self.assertEqual(meta["bounds"]["minX"], 16)
        self.assertEqual(meta["bounds"]["maxX"], 96)
        self.assertEqual(meta["blocking"]["width"], width)
        self.assertEqual(meta["blocking"]["cellSize"], 16)
        self.assertEqual(meta["spawners"][0]["monsterName"], "波奇鳥")
        self.assertEqual(repo.list_maps()[0]["name"], "測試森林")

        blocked = repo.get_terrain(13, 24, 8)
        self.assertFalse(blocked["walkable"])
        self.assertIsNotNone(blocked["nearest"])
        special = repo.get_terrain(13, 40, 8)
        self.assertTrue(special["special"])

        png = repo.blocking_overlay_png(13)
        self.assertTrue(png.startswith(b"\x89PNG\r\n\x1a\n"))

        preview_dir = self.root / "game-editor" / ".generated" / "map-previews"
        preview_dir.mkdir(parents=True)
        preview = bytearray(b"\x89PNG\r\n\x1a\n" + bytes(8))
        preview += struct.pack(">II", 320, 240)
        (preview_dir / "Map13.png").write_bytes(bytes(preview))
        meta = repo.get_editor_meta(13)
        self.assertTrue(meta["preview"]["available"])
        self.assertEqual(meta["preview"]["width"], 320)
        self.assertEqual(meta["preview"]["height"], 240)
        self.assertEqual(meta["preview"]["url"], "/api/maps/13/preview.png")
        self.assertEqual(repo.preview_file(13).name, "Map13.png")


class QuestRepositoryTests(EditorFixture):
    def test_validation_outline_callback_and_include(self) -> None:
        (self.paths.quests / "Common.txt").write_text("function Helper()\nend\n", encoding="utf-8")
        repo = QuestRepository(self.paths, self.backups)
        content = '#include "Common.txt"\nfunction start()\n\tAsk("hi", "yes", "no", "Next")\nend\nfunction Next()\n\tSay("ok", "End")\nend\n'
        result = repo.validate("Npc.txt", content)
        self.assertTrue(result["ok"], result)
        self.assertIn("start", result["functions"])
        self.assertIn("Next", result["functions"])
        self.assertFalse(result["warnings"])

    def test_missing_callback_warns_and_unclosed_block_errors(self) -> None:
        repo = QuestRepository(self.paths, self.backups)
        content = 'function start()\n Ask("x", "a", "b", "Missing")\n if 1 == 1 then\nend\n'
        result = repo.validate("Bad.txt", content)
        self.assertFalse(result["ok"])
        self.assertTrue(any("Missing" in w for w in result["warnings"]))

    def test_save_preserves_existing_cp950_when_possible(self) -> None:
        path = self.paths.quests / "Npc.txt"
        original = 'function start()\n\tSay("n1你好", "End")\nend\n'
        path.write_bytes(original.encode("cp950"))
        repo = QuestRepository(self.paths, self.backups)
        repo.save("Npc.txt", original.replace("你好", "您好"))
        self.assertEqual(path.read_bytes().decode("cp950").find("您好") >= 0, True)


class AssetRepositoryTests(EditorFixture):
    def test_asset_replace_and_guard(self) -> None:
        path = self.paths.assets / "Monsters" / "a.png"
        path.write_bytes(b"OLD")
        repo = AssetRepository(self.paths, self.backups)
        repo.replace(
            "Monsters/a.png",
            base64.b64encode(b"NEW").decode(),
            source_name="replacement.png",
        )
        self.assertEqual(path.read_bytes(), b"NEW")
        with self.assertRaises(UnsafePathError):
            repo.replace(
                "../server/appsettings.json",
                base64.b64encode(b"BAD").decode(),
                source_name="appsettings.json",
            )

    def test_asset_replace_rejects_wrong_extension_and_missing_target(self) -> None:
        path = self.paths.assets / "Monsters" / "a.png"
        path.write_bytes(b"OLD")
        repo = AssetRepository(self.paths, self.backups)

        with self.assertRaises(ValueError):
            repo.replace(
                "Monsters/a.png",
                base64.b64encode(b"JPEG").decode(),
                source_name="wrong.jpg",
            )
        self.assertEqual(path.read_bytes(), b"OLD")

        with self.assertRaises(FileNotFoundError):
            repo.replace(
                "Monsters/missing.png",
                base64.b64encode(b"NEW").decode(),
                source_name="new.png",
            )


class WebStructureTests(unittest.TestCase):
    def test_all_static_frontend_id_selectors_exist_in_rendered_sources(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        index = (web / "index.html").read_text(encoding="utf-8")
        known_ids = set(re.findall(r'\bid="([A-Za-z0-9_-]+)"', index))
        missing: list[str] = []
        sources: dict[str, str] = {}

        for script_name in (
            "app-core.js",
            "app-health.js",
            "app-skills.js",
            "app-world.js",
            "app-map-editor.js",
            "app-history.js",
            "app-help.js",
            "app-tools.js",
        ):
            source = (web / script_name).read_text(encoding="utf-8")
            sources[script_name] = source
            # Editors render some controls dynamically with innerHTML/template strings.
            known_ids.update(re.findall(r'\bid="([A-Za-z0-9_-]+)"', source))
            known_ids.update(
                re.findall(r"\bfield\('([A-Za-z0-9_-]+)'", source)
            )

        for script_name, source in sources.items():
            for selector_id in re.findall(r"\$\('#([A-Za-z0-9_-]+)'\)", source):
                if selector_id not in known_ids:
                    missing.append(f"{script_name}: #{selector_id}")

        self.assertFalse(
            missing,
            "\nStatic JS id selectors missing from HTML/template sources:\n"
            + "\n".join(sorted(set(missing))),
        )

    def test_frontend_entrypoints_are_wired(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        index = (web / "index.html").read_text(encoding="utf-8")
        for script in ("app-core.js", "app-health.js", "app-skills.js", "app-world.js", "app-map-math.js", "app-map-editor.js", "app-history.js", "app-help.js", "app-tools.js"):
            self.assertIn(f'src="/{script}"', index)
            self.assertTrue((web / script).is_file())
        self.assertTrue((web / "app.css").is_file())

    def test_map_coordinate_math_module_is_loaded_before_editor(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        index = (web / "index.html").read_text(encoding="utf-8")
        math_pos = index.index('src="/app-map-math.js"')
        editor_pos = index.index('src="/app-map-editor.js"')
        self.assertLess(math_pos, editor_pos)
        math_source = (web / "app-map-math.js").read_text(encoding="utf-8")
        map_editor = (web / "app-map-editor.js").read_text(encoding="utf-8")
        self.assertIn("worldToCell", math_source)
        self.assertIn("cellCenter", math_source)
        self.assertIn("SGOMapMath.worldToPercent", map_editor)
        self.assertIn("SGOMapMath.clientToWorld", map_editor)

    def test_intuitive_map_editor_is_wired(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        index = (web / "index.html").read_text(encoding="utf-8")
        map_editor = (web / "app-map-editor.js").read_text(encoding="utf-8")
        self.assertIn('id="map-stage"', index)
        self.assertIn('id="map-enemy-search"', index)
        self.assertIn("startMapMarkerDrag", map_editor)
        self.assertIn("addMapRowAt", map_editor)
        self.assertIn("duplicateMapRows", map_editor)

    def test_friendly_shortcuts_are_wired(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        index = (web / "index.html").read_text(encoding="utf-8")
        skills = (web / "app-skills.js").read_text(encoding="utf-8")
        tools = (web / "app-tools.js").read_text(encoding="utf-8")
        self.assertIn('id="script-snippet"', index)
        self.assertIn("SCRIPT_SNIPPETS", tools)
        self.assertIn("dragover", tools)
        self.assertIn("COMMON_LEVEL_FIELDS", skills)
        self.assertIn("ADVANCED_LEVEL_FIELDS", skills)

    def test_advanced_map_editor_commands_are_wired(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        index = (web / "index.html").read_text(encoding="utf-8")
        map_editor = (web / "app-map-editor.js").read_text(encoding="utf-8")
        workflow = (GAME_EDITOR_ROOT.parent / ".github" / "workflows" / "server-build.yml").read_text(encoding="utf-8")
        for control_id in (
            "map-undo", "map-redo", "map-zoom-in", "map-zoom-out", "map-zoom-fit",
            "map-batch-arm", "map-batch-count", "map-batch-radius", "map-stage-scroller",
        ):
            self.assertIn(f'id="{control_id}"', index)
        for symbol in (
            "undoMapEdit", "redoMapEdit", "copyMapSelection", "pasteMapSelection",
            "startMarqueeSelection", "setMapZoom", "addMapBatchAt", "handleMapKeyboard",
        ):
            self.assertIn(symbol, map_editor)
        self.assertIn("app-map-editor.js", workflow)
        self.assertNotIn("// Maps", (web / "app-world.js").read_text(encoding="utf-8"))

    def test_health_dashboard_and_pre_save_validation_are_wired(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        index = (web / "index.html").read_text(encoding="utf-8")
        health = (web / "app-health.js").read_text(encoding="utf-8")
        map_editor = (web / "app-map-editor.js").read_text(encoding="utf-8")
        editor = (GAME_EDITOR_ROOT / "editor.py").read_text(encoding="utf-8")
        workflow = (GAME_EDITOR_ROOT.parent / ".github" / "workflows" / "server-build.yml").read_text(encoding="utf-8")

        for control_id in (
            "health-refresh", "health-errors", "health-warnings", "health-list",
            "health-search", "health-severity", "health-category",
            "map-validation-modal", "map-validation-apply", "map-validation-confirm",
        ):
            self.assertIn(f'id="{control_id}"', index)
        self.assertIn("/api/health", editor)
        self.assertIn("/api/references/", editor)
        self.assertIn("/validate", editor)
        self.assertIn("validateMapBeforeSave", health)
        self.assertIn("showMapValidation", health)
        self.assertIn("套用", health)
        self.assertIn("commitMapEdit", health)
        self.assertIn("validateMapBeforeSave", map_editor)
        self.assertIn("app-health.js", workflow)
        self.assertIn("force=1", editor)
        self.assertIn("?force=1", (web / "app-world.js").read_text(encoding="utf-8"))
        self.assertIn("?force=1", (web / "app-tools.js").read_text(encoding="utf-8"))

    def test_backup_history_diff_restore_ui_and_api_are_wired(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        index = (web / "index.html").read_text(encoding="utf-8")
        history = (web / "app-history.js").read_text(encoding="utf-8")
        core = (web / "app-core.js").read_text(encoding="utf-8")
        editor = (GAME_EDITOR_ROOT / "editor.py").read_text(encoding="utf-8")
        workflow = (GAME_EDITOR_ROOT.parent / ".github" / "workflows" / "server-build.yml").read_text(encoding="utf-8")

        for control_id in (
            "history-search", "history-refresh", "history-list",
            "history-detail-title", "history-detail-meta", "history-diff", "history-restore",
        ):
            self.assertIn(f'id="{control_id}"', index)
        self.assertIn("data-view=\"history\"", index)
        self.assertIn("case 'history': return loadHistory()", core)
        self.assertIn("/api/history", editor)
        self.assertIn("/api/history/restore", editor)
        self.assertIn("restoreSelectedHistory", history)
        self.assertIn("backupSha256", history)
        self.assertIn("app-history.js", workflow)

    def test_health_filters_are_wired(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        health = (web / "app-health.js").read_text(encoding="utf-8")
        self.assertIn("refreshHealthCategoryOptions", health)
        self.assertIn("health-search", health)
        self.assertIn("health-severity", health)
        self.assertIn("health-category", health)

    def test_onboarding_preview_and_browser_e2e_are_wired(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        index = (web / "index.html").read_text(encoding="utf-8")
        help_js = (web / "app-help.js").read_text(encoding="utf-8")
        map_editor = (web / "app-map-editor.js").read_text(encoding="utf-8")
        tools = (web / "app-tools.js").read_text(encoding="utf-8")
        editor = (GAME_EDITOR_ROOT / "editor.py").read_text(encoding="utf-8")
        workflow = (GAME_EDITOR_ROOT.parent / ".github" / "workflows" / "game-editor-e2e.yml").read_text(encoding="utf-8")
        exporter = (GAME_EDITOR_ROOT.parent / "client" / "tools" / "export_map_previews.gd").read_text(encoding="utf-8")
        e2e = (GAME_EDITOR_ROOT / "tests" / "e2e" / "editor.spec.js").read_text(encoding="utf-8")

        for control_id in ("help-open", "help-modal", "help-dont-show", "map-preview-toggle", "map-show-preview"):
            self.assertIn(f'id="{control_id}"', index)
        self.assertIn("HELP_STORAGE_KEY", help_js)
        self.assertIn("maybeShowFirstRunHelp", help_js)
        self.assertIn("map-preview-layer", map_editor)
        self.assertIn("state.mapShowPreview", tools)
        self.assertIn("/preview.png", editor)
        self.assertIn("Map%02d.scn", exporter)
        self.assertIn("map_bounds.json", exporter)
        self.assertIn("playwright", workflow.lower())
        self.assertIn("map add drag undo redo save and reload", e2e)

    def test_safe_map_preferences_are_persisted_without_unsaved_payloads(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        core = (web / "app-core.js").read_text(encoding="utf-8")
        tools = (web / "app-tools.js").read_text(encoding="utf-8")
        map_editor = (web / "app-map-editor.js").read_text(encoding="utf-8")

        self.assertIn("EDITOR_PREFS_KEY", core)
        self.assertIn("loadEditorPreferences", core)
        self.assertIn("saveEditorPreferences", core)
        self.assertIn("applyEditorPreferencesToControls", core)
        for key in (
            "mapId", "mapTab", "mapSelectedEnemyId",
            "mapShowPreview", "mapShowBlocking", "mapShowSpawners",
            "mapShowLabels", "mapOnlySelectedType", "mapSnapWalkable",
        ):
            self.assertIn(f"'{key}'", core)

        # Never persist editable rows/history/clipboard/dirty state.
        pref_block = core[core.index("const EDITOR_PREF_FIELDS"):core.index("function loadEditorPreferences")]
        for unsafe in ("mapRows", "mapUndo", "mapRedo", "mapClipboard", "mapDirty"):
            self.assertNotIn(unsafe, pref_block)

        self.assertIn("applyEditorPreferencesToControls()", tools)
        self.assertRegex(core, r"\$\$\('\[data-map-tab\]'\)\.forEach")
        self.assertNotRegex(core, r"(?<!\$)\$\('\.segmented \[data-map-tab\]'\)\.forEach")
        self.assertGreaterEqual(tools.count("saveEditorPreferences()"), 6)
        self.assertIn("saveEditorPreferences()", map_editor)

    def test_map_save_uses_optimistic_revision_guard(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        core = (web / "app-core.js").read_text(encoding="utf-8")
        map_editor = (web / "app-map-editor.js").read_text(encoding="utf-8")
        editor = (GAME_EDITOR_ROOT / "editor.py").read_text(encoding="utf-8")
        map_repo = (GAME_EDITOR_ROOT / "sgo_editor" / "map_repo.py").read_text(encoding="utf-8")

        self.assertIn("mapRevision", core)
        self.assertIn("?envelope=1", map_editor)
        self.assertIn("expectedRevision:state.mapRevision", map_editor)
        self.assertIn("mutation_lock", editor)
        self.assertIn("expectedRevision", editor)
        self.assertIn("其他 Editor 分頁或程序修改", editor)
        self.assertIn("spawn_revision", map_repo)

    def test_no_frontend_single_selector_is_used_with_foreach(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        pattern = re.compile(r"(\$+)\([^\n;]+?\)\.forEach")
        offenders: list[str] = []
        for path in sorted(web.glob("app-*.js")):
            source = path.read_text(encoding="utf-8")
            for match in pattern.finditer(source):
                if match.group(1) != "$":
                    offenders.append(f"{path.name}: {match.group(0)}")
        self.assertEqual(offenders, [], "\n".join(offenders))

    def test_collision_aware_map_ui_is_wired(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        index = (web / "index.html").read_text(encoding="utf-8")
        map_editor = (web / "app-map-editor.js").read_text(encoding="utf-8")
        editor = (GAME_EDITOR_ROOT / "editor.py").read_text(encoding="utf-8")
        for control_id in ("map-show-blocking", "map-show-spawners", "map-snap-walkable"):
            self.assertIn(f'id="{control_id}"', index)
        self.assertIn("normalizeMapPoint", map_editor)
        self.assertIn("renderSpawnerZones", map_editor)
        self.assertIn("/blocking.png", map_editor)
        self.assertIn("/editor-meta", editor)
        self.assertIn("/terrain", editor)

    def test_all_editors_guard_unsaved_changes(self) -> None:
        web = GAME_EDITOR_ROOT / "web"
        core = (web / "app-core.js").read_text(encoding="utf-8")
        skills = (web / "app-skills.js").read_text(encoding="utf-8")
        enemies = (web / "app-world.js").read_text(encoding="utf-8")
        tools = (web / "app-tools.js").read_text(encoding="utf-8")
        map_editor = (web / "app-map-editor.js").read_text(encoding="utf-8")

        for flag in ("skillDirty", "enemyDirty", "scriptDirty", "mapDirty"):
            self.assertIn(flag, core)
        self.assertIn("confirmDiscardCurrentChanges", core)
        self.assertIn("state.skillDirty", skills)
        self.assertIn("state.enemyDirty", enemies)
        self.assertIn("state.scriptDirty", tools)
        self.assertIn("beforeunload", map_editor)

    def test_map_tab_buttons_bind_with_query_all_not_single_query(self) -> None:
        tools = (GAME_EDITOR_ROOT / "web" / "app-tools.js").read_text(encoding="utf-8")
        self.assertRegex(tools, r"\$\$\('\[data-map-tab\]'\)\.forEach")
        self.assertNotRegex(tools, r"(?<!\$)\$\('\[data-map-tab\]'\)\.forEach")

    def test_asset_drag_drop_sends_source_name_for_format_guard(self) -> None:
        tools = (GAME_EDITOR_ROOT / "web" / "app-tools.js").read_text(encoding="utf-8")
        editor = (GAME_EDITOR_ROOT / "editor.py").read_text(encoding="utf-8")
        asset_repo = (GAME_EDITOR_ROOT / "sgo_editor" / "asset_repo.py").read_text(encoding="utf-8")
        self.assertIn("sourceName:file.name", tools)
        self.assertIn('body.get("sourceName"', editor)
        self.assertIn("_canonical_extension", asset_repo)

    def test_map_mutation_payload_requires_complete_revision_envelope(self) -> None:
        rows = [{"index": -1, "enemyId": 1, "x": 8, "y": 8, "sourceFlags": 0}]
        payload = {"items": rows, "expectedRevision": "abc123"}
        items, revision, envelope = EditorHandler._map_mutation_payload(payload)
        self.assertIs(items, rows)
        self.assertEqual(revision, "abc123")
        self.assertTrue(envelope)

        for bad in (
            rows,
            {},
            {"expectedRevision": "abc123"},
            {"items": "not-a-list", "expectedRevision": "abc123"},
            {"items": rows},
            {"items": rows, "expectedRevision": ""},
            "not-an-object",
        ):
            with self.subTest(bad=bad):
                with self.assertRaises(ValueError):
                    EditorHandler._map_mutation_payload(bad)

    def test_local_host_header_parser_accepts_only_loopback_hosts(self) -> None:
        accepted = {
            "": "",
            "127.0.0.1": "127.0.0.1",
            "127.0.0.1:8765": "127.0.0.1",
            "localhost": "localhost",
            "localhost:8765": "localhost",
            "::1": "::1",
            "[::1]": "::1",
            "[::1]:8765": "::1",
        }
        for raw, expected in accepted.items():
            with self.subTest(raw=raw):
                self.assertEqual(EditorHandler._parse_host_header(raw), expected)

        non_local = {
            "evil.example": "evil.example",
            "evil.example:8765": "evil.example",
            "[2001:db8::1]:8765": "2001:db8::1",
            "[::evil]:8765": "::evil",
        }
        for raw, expected in non_local.items():
            with self.subTest(raw=raw):
                self.assertEqual(EditorHandler._parse_host_header(raw), expected)

        malformed = (
            "[::1]evil",
            "[::1]:abc",
            "2001:db8::1",
            "[",
        )
        for raw in malformed:
            with self.subTest(raw=raw):
                self.assertIsNone(EditorHandler._parse_host_header(raw))

        handler = object.__new__(EditorHandler)
        for raw in accepted:
            handler.headers = {"Host": raw}
            self.assertTrue(handler._local_host_only(), raw)
        for raw in (*non_local.keys(), *malformed):
            handler.headers = {"Host": raw}
            self.assertFalse(handler._local_host_only(), raw)

    def test_editor_defaults_to_localhost_only(self) -> None:
        source = (GAME_EDITOR_ROOT / "editor.py").read_text(encoding="utf-8")
        self.assertIn('default="127.0.0.1"', source)
        self.assertIn('{"127.0.0.1", "localhost", "::1"}', source)


if __name__ == "__main__":
    unittest.main()
