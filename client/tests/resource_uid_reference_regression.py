"""Check that skill .tres Script UIDs agree with their committed .gd.uid.

Run: python client/tests/resource_uid_reference_regression.py
No Godot, network, editor caches or third-party packages are required.
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKILLS = ROOT / "Skills"
SCRIPTS = ROOT / "scripts"
SKILL_SCRIPT_REF = re.compile(
    r'\[ext_resource\s+type="Script"\s+uid="(?P<uid>uid://[^"]+)"'
    r'\s+path="res://scripts/(?P<name>skill_(?:level_)?data\.gd)"'
)


class SkillResourceUIDRegression(unittest.TestCase):
    def test_all_skill_script_uid_references_are_correct(self):
        expected = {
            name: (SCRIPTS / (name + ".uid")).read_text(encoding="utf-8").strip()
            for name in ("skill_data.gd", "skill_level_data.gd")
        }
        self.assertNotEqual(expected["skill_data.gd"], expected["skill_level_data.gd"])
        self.assertEqual(expected["skill_data.gd"], "uid://dyw8x4sgllvgj")
        self.assertEqual(expected["skill_level_data.gd"], "uid://dsegau1jwhqbf")

        examined = 0
        refs = 0
        for tres in SKILLS.rglob("*.tres"):
            examined += 1
            for match in SKILL_SCRIPT_REF.finditer(tres.read_text(encoding="utf-8")):
                refs += 1
                self.assertEqual(
                    match.group("uid"), expected[match.group("name")],
                    f"{tres.relative_to(ROOT)} points to stale script UID"
                )
        self.assertGreaterEqual(examined, 200, "most skill resources are missing")
        self.assertGreaterEqual(refs, 200, "skill resource Script UID references missing")


if __name__ == "__main__":
    unittest.main(verbosity=2)
