using System.Text.Json;
using SGO.Server.Domain.Assets;
using SGO.Server.Domain.Shops;

namespace SGO.Server.Application.Abstractions;

public interface IGameStore
{
    (bool Success, string Message) Register(string username, string password, string email);
    long? Login(string username, string password);

    List<Dictionary<string, object>> Characters(long accountId);
    Dictionary<string, object>? GetCharacter(long accountId, long dbid);
    CharacterAssets GetCharacterAssets(long accountId, long dbid);
    List<Dictionary<string, object>> GetSkills(long accountId, long dbid);
    bool SetSkill(long accountId, long dbid, int skillId, int level);
    bool ClearSkills(long accountId, long dbid);

    long AddInventoryItem(long accountId, long dbid, int itemIndex, int count);
    long AddEquipment(long accountId, long dbid, int itemIndex, int part = 255, int damage = 0, int color = 0);
    long AddPet(long accountId, long dbid, int petId, string name);
    long AddHorse(long accountId, long dbid, int horseIndex, string name, string horseType = "MS", int energyLeft = 0);
    long AddHorseEquipment(long accountId, long dbid, int equipIndex, long horseUid = -1, int part = 255, int damage = 0, int color = 0);

    bool EquipItem(long accountId, long dbid, long itemUid, int targetSlot);
    bool UnequipItem(long accountId, long dbid, int targetSlot);
    bool SetSummonedHorse(long accountId, long dbid, long horseUid);
    Dictionary<string, object>? ChargeHorse(
        long accountId,
        long dbid,
        long horseUid,
        long itemUid,
        int energyAmount);
    bool EquipHorseItem(long accountId, long dbid, long equipUid, long horseUid);
    bool UnequipHorseItem(long accountId, long dbid, long equipUid, long horseUid);
    bool SetPetState(long accountId, long dbid, long petUid, int state);
    bool RenamePet(long accountId, long dbid, long petUid, string name);
    bool EquipPetItem(long accountId, long dbid, long petUid, long itemUid, int targetSlot);
    bool UnequipPetItem(long accountId, long dbid, long petUid, int targetSlot);

    Dictionary<string, object>? UseRecoveryItem(
        long accountId,
        long dbid,
        long itemUid,
        int recoverHp,
        int recoverMp,
        int recoverSp);

    Dictionary<string, object>? ActivateHorseCard(
        long accountId,
        long dbid,
        long itemUid,
        int horseIndex,
        string name,
        string horseType,
        int initialEnergy);

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
        int tabType,
        int count,
        int unitPrice,
        out long newMoney);

    (bool Success, string Message) Create(long accountId, JsonElement data);
    bool Delete(long accountId, long dbid, string password);
    bool ChangeCharacterPassword(long accountId, long dbid, string newPassword);

    bool UpdatePosition(long accountId, long dbid, int x, int y);
    bool UpdateLocation(long accountId, long dbid, int mapId, int x, int y);
    bool SetRebornLocation(long accountId, long dbid, int mapId, int x, int y);
    bool UpdateState(long accountId, long dbid, string state);
    Dictionary<string, object>? SetLevel(
        long accountId,
        long dbid,
        int level);

    Dictionary<string, object>? Revive(long accountId, long dbid);

    Dictionary<string, object>? ApplyDamage(
        long accountId,
        long dbid,
        int damage);

    Dictionary<string, object>? AddRewards(
        long accountId,
        long dbid,
        int exp,
        int money);
}
