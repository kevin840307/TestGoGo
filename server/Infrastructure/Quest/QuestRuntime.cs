using System.Text;
using MoonSharp.Interpreter;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Infrastructure.Quest;

public sealed class QuestExecutionResult
{
    public List<object> Packets { get; } = new();
    public int? NewMapId { get; set; }
    public int? NewX { get; set; }
    public int? NewY { get; set; }
    public int? RebornMapId { get; set; }
    public int? RebornX { get; set; }
    public int? RebornY { get; set; }
}

public sealed class QuestRuntimeBindings
{
    public Func<string, int>? GetFlag { get; init; }
    public Action<string, int>? SetFlag { get; init; }
    public Func<int, int, int>? GetItemCount { get; init; }
    public Func<int, int>? ItemListCountLeft { get; init; }
    public Func<int, int, int, bool>? AddItem { get; init; }
    public Func<int, int, int, bool>? RemoveItem { get; init; }

    public long CallerDbid { get; init; }
    public Func<IReadOnlyList<long>>? GetTeamMemberIds { get; init; }
    public Action<long, string, int>? SetTargetFlag { get; init; }
    public Func<long, string, object?>? GetTargetData { get; init; }
    public Func<long, (double X, double Y)>? GetTargetXY { get; init; }
    public Func<long, int>? GetTargetEmotion { get; init; }
}

public sealed class QuestRuntime
{
    private readonly string _questRoot;
    private readonly ShopCatalog _shops;
    private readonly EnemyDropCatalog? _legacyItems;

