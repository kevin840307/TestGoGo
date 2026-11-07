using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.WorldData;

namespace SGO.Server.Application.Items;

public sealed record TransformationSpec(
    string DisplayName,
    string TransformId,
    int AttackBonus = 0,
    int DefenseBonus = 0,
    int MagicAttackBonus = 0,
    int MagicDefenseBonus = 0,
    int HitBonus = 0,
    int DodgeBonus = 0);

public static class TransformationCatalog
{
    public const int TransformScrollItemId = 2802;
    public const int RestoreScrollItemId = 2806;
    public const int MonsterStoneItemId = 2819;
    public const int DragonStoneItemId = 2820;
    public const int TrueDragonStoneItemId = 2821;
    public const int DurationSeconds = 3600;

    private sealed record Candidate(
        string ServerName,
        int AttackBonus = 0,
        int DefenseBonus = 0,
        int MagicAttackBonus = 0,
        int MagicDefenseBonus = 0,
        int HitBonus = 0,
        int DodgeBonus = 0);

    // Mirrors the recovered client's game-info transformation guide.
    private static readonly Candidate[] NormalCandidates =
    [
        new("波奇鳥"),
        new("波奇王"),
        new("黑森魔獸"),
        new("巨毛蟲"),
        new("幸運球"),
        new("蘑菇蟲"),
        new("滾滾豬"),
        new("星靈騎士"),
        new("精靈哈密"),
        new("油燈幽靈"),
        new("帝王白兔"),
        new("星夢女管家．小葵")
    ];

    private static readonly Candidate[] MonsterStoneCandidates =
    [
        new("蝙蝠", DodgeBonus: 400),
        new("傴僂劍士", AttackBonus: 1000, DodgeBonus: 300),
        new("傴僂重擊手", AttackBonus: 1300),
        new("死靈角鬥士", DefenseBonus: 1300),
        new("死靈魔動使", MagicAttackBonus: 1300),
        new(
            "魔女琪莉亞",
            MagicAttackBonus: 1500,
            MagicDefenseBonus: 1500,
            DodgeBonus: 500),
        new(
            "御龍者",
            AttackBonus: 1500,
            DefenseBonus: 1500,
            HitBonus: 500)
    ];

    private static readonly Candidate Dragon =
        new(
            "火之幼龍",
            AttackBonus: 600,
            DefenseBonus: 600,
            MagicAttackBonus: 600,
            MagicDefenseBonus: 600);

    private static readonly Candidate TrueDragon =
        new(
            "火龍",
            AttackBonus: 1200,
            DefenseBonus: 1200,
            MagicAttackBonus: 1200,
            MagicDefenseBonus: 1200);

    public static TransformationSpec? Resolve(
        WorldDataService world,
        CharacterAssets assets)
    {
        if (HasCarriedItem(assets, TrueDragonStoneItemId))
            return ResolveCandidate(world, TrueDragon);

        if (HasCarriedItem(assets, DragonStoneItemId))
            return ResolveCandidate(world, Dragon);

        var source = HasCarriedItem(
                assets,
                MonsterStoneItemId)
            ? MonsterStoneCandidates
            : NormalCandidates;

        var available = source
            .Select(x => ResolveCandidate(world, x))
            .Where(x => x is not null)
            .Cast<TransformationSpec>()
            .ToArray();

        return available.Length == 0
            ? null
            : available[Random.Shared.Next(available.Length)];
    }

    private static TransformationSpec? ResolveCandidate(
        WorldDataService world,
        Candidate candidate)
    {
        var enemy = world.FindEnemyByName(candidate.ServerName);
        if (enemy is null ||
            string.IsNullOrWhiteSpace(enemy.Sprite))
            return null;

        return new(
            candidate.ServerName,
            enemy.Sprite.Trim(),
            candidate.AttackBonus,
            candidate.DefenseBonus,
            candidate.MagicAttackBonus,
            candidate.MagicDefenseBonus,
            candidate.HitBonus,
            candidate.DodgeBonus);
    }

    private static bool HasCarriedItem(
        CharacterAssets assets,
        int itemIndex)
        => assets.Inventory.Any(x =>
            Convert.ToInt32(x["ItemIndex"]) == itemIndex &&
            Convert.ToInt32(x["Count"]) > 0 &&
            Convert.ToInt32(x["InBank"]) == 0 &&
            Convert.ToInt32(x["InShop"]) == 0);
}
