from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path

@dataclass(frozen=True)
class ProjectPaths:
    root: Path

    @classmethod
    def resolve(cls, configured: str | os.PathLike[str] | None = None) -> "ProjectPaths":
        candidates: list[Path] = []
        if configured:
            candidates.append(Path(configured))
        if os.getenv("SGO_PROJECT_ROOT"):
            candidates.append(Path(os.environ["SGO_PROJECT_ROOT"]))
        here = Path(__file__).resolve()
        candidates.extend([Path.cwd(), here.parent.parent.parent])
        candidates.extend(here.parents)
        seen: set[Path] = set()
        for candidate in candidates:
            try:
                root = candidate.expanduser().resolve()
            except OSError:
                continue
            if root in seen:
                continue
            seen.add(root)
            if cls._looks_like_project(root):
                return cls(root)
        raise FileNotFoundError("找不到 SGO 專案根目錄。請用 --project-root 指定包含 client/server/Old 的目錄。")

    @staticmethod
    def _looks_like_project(root: Path) -> bool:
        return (root / "client").is_dir() and (root / "server").is_dir() and (root / "Old" / "服務端" / "Dat").is_dir()

    @property
    def skills(self) -> Path:
        return self.root / "client" / "Skills"

    @property
    def assets(self) -> Path:
        return self.root / "client" / "Assets"

    @property
    def legacy_dat(self) -> Path:
        return self.root / "Old" / "服務端" / "Dat"

    @property
    def enemy_csv(self) -> Path:
        return self.legacy_dat / "Enemy" / "Enemy_Data.csv"

    @property
    def maps(self) -> Path:
        return self.legacy_dat / "Map"

    @property
    def quests(self) -> Path:
        return self.legacy_dat / "Quest"

    @property
    def backups(self) -> Path:
        return self.root / "game-editor" / ".backups"

    def relative(self, path: Path) -> str:
        return path.resolve().relative_to(self.root.resolve()).as_posix()
