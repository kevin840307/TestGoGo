from __future__ import annotations

import os
import shutil
import tempfile
from datetime import datetime
from pathlib import Path

from .paths import ProjectPaths

class UnsafePathError(ValueError):
    pass

def safe_child(root: Path, relative: str) -> Path:
    if not relative or "\x00" in relative:
        raise UnsafePathError("路徑不可為空")
    rel = Path(relative.replace("\\", "/"))
    if rel.is_absolute() or ".." in rel.parts:
        raise UnsafePathError("不允許絕對路徑或 ..")
    candidate = (root / rel).resolve()
    root_resolved = root.resolve()
    try:
        candidate.relative_to(root_resolved)
    except ValueError as exc:
        raise UnsafePathError("路徑超出允許的資料夾") from exc
    return candidate

class BackupManager:
    def __init__(self, paths: ProjectPaths):
        self.paths = paths

    def backup(self, source: Path, reason: str = "edit") -> Path | None:
        if not source.exists() or not source.is_file():
            return None
        stamp = datetime.now().strftime("%Y%m%d_%H%M%S_%f")
        rel = source.resolve().relative_to(self.paths.root.resolve())
        target = self.paths.backups / f"{stamp}_{reason}" / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        return target

    def atomic_write_bytes(self, target: Path, payload: bytes, reason: str = "edit") -> None:
        target.parent.mkdir(parents=True, exist_ok=True)
        self.backup(target, reason)
        fd, temp_name = tempfile.mkstemp(prefix=f".{target.name}.", dir=target.parent)
        try:
            with os.fdopen(fd, "wb") as stream:
                stream.write(payload)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temp_name, target)
        finally:
            if os.path.exists(temp_name):
                os.unlink(temp_name)

    def atomic_write_text(self, target: Path, text: str, encoding: str = "utf-8", reason: str = "edit") -> None:
        self.atomic_write_bytes(target, text.encode(encoding), reason)
