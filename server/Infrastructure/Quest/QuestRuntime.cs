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

    public Func<int, bool>? HasTitle { get; init; }
    public Func<int, bool>? UnlockTitle { get; init; }

    public Func<int, bool>? HasSkill { get; init; }
    public Func<int, bool>? AddSkill { get; init; }
    public Func<int, int, bool>? SetCareer { get; init; }
    public Func<string, int, bool>? SetData { get; init; }
    public Func<string, int, bool>? ModifyData { get; init; }
    public bool SuppressLegacyMediaDialogs { get; init; }
}

public sealed class QuestRuntime
{
    private readonly string _questRoot;
    private readonly ShopCatalog _shops;
    private readonly EnemyDropCatalog? _legacyItems;
    private readonly LegacyClientDataCatalog? _legacyClient;
    private readonly CurrentClientDataCatalog? _current;

    public QuestRuntime(
        string questRoot,
        ShopCatalog shops,
        EnemyDropCatalog? legacyItems = null,
        LegacyClientDataCatalog? legacyClient = null,
        CurrentClientDataCatalog? current = null)
    {
        _questRoot = questRoot;
        _shops = shops;
        _legacyItems = legacyItems;
        _legacyClient = legacyClient;
        _current = current;
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
        script.Globals["floor"] =
            (Func<double,double>)(value => Math.Floor(value));
        script.Globals["random"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1)
                return DynValue.NewNumber(0);

            var first = (int)args[0].Number;

            if (args.Count == 1)
            {
                if (first <= 0)
                    return DynValue.NewNumber(0);

                return DynValue.NewNumber(
                    Random.Shared.Next(1, first + 1));
            }

            var second = (int)args[1].Number;
            var min = Math.Min(first, second);
            var max = Math.Max(first, second);

            if (min == max)
                return DynValue.NewNumber(min);

            return DynValue.NewNumber(
                Random.Shared.Next(min, max + 1));
        });

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
        {
            if (args.Count < 3 ||
                args[0].Type != DataType.Number ||
                args[1].Type != DataType.Number ||
                args[2].Type != DataType.Number)
                return DynValue.NewString("");

            return DynValue.NewString(DialogFormatter.ColorToken(
                (int)args[0].Number,
                (int)args[1].Number,
                (int)args[2].Number));
        });
        script.Globals["HasTitle"] = (Func<int,int>)(titleId =>
            bindings?.HasTitle?.Invoke(titleId) == true
                ? 1
                : 0);

        script.Globals["AddTitle"] = (Action<int>)(titleId =>
        {
            if (bindings?.HasTitle?.Invoke(titleId) == true)
                return;

            if (bindings?.UnlockTitle?.Invoke(titleId) != true)
                return;

            result.Packets.Add(new
            {
                Type = "UnlockTitle",
                TitleID = titleId
            });
        });
        script.Globals["SetData"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 2)
                return DynValue.NewBoolean(false);

            var key = args[0].CastToString();
            var value = (int)args[1].Number;

            if (!key.Equals(
                    "Title",
                    StringComparison.OrdinalIgnoreCase))
            {
                return DynValue.NewBoolean(false);
            }

            if (bindings?.SetData?.Invoke(key, value) != true)
            {
                throw new ScriptRuntimeException(
                    $"SetData failed: {key}={value}");
            }

            return DynValue.NewBoolean(true);
        });

        script.Globals["ModifyData"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 2)
                return DynValue.NewBoolean(false);

            var key = args[0].CastToString();
            var delta = (int)args[1].Number;

            if (!key.Equals(
                    "Money",
                    StringComparison.OrdinalIgnoreCase))
            {
                return DynValue.NewBoolean(false);
            }

            if (bindings?.ModifyData?.Invoke(key, delta) != true)
            {
                throw new ScriptRuntimeException(
                    $"ModifyData failed: {key} delta={delta}");
            }

            return DynValue.NewBoolean(true);
        });

        script.Globals["SetCareer"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 2)
                return DynValue.NewBoolean(false);

            var career = (int)args[0].Number;
            var job = (int)args[1].Number;

            if (bindings?.SetCareer?.Invoke(career, job) != true)
                throw new ScriptRuntimeException(
                    $"SetCareer failed: Career={career}, Job={job}");

            return DynValue.NewBoolean(true);
        });

        script.Globals["HasSkill"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1)
                return DynValue.NewNumber(0);

            var currentSkillId = ResolveLegacySkillId(
                (int)args[0].Number);

            return DynValue.NewNumber(
                currentSkillId > 0 &&
                bindings?.HasSkill?.Invoke(currentSkillId) == true
                    ? 1
                    : 0);
        });

        script.Globals["AddSkill"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1)
                return DynValue.NewBoolean(false);

            var legacySkillId = (int)args[0].Number;
            var currentSkillId =
                ResolveLegacySkillId(legacySkillId);

            if (currentSkillId <= 0)
            {
                throw new ScriptRuntimeException(
                    $"AddSkill mapping unavailable: legacy skill {legacySkillId}");
            }

            if (bindings?.AddSkill?.Invoke(currentSkillId) != true)
            {
                throw new ScriptRuntimeException(
                    $"AddSkill failed: legacy {legacySkillId} -> current {currentSkillId}");
            }

            return DynValue.NewBoolean(true);
        });

        script.Globals["GetCountItemCount"] =
            DynValue.NewCallback((ctx, args) =>
            {
                if (args.Count < 1)
                    return DynValue.NewNumber(0);

                var mapping = _legacyItems?.ResolveItem(
                    (int)args[0].Number);

                if (mapping is null)
                    return DynValue.NewNumber(0);

                var count = bindings?.GetItemCount?.Invoke(
                    mapping.ItemId,
                    mapping.TabType) ?? 0;

                return DynValue.NewNumber(count);
            });

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

        script.Globals["AddGift"] = DynValue.NewCallback((ctx, args) =>
        {
            // Legacy CDGiftSys.dll exposed AddGift(ok, fail, none).
            // The recovered server currently has no pending-gift provider,
            // so preserve script flow by dispatching the original "none"
            // callback instead of leaving AddGift as a nil Lua global.
            var noneTarget = args.Count > 2
                ? args[2].CastToString()
                : "";

            if (!string.IsNullOrWhiteSpace(noneTarget))
            {
                var target = script.Globals.Get(noneTarget);
                if (target.Type == DataType.Function ||
                    target.Type == DataType.ClrFunction)
                {
                    script.Call(target);
                }
            }

            return DynValue.Nil;
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
            var rawText =
                args.Count > 0
                    ? args[0].CastToString()
                    : "";

            if (bindings?.SuppressLegacyMediaDialogs == true &&
                rawText.StartsWith(
                    "m#",
                    StringComparison.OrdinalIgnoreCase))
            {
                return DynValue.Nil;
            }

            var dialog = DialogFormatter.Parse(rawText, speaker);
            var next = args.Count > 1 ? args[1].CastToString() : "";
            result.Packets.Add(new {
                Type = "NpcDialogResponse",
                EntityID = entityId,
                ScriptName = scriptName,
                DialogType = "ActionDialog",
                Text = dialog.Text,
                PlainText = dialog.PlainText,
                LegacyPrefix = dialog.Prefix,
                DialogLayout = dialog.Layout,
                LegacyMedia = dialog.Media,
                Speaker = dialog.Speaker,
                BubbleType = 0,
                NextNode = next
            });
            return DynValue.Nil;
        });

        script.Globals["Ask"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 2) return DynValue.Nil;

            var dialog = DialogFormatter.Parse(args[0].CastToString(), speaker);
            var target = args[args.Count - 1].CastToString();
            var options = new List<object>();

            for (var i = 1; i < args.Count - 1; i++)
            {
                // Original Lua may concatenate Color() into a choice label.
                // Godot Button has no BBCode parser: keep its text plain and
                // preserve the original target and choice ordering.
                options.Add(new {
                    Text = DialogFormatter.PlainInlineText(args[i].CastToString()),
                    TargetNode = $"{target}#{i - 1}"
                });
            }

            result.Packets.Add(new {
                Type = "NpcDialogResponse",
                EntityID = entityId,
                ScriptName = scriptName,
                DialogType = "Dialog",
                Text = dialog.Text,
                PlainText = dialog.PlainText,
                LegacyPrefix = dialog.Prefix,
                DialogLayout = dialog.Layout,
                Speaker = dialog.Speaker,
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

    private int ResolveLegacySkillId(int legacySkillId)
    {
        if (legacySkillId <= 0 ||
            _legacyClient is null ||
            _current is null)
            return 0;

        var legacyName =
            _legacyClient.FindSkillName(legacySkillId);

        if (string.IsNullOrWhiteSpace(legacyName))
            return 0;

        return _current.FindSkillByName(legacyName)?.SkillId ?? 0;
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
