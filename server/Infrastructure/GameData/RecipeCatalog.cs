using System.Text.Json;

namespace SGO.Server.Infrastructure.GameData;

public sealed record RecipeItemDefinition(
    int ItemId,
    int Count);

public sealed record RecipeDefinition(
    int RecipeId,
    string ActionType,
    string Category,
    int BlueprintId,
    int SuccessRate,
    int MoneyCost,
    IReadOnlyList<RecipeItemDefinition> Inputs,
    IReadOnlyList<RecipeItemDefinition> Outputs);

public sealed class RecipeCatalog
{
    private readonly Dictionary<int, RecipeDefinition> _byId = new();
    private readonly Dictionary<int, RecipeDefinition> _dismantleByInput =
        new();

    public RecipeCatalog(string path)
    {
        if (!File.Exists(path))
            return;

        using var document = JsonDocument.Parse(
            File.ReadAllText(path));

        if (document.RootElement.ValueKind !=
            JsonValueKind.Array)
            return;

        foreach (var element in
                 document.RootElement.EnumerateArray())
        {
            var recipeId = ReadInt(
                element,
                "RecipeID");
            var actionType = ReadString(
                element,
                "ActionType");

            if (recipeId <= 0 ||
                string.IsNullOrWhiteSpace(actionType))
                continue;

            var inputs = ReadItems(
                element,
                "Inputs");
            var outputs = ReadItems(
                element,
                "Outputs");

            if (inputs.Count == 0 ||
                outputs.Count == 0)
                continue;

            var recipe = new RecipeDefinition(
                recipeId,
                actionType,
                ReadString(
                    element,
                    "Category"),
                ReadInt(
                    element,
                    "BlueprintID"),
                Math.Clamp(
                    ReadInt(
                        element,
                        "SuccessRate",
                        100),
                    0,
                    100),
                Math.Max(
                    0,
                    ReadInt(
                        element,
                        "MoneyCost")),
                inputs,
                outputs);

            _byId[recipeId] = recipe;

            if (actionType.Equals(
                    "Dismantle",
                    StringComparison.OrdinalIgnoreCase) &&
                inputs.Count == 1)
            {
                _dismantleByInput[
                    inputs[0].ItemId] = recipe;
            }
        }
    }

    public RecipeDefinition? Find(int recipeId)
        => _byId.TryGetValue(
                recipeId,
                out var recipe)
            ? recipe
            : null;

    public RecipeDefinition? FindDismantle(
        int inputItemId)
        => _dismantleByInput.TryGetValue(
                inputItemId,
                out var recipe)
            ? recipe
            : null;

    public IReadOnlyCollection<RecipeDefinition> Recipes
        => _byId.Values;

    private static List<RecipeItemDefinition> ReadItems(
        JsonElement element,
        string name)
    {
        var result =
            new List<RecipeItemDefinition>();

        if (!element.TryGetProperty(
                name,
                out var items) ||
            items.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in
                 items.EnumerateArray())
        {
            var itemId =
                ReadInt(item, "ItemID");
            var count = Math.Max(
                1,
                ReadInt(
                    item,
                    "Count",
                    1));

            if (itemId > 0)
            {
                result.Add(
                    new RecipeItemDefinition(
                        itemId,
                        count));
            }
        }

        return result;
    }

    private static int ReadInt(
        JsonElement element,
        string name,
        int defaultValue = 0)
        => element.TryGetProperty(
                name,
                out var value) &&
           value.TryGetInt32(
               out var parsed)
            ? parsed
            : defaultValue;

    private static string ReadString(
        JsonElement element,
        string name)
        => element.TryGetProperty(
                name,
                out var value)
            ? value.GetString() ?? ""
            : "";
}
