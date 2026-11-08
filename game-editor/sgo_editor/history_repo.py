from __future__ import annotations

import difflib
import hashlib
from datetime import datetime
from pathlib import Path
from typing import Any

from .paths import ProjectPaths
from .storage import BackupManager, UnsafePathError, safe_child


class HistoryRepository:
    """Browse, diff and safely restore editor-created backups."""

    TEXT_EXTENSIONS = {
        ".txt", ".tres", ".csv", ".json", ".gd", ".tscn", ".cfg", ".ini", ".md",
    }
    MAX_TEXT_BYTES = 512 * 1024
    MAX_DIFF_LINES = 1200

    def __init__(self, paths: ProjectPaths, backups: BackupManager):
        self.paths = paths
        self.backups = backups

    def list(self, query: str = "", limit: int = 300) -> list[dict[str, Any]]:
        limit = max(1, min(int(limit), 1000))
        needle = query.strip().casefold()
        root = self.paths.backups
        if not root.is_dir():
            return []

        items: list[dict[str, Any]] = []
        snapshot_dirs = sorted(
            (path for path in root.iterdir() if path.is_dir()),
            key=lambda path: path.name,
            reverse=True,
        )
        for snapshot in snapshot_dirs:
            stamp, reason = self._parse_snapshot_name(snapshot.name)
            for backup_path in sorted(path for path in snapshot.rglob("*") if path.is_file()):
                rel_backup = backup_path.relative_to(root).as_posix()
                target_rel = backup_path.relative_to(snapshot).as_posix()
                if not self._is_managed_target(target_rel):
                    continue
                haystack = f"{target_rel} {reason} {snapshot.name}".casefold()
                if needle and needle not in haystack:
                    continue
                target = self.paths.root / target_rel
                stat = backup_path.stat()
                items.append({
                    "id": rel_backup,
                    "snapshot": snapshot.name,
                    "timestamp": stamp,
                    "reason": reason,
                    "target": target_rel,
                    "size": stat.st_size,
                    "currentExists": target.is_file(),
                    "currentSize": target.stat().st_size if target.is_file() else None,
                })
                if len(items) >= limit:
                    return items
        return items

    def detail(self, backup_id: str) -> dict[str, Any]:
        backup_path, target, target_rel = self._resolve(backup_id)
        backup_bytes = backup_path.read_bytes()
        current_bytes = target.read_bytes() if target.is_file() else b""
        current_exists = target.is_file()

        detail: dict[str, Any] = {
            "id": backup_id,
            "target": target_rel,
            "reason": self._parse_snapshot_name(backup_path.relative_to(self.paths.backups).parts[0])[1],
            "backupSize": len(backup_bytes),
            "currentExists": current_exists,
            "currentSize": len(current_bytes) if current_exists else None,
            "backupSha256": hashlib.sha256(backup_bytes).hexdigest(),
            "currentSha256": hashlib.sha256(current_bytes).hexdigest() if current_exists else None,
            "same": current_exists and backup_bytes == current_bytes,
            "text": False,
            "diff": "",
            "diffTruncated": False,
        }

        if self._is_text_candidate(target_rel, backup_bytes, current_bytes):
            before, before_encoding = self._decode_text(backup_bytes)
            after, after_encoding = self._decode_text(current_bytes) if current_exists else ("", "missing")
            lines = list(difflib.unified_diff(
                before.splitlines(),
                after.splitlines(),
                fromfile=f"backup/{target_rel}",
                tofile=f"current/{target_rel}",
                lineterm="",
                n=3,
            ))
            truncated = len(lines) > self.MAX_DIFF_LINES
            detail.update({
                "text": True,
                "backupEncoding": before_encoding,
                "currentEncoding": after_encoding,
                "diff": "\n".join(lines[: self.MAX_DIFF_LINES]),
                "diffTruncated": truncated,
            })
        return detail

    def restore(self, backup_id: str) -> dict[str, Any]:
        backup_path, target, target_rel = self._resolve(backup_id)
        payload = backup_path.read_bytes()
        self.backups.atomic_write_bytes(target, payload, reason="restore")
        return {
            "ok": True,
            "target": target_rel,
            "size": len(payload),
            "sha256": hashlib.sha256(payload).hexdigest(),
        }

    def _resolve(self, backup_id: str) -> tuple[Path, Path, str]:
        backup_path = safe_child(self.paths.backups, backup_id)
        if not backup_path.is_file():
            raise FileNotFoundError(f"找不到備份：{backup_id}")
        rel = backup_path.relative_to(self.paths.backups)
        if len(rel.parts) < 2:
            raise UnsafePathError("備份路徑格式不正確")
        target_rel = Path(*rel.parts[1:]).as_posix()
        if not self._is_managed_target(target_rel):
            raise UnsafePathError("此備份不屬於 Editor 可還原的資料範圍")
        target = safe_child(self.paths.root, target_rel)
        return backup_path, target, target_rel

    def _is_managed_target(self, target_rel: str) -> bool:
        target = (self.paths.root / target_rel).resolve()
        allowed_dirs = [
            self.paths.skills.resolve(),
            self.paths.assets.resolve(),
            self.paths.maps.resolve(),
            self.paths.quests.resolve(),
        ]
        if target == self.paths.enemy_csv.resolve():
            return True
        for root in allowed_dirs:
            try:
                target.relative_to(root)
                return True
            except ValueError:
                continue
        return False

    @staticmethod
    def _parse_snapshot_name(name: str) -> tuple[str, str]:
        # BackupManager names snapshots YYYYMMDD_HHMMSS_microseconds_reason.
        parts = name.split("_", 3)
        if len(parts) == 4 and parts[0].isdigit() and parts[1].isdigit() and parts[2].isdigit():
            raw = f"{parts[0]}_{parts[1]}_{parts[2]}"
            try:
                dt = datetime.strptime(raw, "%Y%m%d_%H%M%S_%f")
                return dt.isoformat(timespec="seconds"), parts[3] or "edit"
            except ValueError:
                pass
        return "", name

    def _is_text_candidate(self, target_rel: str, before: bytes, after: bytes) -> bool:
        if Path(target_rel).suffix.lower() not in self.TEXT_EXTENSIONS:
            return False
        return len(before) <= self.MAX_TEXT_BYTES and len(after) <= self.MAX_TEXT_BYTES

    @staticmethod
    def _decode_text(payload: bytes) -> tuple[str, str]:
        for encoding in ("utf-8", "cp950"):
            try:
                return payload.decode(encoding), encoding
            except UnicodeDecodeError:
                continue
        return payload.decode("utf-8", errors="replace"), "utf-8/replace"
