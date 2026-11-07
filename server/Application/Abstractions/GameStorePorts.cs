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
    Dictionary<string, object>? ReviveBySkill(
        long accountId,
        long dbid,
        int hpPercent,
        int mpPercent,
        bool clearDeathExp);
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
    Dictionary<string, object>? RecoverResources(
        long accountId,
        long dbid,
        int hpAmount,
        int mpAmount,
        int spAmount);
    Dictionary<string, object>? AddRewards(
        long accountId,
        long dbid,
        int exp,
        int money);
    Dictionary<string, object>? SetExperienceProgress(
        long accountId,
        long dbid,
        int level,
        int maxExp,
        int abilityPointsToAdd);
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
        int rewardPart = 255,
        int rewardDamage = 0);
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

public interface IQuestStore
{
    int GetQuestFlag(
        long accountId,
        long dbid,
        string flagName);

    bool SetQuestFlag(
        long accountId,
        long dbid,
        string flagName,
        int value);

    IReadOnlyList<Dictionary<string, object>> GetQuests(
        long accountId,
        long dbid);

    bool UpsertQuest(
        long accountId,
        long dbid,
        int questId,
        int status,
        int currentStep,
        int stepProgress);

    bool AbandonQuest(
        long accountId,
        long dbid,
        int questId);
}

public interface IMallStore
{
    long GetMallPoints(long accountId);

    bool AdjustMallPoints(
        long accountId,
        long delta,
        out long newBalance);
}

public sealed record ClubMemberData(
    long DbId,
    string Name,
    int Level,
    int Career,
    int Job,
    int Position,
    long Donation);

public sealed record ClubResearchData(
    int ResearchId,
    int Level);

public sealed record ClubSnapshot(
    long ClubId,
    string ClubName,
    int ClubLevel,
    long ClubExp,
    long ClubSaving,
    string ClubMessage,
    int ActiveResearchId,
    long ActiveResearchEndUtcMs,
    int MyPosition,
    long MyDonation,
    IReadOnlyList<ClubMemberData> Members,
    IReadOnlyList<ClubResearchData> Researches);

public sealed record ClubMutationResult(
    bool Success,
    string Message);

public interface IClubStore
{
    ClubSnapshot? GetClubSnapshot(
        long accountId,
        long dbid,
        long nowUtcMs);

    long GetClubIdForCharacter(long dbid);

    ClubMutationResult CreateClub(
        long accountId,
        long dbid,
        string clubName,
        long nowUtcMs);

    ClubMutationResult JoinClub(
        long accountId,
        long dbid,
        long clubId,
        long nowUtcMs);

    ClubMutationResult UpdateClubMessage(
        long accountId,
        long dbid,
        string message);

    ClubMutationResult DonateClubMoney(
        long accountId,
        long dbid,
        long amount);

    ClubMutationResult DonateClubExp(
        long accountId,
        long dbid,
        long amount);

    ClubMutationResult StartClubResearch(
        long accountId,
        long dbid,
        int researchId,
        long nowUtcMs);
}

public sealed record RankEntry(
    long DbId,
    long AccountId,
    string Name,
    long Value,
    long MessageOwnerDbId = 0,
    long AppearanceDbId = 0);

public interface IRankStore
{
    IReadOnlyList<RankEntry> GetRankEntries(
        int rankType,
        int limit);

    string GetRankMessage(
        int rankType,
        long dbid);

    bool SetRankMessage(
        long accountId,
        long dbid,
        int rankType,
        string message);
}

public interface ITitleStore
{
    IReadOnlyList<int> GetUnlockedTitles(
        long accountId,
        long dbid);

    bool HasTitle(
        long accountId,
        long dbid,
        int titleId);

    bool UnlockTitle(
        long accountId,
        long dbid,
        int titleId);

    bool SetCurrentTitle(
        long accountId,
        long dbid,
        int titleId);
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

public sealed record DirectTransferResult(
    bool Success,
    string Message,
    long SenderMoney,
    long TargetMoney,
    long TargetAccountId);

public interface IDirectTransferStore
{
    DirectTransferResult TransferMoney(
        long accountId,
        long dbid,
        long targetDbid,
        long amount);
}

public sealed record RepairAssetResult(
    bool Success,
    string Message,
    long Money);

public interface IRepairStore : ICharacterAssetReader
{
    RepairAssetResult RepairAsset(
        long accountId,
        long dbid,
        long uid,
        bool isMechaEquipment,
        int repairedDurability,
        long cost);
}

public sealed record TradeAssetOffer(
    int TabType,
    long Uid,
    int ItemIndex,
    int Count);

public sealed record TradeCommitResult(
    bool Success,
    string Message,
    long FirstMoney,
    long SecondMoney);

public interface ITradeStore : ICharacterAssetReader
{
    TradeCommitResult CommitTrade(
        long firstAccountId,
        long firstDbid,
        long firstMoney,
        IReadOnlyList<TradeAssetOffer> firstOffers,
        long secondAccountId,
        long secondDbid,
        long secondMoney,
        IReadOnlyList<TradeAssetOffer> secondOffers);
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
    GroundItemRuntime? CreateGroundItem(
        long ownerDbid,
        int tabType,
        int itemIndex,
        int count,
        int mapId,
        double x,
        double y,
        int damage = 0,
        int color = 0,
        int part = 255,
        int protectSeconds = 0,
        int lifetimeSeconds = 0);

    IReadOnlyList<int> RemoveExpiredGroundItems(
        int mapId,
        DateTimeOffset now);

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

public sealed record PlayerShopReserveResult(
    bool Success,
    long ReservedUid);

public sealed record PlayerShopPurchaseResult(
    bool Success,
    string Message,
    long BuyerMoney,
    long SellerMoney,
    bool SoldOut);

public sealed record CraftIngredient(
    int ItemId,
    int Count);

public sealed record CraftGrant(
    int ItemId,
    int Count,
    AssetPurchaseKind Kind,
    int Part,
    int Durability);

public sealed record DismantleTarget(
    int TabType,
    long Uid,
    int ItemId,
    int Count);

public sealed record CraftCommitResult(
    bool Success,
    string Message,
    long Money);

public interface ICraftingStore : ICharacterAssetReader
{
    CraftCommitResult CommitCraft(
        long accountId,
        long dbid,
        long moneyCost,
        IReadOnlyList<CraftIngredient> ingredients,
        IReadOnlyList<CraftGrant> grants);

    CraftCommitResult CommitDismantle(
        long accountId,
        long dbid,
        long moneyCost,
        IReadOnlyList<DismantleTarget> targets,
        IReadOnlyList<CraftGrant> grants);
}

public interface IPlayerShopStore : ICharacterAssetReader
{
    PlayerShopReserveResult SetPlayerShopAssetState(
        long accountId,
        long dbid,
        long uid,
        int tabType,
        int count,
        bool inShop);

    void ClearPlayerShopAssets(
        long accountId,
        long dbid);

    PlayerShopPurchaseResult BuyPlayerShopAsset(
        long buyerAccountId,
        long buyerDbid,
        long sellerAccountId,
        long sellerDbid,
        long uid,
        int tabType,
        int count,
        long unitPrice);
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
        int itemPart = 255,
        int itemDamage = 0);

    bool SellAsset(
        long accountId,
        long dbid,
        long uid,
        AssetPurchaseKind kind,
        int count,
        int unitPrice,
        out long newMoney);
}
