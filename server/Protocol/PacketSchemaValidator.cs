using System.Text.Json;

namespace SGO.Server.Protocol;

/// <summary>
/// Validate wire shapes before calling mutation handlers. Rules only cover
/// fields the recovered Godot client always sends; optional legacy fields
/// remain optional to preserve protocol compatibility.
/// </summary>
public static class PacketSchemaValidator
{
    private enum FieldType { Integer, LongInteger, Text, NonEmptyText, Boolean, LongIntegerArray }
    private readonly record struct Field(string Name, FieldType Type, bool Required = true);

    private static readonly IReadOnlyDictionary<string, Field[]> Rules =
        new Dictionary<string, Field[]>(StringComparer.Ordinal)
        {
            ["LoginRequest"] =
            [
                new("User", FieldType.NonEmptyText),
                new("Pass", FieldType.Text)
            ],
            ["RegisterAccountRequest"] =
            [
                new("LoginName", FieldType.NonEmptyText),
                new("Password", FieldType.Text),
                new("EMAIL", FieldType.Text)
            ],
            ["WorldReady"] = [new("MapID", FieldType.Integer)],
            ["SyncPosition"] = [new("X", FieldType.Integer), new("Y", FieldType.Integer)],
            ["UseItemRequest"] = [new("ItemUID", FieldType.LongInteger)],
            ["PickUpItemRequest"] = [new("EntityID", FieldType.Integer)],
            ["AttackEntity"] =
            [
                new("TargetEntityID", FieldType.Integer),
                new("TargetType", FieldType.Integer, false)
            ],
            ["UseSkill"] =
            [
                new("SkillID", FieldType.Integer),
                new("TargetID", FieldType.Integer, false),
                new("TargetType", FieldType.Integer, false),
                new("TargetX", FieldType.Integer, false),
                new("TargetY", FieldType.Integer, false)
            ],
            ["UpdateShortcut"] =
            [
                new("SlotIndex", FieldType.Integer),
                new("ShortcutType", FieldType.Integer),
                new("TargetID", FieldType.Integer)
            ],
            ["RepairItemRequest"] =
            [
                new("UID", FieldType.LongInteger),
                new("ItemType", FieldType.NonEmptyText)
            ],
            ["BankTransfer"] =
            [
                new("UID", FieldType.LongInteger),
                new("TabType", FieldType.Integer),
                new("Amount", FieldType.Integer),
                new("ToBank", FieldType.Boolean)
            ],
            ["TradeRequest"] = [new("TargetEntityID", FieldType.LongInteger)],
            ["TradeReply"] =
            [
                new("RequesterDBID", FieldType.LongInteger),
                new("Accept", FieldType.Boolean)
            ],
            ["TradeAddItem"] =
            [
                new("UID", FieldType.LongInteger),
                new("TabType", FieldType.Integer),
                new("Count", FieldType.Integer),
                new("ItemIndex", FieldType.Integer, false)
            ],
            ["TradeRemoveItem"] =
            [
                new("UID", FieldType.LongInteger),
                new("TabType", FieldType.Integer)
            ],
            ["TradeSetMoney"] = [new("Money", FieldType.LongInteger)],
            ["ShopAction"] = [new("Action", FieldType.NonEmptyText)],
            ["MallAction"] = [new("Action", FieldType.NonEmptyText)],
            ["PetAction"] = [new("Action", FieldType.NonEmptyText)],
            ["PartyAction"] =
            [
                new("Action", FieldType.NonEmptyText),
                new("TargetDBID", FieldType.LongInteger, false)
            ],
            ["DeleteCharacterRequest"] =
            [
                new("DBID", FieldType.Integer),
                new("Password", FieldType.Text)
            ],
            ["SelectCharacterRequest"] = [new("DBID", FieldType.Integer)],
            ["CreateCharacterRequest"] =
            [
                new("Name", FieldType.NonEmptyText),
                new("Password", FieldType.Text),
                new("Race", FieldType.Integer),
                new("Hair", FieldType.Integer),
                new("HairColor", FieldType.Integer),
                new("Str", FieldType.Integer),
                new("Dex", FieldType.Integer),
                new("Con", FieldType.Integer),
                new("Wis", FieldType.Integer)
            ],
            ["ModifyPasswordRequest"] =
            [
                new("DBID", FieldType.Integer),
                new("NewPwd", FieldType.Text)
            ],
            ["EquipRequest"] =
            [
                new("ItemUID", FieldType.LongInteger),
                new("TargetSlot", FieldType.Integer)
            ],
            ["UnequipRequest"] = [new("TargetSlot", FieldType.Integer)],
            ["TrainTravelReady"] = [new("TravelID", FieldType.NonEmptyText)],
            ["AllocateStatRequest"] = [new("StatType", FieldType.NonEmptyText)],
            ["DriveMechaRequest"] = [new("HorseUID", FieldType.LongInteger)],
            ["SetTitleRequest"] = [new("TitleID", FieldType.Integer)],
            ["DirectTransfer"] =
            [
                new("TargetDBID", FieldType.LongInteger),
                new("Amount", FieldType.LongInteger)
            ],
            ["ReviveAccept"] =
            [
                new("SkillID", FieldType.Integer),
                new("SkillLevel", FieldType.Integer)
            ],
            ["MegaphoneMessage"] =
            [
                new("ItemUID", FieldType.LongInteger),
                new("Message", FieldType.NonEmptyText)
            ],
            ["CraftItemRequest"] =
            [
                new("RecipeID", FieldType.Integer),
                new("BatchCount", FieldType.Integer)
            ],
            ["DismantleItemRequest"] =
            [
                new("ItemUIDs", FieldType.LongIntegerArray),
                new("ItemType", FieldType.NonEmptyText)
            ],
            ["ToggleAutoSaint"] =
            [
                new("IsOn", FieldType.Boolean),
                new("MpThreshold", FieldType.Integer),
                new("SpThreshold", FieldType.Integer)
            ],
            ["ChatMessage"] = [new("Message", FieldType.Text)],
            ["ClubAction"] = [new("Action", FieldType.NonEmptyText)],
            ["NpcOptionRequest"] = [new("EntityID", FieldType.Integer)],
            ["AbandonQuest"] = [new("QuestID", FieldType.Integer)],
            ["HorseEquipRequest"] =
            [
                new("EquipUID", FieldType.LongInteger),
                new("HorseUID", FieldType.LongInteger)
            ],
            ["HorseUnequipRequest"] =
            [
                new("EquipUID", FieldType.LongInteger),
                new("HorseUID", FieldType.LongInteger)
            ],
            ["HorseChargeRequest"] =
            [
                new("HorseUID", FieldType.LongInteger),
                new("ItemUID", FieldType.LongInteger)
            ],
            ["DropItemRequest"] =
            [
                new("TabType", FieldType.Integer),
                new("ItemUID", FieldType.LongInteger),
                new("Count", FieldType.Integer, false),
                new("DropX", FieldType.Integer, false),
                new("DropY", FieldType.Integer, false)
            ]
        };

