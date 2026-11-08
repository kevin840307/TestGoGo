"""Unit tests for legacy quest host-function compatibility audit."""
import tempfile
import unittest
from pathlib import Path

from quest_host_audit import scan


class QuestHostAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="sgo-quest-host-")
        self.root = Path(self.temp.name)
        self.write(
            "server/Infrastructure/Quest/QuestRuntime.cs",
            '''
script.Globals["Say"] = DynValue.NewCallback((ctx, args) => DynValue.Nil);
script.Globals["GetChoice"] = (Func<int>)(() => 0);
script.Globals["QuestEnd"] = (Action)(() => { });
''',
        )

    def tearDown(self):
        self.temp.cleanup()

    def write(self, path, body):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(body, encoding="utf-8")

    def test_bound_and_script_local_calls_are_accepted(self):
        self.write(
            "Old/服務端/Dat/Quest/ok.txt",
            '''
function LocalNode()
    Say("hello", "End")
end

function start()
    local value = tostring(GetChoice())
    LocalNode()
    QuestEnd()
end
''',
        )
        result = scan(self.root)
        self.assertEqual(1, result["quest_script_count"])
        self.assertEqual(0, result["unsupported_call_count"])
        self.assertEqual({}, result["unsupported_calls"])

    def test_unbound_host_call_reports_file_and_line(self):
        self.write(
            "Old/服務端/Dat/Quest/bad.txt",
            '''
function start()
    MissingLegacyApi(123)
end
''',
        )
        result = scan(self.root)
        self.assertEqual(1, result["unsupported_function_count"])
        self.assertIn("MissingLegacyApi", result["unsupported_calls"])
        issue = result["unsupported_calls"]["MissingLegacyApi"][0]
        self.assertTrue(issue["path"].endswith("bad.txt"))
        self.assertEqual(3, issue["line"])

    def test_comments_strings_and_member_calls_do_not_false_positive(self):
        self.write(
            "Old/服務端/Dat/Quest/noise.txt",
            '''
function start()
    -- FakeCall()
    Say("FakeStringCall()", "End")
    local x = math.floor(1.2)
    local y = string.len("x")
end
''',
        )
        result = scan(self.root)
        self.assertEqual({}, result["unsupported_calls"])


if __name__ == "__main__":
    unittest.main()
