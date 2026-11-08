from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path

GAME_EDITOR_ROOT = Path(__file__).resolve().parents[1]
if str(GAME_EDITOR_ROOT) not in sys.path:
    sys.path.insert(0, str(GAME_EDITOR_ROOT))

from sgo_editor.map_repo import MapRepository
from sgo_editor.paths import ProjectPaths
from sgo_editor.storage import BackupManager


class AdvancedMapEditorRepositoryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / "client" / "Skills").mkdir(parents=True)
        (self.root / "client" / "Assets" / "GameInfo").mkdir(parents=True)
        (self.root / "client" / "maps").mkdir(parents=True)
        (self.root / "server").mkdir()
        dat = self.root / "Old" / "服務端" / "Dat"
        (dat / "Enemy").mkdir(parents=True)
        (dat / "Map" / "Map13").mkdir(parents=True)
        (dat / "Quest").mkdir(parents=True)
        (self.root / "game-editor").mkdir()
        self.paths = ProjectPaths.resolve(self.root)
        self.repo = MapRepository(self.paths, BackupManager(self.paths))
        self.blocking = self.paths.maps / "Map13" / "Blocking.dat"

    def tearDown(self) -> None:
        self.temp.cleanup()

    def _write_blocking(self, cells: bytes, extra: bytes = b"") -> None:
        self.blocking.write_bytes(struct.pack("<ii", 4, 4) + cells + extra)

    def test_batch_snap_uses_one_map_and_preserves_unsnapped_mode(self) -> None:
        cells = bytearray(16)
        cells[0] = 1
        cells[5] = 3
        self._write_blocking(bytes(cells))

        snapped = self.repo.snap_points(
            13,
            [{"x": 8, "y": 8}, {"x": 24, "y": 8}, {"x": 24, "y": 24}],
            snap=True,
        )
        self.assertEqual(len(snapped), 3)
        self.assertTrue(snapped[0]["snapped"])
        self.assertTrue(snapped[0]["walkable"])
        self.assertFalse(snapped[1]["snapped"])

        raw = self.repo.snap_points(13, [{"x": 8, "y": 8}], snap=False)
        self.assertEqual(raw[0]["x"], 8)
        self.assertEqual(raw[0]["y"], 8)
        self.assertFalse(raw[0]["walkable"])
        self.assertFalse(raw[0]["snapped"])

    def test_blocking_cache_refreshes_when_source_file_changes(self) -> None:
        cells = bytearray(16)
        cells[0] = 1
        self._write_blocking(bytes(cells))
        first = self.repo.get_terrain(13, 8, 8)
        first_png = self.repo.blocking_overlay_png(13)
        self.assertFalse(first["walkable"])

        cells[0] = 0
        cells[1] = 1
        self._write_blocking(bytes(cells), extra=b"\x00")
        second = self.repo.get_terrain(13, 8, 8)
        second_png = self.repo.blocking_overlay_png(13)
        self.assertTrue(second["walkable"])
        self.assertNotEqual(first_png, second_png)

    def test_invalid_blocking_file_degrades_without_crashing_editor(self) -> None:
        self.blocking.write_bytes(b"")
        meta = self.repo.get_editor_meta(13)
        self.assertFalse(meta["blocking"]["hasBlocking"])
        self.assertIn("太短", meta["blocking"]["error"])
        self.assertIsNone(self.repo._read_blocking(13))

    def test_batch_limit_blocks_accidental_huge_editor_request(self) -> None:
        self._write_blocking(bytes(16))
        with self.assertRaises(ValueError):
            self.repo.snap_points(13, [{"x": 0, "y": 0}] * 5001)


if __name__ == "__main__":
    unittest.main()
