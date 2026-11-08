from __future__ import annotations

import re
from pathlib import Path
from typing import Any

from .paths import ProjectPaths
from .storage import BackupManager, safe_child


class QuestRepository:
    SPECIAL_CALLBACKS = {"End", "QuestEnd", "start"}

    def __init__(self, paths: ProjectPaths, backups: BackupManager):
        self.paths = paths
        self.backups = backups

    @staticmethod
    def _decode(path: Path) -> tuple[str, str]:
        data = path.read_bytes()
        try:
            return data.decode("utf-8"), "utf-8"
        except UnicodeDecodeError:
            return data.decode("cp950"), "cp950"

    def list(self, query: str = "") -> list[dict[str, Any]]:
        q = query.strip().casefold()
        result: list[dict[str, Any]] = []
        for path in sorted(self.paths.quests.glob("*.txt"), key=lambda p: p.name.casefold()):
            if q and q not in path.name.casefold():
                continue
            try:
                text, enc = self._decode(path)
                funcs = self._functions(text)
                result.append({"name": path.name, "functions": funcs, "encoding": enc, "size": path.stat().st_size})
            except Exception as exc:
                result.append({"name": path.name, "functions": [], "encoding": "unknown", "size": path.stat().st_size, "error": str(exc)})
        return result

    def get(self, name: str) -> dict[str, Any]:
        path = self._script_path(name)
        if not path.exists():
            raise FileNotFoundError(f"找不到 NPC 腳本 {name}")
        content, encoding = self._decode(path)
        validation = self.validate(name, content)
        return {
            "name": path.name,
            "content": content,
            "encoding": encoding,
            "functions": self._functions(content),
            "validation": validation,
        }

    def save(self, name: str, content: str, force: bool = False) -> dict[str, Any]:
        path = self._script_path(name)
        validation = self.validate(path.name, content)
        if validation["errors"] and not force:
            raise ValueError("腳本驗證失敗：" + "；".join(validation["errors"]))
        encoding = "utf-8"
        if path.exists():
            _, encoding = self._decode(path)
        try:
            payload = content.encode(encoding)
        except UnicodeEncodeError:
            encoding = "utf-8"
            payload = content.encode("utf-8")
        self.backups.atomic_write_bytes(path, payload, "quest")
        return self.get(path.name)

    def delete(self, name: str) -> None:
        path = self._script_path(name)
        if not path.exists():
            raise FileNotFoundError(f"找不到 NPC 腳本 {name}")
        self.backups.backup(path, "quest_delete")
        path.unlink()

    def validate(self, name: str, content: str) -> dict[str, Any]:
        errors: list[str] = []
        warnings: list[str] = []
        expanded = self._expand_includes(name, content, errors, set())
        stripped = self._strip_comments(expanded)
        functions = self._functions(stripped)
        self._validate_blocks(stripped, errors)
        self._validate_delimiters(stripped, errors)
        callbacks = self._callbacks(stripped)
        known = set(functions) | self.SPECIAL_CALLBACKS
        for callback in sorted(callbacks):
            if callback not in known:
                warnings.append(f"Callback '{callback}' 在此腳本中找不到對應 function")
        if "function start" not in stripped:
            warnings.append("找不到 function start()；一般 NPC 互動入口通常需要 start")
        return {"ok": not errors, "errors": errors, "warnings": warnings, "functions": functions}

    def _script_path(self, name: str) -> Path:
        clean = name.strip()
        if not clean.lower().endswith(".txt"):
            clean += ".txt"
        path = safe_child(self.paths.quests, clean)
        if path.parent != self.paths.quests.resolve():
            raise ValueError("NPC 腳本只允許放在 Quest 根目錄")
        return path

    def _expand_includes(self, name: str, content: str, errors: list[str], stack: set[str]) -> str:
        key = name.casefold()
        if key in stack:
            errors.append(f"偵測到循環 #include: {name}")
            return content
        stack = set(stack)
        stack.add(key)
        output: list[str] = []
        for line_no, raw in enumerate(content.replace("\r\n", "\n").replace("\r", "\n").split("\n"), start=1):
            line = raw.strip()
            if line.lower().startswith("#include"):
                match = re.search(r'#include\s+"([^"]+)"', line, re.IGNORECASE)
                if not match:
                    errors.append(f"{name}:{line_no} #include 格式錯誤")
                    continue
                include_name = match.group(1)
                try:
                    include_path = self._script_path(include_name)
                except ValueError as exc:
                    errors.append(f"{name}:{line_no} {exc}")
                    continue
                if not include_path.exists():
                    errors.append(f"{name}:{line_no} 找不到 include: {include_name}")
                    continue
                include_text, _ = self._decode(include_path)
                output.append(self._expand_includes(include_path.name, include_text, errors, stack))
            else:
                output.append(raw.replace("\\“", "“").replace("\\”", "”").replace("\\‘", "‘").replace("\\’", "’"))
        return "\n".join(output)

    @staticmethod
    def _functions(content: str) -> list[str]:
        return re.findall(r"(?m)^\s*function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", content)

    @staticmethod
    def _callbacks(content: str) -> set[str]:
        callbacks: set[str] = set()
        for line in content.splitlines():
            if not re.search(r"\b(?:Ask|Say)\s*\(", line):
                continue
            strings = re.findall(r'"((?:\\.|[^"\\])*)"', line)
            if len(strings) >= 2:
                callbacks.add(strings[-1])
        return callbacks

    @staticmethod
    def _strip_comments(content: str) -> str:
        output: list[str] = []
        for line in content.splitlines():
            in_string = False
            escaped = False
            cut = len(line)
            for i, ch in enumerate(line):
                if escaped:
                    escaped = False
                    continue
                if ch == "\\" and in_string:
                    escaped = True
                    continue
                if ch == '"':
                    in_string = not in_string
                    continue
                if not in_string and ch == "-" and i + 1 < len(line) and line[i + 1] == "-":
                    cut = i
                    break
            output.append(line[:cut])
        return "\n".join(output)

    @staticmethod
    def _validate_delimiters(content: str, errors: list[str]) -> None:
        stack: list[tuple[str, int]] = []
        pairs = {")": "(", "]": "[", "}": "{"}
        in_string = False
        escaped = False
        line_no = 1
        for ch in content:
            if ch == "\n":
                line_no += 1
            if escaped:
                escaped = False
                continue
            if in_string and ch == "\\":
                escaped = True
                continue
            if ch == '"':
                in_string = not in_string
                continue
            if in_string:
                continue
            if ch in "([{":
                stack.append((ch, line_no))
            elif ch in pairs:
                if not stack or stack[-1][0] != pairs[ch]:
                    errors.append(f"第 {line_no} 行附近有不成對的 '{ch}'")
                    return
                stack.pop()
        if in_string:
            errors.append("有未關閉的雙引號字串")
        if stack:
            opener, ln = stack[-1]
            errors.append(f"第 {ln} 行的 '{opener}' 沒有關閉")

    @staticmethod
    def _validate_blocks(content: str, errors: list[str]) -> None:
        stack: list[tuple[str, int]] = []
        for line_no, raw in enumerate(content.splitlines(), start=1):
            line = raw.strip()
            if not line:
                continue
            if re.match(r"^function\b", line):
                stack.append(("function", line_no))
            elif re.match(r"^if\b.*\bthen\s*$", line):
                stack.append(("if", line_no))
            elif re.match(r"^(?:for|while)\b.*\bdo\s*$", line):
                stack.append(("loop", line_no))
            elif line == "repeat":
                stack.append(("repeat", line_no))
            elif re.match(r"^until\b", line):
                if not stack or stack[-1][0] != "repeat":
                    errors.append(f"第 {line_no} 行 until 沒有對應 repeat")
                else:
                    stack.pop()
            elif line == "end" or line.startswith("end "):
                if not stack or stack[-1][0] == "repeat":
                    errors.append(f"第 {line_no} 行 end 沒有可關閉的 block")
                else:
                    stack.pop()
        for kind, line_no in stack:
            errors.append(f"第 {line_no} 行開始的 {kind} block 沒有關閉")
