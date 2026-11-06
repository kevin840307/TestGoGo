using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

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
        CharacterAssets assets,
        CurrentClientDataCatalog current)
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
                Convert.ToInt32(eq["InShop"]) != 0 ||
                Convert.ToInt32(eq["Part"]) == 255)
                continue;

            var item = current.FindItem(
                Convert.ToInt32(eq["ItemIdx"]));

            if (item is null)
                continue;

            attack += item.Atk;
            defense += item.Def;
            magicAttack += item.MAtk;
            magicDefense += item.MDef;
            hit += item.Hit;
            dodge += item.Flee;
            lucky += item.Luk;
            attackSpeed += item.Aspd;
            moveSpeed += item.MoveSpeed;
            critRate += item.CritRate;
            critDamage += item.CritDamage;
            attackRange += item.AttackRange;
        }

        var summonedHorseUid = Convert.ToInt64(character["SummonedHorseUID"]);
        if (summonedHorseUid > 0 &&
            assets.Horses.Any(h =>
                Convert.ToInt64(h["HorseUID"]) == summonedHorseUid))
        {
            foreach (var eq in assets.HorseEquips)
            {
                if (Convert.ToInt64(eq["HorseUID"]) != summonedHorseUid)
                    continue;

                var item = current.FindItem(
                    Convert.ToInt32(eq["EquipIdx"]));

                if (item is null)
                    continue;

                attack += item.Atk;
                defense += item.Def;
                magicAttack += item.MAtk;
                magicDefense += item.MDef;
                hit += item.Hit;
                dodge += item.Flee;
                lucky += item.Luk;
                attackSpeed += item.Aspd;
                moveSpeed += item.MoveSpeed;
                critRate += item.CritRate;
                critDamage += item.CritDamage;
                attackRange += item.AttackRange;
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
        CharacterAssets assets,
        CurrentClientDataCatalog current)
    {
        var s = Calculate(character, assets, current);
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
