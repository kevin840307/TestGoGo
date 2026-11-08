"""Regression guards for privileged GM command configuration."""
import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent


class GmSecurityTests(unittest.TestCase):
    def test_server_defaults_privileged_commands_off(self):
        source = (
            ROOT / "server" / "Application" / "Gm" / "GmCommandService.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertIn(
            '_config.GetValue<bool?>("Game:GmCommandsEnabled") ?? false',
            source,
        )
        self.assertNotIn(
            '_config.GetValue<bool?>("Game:GmCommandsEnabled") ?? true',
            source,
        )

        settings = json.loads(
            (ROOT / "server" / "appsettings.json").read_text(
                encoding="utf-8-sig"
            )
        )
        self.assertIs(
            False,
            settings["Game"]["GmCommandsEnabled"],
        )

    def test_only_isolated_test_launchers_opt_in(self):
        workflow = (
            ROOT / ".github" / "workflows" / "server-build.yml"
        ).read_text(encoding="utf-8-sig")
        runner = (
            ROOT / "tests" / "run_protocol_checks.ps1"
        ).read_text(encoding="utf-8-sig")
        smoke = (
            ROOT / "tests" / "SGO.Server.SmokeTests" / "Program.cs"
        ).read_text(encoding="utf-8-sig")

        self.assertIn(
            "export Game__GmCommandsEnabled=true",
            workflow,
        )
        self.assertIn(
            '$env:Game__GmCommandsEnabled = "true"',
            runner,
        )
        self.assertIn(
            'config["Game:GmCommandsEnabled"] = "true";',
            smoke,
        )


if __name__ == "__main__":
    unittest.main()
