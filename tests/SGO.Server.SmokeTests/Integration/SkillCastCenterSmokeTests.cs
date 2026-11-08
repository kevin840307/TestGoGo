using System.Text.Json;
using SGO.Server.Application.Combat;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class SkillCastCenterSmokeTests
{
    private const int SelfCenterAoeSkillId = 99002;

    private sealed record MonsterSnapshot(
        int CurrentHp,
        bool HasTarget,
        long TargetCharacterId,
        long TargetPetUid,
        MonsterAiState AiState,
        bool IsMoving,
        bool HoldingAttackRange,
        DateTimeOffset? RespawnAt);

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

        var inRangeMonsters = session.Monsters.Values
            .Where(x =>
                x.IsAlive &&
                x.MapId == session.CurrentMapId &&
                Math.Pow(x.X - session.LastX, 2) +
                Math.Pow(x.Y - session.LastY, 2) <=
                300 * 300)
            .ToArray();

        var snapshots = inRangeMonsters.ToDictionary(
            x => x.EntityId,
            x => new MonsterSnapshot(
                x.CurrentHp,
                x.HasTarget,
                x.TargetCharacterId,
                x.TargetPetUid,
                x.AiState,
                x.IsMoving,
                x.HoldingAttackRange,
                x.RespawnAt));

        var inRangeBefore = snapshots.ToDictionary(
            x => x.Key,
            x => x.Value.CurrentHp);

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

        foreach (var pair in snapshots)
        {
            if (!session.Monsters.TryGetValue(
                    pair.Key,
                    out var monster))
            {
                continue;
            }

            var snapshot = pair.Value;
            monster.CurrentHp = snapshot.CurrentHp;
            monster.HasTarget = snapshot.HasTarget;
            monster.TargetCharacterId =
                snapshot.TargetCharacterId;
            monster.TargetPetUid =
                snapshot.TargetPetUid;
            monster.AiState = snapshot.AiState;
            monster.IsMoving = snapshot.IsMoving;
            monster.HoldingAttackRange =
                snapshot.HoldingAttackRange;
            monster.RespawnAt = snapshot.RespawnAt;
        }
    }
}
