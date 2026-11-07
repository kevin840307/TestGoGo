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
ACTION_RULE = re.compile(
    r'\["([A-Za-z][A-Za-z0-9_]*:[A-Za-z][A-Za-z0-9_]*)"\]\s*=\s*\[([^\]]*)\]',
    re.S
)
ACTION_CALL = re.compile(
    r"""send_(trade|shop|mall)_action\s*\(\s*['"]([A-Za-z][A-Za-z0-9_]*)['"]\s*,\s*(\{[^{}]*\})""",
    re.S
)
PET_CALL = re.compile(
    r"""send_pet_action\s*\(\s*['"]([A-Za-z][A-Za-z0-9_]*)['"]\s*,\s*[^,()\n]+(?:\s*,\s*(\{[^{}]*\}))?""",
    re.S
)
ACTION_LITERAL = re.compile(
    r"""['"]Action['"]\s*:\s*['"]([A-Za-z][A-Za-z0-9_]*)['"]"""
)


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

    action_rules = {
        name: {field: kind for field, kind in FIELD.findall(body)}
        for name, body in ACTION_RULE.findall(validator.read_text(encoding="utf-8-sig"))
    }

    samples: dict[str, list[dict]] = {}
    action_samples: dict[str, list[dict]] = {}
    for path in sorted(client.rglob("*.gd")):
        source = path.read_text(encoding="utf-8-sig")
        if not any(name in source for name in (
            "send_secure_packet", "send_trade_action", "send_shop_action",
            "send_pet_action", "send_mall_action"
        )):
            continue
        for name, payload in PET_CALL.findall(source):
            # send_pet_action constructs PetAction with PetUID, then
            # merges these literal extra fields.
            action_samples.setdefault("PetAction:" + name, []).append({
                "path": path.relative_to(root).as_posix(),
                "fields": set(KEY.findall(payload)) | {"PetUID"}
            })

        for kind, name, payload in ACTION_CALL.findall(source):
            entry = {
                "path": path.relative_to(root).as_posix(),
                "fields": set(KEY.findall(payload))
            }
            if kind == "trade":
                # The trade helper sends Action as the packet Type.
                samples.setdefault(name, []).append(entry)
            elif kind in ("shop", "mall"):
                action_samples.setdefault(
                    ("ShopAction:" if kind == "shop" else "MallAction:") + name,
                    []).append(entry)

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
            # Direct PetAction and ClubAction dictionaries carry
            # Action in the same wire object.
            if packet_type in ("PetAction", "ClubAction"):
                actions = ACTION_LITERAL.findall(literal)
                if len(actions) == 1:
                    action_samples.setdefault(
                        packet_type + ":" + actions[0], []).append({
                            "path": path.relative_to(root).as_posix(),
                            "fields": fields
                        })

    def compare_rules(requirements_by_type, source_samples):
        checks = []
        for name, requirements in sorted(requirements_by_type.items()):
            instances = source_samples.get(name, [])
            matched = next(
                (entry for entry in instances
                 if requirements.keys() <= entry["fields"]), None
            )
            observed = set().union(*(entry["fields"] for entry in instances))
            checks.append({
                "type": name,
                "required_fields": requirements,
                "status": "MATCHED" if matched else
                          "NOT_FOUND" if not instances else "FIELD_MISMATCH",
                "example": matched["path"] if matched else None,
                "missing_fields": sorted(requirements.keys() - observed),
                "samples_found": len(instances)
            })
        return checks

    checks = compare_rules(rules, samples)
    action_checks = compare_rules(action_rules, action_samples)

    return {
        "checks": checks,
        "action_checks": action_checks,
        "schema_type_count": len(rules),
        "matched_count": sum(x["status"] == "MATCHED" for x in checks),
        "action_schema_count": len(action_rules),
        "action_matched_count": sum(x["status"] == "MATCHED" for x in action_checks),
        "issues": [
            x for x in [*checks, *action_checks] if x["status"] != "MATCHED"
        ],
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
    print("Action schema coverage: {}/{} variants".format(
        result["action_matched_count"], result["action_schema_count"]))
    for issue in result["issues"]:
        print("{}: {} missing={}".format(
            issue["status"], issue["type"], issue["missing_fields"]))
    if args.strict and result["issues"]:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