    // Action-dependent fields are not mandatory for every ShopAction.
    // Keep these separate from the shared Type-level rules so CloseShop
    // and SyncMyShop remain backward compatible with the recovered client.
    private static readonly IReadOnlyDictionary<string, Field[]> ActionRules =
        new Dictionary<string, Field[]>(StringComparer.Ordinal)
        {
            ["ShopAction:BuyNpcItem"] =
            [
                new("ShopID", FieldType.Integer),
                new("ItemIndex", FieldType.Integer),
                new("Count", FieldType.Integer)
            ],
            ["ShopAction:SellNpcItem"] =
            [
                new("UID", FieldType.LongInteger),
                new("TabType", FieldType.Integer),
                new("Count", FieldType.Integer)
            ],
            ["ShopAction:AddItem"] =
            [
                new("UID", FieldType.LongInteger),
                new("Count", FieldType.Integer),
                new("Price", FieldType.LongInteger),
                new("TabType", FieldType.Integer)
            ],
            ["ShopAction:RemoveItem"] =
            [
                new("UID", FieldType.LongInteger),
                new("TabType", FieldType.Integer)
            ],
            ["ShopAction:BuyItem"] =
            [
                new("TargetDBID", FieldType.LongInteger),
                new("UID", FieldType.LongInteger),
                new("Count", FieldType.Integer)
            ],
            ["ShopAction:ViewShop"] =
                [new("TargetDBID", FieldType.LongInteger)],
            // The shared Godot send_pet_action helper always sends PetUID.
            // SetCombatMode and CommandAttack use separate client packets.
            ["PetAction:Summon"] = [new("PetUID", FieldType.LongInteger)],
            ["PetAction:Recall"] = [new("PetUID", FieldType.LongInteger)],
            ["PetAction:AddLife"] = [new("PetUID", FieldType.LongInteger)],
            ["PetAction:ResetStat"] = [new("PetUID", FieldType.LongInteger)],
            ["PetAction:Rename"] =
            [
                new("PetUID", FieldType.LongInteger),
                new("NewName", FieldType.NonEmptyText)
            ],
            ["PetAction:Equip"] =
            [
                new("PetUID", FieldType.LongInteger),
                new("ItemUID", FieldType.LongInteger),
                new("TargetSlot", FieldType.Integer)
            ],
            ["PetAction:Unequip"] =
            [
                new("PetUID", FieldType.LongInteger),
                new("TargetSlot", FieldType.Integer)
            ],
            ["PetAction:Feed"] =
            [
                new("PetUID", FieldType.LongInteger),
                new("ItemUID", FieldType.LongInteger)
            ],
            ["PetAction:SetMainSkill"] =
            [
                new("PetUID", FieldType.LongInteger),
                new("SkillID", FieldType.Integer)
            ],
            ["PetAction:AllocateStat"] =
            [
                new("PetUID", FieldType.LongInteger),
                new("StatType", FieldType.NonEmptyText)
            ],
            ["PetAction:SetCombatMode"] =
                [new("CombatMode", FieldType.Integer)],
            ["PetAction:CommandAttack"] =
                [new("TargetEntityID", FieldType.Integer)],
            ["PartyAction:Invite"] =
                [new("TargetDBID", FieldType.LongInteger)],
            ["PartyAction:Accept"] =
                [new("TargetDBID", FieldType.LongInteger)],
            ["PartyAction:Decline"] =
                [new("TargetDBID", FieldType.LongInteger)],
            ["PartyAction:Kick"] =
                [new("TargetDBID", FieldType.LongInteger)],
            ["ClubAction:Create"] =
                [new("ClubName", FieldType.NonEmptyText)],
            ["ClubAction:Invite"] =
                [new("TargetDBID", FieldType.LongInteger)],
            ["ClubAction:Accept"] =
                [new("TargetDBID", FieldType.LongInteger)],
            ["ClubAction:UpdateMessage"] =
                [new("ClubMsg", FieldType.Text)],
            ["ClubAction:DonateMoney"] =
                [new("Amount", FieldType.LongInteger)],
            ["ClubAction:DonateExp"] =
                [new("Amount", FieldType.LongInteger)],
            ["ClubAction:Research"] =
                [new("ResearchID", FieldType.Integer)],
            ["MallAction:BuyItem"] =
            [
                new("MallID", FieldType.Integer),
                new("Count", FieldType.Integer)
            ],
            ["MallAction:CreatePayPalDonation"] =
                [new("Amount", FieldType.Integer)],
            ["MallAction:QueryLegacyPoints"] =
            [
                new("LoginName", FieldType.NonEmptyText),
                new("Password", FieldType.Text)
            ],
            ["MallAction:WithdrawLegacyPoints"] =
            [
                new("LoginName", FieldType.NonEmptyText),
                new("Password", FieldType.Text),
                new("Amount", FieldType.Integer)
            ],
            ["MallAction:ClaimDonationGift"] =
                [new("RewardID", FieldType.Integer)]
        };