    public QuestRuntime(
        string questRoot,
        ShopCatalog shops,
        EnemyDropCatalog? legacyItems = null)
    {
        _questRoot = questRoot;
        _shops = shops;
        _legacyItems = legacyItems;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public QuestExecutionResult Execute(
        string scriptName,
        string functionName,
        int entityId,
        string speaker,
        int? choiceIndex,
        Func<string, object?> getData,
        QuestRuntimeBindings? bindings = null)
    {
        var result = new QuestExecutionResult();

        if (functionName.Equals("End", StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("QuestEnd", StringComparison.OrdinalIgnoreCase))
        {
            result.Packets.Add(new
            {
                Type = "NpcDialogResponse",
                EntityID = entityId,
                ScriptName = scriptName,
                DialogType = "Close"
            });
            return result;
        }

        var path = Path.Combine(_questRoot, scriptName);
        if (!File.Exists(path))
        {
            result.Packets.Add(new {
                Type = "SystemMessageChat",
                Message = $"找不到任務腳本: {scriptName}"
            });
            return result;
        }

        var source = LoadAndPreprocessScript(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var script = new Script(CoreModules.Preset_Complete);

        script.Globals["GetChoice"] = (Func<int>)(() => choiceIndex ?? 0);
        script.Globals["GetData"] = (Func<string, object?>)(name => getData(name));

        // Common quest APIs. Flags and item mutations are bound by the
        // application layer so legacy Lua can keep its original API surface.
        var teamMemberIds =
            bindings?.GetTeamMemberIds?.Invoke() ??
            Array.Empty<long>();
        var teamCursor = -1;

        script.Globals["GetCallerPtr"] =
            (Func<long>)(() =>
                bindings?.CallerDbid ?? 0L);

        script.Globals["GetTeamList_First"] =
            DynValue.NewCallback((ctx, args) =>
            {
                teamCursor = 0;
                if (teamMemberIds.Count == 0)
                {
                    return DynValue.NewTuple(
                        DynValue.NewNumber(0),
                        DynValue.NewNumber(0));
                }

                return DynValue.NewTuple(
                    DynValue.NewNumber(1),
                    DynValue.NewNumber(teamMemberIds[0]));
            });

        script.Globals["GetTeamList_Next"] =
            DynValue.NewCallback((ctx, args) =>
            {
                teamCursor++;
                if (teamCursor < 0 ||
                    teamCursor >= teamMemberIds.Count)
                {
                    return DynValue.NewTuple(
                        DynValue.NewNumber(0),
                        DynValue.NewNumber(0));
                }

                return DynValue.NewTuple(
                    DynValue.NewNumber(1),
                    DynValue.NewNumber(
                        teamMemberIds[teamCursor]));
            });

        script.Globals["SetTargetFlag"] =
            (Action<long,string,int>)((target, flag, value) =>
                bindings?.SetTargetFlag?.Invoke(
                    target,
                    flag,
                    value));

        script.Globals["GetTargetData"] =
            DynValue.NewCallback((ctx, args) =>
            {
                if (args.Count < 2)
                    return DynValue.NewNumber(0);

                var target = (long)args[0].Number;
                var key = args[1].CastToString();
                var value =
                    bindings?.GetTargetData?.Invoke(
                        target,
                        key);

                return DynValue.FromObject(
                    script,
                    value ?? 0);
            });

        script.Globals["GetTargetXY"] =
            DynValue.NewCallback((ctx, args) =>
            {
                if (args.Count < 1)
                {
                    return DynValue.NewTuple(
                        DynValue.NewNumber(0),
                        DynValue.NewNumber(0));
                }

                var target = (long)args[0].Number;
                var position =
                    bindings?.GetTargetXY?.Invoke(target) ??
                    (0.0, 0.0);

                return DynValue.NewTuple(
                    DynValue.NewNumber(position.X),
                    DynValue.NewNumber(position.Y));
            });

        script.Globals["GetTargetEmotion"] =
            (Func<long,int>)(target =>
                bindings?.GetTargetEmotion?.Invoke(target) ?? 0);

        script.Globals["GetFlag"] = (Func<string,int>)(name =>
            bindings?.GetFlag?.Invoke(name) ?? 0);
        script.Globals["SetFlag"] = (Action<string,int>)((name, value) =>
            bindings?.SetFlag?.Invoke(name, value));
        script.Globals["GetName"] = (Func<string>)(() =>
            Convert.ToString(getData("NAME")) ?? "");
        script.Globals["GetRealTime"] = (Func<int>)(() =>
            checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        script.Globals["Color"] = DynValue.NewCallback((ctx, args) =>
            DynValue.NewString(""));
        script.Globals["HasTitle"] = (Func<int,int>)(_ => 0);
        script.Globals["AddTitle"] = (Action<int>)(_ => { });
        script.Globals["SetData"] = DynValue.NewCallback((ctx, args) => DynValue.Nil);
        script.Globals["SetCareer"] = DynValue.NewCallback((ctx, args) => DynValue.Nil);

        script.Globals["CheckItem"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1)
                return DynValue.NewNumber(0);

            var legacyItemId = (int)args[0].Number;
            var mapping = _legacyItems?.ResolveItem(legacyItemId);
            if (mapping is null)
                return DynValue.NewNumber(0);

            var count = bindings?.GetItemCount?.Invoke(
                mapping.ItemId,
                mapping.TabType) ?? 0;

            return DynValue.NewNumber(count > 0 ? 1 : 0);
        });

        script.Globals["GetItemCount"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1)
                return DynValue.NewNumber(0);

            var legacyItemId = (int)args[0].Number;
            var mapping = _legacyItems?.ResolveItem(legacyItemId);
            if (mapping is null)
                return DynValue.NewNumber(0);

            var count = bindings?.GetItemCount?.Invoke(
                mapping.ItemId,
                mapping.TabType) ?? 0;

            return DynValue.NewNumber(count);
        });

        script.Globals["ItemListCountLeft"] = (Func<int,int>)(tabType =>
            bindings?.ItemListCountLeft?.Invoke(tabType) ?? 999);

        script.Globals["AddItem"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1)
                return DynValue.NewBoolean(false);

            var legacyItemId = (int)args[0].Number;
            var count = args.Count > 1
                ? Math.Max(1, (int)args[1].Number)
                : 1;
            var mapping = _legacyItems?.ResolveItem(legacyItemId);

            if (mapping is null ||
                bindings?.AddItem is null)
            {
                if (bindings is not null)
                {
                    throw new ScriptRuntimeException(
                        $"AddItem mapping unavailable: legacy item {legacyItemId}");
                }

                return DynValue.NewBoolean(false);
            }

            var success = bindings.AddItem(
                mapping.ItemId,
                mapping.TabType,
                count);

            if (!success)
            {
                throw new ScriptRuntimeException(
                    $"AddItem failed: legacy item {legacyItemId}, count {count}");
            }

            return DynValue.NewBoolean(true);
        });

        script.Globals["RemoveItem"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1)
                return DynValue.NewBoolean(false);

            var legacyItemId = (int)args[0].Number;
            var count = args.Count > 1
                ? Math.Max(1, (int)args[1].Number)
                : 1;
            var mapping = _legacyItems?.ResolveItem(legacyItemId);

            if (mapping is null ||
                bindings?.RemoveItem is null)
            {
                if (bindings is not null)
                {
                    throw new ScriptRuntimeException(
                        $"RemoveItem mapping unavailable: legacy item {legacyItemId}");
                }

                return DynValue.NewBoolean(false);
            }

            var success = bindings.RemoveItem(
                mapping.ItemId,
                mapping.TabType,
                count);

            if (!success)
            {
                throw new ScriptRuntimeException(
                    $"RemoveItem failed: legacy item {legacyItemId}, count {count}");
            }

            return DynValue.NewBoolean(true);
        });

        script.Globals["RankQuery"] = (Action<int>)(rankType =>
        {
            // Source 0=level, 1=money. Recovered client uses 1=level, 2=money.
            result.Packets.Add(new
            {
                Type = "OpenRankWindow",
                RankType = rankType + 1
            });
        });

        script.Globals["SetRebornLocation"] = (Action<int,int,int>)((x,y,mapId) =>
        {
            result.RebornMapId = mapId;
            result.RebornX = x;
            result.RebornY = y;
        });

        script.Globals["QuestEnd"] = (Action)(() =>
        {
            result.Packets.Add(new {
                Type = "NpcDialogResponse",
                EntityID = entityId,
                ScriptName = scriptName,
                DialogType = "Close"
            });
        });

        script.Globals["MovePlayer"] = (Action<int,int,int>)((x,y,mapId) =>
        {
            result.NewMapId = mapId;
            result.NewX = x;
            result.NewY = y;
            result.Packets.Add(new {
                Type = "ChangeMap",
                MapID = mapId,
                X = x,
                Y = y
            });
        });

        script.Globals["Say"] = DynValue.NewCallback((ctx, args) =>
        {
            var text = DialogFormatter.CleanText(
                args.Count > 0 ? args[0].CastToString() : "");
            var next = args.Count > 1 ? args[1].CastToString() : "";
            result.Packets.Add(new {
                Type = "NpcDialogResponse",
                EntityID = entityId,
                ScriptName = scriptName,
                DialogType = "ActionDialog",
                Text = text,
                Speaker = speaker,
                BubbleType = 0,
                NextNode = next
            });
            return DynValue.Nil;
        });

        script.Globals["Ask"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 2) return DynValue.Nil;

            var text = DialogFormatter.CleanText(args[0].CastToString());
            var target = args[args.Count - 1].CastToString();
            var options = new List<object>();

            for (var i = 1; i < args.Count - 1; i++)
            {
                options.Add(new {
                    Text = args[i].CastToString(),
                    TargetNode = $"{target}#{i - 1}"
                });
            }

            result.Packets.Add(new {
                Type = "NpcDialogResponse",
                EntityID = entityId,
                ScriptName = scriptName,
                DialogType = "Dialog",
                Text = text,
                Speaker = speaker,
                BubbleType = 0,
                Options = options
            });

            return DynValue.Nil;
        });

        script.Globals["OpenBank"] = (Action)(() =>
        {
            result.Packets.Add(new
            {
                Type = "OpenBankWindow"
            });
        });

        script.Globals["OpenRepair"] = DynValue.NewCallback((ctx, args) =>
        {
            var repairType = args.Count > 0 ? (int)args[0].Number : 1;
            result.Packets.Add(new
            {
                Type = "OpenRepairWindow",
                RepairType = repairType
            });
            return DynValue.Nil;
        });

        script.Globals["OpenShop"] = DynValue.NewCallback((ctx, args) =>
        {
            var shopId = args.Count > 0 ? (int)args[0].Number : 0;
            var priceRate = args.Count > 1 ? (int)args[1].Number : 100;

            // The client has a dedicated NPC shop UI. Item population is wired
            // separately from item/shop data; returning an empty list here
            // is still a valid functional OpenShop packet and avoids Lua failure.
            result.Packets.Add(new
            {
                Type = "NpcShopViewResponse",
                ShopID = shopId,
                ShopName = speaker,
                PriceRate = priceRate,
                Items = _shops.GetShopItems(shopId)
                    .Select(item => (object)new
                    {
                        ItemIndex = item.ItemIndex,
                        Price = Math.Max(
                            1,
                            checked(item.Price * Math.Clamp(priceRate, 1, 1000) / 100)),
                        TabType = item.TabType,
                        Name = item.Name
                    })
                    .ToArray()
            });
            return DynValue.Nil;
        });

        script.DoString(source);

        var fn = script.Globals.Get(functionName);
        if (fn.Type == DataType.Function || fn.Type == DataType.ClrFunction)
            script.Call(fn);
        else
            result.Packets.Add(new {
                Type = "SystemMessageChat",
                Message = $"找不到腳本節點: {scriptName}/{functionName}"
            });

        return result;
    }

    private string LoadAndPreprocessScript(
        string path,
        HashSet<string> includeStack)
    {
        var fullPath = Path.GetFullPath(path);
        if (!includeStack.Add(fullPath))
            throw new InvalidDataException($"Circular quest include: {fullPath}");

        var source = ReadScriptText(fullPath);
        var output = new StringBuilder();

        foreach (var rawLine in source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("#include", StringComparison.OrdinalIgnoreCase))
            {
                var first = line.IndexOf('"');
                var last = line.LastIndexOf('"');
                if (first >= 0 && last > first)
                {
                    var includeName = line[(first + 1)..last];
                    var includePath = Path.Combine(_questRoot, includeName);
                    output.AppendLine(
                        LoadAndPreprocessScript(includePath, includeStack));
                    continue;
                }
            }

            // Some quest scripts contain backslashes before Chinese smart quotes,
            // e.g. \“...\”. Old CDELua tolerated these; standard Lua/MoonSharp does not.
            output.AppendLine(
                rawLine
                    .Replace("\\“", "“")
                    .Replace("\\”", "”")
                    .Replace("\\‘", "‘")
                    .Replace("\\’", "’"));
        }

        includeStack.Remove(fullPath);
        return output.ToString();
    }

    private static string ReadScriptText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            return utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(950).GetString(bytes);
        }
    }
}
