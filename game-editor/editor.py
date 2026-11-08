from __future__ import annotations

import argparse
import json
import mimetypes
import re
import sys
import threading
import traceback
import urllib.parse
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

from sgo_editor.asset_repo import AssetRepository
from sgo_editor.enemy_repo import EnemyRepository
from sgo_editor.map_repo import MapRepository
from sgo_editor.health_repo import HealthRepository
from sgo_editor.history_repo import HistoryRepository
from sgo_editor.paths import ProjectPaths
from sgo_editor.quest_repo import QuestRepository
from sgo_editor.skill_repo import SkillRepository
from sgo_editor.storage import BackupManager, UnsafePathError, safe_child


class EditorApplication:
    def __init__(self, paths: ProjectPaths):
        self.paths = paths
        backups = BackupManager(paths)
        self.skills = SkillRepository(paths, backups)
        self.enemies = EnemyRepository(paths, backups)
        self.maps = MapRepository(paths, backups)
        self.quests = QuestRepository(paths, backups)
        self.assets = AssetRepository(paths, backups)
        self.health = HealthRepository(paths, self.enemies, self.maps, self.quests, self.skills)
        self.history = HistoryRepository(paths, backups)
        self.mutation_lock = threading.RLock()
        self.web_root = Path(__file__).resolve().parent / "web"


