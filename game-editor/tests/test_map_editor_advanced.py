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

    def test_spawn_audit_reports_existing_blocking_conflicts_without_mutating(self) -> None:
        cells = bytearray(16)
        cells[5] = 1
        self._write_blocking(bytes(cells))

        enemy_payload = (
            struct.pack("<i", 1)
            + struct.pack("<i", 9)
            + b"1\x00"
            + struct.pack("<ii", 24, 24)
            + bytes(MapRepository.ENEMY_TAIL_SIZE)
        )
        (self.paths.maps / "Map13" / "Enemy.dat").write_bytes(enemy_payload)

        meta = self.repo.get_editor_meta(13)
        self.assertEqual(meta["spawnAudit"]["enemies"]["invalidCount"], 1)
        self.assertEqual(meta["spawnAudit"]["enemies"]["invalidIndexes"], [0])

        rows = self.repo.get_enemy_spawns(13)
        self.assertEqual(rows[0]["x"], 24)
        self.assertEqual(rows[0]["y"], 24)

    def test_repository_rejects_duplicate_or_forged_legacy_source_indexes(self) -> None:
        self._write_blocking(bytes(16))
        enemy_payload = (
            struct.pack("<i", 2)
            + struct.pack("<i", 7) + b"1\x00" + struct.pack("<ii", 24, 24) + bytes(MapRepository.ENEMY_TAIL_SIZE)
            + struct.pack("<i", 8) + b"1\x00" + struct.pack("<ii", 40, 40) + bytes(MapRepository.ENEMY_TAIL_SIZE)
        )
        (self.paths.maps / "Map13" / "Enemy.dat").write_bytes(enemy_payload)
        rows = self.repo.get_enemy_spawns(13)

        duplicate = [dict(rows[0]), dict(rows[1])]
        duplicate[1]["index"] = duplicate[0]["index"]
        with self.assertRaisesRegex(ValueError, "重複 source index"):
            self.repo.save_enemy_spawns(13, duplicate)

        forged = [dict(rows[0]), dict(rows[1])]
        forged[0]["index"] = 12345
        with self.assertRaisesRegex(ValueError, "source index 超出範圍"):
            self.repo.save_enemy_spawns(13, forged)

    def test_repository_rejects_binary_overflow_nul_and_trailing_npc_bytes(self) -> None:
        self._write_blocking(bytes(16))

        enemy_payload = (
            struct.pack("<i", 1)
            + struct.pack("<i", 7)
            + b"1\x00"
            + struct.pack("<ii", 24, 24)
            + bytes(MapRepository.ENEMY_TAIL_SIZE)
        )
        (self.paths.maps / "Map13" / "Enemy.dat").write_bytes(enemy_payload)
        enemy_rows = self.repo.get_enemy_spawns(13)
        overflow = [dict(enemy_rows[0])]
        overflow[0]["x"] = 2 ** 31
        with self.assertRaisesRegex(ValueError, "超出 int32 範圍"):
            self.repo.save_enemy_spawns(13, overflow)

        npc_name = "NPC".encode("cp950")
        npc_payload = (
            struct.pack("<i", 1)
            + struct.pack("<iiiB", 1, 8, 8, 0)
            + b"NpcOk.txt\x00"
            + npc_name + b"\x00"
        )
        npc_path = self.paths.maps / "Map13" / "NPC.dat"
        npc_path.write_bytes(npc_payload)
        npc_rows = self.repo.get_npc_spawns(13)

        nul = [dict(npc_rows[0])]
        nul[0]["name"] = "NPC\x00Injected"
        with self.assertRaisesRegex(ValueError, "NUL"):
            self.repo.save_npc_spawns(13, nul)

        npc_path.write_bytes(npc_payload + b"TRAIL")
        with self.assertRaisesRegex(ValueError, "尾端多餘 bytes"):
            self.repo.get_npc_spawns(13)

    def test_spawn_revision_changes_when_binary_data_changes(self) -> None:
        self._write_blocking(bytes(16))
        enemy_path = self.paths.maps / "Map13" / "Enemy.dat"
        enemy_path.write_bytes(struct.pack("<i", 0))
        first = self.repo.spawn_revision(13, "enemies")
        self.assertEqual(len(first), 64)

        enemy_payload = (
            struct.pack("<i", 1)
            + struct.pack("<i", 0)
            + b"1\x00"
            + struct.pack("<ii", 24, 24)
            + bytes(MapRepository.ENEMY_TAIL_SIZE)
        )
        enemy_path.write_bytes(enemy_payload)
        second = self.repo.spawn_revision(13, "enemies")
        self.assertEqual(len(second), 64)
        self.assertNotEqual(first, second)

        with self.assertRaises(ValueError):
            self.repo.spawn_revision(13, "invalid")

    def test_batch_limit_blocks_accidental_huge_editor_request(self) -> None:
        self._write_blocking(bytes(16))
        with self.assertRaises(ValueError):
            self.repo.snap_points(13, [{"x": 0, "y": 0}] * 5001)


if __name__ == "__main__":
    unittest.main()
