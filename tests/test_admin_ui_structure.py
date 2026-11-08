import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
ADMIN = ROOT / "server" / "AdminUi"

class AdminUiStructureTests(unittest.TestCase):
    def test_ui_is_split_into_structure_style_and_behavior(self):
        html = (ADMIN / "index.html").read_text(encoding="utf-8")
        self.assertTrue((ADMIN / "styles.css").is_file())
        self.assertTrue((ADMIN / "app.js").is_file())
        self.assertIn('/admin/assets/styles.css', html)
        self.assertIn('/admin/assets/app.js', html)
        self.assertNotIn('<style>', html)
        self.assertNotIn('<script>', html)

    def test_menu_pages_are_present(self):
        html = (ADMIN / "index.html").read_text(encoding="utf-8")
        for page in ("overview", "analysis", "gm", "players", "logs"):
            self.assertIn(f'data-page="{page}"', html)
            self.assertIn(f'data-page-view="{page}"', html)

    def test_low_impact_refresh_policy_is_preserved(self):
        js = (ADMIN / "app.js").read_text(encoding="utf-8")
        self.assertIn("setInterval(refresh,10000)", js)
        self.assertIn("document.hidden", js)
        self.assertIn("if(state.refreshing||document.hidden)return", js)

    def test_player_search_and_rate_controls_exist(self):
        html = (ADMIN / "index.html").read_text(encoding="utf-8")
        js = (ADMIN / "app.js").read_text(encoding="utf-8")
        for control in (
            "globalPlayerSearch",
            "rateExp",
            "rateMoney",
            "rateDrop",
            "rateDuration",
            "rateApplyBtn",
            "rateCancelBtn",
        ):
            self.assertIn(control, html)
        self.assertIn("function renderGlobalSearch()", js)
        self.assertIn("function applyRates()", js)
        self.assertIn("function cancelRates()", js)
        self.assertIn("fetch('/admin/api/rates'", js)

if __name__ == "__main__":
    unittest.main()
