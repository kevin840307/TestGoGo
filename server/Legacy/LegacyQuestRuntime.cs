using System.Text;
using MoonSharp.Interpreter;

namespace SGO.Server.Legacy;

public sealed class LegacyQuestResult
{
    public List<object> Packets { get; } = new();
    public int? NewMapId { get; set; }
    public int? NewX { get; set; }
    public int? NewY { get; set; }
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
        var path = Path.Combine(_questRoot, scriptName);
        if (!File.Exists(path))
        {
            result.Packets.Add(new {
                Type = "SystemMessageChat",
                Message = $"找不到舊任務腳本: {scriptName}"
            });
            return result;
        }

        var source = ReadScriptText(path);
        var script = new Script(CoreModules.Preset_Complete);

        script.Globals["GetChoice"] = (Func<int>)(() => choiceIndex ?? 0);
        script.Globals["GetData"] = (Func<string, object?>)(name => getData(name));

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
            var text = args.Count > 0 ? args[0].CastToString() : "";
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

            var text = args[0].CastToString();
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

        // Compatibility stubs. These keep simple legacy scripts running while
        // unsupported systems are implemented incrementally.
        script.Globals["OpenShop"] = DynValue.NewCallback((ctx, args) =>
        {
            var shopId = args.Count > 0 ? (int)args[0].Number : 0;
            result.Packets.Add(new {
                Type = "SystemMessageChat",
                Message = $"NPC 商店 {shopId} 尚未實作"
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
