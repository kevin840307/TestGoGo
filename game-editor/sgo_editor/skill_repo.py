from __future__ import annotations

import json
import re
from collections import OrderedDict
from pathlib import Path
from typing import Any

from .paths import ProjectPaths
from .storage import BackupManager

ROOT_FIELDS = [
    "id", "skill_name", "description", "skill_type", "damage_type", "icon_id",
    "fx_use", "fx_hit", "fx_extra", "special_skill",
]
LEVEL_FIELDS = [
    "level", "cast_range", "aoe_radius", "cost_hp", "cost_mp", "cost_sp",
    "base_damage", "damage_multiplier", "cooldown", "duration", "tick_count",
    "max_hp_bonus", "max_mp_bonus", "max_sp_bonus", "atk_bonus", "def_bonus",
    "matk_bonus", "mdef_bonus", "hit_bonus", "flee_bonus", "luk_bonus",
    "aspd_bonus", "move_speed_bonus", "crit_rate_bonus", "crit_damage_bonus",
]
STRING_FIELDS = {"skill_name", "description", "fx_use", "fx_hit", "fx_extra", "special_skill"}


def _split_sections(text: str) -> tuple[list[str], list[tuple[str, list[str]]]]:
    preamble: list[str] = []
    sections: list[tuple[str, list[str]]] = []
    current_header: str | None = None
    current_body: list[str] = []
    for line in text.replace("\r\n", "\n").replace("\r", "\n").split("\n"):
        if line.startswith("[") and line.endswith("]"):
            if current_header is None:
                if current_body:
                    preamble.extend(current_body)
            else:
                sections.append((current_header, current_body))
            current_header = line
            current_body = []
        else:
            current_body.append(line)
    if current_header is None:
        preamble.extend(current_body)
    else:
        sections.append((current_header, current_body))
    return preamble, sections


def _props(lines: list[str]) -> OrderedDict[str, str]:
    result: OrderedDict[str, str] = OrderedDict()
    for line in lines:
        match = re.match(r"^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$", line.strip())
        if match:
            result[match.group(1)] = match.group(2).strip()
    return result


def _read_string(raw: str | None, default: str = "") -> str:
    if raw is None:
        return default
    raw = raw.strip()
    if raw.startswith('"'):
        try:
            return json.loads(raw)
        except Exception:
            return raw.strip('"')
    return raw


def _read_int(raw: str | None, default: int = 0) -> int:
    if raw is None:
        return default
    match = re.match(r"^-?\d+", raw.strip())
    return int(match.group(0)) if match else default


def _quote(value: str) -> str:
    return json.dumps(value, ensure_ascii=False)


def _resource_id(header: str) -> str | None:
    match = re.search(r'\bid="([^"]+)"', header)
    return match.group(1) if match else None


def _ext_id(header: str, needle: str) -> str | None:
    if needle not in header:
        return None
    return _resource_id(header)


