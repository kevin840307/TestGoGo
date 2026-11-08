from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

GAME_EDITOR_ROOT = Path(__file__).resolve().parents[1]
if str(GAME_EDITOR_ROOT) not in sys.path:
    sys.path.insert(0, str(GAME_EDITOR_ROOT))

from sgo_editor.history_repo import HistoryRepository
from sgo_editor.paths import ProjectPaths
from sgo_editor.storage import BackupManager, UnsafePathError


class HistoryRepositoryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / "client" / "Skills").mkdir(parents=True)
        (self.root / "client" / "Assets").mkdir(parents=True)
        (self.root / "client" / "maps").mkdir(parents=True)
        (self.root / "server").mkdir()
        dat = self.root / "Old" / "服務端" / "Dat"
        (dat / "Enemy").mkdir(parents=True)
        (dat / "Map" / "Map13").mkdir(parents=True)
        (dat / "Quest").mkdir(parents=True)
        (self.root / "game-editor").mkdir()

        self.paths = ProjectPaths.resolve(self.root)
        self.backups = BackupManager(self.paths)
        self.history = HistoryRepository(self.paths, self.backups)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def test_text_backup_list_detail_and_restore(self) -> None:
        target = self.paths.quests / "Npc.txt"
        target.write_text("function start()\n  Say(\"old\")\nend\n", encoding="utf-8")

        self.backups.atomic_write_text(
            target,
            "function start()\n  Say(\"new\")\nend\n",
            reason="script",
        )

        items = self.history.list()
        self.assertEqual(len(items), 1)
        self.assertEqual(items[0]["target"], "Old/服務端/Dat/Quest/Npc.txt")
        self.assertEqual(items[0]["reason"], "script")

        detail = self.history.detail(items[0]["id"])
        self.assertTrue(detail["text"])
        self.assertFalse(detail["same"])
        self.assertIn('-  Say("old")', detail["diff"])
        self.assertIn('+  Say("new")', detail["diff"])

        restored = self.history.restore(items[0]["id"])
        self.assertTrue(restored["ok"])
        self.assertIn('Say("old")', target.read_text(encoding="utf-8"))

        # Restore itself must preserve the version it replaced.
        restore_backups = [
            path for path in self.paths.backups.iterdir()
            if path.is_dir() and path.name.endswith("_restore")
        ]
        self.assertTrue(restore_backups)
        self.assertTrue(any(path.name == "Npc.txt" for path in restore_backups[0].rglob("*") if path.is_file()))

    def test_restore_can_recreate_deleted_managed_file(self) -> None:
        target = self.paths.skills / "123.tres"
        target.write_text("old", encoding="utf-8")
        backup = self.backups.backup(target, "skill")
        self.assertIsNotNone(backup)
        target.unlink()

        item = self.history.list()[0]
        detail = self.history.detail(item["id"])
        self.assertFalse(detail["currentExists"])

        self.history.restore(item["id"])
        self.assertEqual(target.read_text(encoding="utf-8"), "old")

    def test_binary_backup_uses_hashes_not_text_diff(self) -> None:
        target = self.paths.maps / "Map13" / "Enemy.dat"
        target.write_bytes(b"\x00\x01\x02")
        self.backups.atomic_write_bytes(target, b"\x00\x01\x03", reason="map_enemy")

        detail = self.history.detail(self.history.list()[0]["id"])
        self.assertFalse(detail["text"])
        self.assertFalse(detail["same"])
        self.assertEqual(len(detail["backupSha256"]), 64)
        self.assertEqual(len(detail["currentSha256"]), 64)

    def test_query_filters_target_and_reason(self) -> None:
        quest = self.paths.quests / "A.txt"
        skill = self.paths.skills / "1.tres"
        quest.write_text("a", encoding="utf-8")
        skill.write_text("s", encoding="utf-8")
        self.backups.atomic_write_text(quest, "b", reason="quest")
        self.backups.atomic_write_text(skill, "t", reason="skill")

        by_target = self.history.list("A.txt")
        self.assertEqual(len(by_target), 1)
        self.assertTrue(by_target[0]["target"].endswith("/A.txt"))

        by_reason = self.history.list("skill")
        self.assertEqual(len(by_reason), 1)
        self.assertEqual(by_reason[0]["reason"], "skill")

    def test_unmanaged_backup_cannot_be_restored(self) -> None:
        rogue = self.paths.backups / "20261108_120000_000001_evil" / "server" / "appsettings.json"
        rogue.parent.mkdir(parents=True)
        rogue.write_text("bad", encoding="utf-8")
        backup_id = rogue.relative_to(self.paths.backups).as_posix()

        with self.assertRaises(UnsafePathError):
            self.history.detail(backup_id)
        with self.assertRaises(UnsafePathError):
            self.history.restore(backup_id)

    def test_traversal_backup_id_is_rejected(self) -> None:
        with self.assertRaises(UnsafePathError):
            self.history.detail("../server/appsettings.json")


if __name__ == "__main__":
    unittest.main()
