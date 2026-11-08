from __future__ import annotations

import math
import re
import sys
import unittest
from pathlib import Path

GAME_EDITOR_ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = GAME_EDITOR_ROOT.parent
if str(GAME_EDITOR_ROOT) not in sys.path:
    sys.path.insert(0, str(GAME_EDITOR_ROOT))

from sgo_editor.enemy_repo import EnemyRepository
from sgo_editor.map_repo import MapRepository
from sgo_editor.paths import ProjectPaths
from sgo_editor.quest_repo import QuestRepository
from sgo_editor.skill_repo import LEVEL_FIELDS, ROOT_FIELDS, SkillRepository
from sgo_editor.storage import BackupManager


class RuntimeCoordinateContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.paths = ProjectPaths.resolve(REPO_ROOT)
        cls.backups = BackupManager(cls.paths)
        cls.maps = MapRepository(cls.paths, cls.backups)

    def test_editor_client_server_share_same_blocking_grid_contract(self) -> None:
        server = (
            REPO_ROOT
            / "server"
            / "Infrastructure"
            / "WorldData"
            / "BlockingMapService.cs"
        ).read_text(encoding="utf-8")
        client = (
            REPO_ROOT / "client" / "scripts" / "legacy_pathfinder.gd"
        ).read_text(encoding="utf-8")

        self.assertEqual(self.maps.CELL_SIZE, 16)
        self.assertEqual(self.maps.BLOCKING_MASK, 0x07)
        self.assertEqual(self.maps.SPECIAL_TERRAIN_FLAG, 0x03)

        self.assertIn("public const int CellSize = 16;", server)
        self.assertIn("private const byte BlockingMask = 0x07;", server)
        self.assertIn("private const byte SpecialTerrainFlag = 0x03;", server)
        self.assertIn("Math.Floor(worldX / CellSize)", server)
        self.assertIn("Math.Floor(worldY / CellSize)", server)

        self.assertIn("const CELL_SIZE: int = 16", client)
        self.assertIn("const BLOCK_MASK: int = 0x07", client)
        self.assertIn("floori(pos.x / float(CELL_SIZE))", client)
        self.assertIn("floori(pos.y / float(CELL_SIZE))", client)
        self.assertIn("cell.x * CELL_SIZE", client)
        self.assertIn("cell.y * CELL_SIZE", client)

    def test_real_map13_spawns_use_same_world_xy_and_walkable_cells(self) -> None:
        meta = self.maps.get_editor_meta(13)
        enemies = self.maps.get_enemy_spawns(13)
        npcs = self.maps.get_npc_spawns(13)

        self.assertTrue(enemies, "Map13 should contain real Enemy.dat spawns")
        self.assertTrue(npcs, "Map13 should contain real NPC.dat spawns")
        self.assertTrue(meta["blocking"]["hasBlocking"])

        bounds = meta["bounds"]
        failures: list[str] = []
        for kind, rows in (("enemy", enemies), ("npc", npcs)):
            for index, row in enumerate(rows):
                x = int(row["x"])
                y = int(row["y"])
                terrain = self.maps.get_terrain(13, x, y)
                expected_cell_x = math.floor(x / self.maps.CELL_SIZE)
                expected_cell_y = math.floor(y / self.maps.CELL_SIZE)

                if terrain.get("cellX") != expected_cell_x or terrain.get("cellY") != expected_cell_y:
                    failures.append(
                        f"{kind}[{index}] ({x},{y}) cell mismatch: "
                        f"editor=({terrain.get('cellX')},{terrain.get('cellY')}) "
                        f"expected=({expected_cell_x},{expected_cell_y})"
                    )
                if not terrain.get("walkable"):
                    failures.append(
                        f"{kind}[{index}] ({x},{y}) is not walkable: "
                        f"flags={terrain.get('flags')}"
                    )
                if not (
                    bounds["contentMinX"] <= x <= bounds["contentMaxX"]
                    and bounds["contentMinY"] <= y <= bounds["contentMaxY"]
                ):
                    failures.append(
                        f"{kind}[{index}] ({x},{y}) outside map_bounds content area"
                    )

        self.assertFalse(failures, "\n" + "\n".join(failures[:20]))

    def test_all_real_map_spawn_coordinates_fit_their_blocking_grids(self) -> None:
        failures: list[str] = []
        for map_info in self.maps.list_maps():
            map_id = int(map_info["id"])
            blocking = self.maps._read_blocking(map_id)
            if blocking is None:
                continue
            width, height, _, _ = blocking

            for kind, loader in (
                ("enemy", self.maps.get_enemy_spawns),
                ("npc", self.maps.get_npc_spawns),
            ):
                try:
                    rows = loader(map_id)
                except Exception as exc:
                    failures.append(f"Map{map_id:02d} {kind}: parse failed: {exc}")
                    continue

                for index, row in enumerate(rows):
                    x = int(row["x"])
                    y = int(row["y"])
                    cell_x = math.floor(x / self.maps.CELL_SIZE)
                    cell_y = math.floor(y / self.maps.CELL_SIZE)
                    if not (0 <= cell_x < width and 0 <= cell_y < height):
                        failures.append(
                            f"Map{map_id:02d} {kind}[{index}] ({x},{y}) -> "
                            f"cell ({cell_x},{cell_y}) outside {width}x{height}"
                        )

        self.assertFalse(failures, "\n" + "\n".join(failures[:40]))

    def test_real_map13_keeps_original_free_world_coordinates(self) -> None:
        # Legacy spawn coordinates are not all cell centers. The editor must not
        # silently snap every existing X/Y to the grid; snapping is only for an
        # invalid/blocked placement.
        rows = self.maps.get_enemy_spawns(13) + self.maps.get_npc_spawns(13)
        non_centered = [
            row for row in rows
            if int(row["x"]) % 16 != 8 or int(row["y"]) % 16 != 8
        ]
        self.assertTrue(
            non_centered,
            "real legacy data should demonstrate free world-space coordinates",
        )

        sample = non_centered[0]
        result = self.maps.snap_points(
            13,
            [{"x": sample["x"], "y": sample["y"]}],
            snap=True,
        )[0]
        self.assertFalse(result["snapped"])
        self.assertEqual(result["x"], sample["x"])
        self.assertEqual(result["y"], sample["y"])

    def test_real_map13_blocked_cell_snaps_to_client_server_cell_center(self) -> None:
        blocking = self.maps._read_blocking(13)
        self.assertIsNotNone(blocking)
        width, height, cells, _ = blocking  # type: ignore[misc]

        blocked_cell: tuple[int, int] | None = None
        for index, raw in enumerate(cells):
            flags = raw & self.maps.BLOCKING_MASK
            if flags not in (0, self.maps.SPECIAL_TERRAIN_FLAG):
                blocked_cell = (index % width, index // width)
                break

        self.assertIsNotNone(blocked_cell, "Map13 should contain a blocked cell")
        cell_x, cell_y = blocked_cell  # type: ignore[misc]
        source_x = cell_x * 16 + 8
        source_y = cell_y * 16 + 8

        before = self.maps.get_terrain(13, source_x, source_y)
        self.assertFalse(before["walkable"])

        snapped = self.maps.snap_points(
            13,
            [{"x": source_x, "y": source_y}],
            snap=True,
        )[0]
        self.assertTrue(snapped["snapped"])
        self.assertEqual(snapped["x"] % 16, 8)
        self.assertEqual(snapped["y"] % 16, 8)
        after = self.maps.get_terrain(13, snapped["x"], snapped["y"])
        self.assertTrue(after["walkable"])


class RealGameDataContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.paths = ProjectPaths.resolve(REPO_ROOT)
        cls.backups = BackupManager(cls.paths)

    def test_all_real_skill_tres_files_parse_with_matching_ids_and_levels(self) -> None:
        repo = SkillRepository(self.paths, self.backups)
        skill_paths = sorted(self.paths.skills.glob("*.tres"))
        self.assertGreater(len(skill_paths), 100)

        failures: list[str] = []
        for path in skill_paths:
            if not path.stem.isdigit():
                continue
            try:
                skill = repo.get(int(path.stem))
            except Exception as exc:
                failures.append(f"{path.name}: parse failed: {exc}")
                continue

            if skill["id"] != int(path.stem):
                failures.append(
                    f"{path.name}: resource id={skill['id']} does not match filename"
                )
            if not str(skill["name"]).strip():
                failures.append(f"{path.name}: empty skill name")
            levels = skill["levels"]
            if not levels:
                failures.append(f"{path.name}: no levels")
                continue
            level_numbers = [int(level["level"]) for level in levels]
            if any(level <= 0 for level in level_numbers):
                failures.append(f"{path.name}: non-positive level {level_numbers}")
            if len(level_numbers) != len(set(level_numbers)):
                failures.append(f"{path.name}: duplicate levels {level_numbers}")

        self.assertFalse(failures, "\n" + "\n".join(failures[:30]))

    def test_real_enemy_csv_has_unique_positive_editor_keys(self) -> None:
        repo = EnemyRepository(self.paths, self.backups)
        rows = repo.list()
        self.assertGreater(len(rows), 100)

        failures: list[str] = []
        ids: list[int] = []
        for row in rows:
            enemy_id = int(row["id"])
            ids.append(enemy_id)
            if enemy_id <= 0:
                failures.append(f"invalid enemy id: {enemy_id}")
            if not str(row["name"]).strip():
                failures.append(f"enemy {enemy_id}: empty name")
            if int(row["level"]) < 1:
                failures.append(f"enemy {enemy_id}: invalid level {row['level']}")
            if int(row["maxHp"]) < 1:
                failures.append(f"enemy {enemy_id}: invalid maxHp {row['maxHp']}")

        duplicates = sorted({x for x in ids if ids.count(x) > 1})
        if duplicates:
            failures.append(f"duplicate enemy ids: {duplicates[:20]}")
        self.assertFalse(failures, "\n" + "\n".join(failures[:30]))

    def test_real_quest_scripts_are_decodable_by_editor(self) -> None:
        repo = QuestRepository(self.paths, self.backups)
        scripts = repo.list()
        self.assertGreater(len(scripts), 100)
        decode_errors = [
            f"{item['name']}: {item.get('error')}"
            for item in scripts
            if item.get("error")
        ]
        self.assertFalse(
            decode_errors,
            "\nQuest scripts unreadable as UTF-8/CP950:\n"
            + "\n".join(decode_errors[:30]),
        )

    def test_skill_editor_schema_matches_godot_resource_exports(self) -> None:
        skill_data = (
            REPO_ROOT / "client" / "scripts" / "skill_data.gd"
        ).read_text(encoding="utf-8")
        level_data = (
            REPO_ROOT / "client" / "scripts" / "skill_level_data.gd"
        ).read_text(encoding="utf-8")

        root_exports = set(
            re.findall(r"@export var ([A-Za-z_][A-Za-z0-9_]*)", skill_data)
        )
        level_exports = set(
            re.findall(r"@export var ([A-Za-z_][A-Za-z0-9_]*)", level_data)
        )

        expected_root = set(ROOT_FIELDS) | {"levels"}
        self.assertEqual(
            root_exports,
            expected_root,
            f"SkillData schema drift: editor={sorted(expected_root)} "
            f"client={sorted(root_exports)}",
        )
        self.assertEqual(
            level_exports,
            set(LEVEL_FIELDS),
            f"SkillLevelData schema drift: editor={sorted(LEVEL_FIELDS)} "
            f"client={sorted(level_exports)}",
        )

    def test_quest_editor_encoding_fallback_matches_server_runtime(self) -> None:
        runtime = (
            REPO_ROOT
            / "server"
            / "Infrastructure"
            / "Quest"
            / "QuestRuntime.cs"
        ).read_text(encoding="utf-8")
        editor = (
            GAME_EDITOR_ROOT / "sgo_editor" / "quest_repo.py"
        ).read_text(encoding="utf-8")

        self.assertIn("new UTF8Encoding(false, true)", runtime)
        self.assertIn("Encoding.GetEncoding(950)", runtime)
        self.assertIn('data.decode("utf-8")', editor)
        self.assertIn('data.decode("cp950")', editor)

    def test_health_skill_asset_rules_match_client_runtime_loaders(self) -> None:
        skill_loader = (
            REPO_ROOT / "client" / "scripts" / "skill_loader.gd"
        ).read_text(encoding="utf-8")
        base_entity = (
            REPO_ROOT / "client" / "scripts" / "base_entity.gd"
        ).read_text(encoding="utf-8")
        monster_entity = (
            REPO_ROOT / "client" / "scripts" / "monster_entity.gd"
        ).read_text(encoding="utf-8")
        health = (
            GAME_EDITOR_ROOT / "sgo_editor" / "health_repo.py"
        ).read_text(encoding="utf-8")

        self.assertIn('ICON_PATH = "res://Skills/Icon/ICON_%03d.png"', skill_loader)
        self.assertIn('"Skill_Effect/%s_%s" % [r_prefix, fx_use]', base_entity)
        self.assertIn('EffectLoader.get_effect("Hit_Effect/%s" % fx_hit)', base_entity)
        self.assertIn('"Skill_Effect/" + s_res.fx_use', monster_entity)
        self.assertIn('"Hit_Effect/" + s_res.fx_use', monster_entity)

        for prefix in ("A_", "B_", "R_"):
            self.assertIn(f'f"{prefix}{{effect_name}}"', health)
        self.assertIn('effect("Hit_Effect", effect_name)', health)
        self.assertIn('ICON_{icon_id:03d}.png', health)

    def test_runtime_dat_reader_layout_still_matches_editor_layout(self) -> None:
        enemy_reader = (
            REPO_ROOT
            / "server"
            / "Infrastructure"
            / "GameData"
            / "EnemyDatReader.cs"
        ).read_text(encoding="utf-8")
        npc_reader = (
            REPO_ROOT
            / "server"
            / "Infrastructure"
            / "GameData"
            / "NpcDatReader.cs"
        ).read_text(encoding="utf-8")

        self.assertEqual(MapRepository.ENEMY_TAIL_SIZE, 207)
        self.assertIn("private const int TailSize = 207;", enemy_reader)
        self.assertRegex(
            enemy_reader,
            re.compile(
                r"ReadInt32\(\).*ReadNullTerminatedAscii.*ReadInt32\(\).*"
                r"ReadInt32\(\).*ReadBytes\(TailSize\)",
                re.DOTALL,
            ),
        )
        self.assertIn("Legacy format includes one reserved byte", npc_reader)
        self.assertRegex(
            npc_reader,
            re.compile(
                r"ReadInt32\(\).*ReadInt32\(\).*ReadInt32\(\).*"
                r"ReadByte\(\).*ReadNullTerminatedAscii",
                re.DOTALL,
            ),
        )


    def test_map_preview_exporter_matches_editor_world_bounds_contract(self) -> None:
        exporter = (
            REPO_ROOT / "client" / "tools" / "export_map_previews.gd"
        ).read_text(encoding="utf-8")
        map_repo = (
            GAME_EDITOR_ROOT / "sgo_editor" / "map_repo.py"
        ).read_text(encoding="utf-8")

        self.assertIn('const BOUNDS_PATH := "res://maps/map_bounds.json"', exporter)
        self.assertIn("const CELL_SIZE := 16", exporter)
        self.assertIn('"res://maps/Map%02d.scn"', exporter)
        self.assertIn("floorf(float(data[\"min_x\"]) / CELL_SIZE) * CELL_SIZE", exporter)
        self.assertIn("ceilf(float(data[\"max_x\"]) / CELL_SIZE) * CELL_SIZE", exporter)
        self.assertIn("image.save_png(output_path)", exporter)

        self.assertIn("CELL_SIZE = 16", map_repo)
        self.assertIn("math.floor(min_x / self.CELL_SIZE) * self.CELL_SIZE", map_repo)
        self.assertIn("math.ceil(max_x / self.CELL_SIZE) * self.CELL_SIZE", map_repo)
        self.assertIn(".generated", map_repo)
        self.assertIn("map-previews", map_repo)


if __name__ == "__main__":
    unittest.main()