class SkillRepository:
    def __init__(self, paths: ProjectPaths, backups: BackupManager):
        self.paths = paths
        self.backups = backups

    def list(self, query: str = "") -> list[dict[str, Any]]:
        query = query.strip().casefold()
        result: list[dict[str, Any]] = []
        for path in sorted(self.paths.skills.glob("*.tres"), key=lambda p: p.name):
            try:
                skill = self.get(int(path.stem))
            except Exception:
                continue
            if query and query not in str(skill["id"]).casefold() and query not in skill["name"].casefold():
                continue
            result.append({
                "id": skill["id"],
                "name": skill["name"],
                "skillType": skill["skillType"],
                "damageType": skill["damageType"],
                "levelCount": len(skill["levels"]),
                "iconId": skill["iconId"],
            })
        return result

    def get(self, skill_id: int) -> dict[str, Any]:
        path = self.paths.skills / f"{int(skill_id)}.tres"
        if not path.exists():
            raise FileNotFoundError(f"找不到技能 {skill_id}")
        return self._parse(path.read_text(encoding="utf-8"), path)

    def save(self, skill_id: int, data: dict[str, Any]) -> dict[str, Any]:
        skill_id = int(skill_id)
        if skill_id <= 0:
            raise ValueError("技能 ID 必須大於 0")
        path = self.paths.skills / f"{skill_id}.tres"
        existing = path.read_text(encoding="utf-8") if path.exists() else None
        text = self._render(skill_id, data, existing)
        self.backups.atomic_write_text(path, text, reason="skill")
        return self.get(skill_id)

    def delete(self, skill_id: int) -> None:
        path = self.paths.skills / f"{int(skill_id)}.tres"
        if not path.exists():
            raise FileNotFoundError(f"找不到技能 {skill_id}")
        self.backups.backup(path, "skill_delete")
        path.unlink()

    def _parse(self, text: str, path: Path | None = None) -> dict[str, Any]:
        _, sections = _split_sections(text)
        root_props: OrderedDict[str, str] = OrderedDict()
        subresources: dict[str, OrderedDict[str, str]] = {}
        for header, body in sections:
            if header == "[resource]":
                root_props = _props(body)
            elif header.startswith("[sub_resource"):
                rid = _resource_id(header)
                if rid:
                    subresources[rid] = _props(body)

        referenced = re.findall(r'SubResource\("([^\"]+)"\)', root_props.get("levels", ""))
        levels: list[dict[str, Any]] = []
        for index, rid in enumerate(referenced):
            props = subresources.get(rid, OrderedDict())
            level = {"level": _read_int(props.get("level"), index + 1)}
            for key in LEVEL_FIELDS:
                if key == "level":
                    continue
                level[self._camel(key)] = _read_int(props.get(key), 0)
            levels.append(level)
        levels.sort(key=lambda x: int(x["level"]))

        inferred_id = int(path.stem) if path and path.stem.isdigit() else 0
        return {
            "id": _read_int(root_props.get("id"), inferred_id),
            "name": _read_string(root_props.get("skill_name"), f"Skill {inferred_id}"),
            "description": _read_string(root_props.get("description")),
            "skillType": _read_int(root_props.get("skill_type")),
            "damageType": _read_int(root_props.get("damage_type")),
            "iconId": _read_int(root_props.get("icon_id")),
            "fxUse": _read_string(root_props.get("fx_use")),
            "fxHit": _read_string(root_props.get("fx_hit")),
            "fxExtra": _read_string(root_props.get("fx_extra")),
            "specialSkill": _read_string(root_props.get("special_skill")),
            "levels": levels,
        }

    def _render(self, skill_id: int, data: dict[str, Any], existing: str | None) -> str:
        if not str(data.get("name", "")).strip():
            raise ValueError("技能名稱不可為空")
        levels_in = data.get("levels") or []
        if not isinstance(levels_in, list) or not levels_in:
            raise ValueError("技能至少需要 1 個等級")
        levels = sorted(levels_in, key=lambda x: int(x.get("level", 0)))
        level_numbers = [int(x.get("level", 0)) for x in levels]
        if any(x <= 0 for x in level_numbers) or len(level_numbers) != len(set(level_numbers)):
            raise ValueError("技能等級必須是大於 0 且不可重複")

        ext_headers: list[str] = []
        old_root: OrderedDict[str, str] = OrderedDict()
        old_levels: dict[int, OrderedDict[str, str]] = {}
        old_ids: dict[int, str] = {}
        level_ext_id = "1_level"
        skill_ext_id = "2_skill"
        if existing:
            _, sections = _split_sections(existing)
            sub_by_id: dict[str, OrderedDict[str, str]] = {}
            for header, body in sections:
                if header.startswith("[ext_resource"):
                    ext_headers.append(header)
                    level_ext_id = _ext_id(header, "skill_level_data.gd") or level_ext_id
                    skill_ext_id = _ext_id(header, "skill_data.gd") or skill_ext_id
                elif header.startswith("[sub_resource"):
                    rid = _resource_id(header)
                    if rid:
                        sub_by_id[rid] = _props(body)
                elif header == "[resource]":
                    old_root = _props(body)
            refs = re.findall(r'SubResource\("([^\"]+)"\)', old_root.get("levels", ""))
            for idx, rid in enumerate(refs):
                props = sub_by_id.get(rid, OrderedDict())
                lvl = _read_int(props.get("level"), idx + 1)
                old_levels[lvl] = props
                old_ids[lvl] = rid
        if not ext_headers:
            ext_headers = [
                '[ext_resource type="Script" path="res://scripts/skill_level_data.gd" id="1_level"]',
                '[ext_resource type="Script" path="res://scripts/skill_data.gd" id="2_skill"]',
            ]

        used_ids = set(old_ids.values())
        refs: list[str] = []
        blocks: list[str] = []
        for lvl_data in levels:
            lvl = int(lvl_data["level"])
            rid = old_ids.get(lvl) or self._new_resource_id(lvl, used_ids)
            used_ids.add(rid)
            refs.append(rid)
            props = OrderedDict(old_levels.get(lvl, {}))
            props["script"] = f'ExtResource("{level_ext_id}")'
            props["level"] = str(lvl)
            for key in LEVEL_FIELDS:
                if key == "level":
                    continue
                props[key] = str(int(lvl_data.get(self._camel(key), 0) or 0))
            ordered = ["script", *LEVEL_FIELDS]
            blocks.append(self._render_block(f'[sub_resource type="Resource" id="{rid}"]', props, ordered))

        root = OrderedDict(old_root)
        root["script"] = f'ExtResource("{skill_ext_id}")'
        values = {
            "id": str(skill_id),
            "skill_name": _quote(str(data.get("name", "")).strip()),
            "description": _quote(str(data.get("description", ""))),
            "skill_type": str(int(data.get("skillType", 0) or 0)),
            "damage_type": str(int(data.get("damageType", 0) or 0)),
            "icon_id": str(int(data.get("iconId", 0) or 0)),
            "fx_use": _quote(str(data.get("fxUse", ""))),
            "fx_hit": _quote(str(data.get("fxHit", ""))),
            "fx_extra": _quote(str(data.get("fxExtra", ""))),
            "special_skill": _quote(str(data.get("specialSkill", ""))),
        }
        root.update(values)
        root["levels"] = f'Array[ExtResource("{level_ext_id}")]([' + ", ".join(f'SubResource("{rid}")' for rid in refs) + "])"

        out = ['[gd_resource type="Resource" script_class="SkillData" format=3]', "", *ext_headers, ""]
        for block in blocks:
            out.extend([block, ""])
        root_order = ["script", *ROOT_FIELDS, "levels"]
        out.append(self._render_block("[resource]", root, root_order))
        return "\n".join(out).rstrip() + "\n"

    @staticmethod
    def _new_resource_id(level: int, used: set[str]) -> str:
        base = f"Resource_editor_{level}"
        candidate = base
        suffix = 2
        while candidate in used:
            candidate = f"{base}_{suffix}"
            suffix += 1
        return candidate

    @staticmethod
    def _render_block(header: str, props: OrderedDict[str, str], preferred: list[str]) -> str:
        lines = [header]
        emitted: set[str] = set()
        for key in preferred:
            if key in props and key not in emitted:
                lines.append(f"{key} = {props[key]}")
                emitted.add(key)
        for key, value in props.items():
            if key not in emitted:
                lines.append(f"{key} = {value}")
        return "\n".join(lines)

    @staticmethod
    def _camel(name: str) -> str:
        parts = name.split("_")
        return parts[0] + "".join(p[:1].upper() + p[1:] for p in parts[1:])
