using SGO.Server.Application.Abstractions;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Items;

/// <summary>
/// The recovered Lantern Transformer mirrors the currently equipped weapon
/// into one of the three current-client lantern visuals. The override changes
/// only appearance slot 5; combat stats continue to come from the real weapon.
/// </summary>
public static class LanternTransformationCatalog
{
    public const int SourceItemId = 2001;
    public const int MeleeLanternItemId = 31639;
    public const int TwinGunLanternItemId = 31640;
    public const int GunLanternItemId = 31641;

    public static CosmeticTransformationDefinition? Resolve(
        CurrentClientDataCatalog current,
        CharacterAssets assets)
    {
        var source = current.FindItem(
            SourceItemId);

        if (source is null ||
            !source.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase) ||
            !source.ScriptName.Equals(
                "燈籠變身器",
                StringComparison.Ordinal))
        {
            return null;
        }

        var equippedWeapon =
            assets.Equips.FirstOrDefault(x =>
                Convert.ToInt32(
                    x["Part"]) == 5 &&
                Convert.ToInt32(
                    x["InBank"]) == 0 &&
                Convert.ToInt32(
                    x["InShop"]) == 0);

        if (equippedWeapon is null)
            return null;

        var equippedItemId =
            Convert.ToInt32(
                equippedWeapon["ItemIdx"]);
        var equipped =
            current.FindItem(
                equippedItemId);

        if (equipped is null ||
            !equipped.Category.Equals(
                "weapon",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(
                equipped.WeaponStyle))
        {
            return null;
        }

        var targetItemId =
            equipped.WeaponStyle switch
            {
                "TwinGun" =>
                    TwinGunLanternItemId,
                "Gun" =>
                    GunLanternItemId,
                "Sword" or
                "Axe" or
                "Spear" or
                "Dagger" or
                "Stick" =>
                    MeleeLanternItemId,
                _ => 0
            };

        if (targetItemId <= 0)
            return null;

        var target =
            current.FindItem(
                targetItemId);

        if (target is null ||
            !target.Category.Equals(
                "weapon",
                StringComparison.OrdinalIgnoreCase) ||
            target.EquipSlot != 5)
        {
            return null;
        }

        return new(
            SourceItemId,
            source.Name,
            true,
            [
                new CharacterCosmeticOverride(
                    5,
                    target.ItemId,
                    Math.Max(
                        0,
                        target.ColorIndex),
                    SourceItemId)
            ]);
    }
}
