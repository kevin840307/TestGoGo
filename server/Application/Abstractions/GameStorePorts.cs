using System.Text.Json;
using SGO.Server.Domain.Assets;
using SGO.Server.Domain.Shops;
using SGO.Server.Domain.World;

namespace SGO.Server.Application.Abstractions;

// Small persistence ports are intentionally aligned to the recovered client
// feature names. A database provider may implement one class that satisfies
// all ports, but application modules should depend only on the port they use.
public interface ICharacterAssetReader
{
    CharacterAssets GetCharacterAssets(long accountId, long dbid);
}

public interface IAccountStore
{
    (bool Success, string Message) Register(
        string username,
        string password,
        string email);
    long? Login(string username, string password);
}

public interface ICharacterStore
{
    List<Dictionary<string, object>> Characters(long accountId);
    Dictionary<string, object>? GetCharacter(long accountId, long dbid);
    (bool Success, string Message) Create(
        long accountId,
        JsonElement data);
    bool Delete(long accountId, long dbid, string password);
    bool ChangeCharacterPassword(
        long accountId,
        long dbid,
        string newPassword);
    bool UpdatePosition(long accountId, long dbid, int x, int y);
    bool UpdateLocation(
        long accountId,
        long dbid,
        int mapId,
        int x,
        int y);
    bool SetRebornLocation(
        long accountId,
        long dbid,
        int mapId,
        int x,
        int y);
    bool UpdateState(long accountId, long dbid, string state);
    Dictionary<string, object>? SetLevel(
        long accountId,
        long dbid,
        int level);
    Dictionary<string, object>? SetAttackSpeed(
        long accountId,
        long dbid,
        int attackSpeed);
    Dictionary<string, object>? Revive(long accountId, long dbid);
    Dictionary<string, object>? ApplyDamage(
        long accountId,
        long dbid,
        int damage);
    Dictionary<string, object>? ApplyHeal(
        long accountId,
        long dbid,
        int amount);
    Dictionary<string, object>? RecoverResources(
        long accountId,
        long dbid,
        int hpAmount,
        int mpAmount);
    Dictionary<string, object>? AddRewards(
        long accountId,
        long dbid,
        int exp,
        int money);
    Dictionary<string, object>? AllocateStat(
        long accountId,
        long dbid,
        string statType);
    Dictionary<string, object>? SetPrimaryStats(
        long accountId,
        long dbid,
        int str,
        int dex,
        int con,
        int wis,
        int expUse);
    Dictionary<string, object>? ApplyGrowthStats(
        long accountId,
        long dbid,
        int maxHp,
        int maxMp,
        int maxSp,
        int attack,
        int defense,
        int magicAttack,
        int magicDefense,
        int hit,
        int dodge,
        int moveSpeed);
}

public interface IInventoryStore : ICharacterAssetReader
{
    long AddInventoryItem(
        long accountId,
        long dbid,
        int itemIndex,
        int count);
    (bool Success, string Message, long RewardUid) OpenDoll(
        long accountId,
        long dbid,
        long dollUid,
        int dollItemIndex,
        int rewardItemIndex,
        AssetPurchaseKind rewardKind,
        int rewardCount = 1,
        int rewardPart = 255);
    Dictionary<string, object>? UseRecoveryItem(
        long accountId,
        long dbid,
        long itemUid,
        int recoverHp,
        int recoverMp,
        int recoverSp);
}

public interface IEquipmentStore : ICharacterAssetReader
{
    long AddEquipment(
        long accountId,
        long dbid,
        int itemIndex,
        int part = 255,
        int damage = 0,
        int color = 0);
    bool EquipItem(
        long accountId,
        long dbid,
        long itemUid,
        int targetSlot);
    bool UnequipItem(
        long accountId,
        long dbid,
        int targetSlot);
}

public interface IPetStore : ICharacterAssetReader
{
    long AddPet(
        long accountId,
        long dbid,
        int petId,
        string name);
    bool SetPetState(
        long accountId,
        long dbid,
        long petUid,
        int state);
    bool RenamePet(
        long accountId,
        long dbid,
        long petUid,
        string name);
    bool EquipPetItem(
        long accountId,
        long dbid,
        long petUid,
        long itemUid,
        int targetSlot);
    bool UnequipPetItem(
        long accountId,
        long dbid,
        long petUid,
        int targetSlot);
}

public interface IMechaStore : ICharacterAssetReader
{
    long AddHorse(
        long accountId,
        long dbid,
        int horseIndex,
        string name,
        string horseType = "MS",
        int energyLeft = 0);
    long AddHorseEquipment(
        long accountId,
        long dbid,
        int equipIndex,
        long horseUid = -1,
        int part = 255,
        int damage = 0,
        int color = 0);
    bool SetSummonedHorse(
        long accountId,
        long dbid,
        long horseUid);
    Dictionary<string, object>? ChargeHorse(
        long accountId,
        long dbid,
        long horseUid,
        long itemUid,
        int energyAmount);
    bool EquipHorseItem(
        long accountId,
        long dbid,
        long equipUid,
        long horseUid);
    bool UnequipHorseItem(
        long accountId,
        long dbid,
        long equipUid,
        long horseUid);
    Dictionary<string, object>? SetHorseLevel(
        long accountId,
        long dbid,
        long horseUid,
        int level);
    Dictionary<string, object>? AddHorseExperience(
        long accountId,
        long dbid,
        long horseUid,
        int exp);
    Dictionary<string, object>? ActivateHorseCard(
        long accountId,
        long dbid,
        long itemUid,
        int horseIndex,
        string name,
        string horseType,
        int initialEnergy);
}

public interface ISkillStore
{
    List<Dictionary<string, object>> GetSkills(
        long accountId,
        long dbid);
    bool SetSkill(
        long accountId,
        long dbid,
        int skillId,
        int level);
    bool ClearSkills(long accountId, long dbid);
    Dictionary<string, object>? SpendSkillResources(
        long accountId,
        long dbid,
        int hpCost,
        int mpCost,
        int spCost);
}

public interface IShortcutStore
{
    List<Dictionary<string, object>> GetShortcuts(
        long accountId,
        long dbid);
    bool UpdateShortcut(
        long accountId,
        long dbid,
        int slotIndex,
        int shortcutType,
        int targetId);
}

public interface IBankStore : ICharacterAssetReader
{
    (bool Success, string Message) BankTransfer(
        long accountId,
        long dbid,
        long uid,
        int tabType,
        int amount,
        bool toBank);
}

public interface IItemDiscardStore : ICharacterAssetReader
{
    (bool Success, string Message) DiscardAsset(
        long accountId,
        long dbid,
        long uid,
        int tabType,
        int count);
}

public interface IWorldGroundItemStore
{
    GroundItemRuntime? DropAssetToGround(
        long accountId,
        long dbid,
        long uid,
        int tabType,
        int count,
        int mapId,
        double x,
        double y);

    IReadOnlyList<GroundItemRuntime> GetGroundItems(int mapId);

    (bool Success, string Message, GroundItemRuntime? Item)
        PickUpGroundItem(
            long accountId,
            long dbid,
            long groundItemId);
}

public interface IShopStore : ICharacterAssetReader
{
    AssetPurchaseResult BuyAsset(
        long accountId,
        long dbid,
        AssetPurchaseKind kind,
        int itemIndex,
        int count,
        int unitPrice,
        int itemPart = 255);

    bool SellAsset(
        long accountId,
        long dbid,
        long uid,
        AssetPurchaseKind kind,
        int count,
        int unitPrice,
        out long newMoney);
}
