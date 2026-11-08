from __future__ import annotations

import re
import struct
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .paths import ProjectPaths
from .storage import BackupManager


@dataclass
class _EnemyRaw:
    source_flags: int
    enemy_id: int
    x: int
    y: int
    tail: bytes


@dataclass
class _NpcRaw:
    npc_type_id: int
    x: int
    y: int
    reserved: int
    script: str
    name_bytes: bytes


class MapRepository:
    ENEMY_TAIL_SIZE = 207

    def __init__(self, paths: ProjectPaths, backups: BackupManager):
        self.paths = paths
        self.backups = backups

    def list_maps(self) -> list[dict[str, Any]]:
        result: list[dict[str, Any]] = []
        for path in sorted(self.paths.maps.glob("Map*"), key=lambda p: p.name):
            if not path.is_dir():
                continue
            match = re.fullmatch(r"Map(\d+)", path.name, re.IGNORECASE)
            if not match:
                continue
            map_id = int(match.group(1))
            result.append({
                "id": map_id,
                "name": path.name,
                "enemyCount": len(self._read_enemies(path / "Enemy.dat")) if (path / "Enemy.dat").exists() else 0,
                "npcCount": len(self._read_npcs(path / "NPC.dat")) if (path / "NPC.dat").exists() else 0,
            })
        return result

    def _map_dir(self, map_id: int) -> Path:
        path = self.paths.maps / f"Map{int(map_id):02d}"
        if not path.exists():
            raise FileNotFoundError(f"找不到地圖 Map{int(map_id):02d}")
        return path

    @staticmethod
    def _read_cstring(data: bytes, offset: int, encoding: str) -> tuple[str, bytes, int]:
        end = data.find(b"\x00", offset)
        if end < 0:
            raise ValueError("字串缺少結尾 NUL")
        raw = data[offset:end]
        return raw.decode(encoding, errors="strict"), raw, end + 1

    def _read_enemies(self, path: Path) -> list[_EnemyRaw]:
        if not path.exists():
            return []
        data = path.read_bytes()
        if len(data) < 4:
            raise ValueError(f"Enemy.dat 太短: {path}")
        count = struct.unpack_from("<i", data, 0)[0]
        if count < 0 or count > 100000:
            raise ValueError(f"Enemy.dat count 異常: {count}")
        offset = 4
        result: list[_EnemyRaw] = []
        for index in range(count):
            if offset + 4 > len(data):
                raise ValueError(f"Enemy.dat record {index} 不完整")
            source_flags = struct.unpack_from("<i", data, offset)[0]
            offset += 4
            enemy_text, _, offset = self._read_cstring(data, offset, "ascii")
            try:
                enemy_id = int(enemy_text)
            except ValueError as exc:
                raise ValueError(f"Enemy.dat record {index} enemy id 無效: {enemy_text}") from exc
            if offset + 8 + self.ENEMY_TAIL_SIZE > len(data):
                raise ValueError(f"Enemy.dat record {index} 不完整")
            x, y = struct.unpack_from("<ii", data, offset)
            offset += 8
            tail = data[offset:offset + self.ENEMY_TAIL_SIZE]
            offset += self.ENEMY_TAIL_SIZE
            result.append(_EnemyRaw(source_flags, enemy_id, x, y, tail))
        if offset != len(data):
            raise ValueError(f"Enemy.dat 有 {len(data) - offset} 個尾端多餘 bytes")
        return result

    def get_enemy_spawns(self, map_id: int) -> list[dict[str, Any]]:
        records = self._read_enemies(self._map_dir(map_id) / "Enemy.dat")
        return [
            {"index": i, "enemyId": r.enemy_id, "x": r.x, "y": r.y, "sourceFlags": r.source_flags}
            for i, r in enumerate(records)
        ]

    def save_enemy_spawns(self, map_id: int, items: list[dict[str, Any]]) -> list[dict[str, Any]]:
        if len(items) > 10000:
            raise ValueError("單張地圖怪物出生點不可超過 10000")
        path = self._map_dir(map_id) / "Enemy.dat"
        old = self._read_enemies(path)
        records: list[_EnemyRaw] = []
        for item in items:
            idx = int(item.get("index", -1))
            tail = old[idx].tail if 0 <= idx < len(old) else bytes(self.ENEMY_TAIL_SIZE)
            source_flags = int(item.get("sourceFlags", old[idx].source_flags if 0 <= idx < len(old) else 0))
            enemy_id = int(item.get("enemyId", 0))
            if enemy_id <= 0:
                raise ValueError("Enemy ID 必須大於 0")
            records.append(_EnemyRaw(source_flags, enemy_id, int(item.get("x", 0)), int(item.get("y", 0)), tail))
        payload = bytearray(struct.pack("<i", len(records)))
        for r in records:
            payload += struct.pack("<i", r.source_flags)
            payload += str(r.enemy_id).encode("ascii") + b"\x00"
            payload += struct.pack("<ii", r.x, r.y)
            payload += r.tail
        self.backups.atomic_write_bytes(path, bytes(payload), "map_enemy")
        return self.get_enemy_spawns(map_id)

    def _read_npcs(self, path: Path) -> list[_NpcRaw]:
        if not path.exists():
            return []
        data = path.read_bytes()
        if len(data) < 4:
            raise ValueError(f"NPC.dat 太短: {path}")
        count = struct.unpack_from("<i", data, 0)[0]
        if count < 0 or count > 100000:
            raise ValueError(f"NPC.dat count 異常: {count}")
        offset = 4
        result: list[_NpcRaw] = []
        for index in range(count):
            if offset + 13 > len(data):
                raise ValueError(f"NPC.dat record {index} 不完整")
            npc_type_id, x, y = struct.unpack_from("<iii", data, offset)
            offset += 12
            reserved = data[offset]
            offset += 1
            script, _, offset = self._read_cstring(data, offset, "ascii")
            _, name_bytes, offset = self._read_cstring(data, offset, "cp950")
            result.append(_NpcRaw(npc_type_id, x, y, reserved, script, name_bytes))
        return result

    def get_npc_spawns(self, map_id: int) -> list[dict[str, Any]]:
        records = self._read_npcs(self._map_dir(map_id) / "NPC.dat")
        return [
            {
                "index": i,
                "npcTypeId": r.npc_type_id,
                "x": r.x,
                "y": r.y,
                "script": r.script,
                "name": r.name_bytes.decode("cp950", errors="replace"),
            }
            for i, r in enumerate(records)
        ]

    def save_npc_spawns(self, map_id: int, items: list[dict[str, Any]]) -> list[dict[str, Any]]:
        if len(items) > 10000:
            raise ValueError("單張地圖 NPC 不可超過 10000")
        path = self._map_dir(map_id) / "NPC.dat"
        old = self._read_npcs(path)
        payload = bytearray(struct.pack("<i", len(items)))
        for item in items:
            idx = int(item.get("index", -1))
            reserved = old[idx].reserved if 0 <= idx < len(old) else 0
            npc_type_id = int(item.get("npcTypeId", 0))
            if npc_type_id <= 0:
                raise ValueError("NPC Type ID 必須大於 0")
            script = str(item.get("script", "")).strip()
            name = str(item.get("name", "")).strip()
            if not script or not name:
                raise ValueError("NPC Script 與 Name 不可為空")
            try:
                script_bytes = script.encode("ascii")
                name_bytes = name.encode("cp950")
            except UnicodeEncodeError as exc:
                raise ValueError(f"NPC 名稱/腳本包含舊格式無法編碼的文字: {exc}") from exc
            payload += struct.pack("<iiiB", npc_type_id, int(item.get("x", 0)), int(item.get("y", 0)), reserved)
            payload += script_bytes + b"\x00"
            payload += name_bytes + b"\x00"
        self.backups.atomic_write_bytes(path, bytes(payload), "map_npc")
        return self.get_npc_spawns(map_id)
