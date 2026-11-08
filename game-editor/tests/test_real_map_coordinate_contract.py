from __future__ import annotations

import json
import math
import re
import shutil
import struct
import sys
import tempfile
import unittest
from pathlib import Path

GAME_EDITOR_ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = GAME_EDITOR_ROOT.parent
if str(GAME_EDITOR_ROOT) not in sys.path:
    sys.path.insert(0, str(GAME_EDITOR_ROOT))

from sgo_editor.map_repo import MapRepository
from sgo_editor.paths import ProjectPaths
from sgo_editor.storage import BackupManager


class RealMapCoordinateContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.paths = ProjectPaths.resolve(REPO_ROOT)
        cls.repo = MapRepository(cls.paths, BackupManager(cls.paths))

    def test_client_server_editor_use_same_grid_contract(self) -> None:
        client = (REPO_ROOT / "client" / "scripts" / "legacy_pathfinder.gd").read_text(encoding="utf-8")
        server = (REPO_ROOT / "server" / "Infrastructure" / "WorldData" / "BlockingMapService.cs").read_text(encoding="utf-8")

        client_cell = int(re.search(r"const CELL_SIZE:\s*int\s*=\s*(\d+)", client).group(1))
        client_mask = int(re.search(r"const BLOCK_MASK:\s*int\s*=\s*(0x[0-9A-Fa-f]+)", client).group(1), 16)
        server_cell = int(re.search(r"public const int CellSize\s*=\s*(\d+)", server).group(1))
        server_mask = int(re.search(r"BlockingMask\s*=\s*(0x[0-9A-Fa-f]+)", server).group(1), 16)
        server_special = int(re.search(r"SpecialTerrainFlag\s*=\s*(0x[0-9A-Fa-f]+)", server).group(1), 16)

        self.assertEqual(client_cell, MapRepository.CELL_SIZE)
        self.assertEqual(server_cell, MapRepository.CELL_SIZE)
        self.assertEqual(client_mask, MapRepository.BLOCKING_MASK)
        self.assertEqual(server_mask, MapRepository.BLOCKING_MASK)
        self.assertEqual(server_special, MapRepository.SPECIAL_TERRAIN_FLAG)
        self.assertIn("flags == 3", client)
        self.assertIn("floori(pos.x / float(CELL_SIZE))", client)
        self.assertIn("floori(pos.y / float(CELL_SIZE))", client)
        self.assertIn("Math.Floor(worldX / CellSize)", server)
        self.assertIn("Math.Floor(worldY / CellSize)", server)

    def test_real_blocking_files_and_map_bounds_share_world_coordinate_space(self) -> None:
        bounds_path = REPO_ROOT / "client" / "maps" / "map_bounds.json"
        bounds = json.loads(bounds_path.read_text(encoding="utf-8"))
        checked = 0

        for blocking_path in sorted(self.paths.maps.glob("Map*/Blocking.dat")):
            match = re.fullmatch(r"Map(\d+)", blocking_path.parent.name, re.IGNORECASE)
            if not match:
                continue
            map_id = int(match.group(1))
            raw = blocking_path.read_bytes()
            self.assertGreaterEqual(len(raw), 8, blocking_path.as_posix())
            width, height = struct.unpack_from("<ii", raw, 0)
            self.assertGreater(width, 0, blocking_path.as_posix())
            self.assertGreater(height, 0, blocking_path.as_posix())
            self.assertGreaterEqual(len(raw), 8 + width * height, blocking_path.as_posix())

            if str(map_id) in bounds:
                b = bounds[str(map_id)]
                world_width = width * MapRepository.CELL_SIZE
                world_height = height * MapRepository.CELL_SIZE
                self.assertGreaterEqual(int(b["min_x"]), 0, f"Map{map_id:02d} min_x")
                self.assertGreaterEqual(int(b["min_y"]), 0, f"Map{map_id:02d} min_y")
                self.assertLessEqual(int(b["max_x"]), world_width, f"Map{map_id:02d} max_x exceeds Blocking grid")
                self.assertLessEqual(int(b["max_y"]), world_height, f"Map{map_id:02d} max_y exceeds Blocking grid")
                self.assertLess(int(b["min_x"]), int(b["max_x"]))
                self.assertLess(int(b["min_y"]), int(b["max_y"]))
            checked += 1

        self.assertGreaterEqual(checked, 10, "expected real Blocking.dat maps in repository")

    def test_real_enemy_and_npc_coordinates_map_to_the_same_blocking_cells(self) -> None:
        checked_spawns = 0
        checked_maps = 0
        failures: list[str] = []

        for blocking_path in sorted(self.paths.maps.glob("Map*/Blocking.dat")):
            match = re.fullmatch(r"Map(\d+)", blocking_path.parent.name, re.IGNORECASE)
            if not match:
                continue
            map_id = int(match.group(1))
            raw = blocking_path.read_bytes()
            if len(raw) < 8:
                continue
            width, height = struct.unpack_from("<ii", raw, 0)
            if width <= 0 or height <= 0 or len(raw) < 8 + width * height:
                continue
            cells = raw[8:8 + width * height]
            checked_maps += 1

            try:
                rows = self.repo.get_enemy_spawns(map_id) + self.repo.get_npc_spawns(map_id)
            except (ValueError, UnicodeError) as exc:
                failures.append(f"Map{map_id:02d} parse failed: {exc}")
                continue

            for row in rows:
                x = int(row["x"])
                y = int(row["y"])
                cell_x = math.floor(x / MapRepository.CELL_SIZE)
                cell_y = math.floor(y / MapRepository.CELL_SIZE)
                if not (0 <= cell_x < width and 0 <= cell_y < height):
                    failures.append(
                        f"Map{map_id:02d} spawn ({x},{y}) -> cell ({cell_x},{cell_y}) outside {width}x{height}"
                    )
                    continue

                terrain = self.repo.get_terrain(map_id, x, y)
                expected_flags = cells[cell_y * width + cell_x] & MapRepository.BLOCKING_MASK
                if terrain.get("cellX") != cell_x or terrain.get("cellY") != cell_y:
                    failures.append(
                        f"Map{map_id:02d} ({x},{y}) editor cell "
                        f"({terrain.get('cellX')},{terrain.get('cellY')}) != ({cell_x},{cell_y})"
                    )
                if terrain.get("flags") != expected_flags:
                    failures.append(
                        f"Map{map_id:02d} ({x},{y}) flags {terrain.get('flags')} != raw {expected_flags}"
                    )
                checked_spawns += 1

        self.assertGreaterEqual(checked_maps, 10)
        self.assertGreater(checked_spawns, 0, "no real Enemy.dat/NPC.dat coordinates were checked")
        self.assertFalse(failures, "\n".join(failures[:40]))

    def test_real_spawn_audit_matches_raw_blocking_conflicts(self) -> None:
        checked = 0
        for blocking_path in sorted(self.paths.maps.glob("Map*/Blocking.dat")):
            match = re.fullmatch(r"Map(\\d+)", blocking_path.parent.name, re.IGNORECASE)
            if not match:
                continue
            map_id = int(match.group(1))
            meta = self.repo.get_editor_meta(map_id)
            if not meta["blocking"]["hasBlocking"]:
                continue

            for kind, rows in (
                ("enemies", self.repo.get_enemy_spawns(map_id)),
                ("npcs", self.repo.get_npc_spawns(map_id)),
            ):
                expected = [
                    index
                    for index, row in enumerate(rows)
                    if not self.repo.get_terrain(map_id, row["x"], row["y"])["walkable"]
                ]
                self.assertEqual(
                    meta["spawnAudit"][kind]["invalidIndexes"],
                    expected,
                    f"Map{map_id:02d} {kind} audit mismatch",
                )
                self.assertEqual(
                    meta["spawnAudit"][kind]["invalidCount"],
                    len(expected),
                )
                checked += len(rows)

        self.assertGreater(checked, 0)

    def test_real_blocked_cell_snap_lands_on_client_server_cell_center(self) -> None:
        found = False
        for blocking_path in sorted(self.paths.maps.glob("Map*/Blocking.dat")):
            match = re.fullmatch(r"Map(\d+)", blocking_path.parent.name, re.IGNORECASE)
            if not match:
                continue
            map_id = int(match.group(1))
            raw = blocking_path.read_bytes()
            if len(raw) < 8:
                continue
            width, height = struct.unpack_from("<ii", raw, 0)
            if width <= 2 or height <= 2 or len(raw) < 8 + width * height:
                continue
            cells = raw[8:8 + width * height]

            candidate = None
            for y in range(1, height - 1):
                for x in range(1, width - 1):
                    flags = cells[y * width + x] & MapRepository.BLOCKING_MASK
                    if flags == 0:
                        continue
                    neighbors = (
                        cells[y * width + x - 1] & MapRepository.BLOCKING_MASK,
                        cells[y * width + x + 1] & MapRepository.BLOCKING_MASK,
                        cells[(y - 1) * width + x] & MapRepository.BLOCKING_MASK,
                        cells[(y + 1) * width + x] & MapRepository.BLOCKING_MASK,
                    )
                    if 0 in neighbors:
                        candidate = (x, y)
                        break
                if candidate:
                    break
            if candidate is None:
                continue

            x, y = candidate
            center_x = x * MapRepository.CELL_SIZE + MapRepository.CELL_SIZE // 2
            center_y = y * MapRepository.CELL_SIZE + MapRepository.CELL_SIZE // 2
            result = self.repo.snap_points(map_id, [{"x": center_x, "y": center_y}], snap=True)[0]
            if result.get("unresolved"):
                continue

            self.assertTrue(result["snapped"])
            self.assertEqual(result["x"] % MapRepository.CELL_SIZE, MapRepository.CELL_SIZE // 2)
            self.assertEqual(result["y"] % MapRepository.CELL_SIZE, MapRepository.CELL_SIZE // 2)
            terrain = self.repo.get_terrain(map_id, result["x"], result["y"])
            self.assertTrue(terrain["walkable"])
            self.assertEqual(terrain["flags"], 0)
            found = True
            break

        self.assertTrue(found, "could not find a real blocked cell adjacent to a walkable cell")

    def test_real_map13_enemy_and_npc_round_trip_is_byte_identical(self) -> None:
        source_map = self.paths.maps / "Map13"
        self.assertTrue((source_map / "Enemy.dat").is_file())
        self.assertTrue((source_map / "NPC.dat").is_file())

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "client" / "Skills").mkdir(parents=True)
            (root / "client" / "Assets" / "GameInfo").mkdir(parents=True)
            (root / "client" / "maps").mkdir(parents=True)
            (root / "server").mkdir()
            dat = root / "Old" / "服務端" / "Dat"
            (dat / "Enemy").mkdir(parents=True)
            target_map = dat / "Map" / "Map13"
            target_map.mkdir(parents=True)
            (dat / "Quest").mkdir(parents=True)
            (root / "game-editor").mkdir()

            for name in ("Enemy.dat", "NPC.dat", "Blocking.dat"):
                source = source_map / name
                if source.exists():
                    shutil.copy2(source, target_map / name)

            paths = ProjectPaths.resolve(root)
            repo = MapRepository(paths, BackupManager(paths))

            enemy_path = target_map / "Enemy.dat"
            enemy_before = enemy_path.read_bytes()
            enemy_rows = repo.get_enemy_spawns(13)
            repo.save_enemy_spawns(13, enemy_rows)
            self.assertEqual(enemy_path.read_bytes(), enemy_before)

            npc_path = target_map / "NPC.dat"
            npc_before = npc_path.read_bytes()
            npc_rows = repo.get_npc_spawns(13)
            repo.save_npc_spawns(13, npc_rows)
            self.assertEqual(npc_path.read_bytes(), npc_before)


if __name__ == "__main__":
    unittest.main()
