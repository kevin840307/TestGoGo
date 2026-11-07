using SGO.Server.Application.Abstractions;

namespace SGO.Server.Application.Rank;

public sealed class RankService
{
    private readonly IGameStore _store;

    public RankService(IGameStore store)
    {
        _store = store;
    }

    public IReadOnlyList<object> GetRankData(
        long accountId,
        long dbid,
        int rankType)
    {
        var rows = _store.GetRankEntries(rankType, 50);
        var rankList = rows
            .Select((x, index) => new
            {
                Rank = index + 1,
                Name = x.Name,
                Value = x.Value,
                DBID = x.DbId
            })
            .Cast<object>()
            .ToArray();

        Dictionary<string, object>? top1Equips = null;
        var top1Message = "尚無宣言";

        if (rows.Count > 0)
        {
            var top = rows[0];
            var messageOwnerDbId =
                top.MessageOwnerDbId > 0
                    ? top.MessageOwnerDbId
                    : top.DbId;
            var appearanceDbId =
                top.AppearanceDbId > 0
                    ? top.AppearanceDbId
                    : top.DbId;

            top1Message = _store.GetRankMessage(
                rankType,
                messageOwnerDbId);

            var character = _store.GetCharacter(
                top.AccountId,
                appearanceDbId);

            if (character is not null)
            {
                var assets = _store.GetCharacterAssets(
                    top.AccountId,
                    appearanceDbId);

                top1Equips = BuildPaperDoll(
                    character,
                    assets.Equips);
            }
        }

        return [new
        {
            Type = "RankDataResponse",
            RankType = rankType,
            RankList = rankList,
            Top1Message = top1Message,
            Top1Equips = top1Equips
        }];
    }

    public IReadOnlyList<object> UpdateMessage(
        long accountId,
        long dbid,
        int rankType,
        string message)
    {
        message = message.Trim();

        if (message.Length > 20)
        {
            return [new
            {
                Type = "UpdateRankMessageResponse",
                Success = false,
                Message = "宣言最多 20 個字"
            }];
        }

        var top = _store.GetRankEntries(
            rankType,
            1).FirstOrDefault();

        var ownerDbId = top?.MessageOwnerDbId > 0
            ? top.MessageOwnerDbId
            : top?.DbId ?? 0;

        if (top is null || ownerDbId != dbid)
        {
            return [new
            {
                Type = "UpdateRankMessageResponse",
                Success = false,
                Message = "只有目前排行榜第一名可以修改宣言"
            }];
        }

        var success = _store.SetRankMessage(
            accountId,
            dbid,
            rankType,
            message);

        return [new
        {
            Type = "UpdateRankMessageResponse",
            Success = success,
            Message = success
                ? "排行榜宣言已更新"
                : "排行榜宣言更新失敗"
        }];
    }

    private static Dictionary<string, object> BuildPaperDoll(
        Dictionary<string, object> character,
        IReadOnlyList<Dictionary<string, object>> equips)
    {
        var result = new Dictionary<string, object>
        {
            ["Race"] = character.GetValueOrDefault("Race", 0),
            ["Hair"] = character.GetValueOrDefault("Hair", 0),
            ["HairColor"] = character.GetValueOrDefault("HairColor", 0),
            ["EquipColor0"] = 0,
            ["Bonnet"] = 0,
            ["Left_hand"] = 0,
            ["Right_hand"] = 0,
            ["Upper_armor"] = 0,
            ["Lower_armor"] = 0,
            ["Weapon"] = 0,
            ["Shield"] = 0,
            ["Back"] = 0
        };

        foreach (var eq in equips)
        {
            if (Convert.ToInt32(eq["InBank"]) != 0 ||
                Convert.ToInt32(eq["InShop"]) != 0)
                continue;

            var part = Convert.ToInt32(eq["Part"]);
            if (part is < 0 or > 7)
                continue;

            var item = Convert.ToInt32(eq["ItemIdx"]);
            var color = Convert.ToInt32(eq["Color"]);

            switch (part)
            {
                case 0: result["Bonnet"] = item; break;
                case 1: result["Left_hand"] = item; break;
                case 2: result["Right_hand"] = item; break;
                case 3: result["Upper_armor"] = item; break;
                case 4: result["Lower_armor"] = item; break;
                case 5: result["Weapon"] = item; break;
                case 6: result["Shield"] = item; break;
                case 7: result["Back"] = item; break;
            }

            result[$"EquipColor{part}"] = color;
        }

        return result;
    }
}
