using System.Text.Json;
using CedarSharp;
using DogwoodSharp;

namespace CedarSharp.E1Prototype;

/// <summary>Builds CedarSharp authorization requests and shared engine plumbing for the corpus.</summary>
public static class E1Requests
{
    public static readonly CedarEngine Engine = new();

    public static CedarAuthorizationRequest Build(E1Bundle bundle, E1Case testCase, string? entitiesJson = null)
    {
        var entities = testCase.MergeActionEntities
            ? MergeActionEntities(entitiesJson ?? testCase.EntitiesJson, bundle)
            : entitiesJson ?? testCase.EntitiesJson;
        return new CedarAuthorizationRequest(
            new CedarEntityUid(testCase.Principal.Type, testCase.Principal.Id),
            new CedarEntityUid(testCase.Action.Type, testCase.Action.Id),
            new CedarEntityUid(testCase.Resource.Type, testCase.Resource.Id),
            bundle.PolicySet,
            testCase.ContextJson,
            entities,
            testCase.PassSchema ? bundle.SchemaJson : null,
            testCase.ValidateRequest);
    }

    /// <summary>Runs one corpus case through Dogwood lowering and CedarSharp authorization.</summary>
    public static (E1Bundle Bundle, CedarAuthorizationResult Result) Run(E1Case testCase)
    {
        var bundle = E1Bundle.FromDogwood(testCase.Source, testCase.Schema);
        var result = Engine.Authorize(Build(bundle, testCase));
        return (bundle, result);
    }

    /// <summary>
    /// Synthesizes the schema's action-hierarchy entities from the exported
    /// augmented schema JSON and merges them into an entities array. Cedar
    /// derives the same action entities and ancestor edges from the schema, so
    /// this path is redundant when the schema is attached. It exists to prove
    /// that redundancy and to model the upstream pipeline's explicit merge.
    /// </summary>
    public static string MergeActionEntities(string entitiesJson, E1Bundle bundle)
    {
        using var schema = JsonDocument.Parse(bundle.Lowered.CedarSchemaJson);
        var actions = ActionEntities(schema.RootElement);
        var trimmed = entitiesJson.Trim();
        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
        {
            throw new InvalidOperationException("The entities payload is not a JSON array.");
        }

        var inner = trimmed[1..^1].Trim();
        var merged = inner.Length == 0
            ? string.Join(",", actions)
            : string.Join(",", actions.Prepend(inner));
        return $"[{merged}]";
    }

    /// <summary>
    /// Every action entity declared by the exported augmented schema, in Cedar's
    /// entities JSON form.
    /// </summary>
    /// <remarks>
    /// The exported Cedar JSON schema carries the action hierarchy in each
    /// action's <c>memberOf</c> list, whose entries name the group actions the
    /// action belongs to. Cedar's own schema-derived action entities use the
    /// same edges, so a payload synthesized this way matches what Cedar would
    /// add from the schema. Cedar adds those entities itself when a schema is
    /// present, so this is a redundant path kept as evidence.
    /// </remarks>
    public static IReadOnlyList<string> ActionEntities(JsonElement schema)
    {
        var entities = new List<string>();
        foreach (var namespaceProperty in schema.EnumerateObject())
        {
            var namespaceName = namespaceProperty.Name;
            if (!namespaceProperty.Value.TryGetProperty("actions", out var actions))
            {
                continue;
            }

            var actionType = namespaceName.Length == 0 ? "Action" : $"{namespaceName}::Action";
            foreach (var action in actions.EnumerateObject())
            {
                var parents = new List<string>();
                if (action.Value.TryGetProperty("memberOf", out var memberOf))
                {
                    foreach (var parent in memberOf.EnumerateArray())
                    {
                        var parentType = parent.TryGetProperty("type", out var type)
                            ? type.GetString() ?? actionType
                            : actionType;
                        var parentId = parent.GetProperty("id").GetString()
                            ?? throw new JsonException("An action parent is missing its id.");
                        parents.Add(ParentUid(parentType, parentId));
                    }
                }

                entities.Add(ActionEntity(actionType, action.Name, parents));
            }
        }

        return entities;
    }

    private static string ParentUid(string type, string id) =>
        "{\"type\":" + Json(type) + ",\"id\":" + Json(id) + "}";

    private static string ActionEntity(string type, string id, IReadOnlyList<string> parents) =>
        "{\"uid\":{\"type\":" + Json(type) + ",\"id\":" + Json(id) + "},\"attrs\":{},\"parents\":[" +
        string.Join(",", parents) + "]}";

    private static string Json(string value) => E1ReferenceInput.JsonString(value);
}
