from __future__ import annotations

import base64
import mimetypes
from pathlib import Path
from typing import Any

from .paths import ProjectPaths
from .storage import BackupManager, safe_child


class AssetRepository:
    MAX_REPLACE_BYTES = 25 * 1024 * 1024
    ALLOWED_EXTENSIONS = {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg", ".json", ".tres",
        ".txt", ".ogg", ".wav", ".mp3", ".ogv",
    }
    PREVIEW_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg"}

    def __init__(self, paths: ProjectPaths, backups: BackupManager):
        self.paths = paths
        self.backups = backups

    def folders(self) -> list[str]:
        return sorted([p.name for p in self.paths.assets.iterdir() if p.is_dir()], key=str.casefold)

    def list(self, folder: str = "", query: str = "", limit: int = 500) -> list[dict[str, Any]]:
        root = self.paths.assets if not folder else safe_child(self.paths.assets, folder)
        if not root.exists() or not root.is_dir():
            raise FileNotFoundError(f"找不到素材資料夾 {folder}")
        q = query.strip().casefold()
        result: list[dict[str, Any]] = []
        for path in root.rglob("*"):
            if not path.is_file() or path.name.endswith(".uid"):
                continue
            rel = path.relative_to(self.paths.assets).as_posix()
            if q and q not in rel.casefold():
                continue
            result.append({
                "path": rel,
                "name": path.name,
                "size": path.stat().st_size,
                "extension": path.suffix.lower(),
                "previewable": path.suffix.lower() in self.PREVIEW_EXTENSIONS,
            })
            if len(result) >= max(1, min(int(limit), 2000)):
                break
        result.sort(key=lambda x: x["path"].casefold())
        return result

    def get_file(self, relative: str) -> tuple[Path, str]:
        path = safe_child(self.paths.assets, relative)
        if not path.exists() or not path.is_file():
            raise FileNotFoundError(f"找不到素材 {relative}")
        mime = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
        return path, mime

    @staticmethod
    def _canonical_extension(value: str) -> str:
        ext = Path(value).suffix.lower()
        return ".jpg" if ext == ".jpeg" else ext

    def replace(
        self,
        relative: str,
        base64_content: str,
        source_name: str | None = None,
    ) -> dict[str, Any]:
        path = safe_child(self.paths.assets, relative)
        if not path.exists() or not path.is_file():
            raise FileNotFoundError(f"找不到要替換的素材 {relative}")
        if path.suffix.lower() not in self.ALLOWED_EXTENSIONS:
            raise ValueError(f"不允許替換此副檔名: {path.suffix}")
        if source_name:
            target_ext = self._canonical_extension(path.name)
            source_ext = self._canonical_extension(source_name)
            if target_ext != source_ext:
                raise ValueError(
                    f"替換素材格式不一致: 目標 {path.suffix.lower()}，"
                    f"來源 {Path(source_name).suffix.lower() or '(無副檔名)'}"
                )
        try:
            payload = base64.b64decode(base64_content, validate=True)
        except Exception as exc:
            raise ValueError("Base64 素材內容無效") from exc
        if len(payload) > self.MAX_REPLACE_BYTES:
            raise ValueError("單一素材不可超過 25 MB")
        self.backups.atomic_write_bytes(path, payload, "asset")
        return {
            "path": path.relative_to(self.paths.assets).as_posix(),
            "name": path.name,
            "size": len(payload),
            "extension": path.suffix.lower(),
            "previewable": path.suffix.lower() in self.PREVIEW_EXTENSIONS,
        }
