from __future__ import annotations

from typing import Any

from .paths import ProjectPaths
from .storage import BackupManager


class EnemyRepository:
    MIN_COLUMNS = 180

    def __init__(self, paths: ProjectPaths, backups: BackupManager):
        self.paths = paths
        self.backups = backups

    def _read_rows(self) -> list[list[str]]:
        path = self.paths.enemy_csv
        if not path.exists():
            return []
        text = path.read_text(encoding="cp950", errors="strict")
        return [line.split(",") for line in text.splitlines() if line.strip()]

    def _write_rows(self, rows: list[list[str]]) -> None:
        payload = "\r\n".join(",".join(cols) for cols in rows) + ("\r\n" if rows else "")
        self.backups.atomic_write_text(self.paths.enemy_csv, payload, "cp950", "enemy")

    @staticmethod
    def _summary(cols: list[str]) -> dict[str, Any]:
        def as_int(index: int, default: int = 0) -> int:
            try:
                return int(cols[index].strip())
            except (IndexError, ValueError):
                return default

        return {
            "id": as_int(0),
            "name": cols[2].strip() if len(cols) > 2 else "",
            "level": as_int(3, 1),
            "sprite": cols[6].strip() if len(cols) > 6 else "",
            "maxHp": as_int(9, 1),
        }

    def list(self, query: str = "") -> list[dict[str, Any]]:
        q = query.strip().casefold()
        result: list[dict[str, Any]] = []
        for row in self._read_rows():
            item = self._summary(row)
            if q and q not in str(item["id"]) and q not in item["name"].casefold() and q not in item["sprite"].casefold():
                continue
            result.append(item)
        return result

    def get(self, enemy_id: int) -> dict[str, Any]:
        enemy_id = int(enemy_id)
        for row in self._read_rows():
            item = self._summary(row)
            if item["id"] == enemy_id:
                return {**item, "columnCount": len(row)}
        raise FileNotFoundError(f"找不到怪物 {enemy_id}")

    def save(self, enemy_id: int, data: dict[str, Any]) -> dict[str, Any]:
        enemy_id = int(enemy_id)
        if enemy_id <= 0:
            raise ValueError("怪物 ID 必須大於 0")
        name = str(data.get("name", "")).strip()
        sprite = str(data.get("sprite", "")).strip()
        if not name:
            raise ValueError("怪物名稱不可為空")
        if "," in name or "," in sprite or "\r" in name or "\n" in name:
            raise ValueError("名稱與 Sprite 不可包含逗號或換行")
        level = max(1, int(data.get("level", 1) or 1))
        max_hp = max(1, int(data.get("maxHp", 1) or 1))
        clone_from = data.get("cloneFromId")

        rows = self._read_rows()
        index = next((i for i, row in enumerate(rows) if self._summary(row)["id"] == enemy_id), None)
        if index is None:
            template: list[str] | None = None
            if clone_from not in (None, ""):
                clone_id = int(clone_from)
                template = next((list(row) for row in rows if self._summary(row)["id"] == clone_id), None)
                if template is None:
                    raise ValueError(f"找不到要複製的怪物 {clone_id}")
            if template is None:
                template = [""] * self.MIN_COLUMNS
                template[1] = str(enemy_id)
            row = template
            rows.append(row)
            index = len(rows) - 1
        else:
            row = rows[index]

        if len(row) < self.MIN_COLUMNS:
            row.extend([""] * (self.MIN_COLUMNS - len(row)))
        row[0] = str(enemy_id)
        if not row[1].strip():
            row[1] = str(enemy_id)
        row[2] = name
        row[3] = str(level)
        row[6] = sprite
        row[9] = str(max_hp)
        rows[index] = row
        self._write_rows(rows)
        return self.get(enemy_id)

    def delete(self, enemy_id: int) -> None:
        enemy_id = int(enemy_id)
        rows = self._read_rows()
        filtered = [row for row in rows if self._summary(row)["id"] != enemy_id]
        if len(filtered) == len(rows):
            raise FileNotFoundError(f"找不到怪物 {enemy_id}")
        self._write_rows(filtered)
