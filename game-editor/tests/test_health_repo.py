from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path

GAME_EDITOR_ROOT = Path(__file__).resolve().parents[1]
if str(GAME_EDITOR_ROOT) not in sys.path:
    sys.path.insert(0, str(GAME_EDITOR_ROOT))

from sgo_editor.enemy_repo import EnemyRepository
from sgo_editor.health_repo import HealthRepository
from sgo_editor.map_repo import MapRepository
from sgo_editor.paths import ProjectPaths
from sgo_editor.quest_repo import QuestRepository
from sgo_editor.skill_repo import SkillRepository
from sgo_editor.storage import BackupManager


class HealthRepositoryTests(unittest.TestCase):
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

        cols = [""] * 180
        cols[0] = cols[1] = "1"
        cols[2] = "存在怪物"
        cols[3] = "1"
        cols[6] = "EN001"
        cols[9] = "100"
        (dat / "Enemy" / "Enemy_Data.csv").write_text(",".join(cols) + "\r\n", encoding="cp950")

        (dat / "Quest" / "NpcOk.txt").write_text(
            'function start()\n\tSay("n1ok", "End")\nend\n',
            encoding="utf-8",
        )
        (dat / "Quest" / "IncludeUser.txt").write_text(
            '#include "NpcOk.txt"\nfunction start()\nend\n',
            encoding="utf-8",
        )

        width = height = 8
        cells = bytearray(width * height)
        cells[1 * width + 1] = 1
        (dat / "Map" / "Map13" / "Blocking.dat").write_bytes(
            struct.pack("<ii", width, height) + bytes(cells)
        )

        enemy_payload = bytearray(struct.pack("<i", 3))
        for enemy_id, x, y in ((1, 24, 24), (99, 40, 40), (1, 24, 24)):
            enemy_payload += struct.pack("<i", 0)
            enemy_payload += str(enemy_id).encode("ascii") + b"\x00"
            enemy_payload += struct.pack("<ii", x, y)
            enemy_payload += bytes(207)
        (dat / "Map" / "Map13" / "Enemy.dat").write_bytes(enemy_payload)

        npc_name = "NPC".encode("cp950")
        npc_payload = bytearray(struct.pack("<i", 2))
        npc_payload += struct.pack("<iiiB", 1, 8, 8, 0) + b"NpcOk.txt\x00" + npc_name + b"\x00"
        npc_payload += struct.pack("<iiiB", 1, 56, 56, 0) + b"Missing.txt\x00" + npc_name + b"\x00"
        (dat / "Map" / "Map13" / "NPC.dat").write_bytes(npc_payload)

        self.paths = ProjectPaths.resolve(self.root)
        backups = BackupManager(self.paths)
        self.enemies = EnemyRepository(self.paths, backups)
        self.maps = MapRepository(self.paths, backups)
        self.quests = QuestRepository(self.paths, backups)
        self.skills = SkillRepository(self.paths, backups)
        self.health = HealthRepository(self.paths, self.enemies, self.maps, self.quests, self.skills)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def test_health_scan_finds_cross_file_reference_and_duplicate_problems(self) -> None:
        report = self.health.scan(force=True)
        categories = [issue["category"] for issue in report["issues"]]
        self.assertIn("missing_enemy", categories)
        self.assertIn("missing_npc_script", categories)
        self.assertIn("duplicate_enemy_spawn", categories)
        self.assertIn("blocking_conflict", categories)
        self.assertGreaterEqual(report["summary"]["errors"], 2)
        self.assertGreaterEqual(report["summary"]["warnings"], 2)

    def test_pre_save_validation_blocks_new_errors_but_only_warns_historical_problem(self) -> None:
        rows = self.maps.get_enemy_spawns(13)
        historical = self.health.validate_map_items(13, "enemies", rows)
        self.assertTrue(any(x["category"] == "missing_enemy" for x in historical["warnings"]))
        self.assertFalse(any(x["category"] == "missing_enemy" for x in historical["errors"]))

        changed = [dict(row) for row in rows]
        changed[0]["enemyId"] = 123456
        result = self.health.validate_map_items(13, "enemies", changed)
        self.assertFalse(result["ok"])
        self.assertTrue(any(x["category"] == "missing_enemy" for x in result["errors"]))

    def test_pre_save_validation_returns_blocking_repair_preview(self) -> None:
        rows = self.maps.get_enemy_spawns(13)
        rows[0] = {**rows[0], "x": 24, "y": 24}
        report = self.health.validate_map_items(13, "enemies", rows)
        blocking = [x for x in report["errors"] if x["category"] == "blocking_conflict"]
        self.assertTrue(blocking)
        self.assertTrue(report["suggestions"])
        suggestion = report["suggestions"][0]
        self.assertEqual(suggestion["from"], {"x": 24, "y": 24})
        self.assertEqual(suggestion["to"]["x"] % 16, 8)
        self.assertEqual(suggestion["to"]["y"] % 16, 8)

    def test_reference_impact_finds_map_and_include_users(self) -> None:
        enemy = self.health.references("enemy", "1")
        self.assertEqual(enemy["count"], 2)
        self.assertTrue(all(x["type"] == "map_enemy" for x in enemy["references"]))

        script = self.health.references("script", "NpcOk.txt")
        types = [x["type"] for x in script["references"]]
        self.assertIn("map_npc", types)
        self.assertIn("quest_include", types)

    def test_health_cache_can_be_invalidated(self) -> None:
        first = self.health.scan(force=True)
        self.assertIs(first, self.health.scan())
        self.health.invalidate()
        second = self.health.scan()
        self.assertIsNot(first, second)


if __name__ == "__main__":
    unittest.main()
