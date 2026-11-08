from __future__ import annotations

import re
import time
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

from .enemy_repo import EnemyRepository
from .map_repo import MapRepository
from .paths import ProjectPaths
from .quest_repo import QuestRepository
from .skill_repo import SkillRepository


class HealthRepository:
    """Cross-file validation for editor-facing game data.

    The scanner intentionally distinguishes historical problems from errors
    introduced by the current edit. Project health reports all known issues,
    while pre-save validation only blocks newly introduced invalid references
    or invalid placement.
    """

    CACHE_SECONDS = 10.0
    MAX_ISSUES = 1000

    def __init__(
        self,
        paths: ProjectPaths,
        enemies: EnemyRepository,
        maps: MapRepository,
        quests: QuestRepository,
        skills: SkillRepository,
    ):
        self.paths = paths
        self.enemies = enemies
        self.maps = maps
        self.quests = quests
        self.skills = skills
        self._cache: tuple[float, dict[str, Any]] | None = None

    def invalidate(self) -> None:
        self._cache = None

    def scan(self, force: bool = False) -> dict[str, Any]:
        now = time.monotonic()
        if not force and self._cache and now - self._cache[0] < self.CACHE_SECONDS:
            return self._cache[1]

        issues: list[dict[str, Any]] = []
        enemy_ids = {int(item["id"]) for item in self.enemies.list()}
        script_lookup = {
            path.name.casefold(): path.name
            for path in self.paths.quests.glob("*.txt")
            if path.is_file()
        }

        # Skills: parse failures are important because list() intentionally hides
        # malformed resources to keep the UI usable. Asset checks mirror the
        # actual SkillLoader/BaseEntity EffectLoader lookup rules.
        icon_dir = self.paths.skills / "Icon"
        if not icon_dir.is_dir():
            self._add_issue(
                issues, "warning", "skill_icon_assets_unavailable",
                "技能 Icon 目錄不存在，無法驗證 icon_id",
                "Client SkillLoader 會讀取 res://Skills/Icon/ICON_%03d.png；目前 repository 找不到 client/Skills/Icon。",
                target={"view": "skills"},
            )

        for path in sorted(self.paths.skills.glob("*.tres"), key=lambda p: p.name):
            if not path.stem.isdigit():
                continue
            try:
                skill = self.skills.get(int(path.stem))
                skill_id = int(path.stem)
                if int(skill["id"]) != skill_id:
                    self._add_issue(
                        issues, "error", "skill_id_mismatch",
                        f"技能 {path.name} 的 resource id 與檔名不一致",
                        f"resource id={skill['id']}，filename id={path.stem}",
                        target={"view": "skills", "id": skill_id},
                    )

                if icon_dir.is_dir():
                    icon_id = int(skill.get("iconId", 0))
                    icon_path = icon_dir / f"ICON_{icon_id:03d}.png"
                    if icon_id >= 0 and not icon_path.is_file():
                        self._add_issue(
                            issues, "warning", "missing_skill_icon",
                            f"技能 {skill_id} 找不到 Icon {icon_id}",
                            f"預期素材：client/Skills/Icon/{icon_path.name}",
                            target={"view": "skills", "id": skill_id},
                        )

                for field, effect_name in (
                    ("fxUse", str(skill.get("fxUse", "")).strip()),
                    ("fxHit", str(skill.get("fxHit", "")).strip()),
                    ("fxExtra", str(skill.get("fxExtra", "")).strip()),
                ):
                    if not effect_name or effect_name == "0":
                        continue
                    candidates = self._skill_effect_candidates(field, effect_name)
                    if not any(path.is_file() for path in candidates):
                        self._add_issue(
                            issues, "warning", "missing_skill_effect",
                            f"技能 {skill_id} 的 {field} 找不到特效",
                            f"{effect_name}；runtime 候選："
                            + ", ".join(self.paths.relative(path) for path in candidates),
                            target={"view": "skills", "id": skill_id},
                        )
            except Exception as exc:
                self._add_issue(
                    issues, "error", "skill_parse",
                    f"技能 {path.name} 無法解析", str(exc),
                    target={"view": "skills"},
                )

        map_count = 0
        spawn_count = 0
        for map_info in self.maps.list_maps():
            map_id = int(map_info["id"])
            map_count += 1
            try:
                enemy_rows = self.maps.get_enemy_spawns(map_id)
            except Exception as exc:
                self._add_issue(
                    issues, "error", "enemy_dat_parse",
                    f"Map{map_id:02d} Enemy.dat 無法解析", str(exc),
                    target={"view": "maps", "mapId": map_id, "tab": "enemies"},
                )
                enemy_rows = []
            try:
                npc_rows = self.maps.get_npc_spawns(map_id)
            except Exception as exc:
                self._add_issue(
                    issues, "error", "npc_dat_parse",
                    f"Map{map_id:02d} NPC.dat 無法解析", str(exc),
                    target={"view": "maps", "mapId": map_id, "tab": "npcs"},
                )
                npc_rows = []

            spawn_count += len(enemy_rows) + len(npc_rows)
            blocking = self.maps._read_blocking(map_id)
            blocking_error = self.maps._blocking_errors.get(map_id, "")
            if blocking_error:
                self._add_issue(
                    issues, "error", "blocking_invalid",
                    f"Map{map_id:02d} Blocking.dat 無效", blocking_error,
                    target={"view": "maps", "mapId": map_id, "tab": "enemies"},
                )

            enemy_dupes: dict[tuple[int, int, int], list[int]] = defaultdict(list)
            for index, row in enumerate(enemy_rows):
                enemy_id = int(row["enemyId"])
                x, y = int(row["x"]), int(row["y"])
                enemy_dupes[(enemy_id, x, y)].append(index)

                if enemy_id not in enemy_ids:
                    self._add_issue(
                        issues, "error", "missing_enemy",
                        f"Map{map_id:02d} 引用了不存在的 Enemy {enemy_id}",
                        f"出生點 #{index + 1}，座標 ({x}, {y})",
                        target={"view": "maps", "mapId": map_id, "tab": "enemies", "index": index},
                    )

                self._audit_terrain(
                    issues, map_id, "enemies", index, x, y, blocking,
                    label=f"Enemy {enemy_id}",
                )

            for key, indexes in enemy_dupes.items():
                if len(indexes) <= 1:
                    continue
                enemy_id, x, y = key
                self._add_issue(
                    issues, "warning", "duplicate_enemy_spawn",
                    f"Map{map_id:02d} 有 {len(indexes)} 筆完全重複的怪物出生點",
                    f"Enemy {enemy_id} · ({x}, {y}) · rows {[i + 1 for i in indexes]}",
                    target={"view": "maps", "mapId": map_id, "tab": "enemies", "index": indexes[0]},
                )

            npc_dupes: dict[tuple[int, int, int, str], list[int]] = defaultdict(list)
            for index, row in enumerate(npc_rows):
                script = str(row["script"]).strip()
                x, y = int(row["x"]), int(row["y"])
                npc_dupes[(int(row["npcTypeId"]), x, y, script.casefold())].append(index)
                if script.casefold() not in script_lookup:
                    self._add_issue(
                        issues, "error", "missing_npc_script",
                        f"Map{map_id:02d} NPC 引用了不存在的 Script",
                        f"{row['name']} · {script or '(empty)'} · ({x}, {y})",
                        target={"view": "maps", "mapId": map_id, "tab": "npcs", "index": index},
                    )

                self._audit_terrain(
                    issues, map_id, "npcs", index, x, y, blocking,
                    label=f"NPC {row['name']}",
                )

            for key, indexes in npc_dupes.items():
                if len(indexes) <= 1:
                    continue
                npc_type, x, y, script = key
                self._add_issue(
                    issues, "warning", "duplicate_npc_spawn",
                    f"Map{map_id:02d} 有 {len(indexes)} 筆完全重複的 NPC",
                    f"NPC Type {npc_type} · {script} · ({x}, {y}) · rows {[i + 1 for i in indexes]}",
                    target={"view": "maps", "mapId": map_id, "tab": "npcs", "index": indexes[0]},
                )

        quest_count = 0
        for path in sorted(self.paths.quests.glob("*.txt"), key=lambda p: p.name.casefold()):
            quest_count += 1
            try:
                content, _ = self.quests._decode(path)
                result = self.quests.validate(path.name, content)
            except Exception as exc:
                self._add_issue(
                    issues, "error", "quest_parse",
                    f"{path.name} 無法讀取 / 驗證", str(exc),
                    target={"view": "scripts", "script": path.name},
                )
                continue

            for message in result.get("errors", []):
                self._add_issue(
                    issues, "error", "quest_validation",
                    f"{path.name} 驗證失敗", str(message),
                    target={"view": "scripts", "script": path.name},
                )
            for message in result.get("warnings", []):
                self._add_issue(
                    issues, "warning", "quest_warning",
                    f"{path.name} 有腳本警告", str(message),
                    target={"view": "scripts", "script": path.name},
                )

        error_count = sum(1 for item in issues if item["severity"] == "error")
        warning_count = sum(1 for item in issues if item["severity"] == "warning")
        categories = Counter(item["category"] for item in issues)
        result = {
            "ok": error_count == 0,
            "summary": {
                "errors": error_count,
                "warnings": warning_count,
                "issues": len(issues),
                "maps": map_count,
                "spawns": spawn_count,
                "quests": quest_count,
                "categories": dict(sorted(categories.items())),
            },
            "issues": issues[: self.MAX_ISSUES],
            "truncated": len(issues) > self.MAX_ISSUES,
        }
        self._cache = (now, result)
        return result

    def _skill_effect_candidates(self, field: str, effect_name: str) -> list[Path]:
        effects = self.paths.assets / "Effects"

        def effect(folder: str, name: str) -> Path:
            return effects / folder / f"{name}.tres"

        if field == "fxUse":
            # Player BaseEntity prefixes the use effect by race; MonsterEntity
            # also supports a direct Skill_Effect name and Hit_Effect fallback.
            return [
                effect("Skill_Effect", f"A_{effect_name}"),
                effect("Skill_Effect", f"B_{effect_name}"),
                effect("Skill_Effect", f"R_{effect_name}"),
                effect("Skill_Effect", effect_name),
                effect("Hit_Effect", effect_name),
            ]

        if effect_name.startswith("Normal"):
            preferred = effect("Normal_Effect", effect_name)
        elif effect_name.startswith("Hit_Effect"):
            preferred = effect("Hit_Effect", effect_name)
        else:
            preferred = effect("Skill_Effect", effect_name)

        # BaseEntity/MonsterEntity fall back to Hit_Effect when the preferred
        # path is absent. Keep the same behavior in health validation.
        fallback = effect("Hit_Effect", effect_name)
        return [preferred] if preferred == fallback else [preferred, fallback]

    def _audit_terrain(
        self,
        issues: list[dict[str, Any]],
        map_id: int,
        tab: str,
        index: int,
        x: int,
        y: int,
        blocking: tuple[int, int, bytes, Path] | None,
        label: str,
    ) -> None:
        if blocking is None:
            return
        width, height, _, _ = blocking
        cell_x, cell_y = self.maps.world_to_cell(x, y)
        if not (0 <= cell_x < width and 0 <= cell_y < height):
            self._add_issue(
                issues, "error", "spawn_outside_grid",
                f"Map{map_id:02d} {label} 超出 Blocking grid",
                f"({x}, {y}) → cell ({cell_x}, {cell_y})，grid={width}×{height}",
                target={"view": "maps", "mapId": map_id, "tab": tab, "index": index},
            )
            return

        terrain = self.maps._terrain_from_blocking(blocking, x, y)
        if terrain.get("walkable"):
            return
        nearest = terrain.get("nearest")
        suggestion = None
        if isinstance(nearest, dict):
            suggestion = {
                "from": {"x": x, "y": y},
                "to": {"x": int(nearest["x"]), "y": int(nearest["y"])},
            }
        self._add_issue(
            issues, "warning", "blocking_conflict",
            f"Map{map_id:02d} {label} 位在不可走格",
            f"({x}, {y}) → cell ({cell_x}, {cell_y}) · flags={terrain.get('flags')}",
            target={"view": "maps", "mapId": map_id, "tab": tab, "index": index},
            suggestion=suggestion,
        )

    def validate_map_items(self, map_id: int, tab: str, items: list[dict[str, Any]]) -> dict[str, Any]:
        if tab not in {"enemies", "npcs"}:
            raise ValueError("tab 必須是 enemies 或 npcs")
        if len(items) > 10000:
            raise ValueError("單張地圖資料不可超過 10000 筆")

        old_rows = (
            self.maps.get_enemy_spawns(map_id)
            if tab == "enemies"
            else self.maps.get_npc_spawns(map_id)
        )
        old_by_index = {int(row.get("index", -1)): row for row in old_rows}
        blocking = self.maps._read_blocking(map_id)
        enemy_ids = {int(item["id"]) for item in self.enemies.list()} if tab == "enemies" else set()
        scripts = {
            path.name.casefold()
            for path in self.paths.quests.glob("*.txt")
            if path.is_file()
        } if tab == "npcs" else set()

        errors: list[dict[str, Any]] = []
        warnings: list[dict[str, Any]] = []
        suggestions: list[dict[str, Any]] = []
        seen_existing: set[int] = set()
        seen_source_indexes: set[int] = set()
        added = moved = changed = 0
        duplicate_keys: dict[tuple[Any, ...], list[int]] = defaultdict(list)

        def int32_field(row_index: int, value: Any, label: str) -> tuple[int, bool]:
            try:
                parsed = int(value)
            except (TypeError, ValueError):
                self._validation_issue(
                    errors, row_index, "invalid_integer",
                    f"{label} 必須是整數",
                )
                return 0, False
            if parsed < MapRepository.INT32_MIN or parsed > MapRepository.INT32_MAX:
                self._validation_issue(
                    errors, row_index, "invalid_int32",
                    f"{label} 超出 int32 範圍：{parsed}",
                )
                return 0, False
            return parsed, True

        for row_index, row in enumerate(items):
            if not isinstance(row, dict):
                self._validation_issue(
                    errors, row_index, "invalid_row",
                    "地圖資料列必須是物件",
                )
                continue
            try:
                source_index = int(row.get("index", -1))
            except (TypeError, ValueError):
                self._validation_issue(
                    errors, row_index, "invalid_source_index",
                    "index 必須是整數；新資料請使用 -1",
                )
                source_index = -1

            old = old_by_index.get(source_index)
            if source_index != -1:
                if source_index < 0 or source_index not in old_by_index:
                    self._validation_issue(
                        errors, row_index, "invalid_source_index",
                        f"source index {source_index} 不存在；新資料請使用 -1",
                    )
                    old = None
                elif source_index in seen_source_indexes:
                    self._validation_issue(
                        errors, row_index, "duplicate_source_index",
                        f"source index {source_index} 被重複使用；這可能錯用 legacy metadata",
                    )
                else:
                    seen_source_indexes.add(source_index)
                    seen_existing.add(source_index)

            x, x_valid = int32_field(row_index, row.get("x", 0), "X")
            y, y_valid = int32_field(row_index, row.get("y", 0), "Y")
            coordinates_valid = x_valid and y_valid
            if old is None:
                added += 1
            else:
                if int(old.get("x", 0)) != x or int(old.get("y", 0)) != y:
                    moved += 1
                if self._meaningful_row_changed(tab, old, row):
                    changed += 1

            if tab == "enemies":
                enemy_id, enemy_id_valid = int32_field(row_index, row.get("enemyId", 0), "Enemy ID")
                source_flags, source_flags_valid = int32_field(
                    row_index,
                    row.get("sourceFlags", old.get("sourceFlags", 0) if old else 0),
                    "Enemy sourceFlags",
                )
                _ = source_flags, source_flags_valid
                duplicate_keys[(enemy_id, x, y)].append(row_index)
                if enemy_id_valid and enemy_id <= 0:
                    self._validation_issue(
                        errors, row_index, "invalid_enemy_id",
                        "Enemy ID 必須大於 0",
                    )
                elif enemy_id_valid and enemy_id not in enemy_ids:
                    historical = bool(old and int(old.get("enemyId", 0)) == enemy_id)
                    self._validation_issue(
                        warnings if historical else errors,
                        row_index,
                        "missing_enemy",
                        f"Enemy {enemy_id} 不存在於 Enemy_Data.csv",
                    )
            else:
                npc_type_id, npc_type_valid = int32_field(row_index, row.get("npcTypeId", 0), "NPC Type ID")
                script = str(row.get("script", "")).strip()
                name = str(row.get("name", "")).strip()
                duplicate_keys[(npc_type_id, x, y, script.casefold())].append(row_index)

                if npc_type_valid and npc_type_id <= 0:
                    self._validation_issue(
                        errors, row_index, "invalid_npc_type",
                        "NPC Type ID 必須大於 0",
                    )
                if not script or not name:
                    self._validation_issue(
                        errors, row_index, "invalid_npc_text",
                        "NPC Script 與 Name 不可為空",
                    )
                elif "\x00" in script or "\x00" in name:
                    self._validation_issue(
                        errors, row_index, "invalid_npc_text",
                        "NPC Script 與 Name 不可包含 NUL 字元",
                    )
                else:
                    try:
                        script.encode("ascii")
                    except UnicodeEncodeError:
                        self._validation_issue(
                            errors, row_index, "invalid_npc_encoding",
                            "NPC Script 必須可用 ASCII 編碼",
                        )
                    try:
                        name.encode("cp950")
                    except UnicodeEncodeError:
                        self._validation_issue(
                            errors, row_index, "invalid_npc_encoding",
                            "NPC Name 必須可用 CP950 編碼",
                        )

                if script and script.casefold() not in scripts:
                    historical = bool(old and str(old.get("script", "")).strip().casefold() == script.casefold())
                    self._validation_issue(
                        warnings if historical else errors,
                        row_index,
                        "missing_npc_script",
                        f"NPC Script {script} 不存在",
                    )

            if blocking is not None and coordinates_valid:
                terrain = self.maps._terrain_from_blocking(blocking, x, y)
                if not terrain.get("walkable"):
                    historical = bool(
                        old
                        and int(old.get("x", 0)) == x
                        and int(old.get("y", 0)) == y
                    )
                    target = warnings if historical else errors
                    self._validation_issue(
                        target,
                        row_index,
                        "blocking_conflict",
                        f"座標 ({x}, {y}) 位於不可走格，flags={terrain.get('flags')}",
                    )
                    nearest = terrain.get("nearest")
                    if isinstance(nearest, dict):
                        suggestions.append({
                            "rowIndex": row_index,
                            "from": {"x": x, "y": y},
                            "to": {"x": int(nearest["x"]), "y": int(nearest["y"])},
                        })

        for key, indexes in duplicate_keys.items():
            if len(indexes) <= 1:
                continue
            warnings.append({
                "rowIndex": indexes[0],
                "category": "duplicate_spawn",
                "message": f"有 {len(indexes)} 筆完全重複的位置 / 引用：rows {[i + 1 for i in indexes]}",
            })

        deleted = len([idx for idx in old_by_index if idx not in seen_existing])
        return {
            "ok": not errors,
            "errors": errors,
            "warnings": warnings,
            "suggestions": suggestions,
            "changes": {
                "added": added,
                "deleted": deleted,
                "moved": moved,
                "changed": changed,
                "total": len(items),
            },
        }

    @staticmethod
    def _meaningful_row_changed(tab: str, old: dict[str, Any], new: dict[str, Any]) -> bool:
        fields = (
            ("enemyId", "sourceFlags")
            if tab == "enemies"
            else ("npcTypeId", "name", "script")
        )
        return any(str(old.get(key, "")) != str(new.get(key, "")) for key in fields)

    @staticmethod
    def _validation_issue(target: list[dict[str, Any]], row_index: int, category: str, message: str) -> None:
        target.append({"rowIndex": row_index, "category": category, "message": message})

    def references(self, kind: str, key: str) -> dict[str, Any]:
        refs: list[dict[str, Any]] = []
        if kind == "enemy":
            enemy_id = int(key)
            for map_info in self.maps.list_maps():
                map_id = int(map_info["id"])
                try:
                    rows = self.maps.get_enemy_spawns(map_id)
                except Exception:
                    continue
                for index, row in enumerate(rows):
                    if int(row["enemyId"]) == enemy_id:
                        refs.append({
                            "type": "map_enemy",
                            "mapId": map_id,
                            "index": index,
                            "x": int(row["x"]),
                            "y": int(row["y"]),
                        })
        elif kind == "script":
            script_key = key.strip().casefold()
            for map_info in self.maps.list_maps():
                map_id = int(map_info["id"])
                try:
                    rows = self.maps.get_npc_spawns(map_id)
                except Exception:
                    continue
                for index, row in enumerate(rows):
                    if str(row["script"]).strip().casefold() == script_key:
                        refs.append({
                            "type": "map_npc",
                            "mapId": map_id,
                            "index": index,
                            "name": row["name"],
                            "x": int(row["x"]),
                            "y": int(row["y"]),
                        })

            include_pattern = re.compile(r'#include\s+"([^"]+)"', re.IGNORECASE)
            for path in self.paths.quests.glob("*.txt"):
                if path.name.casefold() == script_key:
                    continue
                try:
                    content, _ = self.quests._decode(path)
                except Exception:
                    continue
                includes = [match.casefold() for match in include_pattern.findall(content)]
                if script_key in includes:
                    refs.append({"type": "quest_include", "script": path.name})
        else:
            raise ValueError("kind 必須是 enemy 或 script")

        return {"kind": kind, "key": key, "count": len(refs), "references": refs}

    @staticmethod
    def _add_issue(
        issues: list[dict[str, Any]],
        severity: str,
        category: str,
        title: str,
        detail: str,
        target: dict[str, Any] | None = None,
        suggestion: dict[str, Any] | None = None,
    ) -> None:
        issues.append({
            "severity": severity,
            "category": category,
            "title": title,
            "detail": detail,
            "target": target,
            "suggestion": suggestion,
        })
