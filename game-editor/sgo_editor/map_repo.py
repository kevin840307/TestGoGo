from __future__ import annotations

import binascii
import hashlib
import json
import math
import re
import struct
import zlib
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
    INT32_MIN = -(2 ** 31)
    INT32_MAX = 2 ** 31 - 1
    ENEMY_TAIL_SIZE = 207
    CELL_SIZE = 16
    BLOCKING_MASK = 0x07
    SPECIAL_TERRAIN_FLAG = 0x03

    def __init__(self, paths: ProjectPaths, backups: BackupManager):
        self.paths = paths
        self.backups = backups
        self._blocking_cache: dict[int, tuple[tuple[int, int], tuple[int, int, bytes, Path]]] = {}
        self._blocking_png_cache: dict[int, tuple[tuple[Any, ...], bytes]] = {}
        self._blocking_errors: dict[int, str] = {}
        self._json_cache: dict[Path, tuple[tuple[int, int], dict[str, Any]]] = {}

    def list_maps(self) -> list[dict[str, Any]]:
        result: list[dict[str, Any]] = []
        map_names = self._map_names()
        for path in sorted(self.paths.maps.glob("Map*"), key=lambda p: p.name):
            if not path.is_dir():
                continue
            match = re.fullmatch(r"Map(\d+)", path.name, re.IGNORECASE)
            if not match:
                continue
            map_id = int(match.group(1))
            result.append({
                "id": map_id,
                "name": map_names.get(map_id) or path.name,
                "technicalName": path.name,
                "enemyCount": self._record_count(path / "Enemy.dat"),
                "npcCount": self._record_count(path / "NPC.dat"),
            })
        return result

    def _map_dir(self, map_id: int) -> Path:
        path = self.paths.maps / f"Map{int(map_id):02d}"
        if not path.exists():
            raise FileNotFoundError(f"找不到地圖 Map{int(map_id):02d}")
        return path

    def spawn_revision(self, map_id: int, tab: str) -> str:
        if tab not in {"enemies", "npcs"}:
            raise ValueError("tab 必須是 enemies 或 npcs")
        filename = "Enemy.dat" if tab == "enemies" else "NPC.dat"
        path = self._map_dir(map_id) / filename
        if not path.exists():
            return "missing"
        return hashlib.sha256(path.read_bytes()).hexdigest()

    def _spawn_config_path(self) -> Path:
        return self.paths.assets / "GameInfo" / "MapSpawnConfig.json"

    def _bounds_path(self) -> Path:
        return self.paths.root / "client" / "maps" / "map_bounds.json"

    def _preview_path(self, map_id: int) -> Path:
        return (
            self.paths.root
            / "game-editor"
            / ".generated"
            / "map-previews"
            / f"Map{int(map_id):02d}.png"
        )

    @staticmethod
    def _png_dimensions(path: Path) -> tuple[int, int] | None:
        try:
            head = path.read_bytes()[:24]
        except OSError:
            return None
        if len(head) < 24 or not head.startswith(b"\x89PNG\r\n\x1a\n"):
            return None
        width, height = struct.unpack(">II", head[16:24])
        if width <= 0 or height <= 0:
            return None
        return width, height

    def preview_file(self, map_id: int) -> Path:
        self._map_dir(map_id)  # Keep the endpoint scoped to real maps.
        path = self._preview_path(map_id)
        if not path.is_file():
            raise FileNotFoundError(
                f"Map{int(map_id):02d} 尚未產生地圖預覽；請執行 game-editor/export_map_previews.bat"
            )
        if self._png_dimensions(path) is None:
            raise ValueError(f"地圖預覽 PNG 無效: {path}")
        return path

    def _read_json(self, path: Path) -> dict[str, Any]:
        if not path.exists():
            self._json_cache.pop(path, None)
            return {}
        try:
            stat = path.stat()
            cache_key = (stat.st_mtime_ns, stat.st_size)
            cached = self._json_cache.get(path)
            if cached is not None and cached[0] == cache_key:
                return cached[1]
            value = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError):
            return {}
        result = value if isinstance(value, dict) else {}
        self._json_cache[path] = (cache_key, result)
        if len(self._json_cache) > 8:
            self._json_cache.pop(next(iter(self._json_cache)))
        return result

    @staticmethod
    def _record_count(path: Path) -> int:
        if not path.exists():
            return 0
        try:
            with path.open("rb") as stream:
                head = stream.read(4)
            if len(head) != 4:
                return 0
            count = struct.unpack("<i", head)[0]
            return count if 0 <= count <= 100000 else 0
        except OSError:
            return 0

    def _map_names(self) -> dict[int, str]:
        config = self._read_json(self._spawn_config_path())
        result: dict[int, str] = {}
        for raw_id, item in config.items():
            if not isinstance(item, dict):
                continue
            try:
                map_id = int(raw_id)
            except (TypeError, ValueError):
                continue
            name = str(item.get("MapName", "")).strip()
            if name:
                result[map_id] = name
        return result

    def _read_blocking(self, map_id: int) -> tuple[int, int, bytes, Path] | None:
        path = self._map_dir(map_id) / "Blocking.dat"
        if not path.exists():
            self._blocking_cache.pop(int(map_id), None)
            self._blocking_png_cache.pop(int(map_id), None)
            self._blocking_errors.pop(int(map_id), None)
            return None

        stat = path.stat()
        cache_key = (stat.st_mtime_ns, stat.st_size)
        cached = self._blocking_cache.get(int(map_id))
        if cached is not None and cached[0] == cache_key:
            return cached[1]

        data = path.read_bytes()
        error: str | None = None
        if len(data) < 8:
            error = f"Blocking.dat 太短（{len(data)} bytes）"
        else:
            width, height = struct.unpack_from("<ii", data, 0)
            if width <= 0 or height <= 0:
                error = f"Blocking.dat 尺寸異常: {width}x{height}"
            else:
                cell_count = width * height
                if cell_count > 50_000_000 or len(data) < 8 + cell_count:
                    error = f"Blocking.dat 資料不完整: {width}x{height}"

        if error is not None:
            self._blocking_cache.pop(int(map_id), None)
            self._blocking_png_cache.pop(int(map_id), None)
            self._blocking_errors[int(map_id)] = error
            return None

        self._blocking_errors.pop(int(map_id), None)

        result = (width, height, data[8:8 + cell_count], path)
        if int(map_id) not in self._blocking_cache and len(self._blocking_cache) >= 8:
            oldest_id = next(iter(self._blocking_cache))
            self._blocking_cache.pop(oldest_id, None)
            self._blocking_png_cache.pop(oldest_id, None)
        self._blocking_cache[int(map_id)] = (cache_key, result)
        self._blocking_png_cache.pop(int(map_id), None)
        return result

    def _editor_bounds(self, map_id: int, blocking: tuple[int, int, bytes, Path] | None = None) -> dict[str, int]:
        bounds_data = self._read_json(self._bounds_path())
        raw = bounds_data.get(str(map_id))
        if isinstance(raw, dict):
            try:
                min_x = int(raw["min_x"])
                min_y = int(raw["min_y"])
                max_x = int(raw["max_x"])
                max_y = int(raw["max_y"])
                if max_x > min_x and max_y > min_y:
                    # Align to Blocking.dat cells so the collision overlay and world
                    # coordinates map pixel-perfectly in the editor.
                    return {
                        "minX": math.floor(min_x / self.CELL_SIZE) * self.CELL_SIZE,
                        "minY": math.floor(min_y / self.CELL_SIZE) * self.CELL_SIZE,
                        "maxX": math.ceil(max_x / self.CELL_SIZE) * self.CELL_SIZE,
                        "maxY": math.ceil(max_y / self.CELL_SIZE) * self.CELL_SIZE,
                        "contentMinX": min_x,
                        "contentMinY": min_y,
                        "contentMaxX": max_x,
                        "contentMaxY": max_y,
                    }
            except (KeyError, TypeError, ValueError):
                pass

        if blocking is not None:
            width, height, _, _ = blocking
            return {
                "minX": 0,
                "minY": 0,
                "maxX": width * self.CELL_SIZE,
                "maxY": height * self.CELL_SIZE,
                "contentMinX": 0,
                "contentMinY": 0,
                "contentMaxX": width * self.CELL_SIZE,
                "contentMaxY": height * self.CELL_SIZE,
            }

        rows = self.get_enemy_spawns(map_id) + self.get_npc_spawns(map_id)
        if rows:
            xs = [int(x["x"]) for x in rows]
            ys = [int(x["y"]) for x in rows]
            return {
                "minX": min(xs) - 256,
                "minY": min(ys) - 256,
                "maxX": max(xs) + 256,
                "maxY": max(ys) + 256,
                "contentMinX": min(xs),
                "contentMinY": min(ys),
                "contentMaxX": max(xs),
                "contentMaxY": max(ys),
            }

        return {
            "minX": 0, "minY": 0, "maxX": 4096, "maxY": 4096,
            "contentMinX": 0, "contentMinY": 0, "contentMaxX": 4096, "contentMaxY": 4096,
        }

    def get_editor_meta(self, map_id: int) -> dict[str, Any]:
        blocking = self._read_blocking(map_id)
        bounds = self._editor_bounds(map_id, blocking)
        config = self._read_json(self._spawn_config_path())
        map_config = config.get(str(map_id), {})
        if not isinstance(map_config, dict):
            map_config = {}

        spawners: list[dict[str, Any]] = []
        raw_spawners = map_config.get("Spawners", [])
        if isinstance(raw_spawners, list):
            for raw in raw_spawners:
                if not isinstance(raw, dict):
                    continue
                rect = raw.get("Rect", {})
                if not isinstance(rect, dict):
                    continue
                try:
                    x1 = int(rect.get("X1", 0))
                    y1 = int(rect.get("Y1", 0))
                    x2 = int(rect.get("X2", 0))
                    y2 = int(rect.get("Y2", 0))
                except (TypeError, ValueError):
                    continue
                if x2 <= x1 or y2 <= y1:
                    continue
                spawners.append({
                    "name": str(raw.get("ZoneName", "")).strip(),
                    "monsterName": str(raw.get("MonsterName", "")).strip(),
                    "maxCount": int(raw.get("MaxCount", 0) or 0),
                    "respawnTime": int(raw.get("RespawnTime", 0) or 0),
                    "x1": x1, "y1": y1, "x2": x2, "y2": y2,
                })

        blocking_meta: dict[str, Any] = {
            "hasBlocking": blocking is not None,
            "cellSize": self.CELL_SIZE,
            "error": self._blocking_errors.get(int(map_id), ""),
        }
        spawn_audit: dict[str, Any] = {
            "enemies": {"invalidIndexes": [], "invalidCount": 0},
            "npcs": {"invalidIndexes": [], "invalidCount": 0},
        }
        if blocking is not None:
            width, height, _, _ = blocking
            blocking_meta.update({"width": width, "height": height})
            for key, rows in (
                ("enemies", self.get_enemy_spawns(map_id)),
                ("npcs", self.get_npc_spawns(map_id)),
            ):
                invalid_indexes: list[int] = []
                for index, row in enumerate(rows):
                    terrain = self._terrain_from_blocking(
                        blocking,
                        float(row["x"]),
                        float(row["y"]),
                    )
                    if not terrain.get("walkable"):
                        invalid_indexes.append(index)
                spawn_audit[key] = {
                    "invalidIndexes": invalid_indexes,
                    "invalidCount": len(invalid_indexes),
                }

        preview_path = self._preview_path(map_id)
        preview_dimensions = self._png_dimensions(preview_path) if preview_path.is_file() else None
        preview_meta: dict[str, Any] = {
            "available": preview_dimensions is not None,
            "url": f"/api/maps/{int(map_id)}/preview.png" if preview_dimensions is not None else "",
        }
        if preview_dimensions is not None:
            preview_meta.update({
                "width": preview_dimensions[0],
                "height": preview_dimensions[1],
                "mtimeNs": preview_path.stat().st_mtime_ns,
            })

        return {
            "id": int(map_id),
            "name": str(map_config.get("MapName", "")).strip() or f"Map{int(map_id):02d}",
            "bounds": bounds,
            "blocking": blocking_meta,
            "spawnAudit": spawn_audit,
            "spawners": spawners,
            "preview": preview_meta,
        }

    @classmethod
    def world_to_cell(cls, world_x: float, world_y: float) -> tuple[int, int]:
        """Match Client/Server collision addressing exactly: floor(world / 16)."""
        return (
            math.floor(world_x / cls.CELL_SIZE),
            math.floor(world_y / cls.CELL_SIZE),
        )

    @classmethod
    def cell_center(cls, cell_x: int, cell_y: int) -> tuple[int, int]:
        """Return the same 16x16 cell center used by the client pathfinder."""
        half = cls.CELL_SIZE // 2
        return (
            cell_x * cls.CELL_SIZE + half,
            cell_y * cls.CELL_SIZE + half,
        )

    def _terrain_from_blocking(
        self,
        blocking: tuple[int, int, bytes, Path] | None,
        world_x: float,
        world_y: float,
    ) -> dict[str, Any]:
        if blocking is None:
            return {
                "hasBlocking": False,
                "walkable": True,
                "special": False,
                "flags": 0,
                "x": world_x,
                "y": world_y,
                "nearest": {
                    "x": int(round(world_x)),
                    "y": int(round(world_y)),
                },
            }

        width, height, cells, _ = blocking
        cell_x, cell_y = self.world_to_cell(world_x, world_y)

        def flags_at(x: int, y: int) -> int:
            if x < 0 or y < 0 or x >= width or y >= height:
                return 255
            return cells[y * width + x] & self.BLOCKING_MASK

        flags = flags_at(cell_x, cell_y)
        result: dict[str, Any] = {
            "hasBlocking": True,
            "cellX": cell_x,
            "cellY": cell_y,
            "flags": flags,
            "walkable": flags == 0,
            "special": flags == self.SPECIAL_TERRAIN_FLAG,
        }
        if flags == 0:
            result["nearest"] = {
                "x": int(round(world_x)),
                "y": int(round(world_y)),
                "cellX": cell_x,
                "cellY": cell_y,
            }
            return result

        for radius in range(1, 17):
            candidates: list[tuple[int, int]] = []
            for dx in range(-radius, radius + 1):
                candidates.append((cell_x + dx, cell_y - radius))
                candidates.append((cell_x + dx, cell_y + radius))
            for dy in range(-radius + 1, radius):
                candidates.append((cell_x - radius, cell_y + dy))
                candidates.append((cell_x + radius, cell_y + dy))
            for x, y in candidates:
                if flags_at(x, y) == 0:
                    center_x, center_y = self.cell_center(x, y)
                    result["nearest"] = {
                        "x": center_x,
                        "y": center_y,
                        "cellX": x,
                        "cellY": y,
                    }
                    return result
        result["nearest"] = None
        return result

    def get_terrain(self, map_id: int, world_x: float, world_y: float) -> dict[str, Any]:
        return self._terrain_from_blocking(
            self._read_blocking(map_id),
            world_x,
            world_y,
        )

    def snap_points(self, map_id: int, points: list[dict[str, Any]], snap: bool = True) -> list[dict[str, Any]]:
        if len(points) > 5000:
            raise ValueError("單次座標檢查不可超過 5000 點")

        # A group drag / batch placement may contain many points. Read/stat
        # Blocking.dat once for the whole request instead of once per point.
        blocking = self._read_blocking(map_id)
        result: list[dict[str, Any]] = []
        for point in points:
            x = float(point.get("x", 0))
            y = float(point.get("y", 0))
            terrain = self._terrain_from_blocking(blocking, x, y)
            if terrain.get("walkable") or not snap:
                result.append({
                    "x": int(round(x)),
                    "y": int(round(y)),
                    "walkable": bool(terrain.get("walkable")),
                    "special": bool(terrain.get("special")),
                    "snapped": False,
                })
                continue

            nearest = terrain.get("nearest")
            if isinstance(nearest, dict):
                result.append({
                    "x": int(nearest["x"]),
                    "y": int(nearest["y"]),
                    "walkable": True,
                    "special": False,
                    "snapped": True,
                })
            else:
                result.append({
                    "x": int(round(x)),
                    "y": int(round(y)),
                    "walkable": False,
                    "special": bool(terrain.get("special")),
                    "snapped": False,
                    "unresolved": True,
                })
        return result

    @staticmethod
    def _png_chunk(kind: bytes, payload: bytes) -> bytes:
        return (
            struct.pack(">I", len(payload))
            + kind
            + payload
            + struct.pack(">I", binascii.crc32(kind + payload) & 0xFFFFFFFF)
        )

    def blocking_overlay_png(self, map_id: int) -> bytes:
        blocking = self._read_blocking(map_id)
        if blocking is None:
            raise FileNotFoundError(f"Map{int(map_id):02d} 沒有 Blocking.dat")

        width, height, cells, path = blocking
        bounds = self._editor_bounds(map_id, blocking)
        start_x = max(0, int(bounds["minX"]) // self.CELL_SIZE)
        start_y = max(0, int(bounds["minY"]) // self.CELL_SIZE)
        end_x = min(width, math.ceil(int(bounds["maxX"]) / self.CELL_SIZE))
        end_y = min(height, math.ceil(int(bounds["maxY"]) / self.CELL_SIZE))
        out_w = max(1, end_x - start_x)
        out_h = max(1, end_y - start_y)

        cache_key = (
            path.stat().st_mtime_ns,
            start_x, start_y, end_x, end_y,
        )
        cached = self._blocking_png_cache.get(int(map_id))
        if cached is not None and cached[0] == cache_key:
            return cached[1]

        raw = bytearray()
        for y in range(start_y, end_y):
            raw.append(0)  # PNG filter: None
            base = y * width
            for x in range(start_x, end_x):
                flags = cells[base + x] & self.BLOCKING_MASK
                if flags == 0:
                    raw.extend((0, 0, 0, 0))
                elif flags == self.SPECIAL_TERRAIN_FLAG:
                    raw.extend((244, 198, 107, 105))
                else:
                    raw.extend((255, 112, 122, 145))

        signature = b"\x89PNG\r\n\x1a\n"
        ihdr = struct.pack(">IIBBBBB", out_w, out_h, 8, 6, 0, 0, 0)
        png = (
            signature
            + self._png_chunk(b"IHDR", ihdr)
            + self._png_chunk(b"IDAT", zlib.compress(bytes(raw), 6))
            + self._png_chunk(b"IEND", b"")
        )
        if int(map_id) not in self._blocking_png_cache and len(self._blocking_png_cache) >= 8:
            self._blocking_png_cache.pop(next(iter(self._blocking_png_cache)), None)
        self._blocking_png_cache[int(map_id)] = (cache_key, png)
        return png

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
        self._validate_source_indexes(items, len(old), "Enemy.dat")
        records: list[_EnemyRaw] = []
        for item in items:
            idx = int(item.get("index", -1))
            tail = old[idx].tail if 0 <= idx < len(old) else bytes(self.ENEMY_TAIL_SIZE)
            source_flags = self._int32(
                item.get("sourceFlags", old[idx].source_flags if 0 <= idx < len(old) else 0),
                "Enemy sourceFlags",
            )
            enemy_id = self._int32(item.get("enemyId", 0), "Enemy ID")
            if enemy_id <= 0:
                raise ValueError("Enemy ID 必須大於 0")
            x = self._int32(item.get("x", 0), "Enemy X")
            y = self._int32(item.get("y", 0), "Enemy Y")
            records.append(_EnemyRaw(source_flags, enemy_id, x, y, tail))
        payload = bytearray(struct.pack("<i", len(records)))
        for r in records:
            payload += struct.pack("<i", r.source_flags)
            payload += str(r.enemy_id).encode("ascii") + b"\x00"
            payload += struct.pack("<ii", r.x, r.y)
            payload += r.tail
        self.backups.atomic_write_bytes(path, bytes(payload), "map_enemy")
        return self.get_enemy_spawns(map_id)

    @classmethod
    def _int32(cls, value: Any, label: str) -> int:
        try:
            result = int(value)
        except (TypeError, ValueError) as exc:
            raise ValueError(f"{label} 必須是整數") from exc
        if result < cls.INT32_MIN or result > cls.INT32_MAX:
            raise ValueError(
                f"{label} 超出 int32 範圍: {result}；"
                f"合法範圍 {cls.INT32_MIN}..{cls.INT32_MAX}"
            )
        return result

    @staticmethod
    def _validate_source_indexes(items: list[dict[str, Any]], old_count: int, label: str) -> None:
        seen: set[int] = set()
        for row_index, item in enumerate(items):
            try:
                source_index = int(item.get("index", -1))
            except (TypeError, ValueError) as exc:
                raise ValueError(f"{label} row {row_index + 1} index 必須是整數") from exc
            if source_index == -1:
                continue
            if source_index < 0 or source_index >= old_count:
                raise ValueError(
                    f"{label} row {row_index + 1} source index 超出範圍: {source_index}；"
                    f"合法值為 -1 或 0..{max(-1, old_count - 1)}"
                )
            if source_index in seen:
                raise ValueError(
                    f"{label} 有重複 source index {source_index}；"
                    "這可能錯用 legacy opaque/reserved metadata"
                )
            seen.add(source_index)

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
        if offset != len(data):
            raise ValueError(f"NPC.dat 有 {len(data) - offset} 個尾端多餘 bytes")
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
        self._validate_source_indexes(items, len(old), "NPC.dat")
        payload = bytearray(struct.pack("<i", len(items)))
        for item in items:
            idx = int(item.get("index", -1))
            reserved = old[idx].reserved if 0 <= idx < len(old) else 0
            npc_type_id = self._int32(item.get("npcTypeId", 0), "NPC Type ID")
            if npc_type_id <= 0:
                raise ValueError("NPC Type ID 必須大於 0")
            x = self._int32(item.get("x", 0), "NPC X")
            y = self._int32(item.get("y", 0), "NPC Y")
            script = str(item.get("script", "")).strip()
            name = str(item.get("name", "")).strip()
            if not script or not name:
                raise ValueError("NPC Script 與 Name 不可為空")
            if "\x00" in script or "\x00" in name:
                raise ValueError("NPC Script 與 Name 不可包含 NUL 字元")
            try:
                script_bytes = script.encode("ascii")
                name_bytes = name.encode("cp950")
            except UnicodeEncodeError as exc:
                raise ValueError(f"NPC 名稱/腳本包含舊格式無法編碼的文字: {exc}") from exc
            payload += struct.pack("<iiiB", npc_type_id, x, y, reserved)
            payload += script_bytes + b"\x00"
            payload += name_bytes + b"\x00"
        self.backups.atomic_write_bytes(path, bytes(payload), "map_npc")
        return self.get_npc_spawns(map_id)
