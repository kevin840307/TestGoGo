using System.Text;
using MoonSharp.Interpreter;

namespace SGO.Server.Infrastructure.Legacy.Quest;

public sealed class LegacyQuestResult
{
    public List<object> Packets { get; } = new();
    public int? NewMapId { get; set; }
    public int? NewX { get; set; }
    public int? NewY { get; set; }
    public int? RebornMapId { get; set; }
    public int? RebornX { get; set; }
    public int? RebornY { get; set; }
}

public sealed class LegacyQuestRuntime
{
    private readonly string _questRoot;

    public LegacyQuestRuntime(string questRoot)
    {
        _questRoot = questRoot;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public LegacyQuestResult Execute(
        string scriptName,
        string functionName,
        int entityId,
        string speaker,
        int? choiceIndex,
        Func<string, object?> getData)
    {
        var result = new LegacyQuestResult();

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
                Message = $"找不到舊任務腳本: {scriptName}"
            });
            return result;
        }

        var source = LoadAndPreprocessScript(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var script = new Script(CoreModules.Preset_Complete);

        script.Globals["GetChoice"] = (Func<int>)(() => choiceIndex ?? 0);
        script.Globals["GetData"] = (Func<string, object?>)(name => getData(name));

        // Common legacy quest APIs. State-changing calls that are not yet wired
        // to persistence are safe compatibility shims instead of nil globals.
        script.Globals["GetFlag"] = (Func<string,int>)(_ => 0);
        script.Globals["SetFlag"] = (Action<string,int>)((_, _) => { });
        script.Globals["GetName"] = (Func<string>)(() =>
            Convert.ToString(getData("NAME")) ?? "");
        script.Globals["Color"] = DynValue.NewCallback((ctx, args) =>
            DynValue.NewString(""));
        script.Globals["HasTitle"] = (Func<int,int>)(_ => 0);
        script.Globals["AddTitle"] = (Action<int>)(_ => { });
        script.Globals["SetData"] = DynValue.NewCallback((ctx, args) => DynValue.Nil);
        script.Globals["SetCareer"] = DynValue.NewCallback((ctx, args) => DynValue.Nil);
        script.Globals["RemoveItem"] = DynValue.NewCallback((ctx, args) => DynValue.Nil);
        script.Globals["AddItem"] = DynValue.NewCallback((ctx, args) => DynValue.Nil);

        script.Globals["RankQuery"] = (Action<int>)(rankType =>
        {
            // Legacy 0=level, 1=money. Recovered client uses 1=level, 2=money.
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
            var text = LegacyDialogFormatter.CleanText(
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

            var text = LegacyDialogFormatter.CleanText(args[0].CastToString());
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
            // separately from legacy item/shop data; returning an empty list here
            // is still a valid functional OpenShop packet and avoids Lua failure.
            result.Packets.Add(new
            {
                Type = "NpcShopViewResponse",
                ShopID = shopId,
                ShopName = speaker,
                PriceRate = priceRate,
                Items = Array.Empty<object>()
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
            throw new InvalidDataException($"Circular legacy quest include: {fullPath}");

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

            // Some legacy scripts contain backslashes before Chinese smart quotes,
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
