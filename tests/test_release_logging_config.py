import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
SERVER = ROOT / "server"


class ReleaseLoggingConfigTests(unittest.TestCase):
    def test_release_default_does_not_enable_debug(self):
        data = json.loads((SERVER / "appsettings.json").read_text(encoding="utf-8"))
        level = data["Logging"]["LogLevel"]["SGO"]
        self.assertIn(level, {"Information", "Warning", "Error", "Critical", "None"})
        self.assertNotEqual(level, "Debug")
        self.assertNotEqual(level, "Trace")

    def test_development_explicitly_enables_debug(self):
        data = json.loads(
            (SERVER / "appsettings.Development.json").read_text(encoding="utf-8")
        )
        self.assertEqual(data["Logging"]["LogLevel"]["SGO"], "Debug")
        self.assertEqual(
            data["Logging"]["LogLevel"]["Microsoft.AspNetCore"],
            "Warning",
        )


if __name__ == "__main__":
    unittest.main()
