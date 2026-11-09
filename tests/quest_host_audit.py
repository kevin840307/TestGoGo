#!/usr/bin/env python3
"""Audit recovered legacy quest calls against QuestRuntime host bindings.

This is deliberately source-level: it catches the common MoonSharp
"attempt to call a nil value" failure before an NPC is clicked. Script-local
functions and ordinary Lua builtins are excluded. Dynamic function values,
metatable calls and runtime data behavior still require integration/E2E tests.
"""
from __future__ import annotations

import argparse
import json
import re
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

HOST = re.compile(
    r'script\.Globals\["([A-Za-z_][A-Za-z0-9_]*)"\]\s*='
)
FUNCTION = re.compile(
    r'(?m)^\s*(?:local\s+)?function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\('
)
CALL = re.compile(
    r'(?<![A-Za-z0-9_.:])([A-Za-z_][A-Za-z0-9_]*)\s*\('
)

LUA_BUILTINS = {
    "assert", "collectgarbage", "dofile", "error", "getmetatable",
    "ipairs", "load", "loadfile", "next", "pairs", "pcall", "print",
    "rawequal", "rawget", "rawlen", "rawset", "require", "select",
    "setmetatable", "tonumber", "tostring", "type", "xpcall",
}


def _strip_comments_and_strings(source: str) -> str:
    """Preserve newlines/positions while hiding comments and quoted strings."""
    out = list(source)
    i = 0
    quote = ""
    long_string = False
    while i < len(source):
        if long_string:
            end = source.find("]]", i)
            if end < 0:
                end = len(source) - 2
            for j in range(i, min(len(source), end + 2)):
                if source[j] != "\n":
                    out[j] = " "
            i = end + 2
            long_string = False
            continue

        if quote:
            if source[i] == "\\":
                if source[i] != "\n":
                    out[i] = " "
                if i + 1 < len(source):
                    if source[i + 1] != "\n":
                        out[i + 1] = " "
                    i += 2
                    continue
            if source[i] == quote:
                out[i] = " "
                quote = ""
            elif source[i] != "\n":
                out[i] = " "
            i += 1
            continue

        if source.startswith("--[[", i):
            end = source.find("]]", i + 4)
            if end < 0:
                end = len(source) - 2
            for j in range(i, min(len(source), end + 2)):
                if source[j] != "\n":
                    out[j] = " "
            i = end + 2
            continue
        if source.startswith("--", i):
            end = source.find("\n", i + 2)
            if end < 0:
                end = len(source)
            for j in range(i, end):
                out[j] = " "
            i = end
            continue
        if source.startswith("[[", i):
            out[i] = out[i + 1] = " "
            i += 2
            long_string = True
            continue
        if source[i] in ('"', "'"):
            quote = source[i]
            out[i] = " "
            i += 1
            continue
        i += 1
    return "".join(out)


def scan(root: Path) -> dict:
    runtime = root / "server/Infrastructure/Quest/QuestRuntime.cs"
    quest_root = root / "Old/服務端/Dat/Quest"
    if not runtime.is_file():
        raise FileNotFoundError(runtime)
    if not quest_root.is_dir():
        raise FileNotFoundError(quest_root)

    host = set(HOST.findall(runtime.read_text(encoding="utf-8-sig")))
    scripts = sorted(quest_root.rglob("*.txt"))
    unsupported: dict[str, list[dict]] = defaultdict(list)
    calls_by_name: dict[str, int] = defaultdict(int)

    for path in scripts:
        source = path.read_text(encoding="utf-8-sig", errors="replace")
        clean = _strip_comments_and_strings(source)
        locals_ = set(FUNCTION.findall(clean))
        # QuestRuntime expands #include from the quest root before executing Lua.
        # Included script functions are not missing host APIs.
        include_stack = [path]
        visited_includes = {path.resolve()}
        while include_stack:
            current = include_stack.pop()
            include_source = current.read_text(encoding="utf-8-sig", errors="replace")
            for include_name in re.findall(
                r'(?im)^\s*#include\s+"([^"]+)"', include_source
            ):
                include_path = (quest_root / include_name).resolve()
                if not include_path.is_file() or include_path in visited_includes:
                    continue
                visited_includes.add(include_path)
                included = include_path.read_text(
                    encoding="utf-8-sig", errors="replace"
                )
                locals_.update(FUNCTION.findall(_strip_comments_and_strings(included)))
                include_stack.append(include_path)

        for match in CALL.finditer(clean):
            name = match.group(1)
            # "function foo(" exposes foo( to CALL as well; local/script
            # functions are intentionally valid.
            if name in locals_ or name in host or name in LUA_BUILTINS:
                continue

            # Skip language constructs that can syntactically precede '('.
            if name in {"if", "for", "while", "return", "function"}:
                continue

            line = clean.count("\n", 0, match.start()) + 1
            unsupported[name].append({
                "path": path.relative_to(root).as_posix(),
                "line": line,
            })
            calls_by_name[name] += 1

    return {
        "quest_script_count": len(scripts),
        "host_binding_count": len(host),
        "host_bindings": sorted(host),
        "unsupported_call_count": sum(calls_by_name.values()),
        "unsupported_function_count": len(unsupported),
        "unsupported_calls": {
            name: entries
            for name, entries in sorted(unsupported.items())
        },
        "limits": [
            "Static direct function calls only; dynamic function variables are not inferred.",
            "A bound host function can still have incomplete gameplay semantics.",
            "Passing this audit does not replace NPC/Lua persistence and reward E2E tests.",
        ],
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
            encoding="utf-8",
        )

    print(
        "Quest host coverage: scripts={} bindings={} unsupported_functions={} "
        "unsupported_calls={}".format(
            result["quest_script_count"],
            result["host_binding_count"],
            result["unsupported_function_count"],
            result["unsupported_call_count"],
        )
    )
    for name, entries in result["unsupported_calls"].items():
        sample = ", ".join(
            "{}:{}".format(x["path"], x["line"])
            for x in entries[:5]
        )
        print("UNBOUND {}: {}{}".format(
            name,
            sample,
            " ..." if len(entries) > 5 else "",
        ))

    return int(args.strict and result["unsupported_call_count"] > 0)


if __name__ == "__main__":
    raise SystemExit(main())
