using SGO.Server.Application.Abstractions;

namespace SGO.Server.Infrastructure.GameData;

public sealed class LegacyPetGrowthCatalog
{
    private readonly int[] _str;
    private readonly (int Hit, int Dodge)[] _dex;
    private readonly int[] _con;
    private readonly (int MaxMp, int MagicAttack)[] _wis;

    public LegacyPetGrowthCatalog(string gameDataRoot)
    {
        var playerRoot = Path.Combine(gameDataRoot, "Player");
        _str = LoadSingle(Path.Combine(playerRoot, "STR_Pet.csv"));
        _dex = LoadPair(Path.Combine(playerRoot, "DEX_Pet.csv"));
        _con = LoadSingle(Path.Combine(playerRoot, "CON_Pet.csv"));
        _wis = LoadPair(Path.Combine(playerRoot, "WIS_Pet.csv"));
    }

    public PetStatAdjustment AllocationDelta(
        string statType,
        int baseValue,
        int allocatedBefore)
    {
        var before = Math.Max(0, baseValue + allocatedBefore);
        var after = before + 1;

        return statType.ToUpperInvariant() switch
        {
            "STR" => new PetStatAdjustment(
                Attack: Value(_str, after) - Value(_str, before)),
            "DEX" => new PetStatAdjustment(
                Hit: DexValue(_dex, after).Hit - DexValue(_dex, before).Hit,
                Dodge: DexValue(_dex, after).Dodge - DexValue(_dex, before).Dodge),
            "CON" => new PetStatAdjustment(
                MaxHp: Value(_con, after) - Value(_con, before)),
            "WIS" => new PetStatAdjustment(
                MaxMp: WisValue(_wis, after).MaxMp - WisValue(_wis, before).MaxMp,
                MagicAttack: WisValue(_wis, after).MagicAttack -
                             WisValue(_wis, before).MagicAttack),
            _ => new PetStatAdjustment()
        };
    }

    public PetStatAdjustment ResetDelta(
        int baseStr,
        int extraStr,
        int baseDex,
        int extraDex,
        int baseCon,
        int extraCon,
        int baseWis,
        int extraWis)
    {
        var str = Value(_str, Math.Max(0, baseStr)) -
                  Value(_str, Math.Max(0, baseStr + extraStr));
        var dexBefore = DexValue(_dex, Math.Max(0, baseDex + extraDex));
        var dexAfter = DexValue(_dex, Math.Max(0, baseDex));
        var con = Value(_con, Math.Max(0, baseCon)) -
                  Value(_con, Math.Max(0, baseCon + extraCon));
        var wisBefore = WisValue(_wis, Math.Max(0, baseWis + extraWis));
        var wisAfter = WisValue(_wis, Math.Max(0, baseWis));

        return new PetStatAdjustment(
            MaxHp: con,
            MaxMp: wisAfter.MaxMp - wisBefore.MaxMp,
            Attack: str,
            MagicAttack: wisAfter.MagicAttack - wisBefore.MagicAttack,
            Hit: dexAfter.Hit - dexBefore.Hit,
            Dodge: dexAfter.Dodge - dexBefore.Dodge);
    }

    private static int[] LoadSingle(string path)
        => File.Exists(path)
            ? File.ReadLines(path)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Select(x => int.TryParse(
                    x.Split(',')[0].Trim(),
                    out var value) ? value : 0)
                .ToArray()
            : Array.Empty<int>();

    private static (int Hit, int Dodge)[] LoadPair(string path)
        => File.Exists(path)
            ? File.ReadLines(path)
                .Select(x => x.Split(','))
                .Where(x => x.Length >= 2)
                .Select(x => (
                    int.TryParse(x[0].Trim(), out var first) ? first : 0,
                    int.TryParse(x[1].Trim(), out var second) ? second : 0))
                .ToArray()
            : Array.Empty<(int, int)>();

    private static int Value(int[] values, int stat)
        => stat <= 0 || values.Length == 0
            ? 0
            : values[Math.Min(values.Length, stat) - 1];

    private static (int Hit, int Dodge) DexValue(
        (int Hit, int Dodge)[] values,
        int stat)
        => stat <= 0 || values.Length == 0
            ? (0, 0)
            : values[Math.Min(values.Length, stat) - 1];

    private static (int MaxMp, int MagicAttack) WisValue(
        (int MaxMp, int MagicAttack)[] values,
        int stat)
        => stat <= 0 || values.Length == 0
            ? (0, 0)
            : values[Math.Min(values.Length, stat) - 1];
}
