import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
COMBAT = ROOT / "server" / "Application" / "Combat" / "CombatService.cs"
DROPS = ROOT / "server" / "Infrastructure" / "GameData" / "EnemyDropCatalog.cs"
ADMIN = ROOT / "server" / "Transport" / "Http" / "AdminEndpoints.cs"
UI = ROOT / "server" / "AdminUi"

class GlobalRateContractTests(unittest.TestCase):
    def test_combat_uses_centralized_rate_math_exactly_once_per_reward_path(self):
        text = COMBAT.read_text(encoding="utf-8")
        self.assertEqual(text.count("GlobalRateMath.ApplyReward("), 4)
        self.assertEqual(text.count("EffectiveExperience(now)"), 2)
        self.assertEqual(text.count("EffectiveMoney(now)"), 2)

    def test_pet_and_mecha_experience_are_not_affected_by_global_character_rate(self):
        text = COMBAT.read_text(encoding="utf-8")
        pet_start = text.index("private void AddActivePetKillExperience")
        mecha_start = text.index("private void AddMechaKillExperience")
        next_after_pet = text.find("private ", pet_start + 10)
        next_after_mecha = text.find("private ", mecha_start + 10)
        pet_block = text[pet_start: next_after_pet if next_after_pet > pet_start else len(text)]
        mecha_block = text[mecha_start: next_after_mecha if next_after_mecha > mecha_start else len(text)]
        self.assertNotIn("_globalRates", pet_block)
        self.assertNotIn("GlobalRateMath", pet_block)
        self.assertNotIn("_globalRates", mecha_block)
        self.assertNotIn("GlobalRateMath", mecha_block)

    def test_drop_rate_uses_one_centralized_probability_formula(self):
        text = DROPS.read_text(encoding="utf-8")
        self.assertIn("GlobalRateMath.ApplyDropChance(", text)
        self.assertEqual(text.count("GlobalRateMath.ApplyDropChance("), 1)
        self.assertNotIn("drop.Chance *", text)

    def test_admin_api_and_ui_rate_schema_stay_aligned(self):
        api = ADMIN.read_text(encoding="utf-8")
        html = (UI / "index.html").read_text(encoding="utf-8")
        js = (UI / "app.js").read_text(encoding="utf-8")
        self.assertIn('app.MapPost("/admin/api/rates"', api)
        self.assertIn('app.MapDelete("/admin/api/rates"', api)
        self.assertIn("ExperienceMultiplier", api)
        self.assertIn("MoneyMultiplier", api)
        self.assertIn("DropMultiplier", api)
        self.assertIn("DurationMinutes", api)
        self.assertIn("globalRates = globalRates.Snapshot()", api)
        for control in ("rateExp", "rateMoney", "rateDrop", "rateDuration"):
            self.assertIn(control, html)
        for field in (
            "experienceMultiplier",
            "moneyMultiplier",
            "dropMultiplier",
            "durationMinutes",
        ):
            self.assertIn(field, js)

    def test_player_search_is_snapshot_only_not_per_keystroke_api(self):
        js = (UI / "app.js").read_text(encoding="utf-8")
        search_start = js.index("function renderGlobalSearch()")
        search_end = js.index("function renderRates", search_start)
        search_body = js[search_start:search_end]
        self.assertNotIn("fetch(", search_body)
        self.assertIn("state.status.players", search_body)

if __name__ == "__main__":
    unittest.main()
