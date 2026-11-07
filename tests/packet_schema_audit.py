#!/usr/bin/env python3
"""Check Godot literal request fields against the C# runtime wire validator.

This is a static *presence* check. Type/value validation happens in
PacketSchemaValidator at runtime; unknown dynamic fields require E2E tests.
Uses only the Python standard library.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RULE = re.compile(r'\["([A-Za-z][A-Za-z0-9_]*)"\]\s*=\s*\[([^\]]*)\]', re.S)
FIELD = re.compile(r'new\("([A-Za-z][A-Za-z0-9_]*)",\s*FieldType\.(\w+)\)')
TYPE = re.compile(r"""['"]Type['"]\s*:\s*['"]([A-Za-z][A-Za-z0-9_]*)['"]""")
KEY = re.compile(r"""['"]([A-Za-z][A-Za-z0-9_]*)['"]\s*:""")


def dictionary_literals(source: str):
    """Return paired GDScript dictionary literals, excluding quoted braces."""
    stack: list[int] = []
    quote = ""
    escaped = False
    comment = False
    for index, char in enumerate(source):
        if comment:
            if char == "\n":
                comment = False
            continue
        if quote:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = ""
            continue
        if char == "#":
            comment = True
        elif char in ('"', "'"):
            quote = char
        elif char == "{":
            stack.append(index)
        elif char == "}" and stack:
            start = stack.pop()
            yield source[start:index + 1]


def scan(root: Path) -> dict:
    validator = root / "server" / "Protocol" / "PacketSchemaValidator.cs"
    client = root / "client"
    if not validator.is_file():
        raise FileNotFoundError(validator)
    if not client.is_dir():
        raise FileNotFoundError(client)

    rules = {
        name: {field: kind for field, kind in FIELD.findall(body)}
        for name, body in RULE.findall(validator.read_text(encoding="utf-8-sig"))
    }
    if not rules:
        raise ValueError("No field rules were found in PacketSchemaValidator")

    samples: dict[str, list[dict]] = {}
    for path in sorted(client.rglob("*.gd")):
        source = path.read_text(encoding="utf-8-sig")
        if "send_secure_packet" not in source:
            continue
        for literal in dictionary_literals(source):
            packet_types = TYPE.findall(literal)
            if len(packet_types) != 1:
                continue
            packet_type = packet_types[0]
            if packet_type not in rules:
                continue
            fields = set(KEY.findall(literal))
            samples.setdefault(packet_type, []).append({
                "path": path.relative_to(root).as_posix(),
                "fields": fields
            })

    checks = []
    for name, requirements in sorted(rules.items()):
        instances = samples.get(name, [])
        matched = next(
            (s for s in instances if requirements.keys() <= s["fields"]),
            None
        )
        observed = set().union(*(s["fields"] for s in instances))
        checks.append({
            "type": name,
            "required_fields": requirements,
            "status": "MATCHED" if matched else
                      "NOT_FOUND" if not instances else "FIELD_MISMATCH",
            "example": matched["path"] if matched else None,
            "missing_fields": sorted(requirements.keys() - observed),
            "samples_found": len(instances)
        })

    return {
        "checks": checks,
        "schema_type_count": len(rules),
        "matched_count": sum(x["status"] == "MATCHED" for x in checks),
        "issues": [x for x in checks if x["status"] != "MATCHED"],
        "limits": [
            "Static dictionary keys are checked; dynamic merged fields may need manual review.",
            "Values and response fields need runtime tests; this is not an E2E certificate."
        ]
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--strict", action="store_true")
    args = parser.parse_args()
    result = scan(args.root)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            json.dumps(result, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8"
        )
    print("Wire schema coverage: {}/{} request types".format(
        result["matched_count"], result["schema_type_count"]))
    for issue in result["issues"]:
        print("{}: {} missing={}".format(
            issue["status"], issue["type"], issue["missing_fields"]))
    if args.strict and result["issues"]:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
