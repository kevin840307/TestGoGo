from __future__ import annotations

import json
import struct
import sys
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer
from pathlib import Path

GAME_EDITOR_ROOT = Path(__file__).resolve().parents[1]
if str(GAME_EDITOR_ROOT) not in sys.path:
    sys.path.insert(0, str(GAME_EDITOR_ROOT))

from editor import EditorApplication, EditorHandler
from sgo_editor.paths import ProjectPaths


class EditorHttpIntegrationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

        (self.root / "client" / "Skills").mkdir(parents=True)
        (self.root / "client" / "Assets" / "GameInfo").mkdir(parents=True)
        (self.root / "client" / "maps").mkdir(parents=True)
        (self.root / "server").mkdir()
        dat = self.root / "Old" / "服務端" / "Dat"
        (dat / "Enemy").mkdir(parents=True)
        (dat / "Map" / "Map13").mkdir(parents=True)
        (dat / "Quest").mkdir(parents=True)
        (self.root / "game-editor").mkdir()

        columns = [""] * 180
        columns[0] = columns[1] = "1"
        columns[2] = "HTTP測試怪物"
        columns[3] = "1"
        columns[6] = "EN001"
        columns[9] = "100"
        (dat / "Enemy" / "Enemy_Data.csv").write_text(
            ",".join(columns) + "\r\n",
            encoding="cp950",
        )

        map_dir = dat / "Map" / "Map13"
        (map_dir / "Enemy.dat").write_bytes(struct.pack("<i", 0))
        (map_dir / "NPC.dat").write_bytes(struct.pack("<i", 0))
        width = height = 8
        (map_dir / "Blocking.dat").write_bytes(
            struct.pack("<ii", width, height) + bytes(width * height)
        )

        self.paths = ProjectPaths.resolve(self.root)
        self.app = EditorApplication(self.paths)
        handler_type = type("HttpTestEditorHandler", (EditorHandler,), {"app": self.app})
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), handler_type)
        self.port = int(self.server.server_address[1])
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    def tearDown(self) -> None:
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=3)
        self.temp.cleanup()

    def request(self, method: str, path: str, body=None) -> tuple[int, dict]:
        data = None
        headers = {}
        if body is not None:
            data = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(
            f"http://127.0.0.1:{self.port}{path}",
            data=data,
            method=method,
            headers=headers,
        )
        try:
            with urllib.request.urlopen(request, timeout=5) as response:
                return int(response.status), json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            return int(exc.code), json.loads(exc.read().decode("utf-8"))

    def test_stale_map_revision_is_rejected_without_overwriting_first_save(self) -> None:
        status, initial = self.request("GET", "/api/maps/13/enemies?envelope=1")
        self.assertEqual(status, 200)
        self.assertEqual(initial["items"], [])
        revision_a = initial["revision"]
        self.assertEqual(len(revision_a), 64)

        first_rows = [{
            "index": -1,
            "enemyId": 1,
            "x": 24,
            "y": 24,
            "sourceFlags": 0,
        }]
        status, saved = self.request(
            "PUT",
            "/api/maps/13/enemies",
            {"items": first_rows, "expectedRevision": revision_a},
        )
        self.assertEqual(status, 200)
        self.assertEqual(saved["items"][0]["x"], 24)
        revision_b = saved["revision"]
        self.assertNotEqual(revision_a, revision_b)

        stale_rows = [{
            "index": -1,
            "enemyId": 1,
            "x": 40,
            "y": 40,
            "sourceFlags": 0,
        }]
        status, conflict = self.request(
            "PUT",
            "/api/maps/13/enemies",
            {"items": stale_rows, "expectedRevision": revision_a},
        )
        self.assertEqual(status, 400)
        self.assertIn("其他 Editor 分頁或程序修改", conflict["error"])

        status, after = self.request("GET", "/api/maps/13/enemies?envelope=1")
        self.assertEqual(status, 200)
        self.assertEqual(after["revision"], revision_b)
        self.assertEqual(len(after["items"]), 1)
        self.assertEqual(after["items"][0]["x"], 24)
        self.assertEqual(after["items"][0]["y"], 24)

    def test_force_delete_rejects_stale_reference_confirmation_token(self) -> None:
        status, initial = self.request("GET", "/api/maps/13/enemies?envelope=1")
        self.assertEqual(status, 200)
        first_rows = [{
            "index": -1,
            "enemyId": 1,
            "x": 24,
            "y": 24,
            "sourceFlags": 0,
        }]
        status, saved = self.request(
            "PUT",
            "/api/maps/13/enemies",
            {"items": first_rows, "expectedRevision": initial["revision"]},
        )
        self.assertEqual(status, 200)

        status, impact = self.request("GET", "/api/references/enemy/1")
        self.assertEqual(status, 200)
        self.assertEqual(impact["count"], 1)
        old_token = impact["token"]

        changed_rows = [
            dict(saved["items"][0]),
            {
                "index": -1,
                "enemyId": 1,
                "x": 40,
                "y": 40,
                "sourceFlags": 0,
            },
        ]
        status, changed = self.request(
            "PUT",
            "/api/maps/13/enemies",
            {"items": changed_rows, "expectedRevision": saved["revision"]},
        )
        self.assertEqual(status, 200)
        self.assertEqual(len(changed["items"]), 2)

        status, rejected = self.request(
            "DELETE",
            "/api/enemies/1?force=1&referenceToken=" + old_token,
        )
        self.assertEqual(status, 400)
        self.assertIn("引用在確認刪除後已發生變化", rejected["error"])

        status, enemy = self.request("GET", "/api/enemies/1")
        self.assertEqual(status, 200)
        self.assertEqual(int(enemy["id"]), 1)

        status, current_impact = self.request("GET", "/api/references/enemy/1")
        self.assertEqual(status, 200)
        self.assertEqual(current_impact["count"], 2)
        self.assertNotEqual(current_impact["token"], old_token)

    def test_raw_list_map_put_is_rejected_to_prevent_revision_bypass(self) -> None:
        status, payload = self.request(
            "PUT",
            "/api/maps/13/enemies",
            [{"index": -1, "enemyId": 1, "x": 24, "y": 24, "sourceFlags": 0}],
        )
        self.assertEqual(status, 400)
        self.assertIn("expectedRevision", payload["error"])

        status, after = self.request("GET", "/api/maps/13/enemies?envelope=1")
        self.assertEqual(status, 200)
        self.assertEqual(after["items"], [])


if __name__ == "__main__":
    unittest.main()
