"""Static checks for the SGO Windows Godot client-test launcher.

This intentionally does not claim actual execution in Windows/Godot.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "run_godot_client_tests.bat"


class GodotClientBatContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.bat = SCRIPT.read_text(encoding="ascii")

    def test_launch_menu_and_valid_modes(self):
        for name in ("all", "render", "focus", "resources", "import", "configure"):
            with self.subTest(mode=name):
                self.assertIn('"%RUN_MODE%"=="' + name + '"', self.bat)
        self.assertIn("choice /C 12345PQ /N", self.bat)

    def test_remembers_user_executable_without_git_tracking(self):
        for token in (
            r"%LOCALAPPDATA%\SGO\GodotClientTests",
            r"\godot_path.txt",
            r"C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe",
            'set /p "ENTERED_GODOT=Godot EXE path: "',
            'if not exist "%ENTERED_GODOT%"',
            'echo(%GODOT_EXE%',
            'if exist "%CONFIG_FILE%"',
        ):
            with self.subTest(token=token):
                self.assertIn(token, self.bat)

    def test_tests_use_project_scene_not_standalone_script(self):
        self.assertIn("--editor --import", self.bat)
        self.assertIn('--scene "res://tests/%TEST_NAME%.tscn"', self.bat)
        self.assertNotIn(" --script ", self.bat)
        for name in (
            "multiclient_render_dedup_regression",
            "focus_performance_regression",
            "render_resource_regression",
        ):
            self.assertIn(name, self.bat)

    def test_failure_does_not_report_pass(self):
        self.assertIn('start "" /wait', self.bat)
        self.assertIn("findstr /L /C:", self.bat)
        self.assertIn("SCRIPT ERROR:", self.bat)
        self.assertIn("[SUCCESS] Requested Godot client tests passed", self.bat)
        self.assertIn("--quit-after 1200", self.bat)

    def test_diagnostics_and_no_server_mutation(self):
        self.assertIn(r"\test-results\godot-client-", self.bat)
        self.assertIn("--log-file", self.bat)
        self.assertIn("import.log", self.bat)
        self.assertNotIn("git reset", self.bat)
        self.assertNotIn("dotnet run", self.bat)


if __name__ == "__main__":
    unittest.main(verbosity=2)
