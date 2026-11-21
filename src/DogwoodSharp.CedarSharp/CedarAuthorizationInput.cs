namespace DogwoodSharp.Experimental.CedarSharp;

/// <summary>
/// A Cedar entity unique identifier expressed as a bare <paramref name="Type"/> and
/// <paramref name="Id"/>. The adapter maps this to the evaluation engine's own UID type, so the
/// public request surface does not expose the engine's types.
/// </summary>
/// <param name="Type">The Cedar entity type (for example <c>App::User</c>).</param>
/// <param name="Id">The Cedar entity id.</param>
public readonly record struct CedarEntityRef(string Type, string Id);

/// <summary>
/// An immutable authorization request snapshot for the schema-carrying CedarSharp adapter. The
/// adapter copies the caller's JSON into the snapshot and forwards the caller entities unchanged;
/// it never synthesizes, adds, or removes an entity, attribute, parent, or relationship.
/// </summary>
/// <param name="Principal">The principal UID.</param>
/// <param name="Action">The action UID.</param>
/// <param name="Resource">The resource UID.</param>
/// <param name="ContextJson">The Cedar-encoded context JSON (a JSON record).</param>
/// <param name="EntitiesJson">The caller entities in Cedar entity JSON form (a JSON array).</param>
public sealed record CedarAuthorizationInput(
    CedarEntityRef Principal, CedarEntityRef Action, CedarEntityRef Resource,
    string ContextJson, string EntitiesJson);
