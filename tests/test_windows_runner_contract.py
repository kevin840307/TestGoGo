"""Static regression guards for the Windows BAT/PowerShell test runner.

Run from any cwd with: python tests/test_windows_runner_contract.py
These checks are NOT a substitute for actually running on Windows.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
BAT = ROOT / "pull_build_test.bat"
RUNNER = ROOT / "tools" / "sgo_test_runner.ps1"
GITIGNORE = ROOT / ".gitignore"


class TestWindowsRunnerContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.bat = BAT.read_text(encoding="utf-8")
        cls.ps1 = RUNNER.read_text(encoding="utf-8")

    def test_double_click_menu_and_noninteractive_modes(self):
        for mode in ("Full", "Aoi", "Wire100", "WireAll", "BuildOnly"):
            with self.subTest(mode=mode):
                self.assertIn(mode, self.ps1)
        # The BAT is intentionally small so other contributors can update it
        # independently of the interactive menu.
        self.assertIn('powershell.exe', self.bat)
        self.assertIn('-File "%RUNNER%" %*', self.bat)
        self.assertIn("[string]$Mode = 'Menu'", self.ps1)
        self.assertIn("Read-Host 'Choose [1-5,Q]'", self.ps1)

    def test_safe_git_pull(self):
        self.assertIn("status --porcelain --untracked-files=no", self.ps1)
        self.assertIn("'pull', '--ff-only', 'origin', $branch", self.ps1)
        self.assertIn("feature/local-server-net8", self.ps1)
        self.assertNotIn("git reset --hard", self.ps1.lower())
        self.assertNotIn("git clean -fd", self.ps1.lower())
        self.assertNotIn("git stash", self.ps1.lower())

    def test_build_and_smoke(self):
        for project in (
            "SGO.Server.csproj",
            "SGO.Server.SmokeTests.csproj",
            "SGO.Server.LoadProbe.csproj",
            "SGO.Server.WireLoadProbe.csproj",
        ):
            self.assertIn(project, self.ps1)
        self.assertIn("'--suite', 'all'", self.ps1)
        self.assertIn("test_global_rate_contract.py", self.ps1)
        self.assertIn("test_admin_ui_structure.py", self.ps1)

    def test_real_wire_uses_isolated_db_and_stops_process(self):
        for keyword in (
            "Database__Sqlite__Path",
            "game.db",
            "127.0.0.1:$port",
            "$port = 5688",
            "Game__GmCommandsEnabled",
            "Game__ClientRoot",
            "Check-TcpPortFree",
            "Stop-Process -Id $server.Id",
            "'--move-pixels', '4'",
            "'--setup-concurrency', '10'",
            "'--mixed-seconds', '30'",
            "RUN-1000",
        ):
            with self.subTest(keyword=keyword):
                self.assertIn(keyword, self.ps1)

    def test_reports_and_ignore(self):
        for keyword in (
            "Start-Transcript",
            "commit.txt",
            "aoi-moving.json",
            "wire-aoi-",
            "wire.stderr.txt",
            "wire.console.txt",
            "server.stdout.log",
            "server.stderr.log",
            "Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json",
        ):
            with self.subTest(keyword=keyword):
                self.assertIn(keyword, self.ps1)
        self.assertIn("/test-results/", GITIGNORE.read_text(encoding="utf-8"))

    def test_ascii_launcher(self):
        self.bat.encode("ascii")
        self.ps1.encode("ascii")


if __name__ == "__main__":
    unittest.main(verbosity=2)
