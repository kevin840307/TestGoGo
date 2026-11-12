using SGO.Server.Application.Combat;

namespace SGO.Server.SmokeTests.World;

internal static class PvpRulesSmokeTests
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;

        Check(!PvpService.IsPvpMapId(15),
            "legacy main city map 15 is non-PvP");
        Check(!PvpService.IsPvpMapId(7) &&
              !PvpService.IsPvpMapId(25) &&
              !PvpService.IsPvpMapId(38),
            "legacy NonPKMap entries remain protected");
        Check(PvpService.IsPvpMapId(13),
            "Nicia plateau map 13 permits PvP");

        var blue = Character(pkStatus: 0);
        Check(!PvpService.IsNormalWhite(blue, now),
            "blue-name player is not a normal white PvP target");
        Check(PvpService.NameColorState(blue, now) == 0,
            "blue/white base color is distinguished by PKStatus");

        var white = Character(pkStatus: 1);
        Check(PvpService.IsNormalWhite(white, now),
            "white-name player is a normal PvP target");
        Check(PvpService.NameColorState(white, now) == 0,
            "normal white player has base color state");

        var pink = Character(
            pkStatus: 1,
            pinkUntilUtcMs: now.AddSeconds(20).ToUnixTimeMilliseconds());
        Check(!PvpService.IsNormalWhite(pink, now),
            "pink attacker is not treated as a white victim");
        Check(PvpService.NameColorState(pink, now) == 2,
            "active hostile timer renders solid pink");

        var flashingPink = Character(
            pkStatus: 1,
            pinkUntilUtcMs: now.AddSeconds(2).ToUnixTimeMilliseconds());
        Check(PvpService.NameColorState(flashingPink, now) == 3,
            "last three pink seconds render flashing pink");

        var red = Character(pkStatus: 1, redMan: 1);
        Check(!PvpService.IsNormalWhite(red, now),
            "red player is not a white victim and does not escalate killers");
        Check(PvpService.NameColorState(red, now) == 1,
            "red player renders red name state");
    }

    private static Dictionary<string, object> Character(
        int pkStatus,
        int redMan = 0,
        long pinkUntilUtcMs = 0)
        => new()
        {
            ["PKStatus"] = pkStatus,
            ["RedMan"] = redMan,
            ["PKPinkUntilUtcMs"] = pinkUntilUtcMs
        };

    private static void Check(bool ok, string label)
        => SGO.Server.SmokeTests.SmokeAssert.Check(ok, label);
}
