using System.Text.Json;
using SGO.Server.Application.Combat;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class SkillCastCenterSmokeTests
{
    private const int SelfCenterAoeSkillId = 99002;

    public static void Run(
        CombatService combat,
        SqliteGameStore store,
        ClientSession session,
        long accountId)
    {
        if (!session.CharacterId.HasValue)
            throw new InvalidOperationException(
                "Skill center smoke requires a selected character.");

        var characterId = session.CharacterId.Value;
        var centerMonster = session.Monsters.Values
            .First(x => x.IsAlive);

        store.SetSkill(
            accountId,
            characterId,
            SelfCenterAoeSkillId,
            1);

        store.UpdateLocation(
            accountId,
            characterId,
            session.CurrentMapId,
            Convert.ToInt32(centerMonster.X),
            Convert.ToInt32(centerMonster.Y));

        session.LastX = centerMonster.X;
        session.LastY = centerMonster.Y;
        session.SkillReadyAt.Remove(
            SelfCenterAoeSkillId);

        var inRangeBefore = session.Monsters.Values
            .Where(x =>
                x.IsAlive &&
                x.MapId == session.CurrentMapId &&
                Math.Pow(x.X - session.LastX, 2) +
                Math.Pow(x.Y - session.LastY, 2) <=
                300 * 300)
            .ToDictionary(
                x => x.EntityId,
                x => x.CurrentHp);

        Check(
            inRangeBefore.Count > 0,
            "self-centered AOE smoke has at least one monster in range");

        var packets = combat.UseSkill(
            session,
            accountId,
            SelfCenterAoeSkillId,
            checked((int)characterId),
            0,
            Convert.ToInt32(session.LastX),
            Convert.ToInt32(session.LastY),
            DateTimeOffset.UtcNow.AddMinutes(30));

        var skillCast = packets
            .Select(packet =>
                JsonSerializer.SerializeToElement(packet))
            .FirstOrDefault(element =>
                element.TryGetProperty(
                    "Type",
                    out var type) &&
                type.GetString() == "SkillCast");

        Check(
            skillCast.ValueKind == JsonValueKind.Object &&
            skillCast.GetProperty("TargetID").GetInt64() ==
                characterId &&
            skillCast.GetProperty("TargetX").GetInt32() ==
                Convert.ToInt32(session.LastX) &&
            skillCast.GetProperty("TargetY").GetInt32() ==
                Convert.ToInt32(session.LastY),
            "self-centered damaging AOE uses caster as target and caster coordinates as center");

        var affectedIds = skillCast
            .GetProperty("AoeTargets")
            .EnumerateArray()
            .Select(x => x.GetProperty("ID").GetInt32())
            .ToHashSet();

        Check(
            inRangeBefore.Keys.All(affectedIds.Contains),
            "self-centered damaging AOE includes every alive monster inside its authoritative radius");

        Check(
            inRangeBefore.Any(pair =>
                session.Monsters.TryGetValue(
                    pair.Key,
                    out var monster) &&
                monster.CurrentHp < pair.Value),
            "self-centered damaging AOE applies damage through the shared batch pipeline");
    }
}
