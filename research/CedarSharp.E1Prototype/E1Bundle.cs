using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using DogwoodSharp;

namespace CedarSharp.E1Prototype;

/// <summary>
/// A validated Dogwood lowering packaged for CedarSharp: the exported Cedar
/// policy bodies paired with the exported Cedar policy IDs, plus the
/// augmented schema in both exported forms.
/// </summary>
/// <remarks>
/// <para>
/// The pairing of body to ID relies on the exported text listing policies in
/// Dogwood source order (the pinned bridge inserts rules in source order and
/// Cedar's policy set preserves insertion order). That is an implementation
/// property, so <see cref="PolicyIdPreservationTests"/> verifies pairings
/// independently; production D3 should replace the split with a per-rule
/// native export (see docs/d3-e1-compatibility.md).
/// </para>
/// <para>
/// This type is prototype tooling. It is not part of the production API and
/// must not graduate without its own review.
/// </para>
/// </remarks>
public sealed class E1Bundle
{
    private E1Bundle(
        DogwoodLoweredPolicySet lowered,
        ImmutableArray<string> policyIds,
        ImmutableArray<string> policyBodies,
        IReadOnlyDictionary<string, string> policiesById,
        CedarPolicySet policySet)
    {
        Lowered = lowered;
        PolicyIds = policyIds;
        PolicyBodies = policyBodies;
        PoliciesById = policiesById;
        PolicySet = policySet;
        SchemaJson = CedarSchema.FromJson(lowered.CedarSchemaJson);
        SchemaText = CedarSchema.FromText(lowered.CedarSchemaText);
    }

    /// <summary>The exact lowering result the bundle was built from.</summary>
    public DogwoodLoweredPolicySet Lowered { get; }

    /// <summary>Exported Cedar policy IDs in Dogwood rule order.</summary>
    public ImmutableArray<string> PolicyIds { get; }

    /// <summary>Split policy bodies in exported-text order (aligned with <see cref="PolicyIds"/>).</summary>
    public ImmutableArray<string> PolicyBodies { get; }

    /// <summary>Exported Cedar policy ID to exact policy body.</summary>
    public IReadOnlyDictionary<string, string> PoliciesById { get; }

    /// <summary>A CedarSharp policy set built with <see cref="CedarPolicySet.FromPolicies"/>, which preserves IDs.</summary>
    public CedarPolicySet PolicySet { get; }

    /// <summary>The augmented schema, from the exported Cedar JSON schema.</summary>
    public CedarSchema SchemaJson { get; }

    /// <summary>The augmented schema, from the exported <c>.cedarschema</c> text.</summary>
    public CedarSchema SchemaText { get; }

    /// <summary>The Dogwood rule index a Cedar policy ID maps back to, or null when unknown.</summary>
    public int? RuleIndexFor(string cedarPolicyId)
    {
        for (var index = 0; index < PolicyIds.Length; index++)
        {
            if (PolicyIds[index] == cedarPolicyId)
            {
                return Lowered.Rules[index].RuleIndex;
            }
        }

        return null;
    }

    /// <summary>
    /// Lowers <paramref name="source"/> and packages the complete export.
    /// </summary>
    /// <exception cref="InvalidOperationException">The lowering did not produce a complete validated export.</exception>
    public static E1Bundle FromDogwood(string source, DogwoodSchema schema)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);
        var lowered = Dogwood.Lower(source, schema);
        if (!lowered.ArtifactsExported)
        {
            var stage = lowered.StageError;
            throw new InvalidOperationException(
                $"The Dogwood lowering did not export artifacts. Stage: {stage?.Stage}, category: {stage?.Category}, message: {stage?.Message}, validation errors: {lowered.Validation.ValidationErrors.Length}.");
        }

        var bodies = CedarPolicyTextSplitter.Split(lowered.CedarPolicyText);
        var rules = lowered.Rules;
        if (bodies.Count != rules.Length)
        {
            throw new InvalidOperationException(
                $"The export split into {bodies.Count} bodies for {rules.Length} rules.");
        }

        var ids = rules.Select(rule => rule.CedarPolicyId).ToImmutableArray();
        var byId = new Dictionary<string, string>(ids.Length, StringComparer.Ordinal);
        for (var index = 0; index < ids.Length; index++)
        {
            if (!byId.TryAdd(ids[index], bodies[index]))
            {
                throw new InvalidOperationException($"Duplicate exported Cedar policy ID '{ids[index]}'.");
            }
        }

        var policySet = CedarPolicySet.FromPolicies(byId);
        var exported = policySet.PolicyIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var expected = ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (!exported.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The CedarSharp policy set did not retain the exported policy IDs.");
        }

        return new E1Bundle(lowered, ids, [.. bodies], byId, policySet);
    }
}

/// <summary>Builds the JSON envelopes the pinned Cedar 4.11 reference consumes.</summary>
/// <remarks>
/// The envelope mirrors the Cedar FFI authorization call shape that the
/// CedarSharp native bridge forwards, built from the same fixture data the
/// CedarSharp request uses, so both engines evaluate identical inputs.
/// </remarks>
public static class E1ReferenceInput
{
    public static string Authorization(
        E1Bundle bundle,
        CedarEntityUid principal,
        CedarEntityUid action,
        CedarEntityUid resource,
        string contextJson,
        string entitiesJson,
        bool validateRequest,
        bool passSchema = true)
    {
        var builder = new StringBuilder();
        builder.Append("{\"principal\":").Append(Uid(principal));
        builder.Append(",\"action\":").Append(Uid(action));
        builder.Append(",\"resource\":").Append(Uid(resource));
        builder.Append(",\"policies\":{\"staticPolicies\":{");
        var first = true;
        foreach (var (id, text) in bundle.PoliciesById)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append(JsonString(id)).Append(':').Append(JsonString(text));
        }

        builder.Append("}}");
        builder.Append(",\"context\":").Append(contextJson);
        builder.Append(",\"entities\":").Append(entitiesJson);
        builder.Append(",\"schema\":").Append(passSchema ? bundle.Lowered.CedarSchemaJson : "null");
        builder.Append(",\"validateRequest\":").Append(validateRequest ? "true" : "false");
        builder.Append('}');
        return builder.ToString();
    }

    public static string Validate(E1Bundle bundle) =>
        "{\"policies\":{\"staticPolicies\":{" +
        string.Join(",", bundle.PoliciesById.Select(pair =>
            $"{JsonString(pair.Key)}:{JsonString(pair.Value)}")) +
        "}},\"schema\":" + bundle.Lowered.CedarSchemaJson + "}";

    private static string Uid(CedarEntityUid uid) =>
        $"{{\"type\":{JsonString(uid.Type)},\"id\":{JsonString(uid.Id)}}}";

    internal static string JsonString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var chunk in JsonSerializerChunks(value))
        {
            builder.Append(chunk);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static IEnumerable<string> JsonSerializerChunks(string value)
    {
        // System.Text.Json's escaping keeps ASCII plus a safe subset; it is
        // valid JSON escaping, which is all the reference requires.
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStringValue(value);
        writer.Flush();
        var utf8 = buffer.WrittenSpan;
        var text = Encoding.UTF8.GetString(utf8);
        yield return text[1..^1];
    }
}
