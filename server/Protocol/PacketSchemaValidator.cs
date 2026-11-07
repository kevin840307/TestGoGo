using System.Text.Json;

namespace SGO.Server.Protocol;

/// <summary>
/// Validate wire shapes before calling mutation handlers. Rules only cover
/// fields the recovered Godot client always sends; optional legacy fields
/// remain optional to preserve protocol compatibility.
/// </summary>
public static class PacketSchemaValidator
{
    private enum FieldType { Integer, Text, NonEmptyText, Boolean, Array }
    private readonly record struct Field(string Name, FieldType Type);

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
            ["UseItemRequest"] = [new("ItemUID", FieldType.Integer)],
            ["PickUpItemRequest"] = [new("EntityID", FieldType.Integer)],
            ["AttackEntity"] = [new("TargetEntityID", FieldType.Integer)],
            ["UseSkill"] = [new("SkillID", FieldType.Integer)],
            ["UpdateShortcut"] =
            [
                new("SlotIndex", FieldType.Integer),
                new("ShortcutType", FieldType.Integer),
                new("TargetID", FieldType.Integer)
            ],
            ["RepairItemRequest"] =
            [
                new("UID", FieldType.Integer),
                new("ItemType", FieldType.NonEmptyText)
            ],
            ["BankTransfer"] =
            [
                new("UID", FieldType.Integer),
                new("TabType", FieldType.Integer),
                new("Amount", FieldType.Integer),
                new("ToBank", FieldType.Boolean)
            ],
            ["TradeRequest"] = [new("TargetEntityID", FieldType.Integer)],
            ["TradeReply"] =
            [
                new("RequesterDBID", FieldType.Integer),
                new("Accept", FieldType.Boolean)
            ],
            ["ShopAction"] = [new("Action", FieldType.NonEmptyText)],
            ["MallAction"] = [new("Action", FieldType.NonEmptyText)],
            ["PetAction"] = [new("Action", FieldType.NonEmptyText)],
            ["PartyAction"] = [new("Action", FieldType.NonEmptyText)],
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
                new("ItemUID", FieldType.Integer),
                new("TargetSlot", FieldType.Integer)
            ],
            ["UnequipRequest"] = [new("TargetSlot", FieldType.Integer)],
            ["TrainTravelReady"] = [new("TravelID", FieldType.NonEmptyText)],
            ["AllocateStatRequest"] = [new("StatType", FieldType.NonEmptyText)],
            ["DriveMechaRequest"] = [new("HorseUID", FieldType.Integer)],
            ["SetTitleRequest"] = [new("TitleID", FieldType.Integer)],
            ["DirectTransfer"] =
            [
                new("TargetDBID", FieldType.Integer),
                new("Amount", FieldType.Integer)
            ],
            ["ReviveAccept"] =
            [
                new("SkillID", FieldType.Integer),
                new("SkillLevel", FieldType.Integer)
            ],
            ["MegaphoneMessage"] =
            [
                new("ItemUID", FieldType.Integer),
                new("Message", FieldType.NonEmptyText)
            ],
            ["CraftItemRequest"] =
            [
                new("RecipeID", FieldType.Integer),
                new("BatchCount", FieldType.Integer)
            ],
            ["DismantleItemRequest"] =
            [
                new("ItemUIDs", FieldType.Array),
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
            ["AbandonQuest"] = [new("QuestID", FieldType.Integer)]
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

        if (!Rules.TryGetValue(packetType, out var fields))
            return true;

        foreach (var field in fields)
        {
            if (!packet.TryGetProperty(field.Name, out var value))
            {
                error = $"缺少必要欄位 {field.Name}";
                return false;
            }

            var valid = field.Type switch
            {
                FieldType.Integer =>
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
                FieldType.Array =>
                    value.ValueKind == JsonValueKind.Array,
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
