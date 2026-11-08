using System.Text.Json;
using SGO.Server.Infrastructure.Quest;
using SGO.Server.SmokeTests.Fixtures;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class QuestDialogSmokeTests
{
    public static void Run()
    {
        Check(DialogFormatter.Parse("n1歡迎").Text == "歡迎",
            "n1 prefix is not rendered literally");
        Check(DialogFormatter.Parse("n3驚訝").Prefix == "n3",
            "numbered NPC presentation is retained");
        Check(DialogFormatter.Parse("p7獲得星幣").Speaker == "Player" &&
              DialogFormatter.Parse("p7獲得星幣").Layout == "Narration",
            "p7 event uses player-side narration");
        Check(DialogFormatter.Parse("n7思考").Layout == "Narration",
            "n7 narration is preserved");
        Check(DialogFormatter.Parse("b第一段#第二段").Text ==
              "第一段\n\n第二段",
            "legacy b help page breaks are supported");
        Check(DialogFormatter.Parse("h熱鍵操作").Layout == "Help",
            "h help layout is retained");
        Check(DialogFormatter.Parse("m#image.spr#na#sound.mp3#").Text ==
              "m#image.spr#na#sound.mp3#",
            "legacy media script strings are not corrupted");
        Check(DialogFormatter.Parse("一般文字").Text == "一般文字",
            "unmarked text remains unchanged");
        foreach (var plain in new[] { "hello", "please travel", "shop", "cancel", "help", "booking" })
        {
            Check(DialogFormatter.Parse(plain).Text == plain &&
                  DialogFormatter.Parse(plain).Prefix == "",
                "unmarked English dialogue is never stripped: " + plain);
        }
        Check(DialogFormatter.Parse("n8未知樣式").Text == "n8未知樣式" &&
              DialogFormatter.Parse("p12未定義").Text == "p12未定義" &&
              DialogFormatter.Parse("s3未定義").Text == "s3未定義",
            "unknown numeric prefixes are not silently consumed");
        Check(DialogFormatter.Parse("n1Hello").Text == "Hello" &&
              DialogFormatter.Parse("n1Hello").Prefix == "n1",
            "recognized numbered codes support English dialogue");
        Check(DialogFormatter.PlainInlineText(
                "前往" + DialogFormatter.ColorToken(12, 34, 56) + "皇城") ==
              "前往皇城" &&
              DialogFormatter.PlainInlineText("please") == "please",
            "choice label formatting removes colors without changing ordinary text");

        var formatted = DialogFormatter.Parse(
            "前往" + DialogFormatter.ColorToken(0, 45, 198) +
            "尼尼亞皇城" + DialogFormatter.ColorToken(0, 0, 0) + "嗎？");
        Check(formatted.Text ==
              "前往[color=#002DC6]尼尼亞皇城[/color]嗎？" &&
              formatted.PlainText == "前往尼尼亞皇城嗎？",
            "RGB directives generate balanced BBCode and plain bubble text");

        using var temp = new TemporaryDirectoryFixture("sgo-dialog-");
        File.WriteAllText(
            Path.Combine(temp.Root, "LegacyDialogTest.txt"),
            """
            function start()
                Say("n1去找"..Color(0,45,198).."大牧師"..Color(0,0,0).."吧！")
                Say("p1好的。")
                Say("p7得到獎勵。")
                Ask("h熱鍵操作", "返回目錄", "Done")
                Ask("p確定要去尼尼亞皇城嗎?", "確定", "取消", "Answer")
                Ask("n前往哪裡?", "到"..Color(0,45,198).."皇城"..Color(0,0,0), "不去", "Choice")
            end
            """);

        // These scripts exercise only Say/Ask/Color; no shop access is made.
        var runtime = new QuestRuntime(temp.Root, null!);
        var result = runtime.Execute(
            "LegacyDialogTest.txt",
            "start",
            123,
            "守衛",
            null,
            _ => 0);

        using var doc = JsonDocument.Parse(
            JsonSerializer.Serialize(result.Packets));
        var dialogs = doc.RootElement;

        Check(dialogs.GetArrayLength() == 6,
            "Lua quest preserves all six dialogue events");

        var colored = dialogs[0];
        Check(colored.GetProperty("Text").GetString() ==
              "去找[color=#002DC6]大牧師[/color]吧！" &&
              colored.GetProperty("PlainText").GetString() ==
              "去找大牧師吧！" &&
              colored.GetProperty("Speaker").GetString() == "NPC" &&
              colored.GetProperty("LegacyPrefix").GetString() == "n1",
            "Lua Say() restores inline color and NPC identity");

        Check(dialogs[1].GetProperty("Speaker").GetString() == "Player" &&
              dialogs[1].GetProperty("LegacyPrefix").GetString() == "p1",
            "p1 Say() correctly routes to the player");
        Check(dialogs[2].GetProperty("DialogLayout").GetString() == "Narration",
            "p7 Say() reports a narrative event");
        Check(dialogs[3].GetProperty("DialogLayout").GetString() == "Help" &&
              dialogs[3].GetProperty("Options")[0]
                  .GetProperty("TargetNode").GetString() == "Done#0",
            "h help-page Ask() keeps its option callback");
        Check(dialogs[4].GetProperty("Speaker").GetString() == "Player" &&
              dialogs[4].GetProperty("LegacyPrefix").GetString() == "p" &&
              dialogs[4].GetProperty("Options").GetArrayLength() == 2 &&
              dialogs[4].GetProperty("Options")[1]
                  .GetProperty("TargetNode").GetString() == "Answer#1",
            "p Ask() preserves selectable routes");
        Check(dialogs[5].GetProperty("Options")[0]
                  .GetProperty("Text").GetString() == "到皇城" &&
              dialogs[5].GetProperty("Options")[0]
                  .GetProperty("TargetNode").GetString() == "Choice#0" &&
              dialogs[5].GetProperty("Options")[1]
                  .GetProperty("TargetNode").GetString() == "Choice#1",
            "Lua Color() in choice text is removed without changing callback mapping");
    }
}