    public static bool TryValidate(
        JsonElement packet,
        string packetType,
        out string error)
    {
        error = "";
        if (packet.ValueKind != JsonValueKind.Object)
        {
            error = "封包必須是 JSON 物件";
            return false;
        }

        Rules.TryGetValue(packetType, out var fields);
        Field[]? actionFields = null;
        if (packet.TryGetProperty("Action", out var action) &&
            action.ValueKind == JsonValueKind.String)
            ActionRules.TryGetValue(
                packetType + ":" + action.GetString(), out actionFields);

        foreach (var field in (fields ?? []).Concat(actionFields ?? []))
        {
            if (!packet.TryGetProperty(field.Name, out var value))
            {
                if (!field.Required)
                    continue;

                error = $"缺少必要欄位 {field.Name}";
                return false;
            }

            var valid = field.Type switch
            {
                FieldType.Integer =>
                    value.ValueKind == JsonValueKind.Number &&
                    value.TryGetInt32(out _),
                FieldType.LongInteger =>
                    value.ValueKind == JsonValueKind.Number &&
                    value.TryGetInt64(out _),
                FieldType.Text =>
                    value.ValueKind == JsonValueKind.String &&
                    value.GetString()!.Length <= 4096,
                FieldType.NonEmptyText =>
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()) &&
                    value.GetString()!.Length <= 256,
                FieldType.Boolean =>
                    value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                FieldType.LongIntegerArray =>
                    value.ValueKind == JsonValueKind.Array &&
                    value.GetArrayLength() <= 512 &&
                    value.EnumerateArray().All(item =>
                        item.ValueKind == JsonValueKind.Number &&
                        item.TryGetInt64(out _)),
                _ => false
            };
            if (!valid)
            {
                error = $"欄位 {field.Name} 的資料型別或格式不正確";
                return false;
            }
        }

        return true;
    }
}
