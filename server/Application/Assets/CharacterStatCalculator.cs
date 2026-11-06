using SGO.Server.Domain.Assets;

namespace SGO.Server.Application.Assets;

public sealed record EffectiveCharacterStats(
    int Attack,
    int Defense,
    int MagicAttack,
    int MagicDefense,
    int Hit,
    int Dodge,
    int Lucky,
    int AttackSpeed,
    int AttackRange,
    int MoveSpeed,
    int CritRate,
    int CritDamage);

public static class CharacterStatCalculator
{
    public static EffectiveCharacterStats Calculate(
        Dictionary<string, object> character,
        CharacterAssets assets)
    {
        var attack = Convert.ToInt32(character["Attack"]);
        var defense = Convert.ToInt32(character["Defense"]);
        var magicAttack = Convert.ToInt32(character["MagicAttack"]);
        var magicDefense = Convert.ToInt32(character["MagicDefense"]);
        var hit = Convert.ToInt32(character["Hit"]);
        var dodge = Convert.ToInt32(character["Dodge"]);
        var lucky = Convert.ToInt32(character["Lucky"]);
        var attackSpeed = Convert.ToInt32(character["AttackSpeed"]);
        var attackRange = Convert.ToInt32(character["AttackRange"]);
        var moveSpeed = Convert.ToInt32(character["MoveSpeed"]);
        var critRate = Convert.ToInt32(character["CritRate"]);
        var critDamage = Convert.ToInt32(character["CritDamage"]);

        foreach (var eq in assets.Equips)
        {
            if (Convert.ToInt32(eq["InBank"]) != 0 ||
                Convert.ToInt32(eq["InShop"]) != 0)
                continue;

            var part = Convert.ToInt32(eq["Part"]);
            if (part == 255) continue;

            switch (part)
            {
                case 1:
                case 2:
                    attack += 5;
                    hit += 2;
                    break;
                case 3:
                case 4:
                    defense += 8;
                    magicDefense += 4;
                    break;
                case 5:
                    attack += 20;
                    attackRange += 10;
                    break;
                case 6:
                    defense += 12;
                    break;
                case 0:
                case 7:
                    defense += 4;
                    break;
                case 8:
                    magicAttack += 8;
                    magicDefense += 4;
                    break;
            }
        }

        var summonedHorseUid = Convert.ToInt64(character["SummonedHorseUID"]);
        if (summonedHorseUid > 0 &&
            assets.Horses.Any(h =>
                Convert.ToInt64(h["HorseUID"]) == summonedHorseUid))
        {
            attack += 20;
            defense += 20;
            moveSpeed += 15;

            foreach (var eq in assets.HorseEquips)
            {
                if (Convert.ToInt64(eq["HorseUID"]) != summonedHorseUid)
                    continue;

                attack += 8;
                defense += 6;
            }
        }

        return new(
            attack,
            defense,
            magicAttack,
            magicDefense,
            hit,
            dodge,
            lucky,
            attackSpeed,
            attackRange,
            moveSpeed,
            critRate,
            critDamage);
    }

    public static object ToPlayerStatusPacket(
        Dictionary<string, object> character,
        CharacterAssets assets)
    {
        var s = Calculate(character, assets);
        return new
        {
            Type = "PlayerStatusSync",
            HP = character["HP"],
            MP = character["MP"],
            SP = character["SP"],
            MaxHP = character["MaxHP"],
            MaxMP = character["MaxMP"],
            MaxSP = character["MaxSP"],
            Attack = s.Attack,
            Defense = s.Defense,
            MagicAttack = s.MagicAttack,
            MagicDefense = s.MagicDefense,
            Hit = s.Hit,
            Dodge = s.Dodge,
            Lucky = s.Lucky,
            AttackSpeed = s.AttackSpeed,
            AttackRange = s.AttackRange,
            MoveSpeed = s.MoveSpeed,
            CritRate = s.CritRate,
            CritDamage = s.CritDamage
        };
    }
}
