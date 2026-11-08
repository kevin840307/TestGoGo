using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Assets;

public sealed record EffectiveCharacterStats(
    int MaxHP,
    int MaxMP,
    int MaxSP,
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
        var maxHp = Convert.ToInt32(character["MaxHP"]);
        var maxMp = Convert.ToInt32(character["MaxMP"]);
        var maxSp = Convert.ToInt32(character["MaxSP"]);
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

        attack += Convert.ToInt32(
            character.GetValueOrDefault("ClubTechAttack", 0));
        defense += Convert.ToInt32(
            character.GetValueOrDefault("ClubTechDefense", 0));
        magicAttack += Convert.ToInt32(
            character.GetValueOrDefault("ClubTechMagicAttack", 0));
        magicDefense += Convert.ToInt32(
            character.GetValueOrDefault("ClubTechMagicDefense", 0));
        hit += Convert.ToInt32(
            character.GetValueOrDefault("ClubTechHit", 0));
        dodge += Convert.ToInt32(
            character.GetValueOrDefault("ClubTechDodge", 0));
        moveSpeed += Convert.ToInt32(
            character.GetValueOrDefault("ClubTechMoveSpeed", 0));
        attackSpeed += Convert.ToInt32(
            character.GetValueOrDefault("ClubTechAttackSpeed", 0));
        critRate += Convert.ToInt32(
            character.GetValueOrDefault("ClubTechCritRate", 0));
        critDamage += Convert.ToInt32(
            character.GetValueOrDefault("ClubTechCritDamage", 0));

        maxHp += Convert.ToInt32(
            character.GetValueOrDefault("TimedMaxHpBonus", 0));
        maxMp += Convert.ToInt32(
            character.GetValueOrDefault("TimedMaxMpBonus", 0));
        maxSp += Convert.ToInt32(
            character.GetValueOrDefault("TimedMaxSpBonus", 0));
        attack += Convert.ToInt32(
            character.GetValueOrDefault("TimedAttackBonus", 0));
        defense += Convert.ToInt32(
            character.GetValueOrDefault("TimedDefenseBonus", 0));
        magicAttack += Convert.ToInt32(
            character.GetValueOrDefault("TimedMagicAttackBonus", 0));
        magicDefense += Convert.ToInt32(
            character.GetValueOrDefault("TimedMagicDefenseBonus", 0));
        hit += Convert.ToInt32(
            character.GetValueOrDefault("TimedHitBonus", 0));
        dodge += Convert.ToInt32(
            character.GetValueOrDefault("TimedDodgeBonus", 0));
        lucky += Convert.ToInt32(
            character.GetValueOrDefault("TimedLuckyBonus", 0));
        attackSpeed += Convert.ToInt32(
            character.GetValueOrDefault("TimedAttackSpeedBonus", 0));
        moveSpeed += Convert.ToInt32(
            character.GetValueOrDefault("TimedMoveSpeedBonus", 0));
        critRate += Convert.ToInt32(
            character.GetValueOrDefault("TimedCritRateBonus", 0));
        critDamage += Convert.ToInt32(
            character.GetValueOrDefault("TimedCritDamageBonus", 0));

        attack += Convert.ToInt32(
            character.GetValueOrDefault("TransformAttackBonus", 0));
        defense += Convert.ToInt32(
            character.GetValueOrDefault("TransformDefenseBonus", 0));
        magicAttack += Convert.ToInt32(
            character.GetValueOrDefault("TransformMagicAttackBonus", 0));
        magicDefense += Convert.ToInt32(
            character.GetValueOrDefault("TransformMagicDefenseBonus", 0));
        hit += Convert.ToInt32(
            character.GetValueOrDefault("TransformHitBonus", 0));
        dodge += Convert.ToInt32(
            character.GetValueOrDefault("TransformDodgeBonus", 0));

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

            maxHp += item.MaxHpBonus;
            maxMp += item.MaxMpBonus;
            maxSp += item.MaxSpBonus;
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

                maxHp += item.MaxHpBonus;
                maxMp += item.MaxMpBonus;
                maxSp += item.MaxSpBonus;
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
            maxHp,
            maxMp,
            maxSp,
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
            Level = character["Level"],
            Money = character["Money"],
            ExpAcc = character["ExpAcc"],
            ExpDeath = character["ExpDeath"],
            MaxExp = character["MaxExp"],
            ExpUse = character["ExpUse"],
            Str = character["Str"],
            Dex = character["Dex"],
            Con = character["Con"],
            Wis = character["Wis"],
            HP = Math.Min(Convert.ToInt32(character["HP"]), s.MaxHP),
            MP = Math.Min(Convert.ToInt32(character["MP"]), s.MaxMP),
            SP = Math.Min(Convert.ToInt32(character["SP"]), s.MaxSP),
            MaxHP = s.MaxHP,
            MaxMP = s.MaxMP,
            MaxSP = s.MaxSP,
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