class EditorHandler(BaseHTTPRequestHandler):
    server_version = "SGOGameEditor/1.0"
    app: EditorApplication

    def log_message(self, fmt: str, *args: Any) -> None:
        sys.stdout.write("[editor] " + (fmt % args) + "\n")

    @staticmethod
    def _parse_host_header(value: str) -> str | None:
        raw = (value or "").strip()
        if not raw:
            return ""
        if raw == "::1":
            return "::1"
        if raw.startswith("["):
            close = raw.find("]")
            if close <= 1:
                return None
            host = raw[1:close].lower()
            suffix = raw[close + 1:]
            if suffix and not re.fullmatch(r":\d+", suffix):
                return None
            return host
        if raw.count(":") > 1:
            return None
        if ":" in raw:
            host, port = raw.rsplit(":", 1)
            if not port.isdigit():
                return None
        else:
            host = raw
        return host.lower()

    def _local_host_only(self) -> bool:
        host = self._parse_host_header(self.headers.get("Host") or "")
        return host in {"127.0.0.1", "localhost", "::1", ""}

    def _json_body(self) -> Any:
        length = int(self.headers.get("Content-Length") or 0)
        if length < 0:
            raise ValueError("Content-Length 不可為負數")
        if length > 30 * 1024 * 1024:
            raise ValueError("Request 太大")
        raw = self.rfile.read(length) if length else b"{}"
        return json.loads(raw.decode("utf-8"))

    @staticmethod
    def _map_mutation_payload(body: Any) -> tuple[list[dict[str, Any]], str, bool]:
        # Legacy callers may still PUT a raw list. New editor callers use an
        # envelope so stale-tab protection cannot be accidentally omitted.
        if isinstance(body, list):
            return body, "", False
        if not isinstance(body, dict):
            raise ValueError("地圖儲存 payload 必須是陣列或物件")
        if "items" not in body or not isinstance(body.get("items"), list):
            raise ValueError("地圖儲存 payload 必須包含 items 陣列")
        expected_revision = str(body.get("expectedRevision", "")).strip()
        if not expected_revision:
            raise ValueError("地圖儲存 payload 缺少 expectedRevision；請重新載入地圖後再儲存")
        return body["items"], expected_revision, True

    def _send_json(self, payload: Any, status: int = 200) -> None:
        body = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _send_bytes(self, payload: bytes, content_type: str, status: int = 200, cache: str = "no-store") -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", cache)
        self.end_headers()
        self.wfile.write(payload)

    def _error(self, exc: Exception) -> None:
        if isinstance(exc, FileNotFoundError):
            status = HTTPStatus.NOT_FOUND
        elif isinstance(exc, (ValueError, UnsafePathError, json.JSONDecodeError)):
            status = HTTPStatus.BAD_REQUEST
        else:
            status = HTTPStatus.INTERNAL_SERVER_ERROR
            traceback.print_exc()
        self._send_json({"error": str(exc), "type": exc.__class__.__name__}, int(status))

    def do_GET(self) -> None:
        if not self._local_host_only():
            self._send_json({"error": "Editor 僅接受 localhost Host"}, 403)
            return
        try:
            parsed = urllib.parse.urlsplit(self.path)
            path = parsed.path
            query = urllib.parse.parse_qs(parsed.query)
            if path == "/api/status":
                self._send_json({
                    "projectRoot": str(self.app.paths.root),
                    "skills": len(self.app.skills.list()),
                    "enemies": len(self.app.enemies.list()),
                    "maps": len(self.app.maps.list_maps()),
                    "scripts": len(self.app.quests.list()),
                    "assetFolders": self.app.assets.folders(),
                })
                return
            if path == "/api/health":
                force = query.get("refresh", ["0"])[0] in {"1", "true", "yes"}
                self._send_json(self.app.health.scan(force=force))
                return
            if path == "/api/history":
                self._send_json(self.app.history.list(
                    query.get("q", [""])[0],
                    int(query.get("limit", ["300"])[0]),
                ))
                return
            if path == "/api/history/detail":
                backup_id = query.get("id", [""])[0]
                self._send_json(self.app.history.detail(backup_id))
                return
            if match := re.fullmatch(r"/api/references/(enemy|script)/(.+)", path):
                kind = match.group(1)
                key = urllib.parse.unquote(match.group(2))
                self._send_json(self.app.health.references(kind, key))
                return
            if path == "/api/skills":
                self._send_json(self.app.skills.list(query.get("q", [""])[0]))
                return
            if match := re.fullmatch(r"/api/skills/(\d+)", path):
                self._send_json(self.app.skills.get(int(match.group(1))))
                return
            if path == "/api/enemies":
                self._send_json(self.app.enemies.list(query.get("q", [""])[0]))
                return
            if match := re.fullmatch(r"/api/enemies/(\d+)", path):
                self._send_json(self.app.enemies.get(int(match.group(1))))
                return
            if path == "/api/maps":
                self._send_json(self.app.maps.list_maps())
                return
            if match := re.fullmatch(r"/api/maps/(\d+)/editor-meta", path):
                self._send_json(self.app.maps.get_editor_meta(int(match.group(1))))
                return
            if match := re.fullmatch(r"/api/maps/(\d+)/blocking\.png", path):
                payload = self.app.maps.blocking_overlay_png(int(match.group(1)))
                self._send_bytes(payload, "image/png", cache="private, max-age=15")
                return
            if match := re.fullmatch(r"/api/maps/(\d+)/preview\.png", path):
                preview = self.app.maps.preview_file(int(match.group(1)))
                self._send_bytes(preview.read_bytes(), "image/png", cache="private, max-age=30")
                return
            if match := re.fullmatch(r"/api/maps/(\d+)/terrain", path):
                map_id = int(match.group(1))
                x = float(query.get("x", ["0"])[0])
                y = float(query.get("y", ["0"])[0])
                self._send_json(self.app.maps.get_terrain(map_id, x, y))
                return
            if match := re.fullmatch(r"/api/maps/(\d+)/enemies", path):
                map_id = int(match.group(1))
                with self.app.mutation_lock:
                    items = self.app.maps.get_enemy_spawns(map_id)
                    revision = self.app.maps.spawn_revision(map_id, "enemies")
                if query.get("envelope", ["0"])[0] in {"1", "true", "yes"}:
                    self._send_json({"items": items, "revision": revision})
                else:
                    self._send_json(items)
                return
            if match := re.fullmatch(r"/api/maps/(\d+)/npcs", path):
                map_id = int(match.group(1))
                with self.app.mutation_lock:
                    items = self.app.maps.get_npc_spawns(map_id)
                    revision = self.app.maps.spawn_revision(map_id, "npcs")
                if query.get("envelope", ["0"])[0] in {"1", "true", "yes"}:
                    self._send_json({"items": items, "revision": revision})
                else:
                    self._send_json(items)
                return
            if path == "/api/scripts":
                self._send_json(self.app.quests.list(query.get("q", [""])[0]))
                return
            if match := re.fullmatch(r"/api/scripts/(.+)", path):
                name = urllib.parse.unquote(match.group(1))
                self._send_json(self.app.quests.get(name))
                return
            if path == "/api/assets":
                self._send_json(self.app.assets.list(
                    query.get("folder", [""])[0],
                    query.get("q", [""])[0],
                    int(query.get("limit", ["500"])[0]),
                ))
                return
            if path == "/api/assets/file":
                rel = query.get("path", [""])[0]
                file_path, mime = self.app.assets.get_file(rel)
                self._send_bytes(file_path.read_bytes(), mime, cache="private, max-age=30")
                return
            if path.startswith("/api/"):
                self._send_json({"error": "找不到 API"}, 404)
                return
            self._serve_static(path)
        except Exception as exc:
            self._error(exc)

    def do_PUT(self) -> None:
        self._mutate("PUT")

    def do_POST(self) -> None:
        self._mutate("POST")

    def do_DELETE(self) -> None:
        self._mutate("DELETE")

    def _mutate(self, method: str) -> None:
        if not self._local_host_only():
            self._send_json({"error": "Editor 僅接受 localhost Host"}, 403)
            return
        try:
            parsed = urllib.parse.urlsplit(self.path)
            path = parsed.path
            query = urllib.parse.parse_qs(parsed.query)
            force = query.get("force", ["0"])[0].lower() in {"1", "true", "yes"}
            body = {} if method == "DELETE" else self._json_body()
            if path == "/api/history/restore":
                if method == "DELETE":
                    raise ValueError("不支援 DELETE")
                backup_id = str(body.get("id", "")).strip()
                with self.app.mutation_lock:
                    result = self.app.history.restore(backup_id)
                self.app.health.invalidate()
                self._send_json(result)
                return
            if match := re.fullmatch(r"/api/skills/(\d+)", path):
                skill_id = int(match.group(1))
                if method == "DELETE":
                    self.app.skills.delete(skill_id)
                    result = {"ok": True}
                else:
                    result = self.app.skills.save(skill_id, body)
                self.app.health.invalidate()
                self._send_json(result)
                return
            if match := re.fullmatch(r"/api/enemies/(\d+)", path):
                enemy_id = int(match.group(1))
                with self.app.mutation_lock:
                    if method == "DELETE":
                        impact = self.app.health.references("enemy", str(enemy_id))
                        if impact["count"] and not force:
                            raise ValueError(
                                f"怪物 {enemy_id} 仍有 {impact['count']} 筆引用；"
                                "請先處理引用，或經使用者確認後以 force=1 刪除"
                            )
                        self.app.enemies.delete(enemy_id)
                        result = {"ok": True}
                    else:
                        result = self.app.enemies.save(enemy_id, body)
                self.app.health.invalidate()
                self._send_json(result)
                return
            if match := re.fullmatch(r"/api/maps/(\d+)/validate", path):
                if method == "DELETE":
                    raise ValueError("不支援 DELETE")
                map_id = int(match.group(1))
                tab = str(body.get("tab", ""))
                items = body.get("items", [])
                if not isinstance(items, list):
                    raise ValueError("items 必須是陣列")
                self._send_json(self.app.health.validate_map_items(map_id, tab, items))
                return
            if match := re.fullmatch(r"/api/maps/(\d+)/enemies", path):
                if method == "DELETE":
                    raise ValueError("不支援直接 DELETE；請 PUT 新的出生點清單")
                map_id = int(match.group(1))
                items, expected_revision, envelope = self._map_mutation_payload(body)
                with self.app.mutation_lock:
                    current_revision = self.app.maps.spawn_revision(map_id, "enemies")
                    if expected_revision and expected_revision != current_revision:
                        raise ValueError(
                            "此地圖怪物資料已被其他 Editor 分頁或程序修改；"
                            "請先重新整理地圖，再重新套用本次修改"
                        )
                    report = self.app.health.validate_map_items(map_id, "enemies", items)
                    if report["errors"]:
                        raise ValueError("地圖驗證失敗：" + "；".join(x["message"] for x in report["errors"][:5]))
                    result = self.app.maps.save_enemy_spawns(map_id, items)
                    revision = self.app.maps.spawn_revision(map_id, "enemies")
                self.app.health.invalidate()
                self._send_json({"items": result, "revision": revision} if envelope else result)
                return
            if match := re.fullmatch(r"/api/maps/(\d+)/npcs", path):
                if method == "DELETE":
                    raise ValueError("不支援直接 DELETE；請 PUT 新的 NPC 清單")
                map_id = int(match.group(1))
                items, expected_revision, envelope = self._map_mutation_payload(body)
                with self.app.mutation_lock:
                    current_revision = self.app.maps.spawn_revision(map_id, "npcs")
                    if expected_revision and expected_revision != current_revision:
                        raise ValueError(
                            "此地圖 NPC 資料已被其他 Editor 分頁或程序修改；"
                            "請先重新整理地圖，再重新套用本次修改"
                        )
                    report = self.app.health.validate_map_items(map_id, "npcs", items)
                    if report["errors"]:
                        raise ValueError("地圖驗證失敗：" + "；".join(x["message"] for x in report["errors"][:5]))
                    result = self.app.maps.save_npc_spawns(map_id, items)
                    revision = self.app.maps.spawn_revision(map_id, "npcs")
                self.app.health.invalidate()
                self._send_json({"items": result, "revision": revision} if envelope else result)
                return
            if match := re.fullmatch(r"/api/maps/(\d+)/snap-points", path):
                if method == "DELETE":
                    raise ValueError("不支援 DELETE")
                points = body.get("points", []) if isinstance(body, dict) else []
                snap = bool(body.get("snap", True)) if isinstance(body, dict) else True
                if not isinstance(points, list):
                    raise ValueError("points 必須是陣列")
                self._send_json(self.app.maps.snap_points(int(match.group(1)), points, snap))
                return
            if path == "/api/scripts/validate" and method != "DELETE":
                self._send_json(self.app.quests.validate(str(body.get("name", "draft.txt")), str(body.get("content", ""))))
                return
            if match := re.fullmatch(r"/api/scripts/(.+)", path):
                name = urllib.parse.unquote(match.group(1))
                with self.app.mutation_lock:
                    if method == "DELETE":
                        impact = self.app.health.references("script", name)
                        if impact["count"] and not force:
                            raise ValueError(
                                f"NPC Script {name} 仍有 {impact['count']} 筆引用；"
                                "請先處理引用，或經使用者確認後以 force=1 刪除"
                            )
                        self.app.quests.delete(name)
                        result = {"ok": True}
                    else:
                        result = self.app.quests.save(name, str(body.get("content", "")), bool(body.get("force", False)))
                self.app.health.invalidate()
                self._send_json(result)
                return
            if path == "/api/assets" and method != "DELETE":
                result = self.app.assets.replace(
                    str(body.get("path", "")),
                    str(body.get("contentBase64", "")),
                    str(body.get("sourceName", "")) or None,
                )
                self.app.health.invalidate()
                self._send_json(result)
                return
            self._send_json({"error": "找不到 API"}, 404)
        except Exception as exc:
            self._error(exc)

    def _serve_static(self, request_path: str) -> None:
        rel = request_path.lstrip("/") or "index.html"
        try:
            path = safe_child(self.app.web_root, rel)
        except UnsafePathError:
            self._send_json({"error": "Invalid path"}, 400)
            return
        if path.is_dir():
            path = path / "index.html"
        if not path.exists() or not path.is_file():
            # SPA fallback
            path = self.app.web_root / "index.html"
        mime = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
        if mime.startswith("text/") or mime in {"application/javascript", "application/json"}:
            mime += "; charset=utf-8"
        self._send_bytes(path.read_bytes(), mime, cache="no-cache")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="SGO Game Data Web Editor")
    parser.add_argument("--project-root", help="SGO repository root")
    parser.add_argument("--host", default="127.0.0.1", help="Bind host; default 127.0.0.1")
    parser.add_argument("--port", type=int, default=8765, help="Bind port; default 8765")
    return parser


def main() -> int:
    args = build_parser().parse_args()
    if args.host not in {"127.0.0.1", "localhost", "::1"}:
        print("[warning] 為避免誤把檔案編輯 API 暴露到網路，Editor 只允許 localhost bind。", file=sys.stderr)
        return 2
    paths = ProjectPaths.resolve(args.project_root)
    app = EditorApplication(paths)
    handler_type = type("BoundEditorHandler", (EditorHandler,), {"app": app})
    server = ThreadingHTTPServer((args.host, args.port), handler_type)
    print(f"SGO Game Editor: http://{args.host}:{args.port}")
    print(f"Project root: {paths.root}")
    print("Ctrl+C 結束。Editor 不會掛進遊戲 Server。")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nEditor stopped.")
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
