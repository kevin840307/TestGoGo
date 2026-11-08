using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Fixtures;

/// <summary>
/// Newly created game characters start with 10,000 money. Economy tests
/// commonly require an exact low balance; adding 100 to starter money would
/// make overspend, repair, trade and shop race assertions meaningless.
/// </summary>
internal static class ExactMoneyFixture
{
    public static void Set(
        SqliteGameStore store,
        long accountId,
        long characterId,
        long target,
        string scenario)
    {
        var character = store.GetCharacter(accountId, characterId);
        Check(character is not null,
            $"character exists for money seed: {scenario}");
        var current = Convert.ToInt64(character!["Money"]);
        var delta = checked((int)(target - current));
        var updated = store.AdjustMoney(
            accountId, characterId, delta);
        Check(updated is not null &&
              Convert.ToInt64(updated["Money"]) == target &&
              Convert.ToInt64(store.GetCharacter(
                  accountId, characterId)!["Money"]) == target,
            $"seed exact money={target} for {scenario}");
    }
}
