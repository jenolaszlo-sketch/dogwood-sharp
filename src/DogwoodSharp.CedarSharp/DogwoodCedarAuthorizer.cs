using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using CedarSharp;
using DogwoodSharp;

namespace DogwoodSharp.Experimental.CedarSharp;

/// <summary>
/// Experimental, fail-closed schema-carrying authorization adapter over CedarSharp 1.0.0
/// (Cedar 4.13.0). One authorizer owns one immutable, complete D3 export bundle: the generated
/// Cedar policy ids, the exact body paired with each id, the canonical augmented schema, and the
/// trusted-pipeline-owned reserved context-field names. The bundle comes only from the native
/// operation 4 export; the operation 3 lowering result carries no per-rule body and is never
/// substituted for it. This type is experimental, outside the stable package, and subject to change
/// before independent qualification. See <c>docs/d3-integration-contract.md</c>.
/// </summary>
/// <remarks>
/// The D3 export surface of <c>DogwoodSharp</c> is internal, because that package's public type and
/// member set is a frozen qualification baseline. This adapter is the public D3 surface: it performs
/// the operation 4 export itself and never exposes a body-less lowering result.
/// </remarks>
/// <remarks>
/// Concurrency and lifecycle: the immutable bundle may be shared by up to four concurrent worker
/// threads. Each authorization owns its request, trusted-runtime, native-call, and diagnostic state;
/// no request buffer or result is shared between calls. Behavior with more than four concurrent
/// workers is outside this contract. A native call that has already started is never cancellable.
/// Disposal releases managed references only: both native images (the D2 bridge and CedarSharp)
/// stay resident until process exit, and this type never unloads or hot-replaces one. Process
/// isolation is the recovery boundary for an untrusted or failed native host.
/// </remarks>
public sealed class DogwoodCedarAuthorizer : IDisposable
{
    private readonly CedarEngine engine;
    private readonly CedarPolicySet policySet;
    private readonly CedarSchema schema;
    private readonly ImmutableArray<CedarPolicyRule> rules;
    private readonly ImmutableHashSet<string> policyIds;
    private readonly ImmutableDictionary<string, int> ruleIndexById;
    private readonly ImmutableArray<string> reservedContextFields;
    private readonly string schemaJson;
    private readonly string? source;
    private readonly DogwoodSchema? loweringSchema;
    private readonly string? providerDeclarations;

    private DogwoodCedarAuthorizer(
        CedarEngine engine,
        CedarPolicySet policySet,
        CedarSchema schema,
        string schemaJson,
        ImmutableArray<CedarPolicyRule> rules,
        ImmutableArray<string> reservedContextFields,
        string? source,
        DogwoodSchema? loweringSchema,
        string? providerDeclarations)
    {
        this.engine = engine;
        this.policySet = policySet;
        this.schema = schema;
        this.schemaJson = schemaJson;
        this.rules = rules;
        this.policyIds = rules.Select(rule => rule.CedarPolicyId).ToImmutableHashSet(StringComparer.Ordinal);
        var map = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            map[rule.CedarPolicyId] = rule.RuleIndex;
        }

        this.ruleIndexById = map.ToImmutable();
        this.reservedContextFields = reservedContextFields.IsDefault ? [] : reservedContextFields;
        this.source = source;
        this.loweringSchema = loweringSchema;
        this.providerDeclarations = providerDeclarations;
    }

    /// <summary>
    /// Lowers and validates one Dogwood source through the operation 4 export and builds an
    /// authorizer from the resulting per-rule Cedar bundle. This is the only public construction
    /// path: the bundle must come from the operation 4 export, and an incomplete export fails
    /// construction rather than being completed.
    /// </summary>
    /// <exception cref="ArgumentNullException">The source or schema is null.</exception>
    /// <exception cref="DogwoodBridgeException">The native bridge could not load or complete operation 4.</exception>
    /// <exception cref="CedarBundleException">The export is incomplete, mispaired, or inconsistent.</exception>
    public static DogwoodCedarAuthorizer ForLoweredBundle(string source, DogwoodSchema schema) =>
        ForLoweredBundleCore(source, schema, providerDeclarations: null);

    /// <summary>
    /// Lowers and validates one Dogwood source through the operation 4 export with trusted provider
    /// declarations and builds an authorizer from the resulting per-rule Cedar bundle. The declaration
    /// text is service-schema input, never caller request context.
    /// </summary>
    /// <exception cref="ArgumentNullException">The source, schema, or provider declarations are null.</exception>
    /// <exception cref="DogwoodBridgeException">The native bridge could not load or complete operation 4.</exception>
    /// <exception cref="CedarBundleException">The export is incomplete, mispaired, or inconsistent.</exception>
    public static DogwoodCedarAuthorizer ForLoweredBundle(
        string source, DogwoodSchema schema, string providerDeclarations)
    {
        ArgumentNullException.ThrowIfNull(providerDeclarations);
        return ForLoweredBundleCore(source, schema, providerDeclarations);
    }

    private static DogwoodCedarAuthorizer ForLoweredBundleCore(
        string source, DogwoodSchema schema, string? providerDeclarations)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);
        var export = Dogwood.ExportCedar(source, schema, providerDeclarations);
        return ForExport(export, source, schema, providerDeclarations);
    }

    /// <summary>
    /// Builds an authorizer from one complete, validated D3 export bundle. The bundle is the
    /// native operation 4 export; a lowering result without a per-rule body is rejected rather than
    /// completed, and there is no fallback path to the operation 3 lowering reply.
    /// </summary>
    /// <exception cref="ArgumentNullException">The export is null.</exception>
    /// <exception cref="CedarBundleException">The export is incomplete, mispaired, or inconsistent.</exception>
    internal static DogwoodCedarAuthorizer ForLoweredBundle(DogwoodCedarExport export) =>
        ForExport(export, source: null, loweringSchema: null, providerDeclarations: null);

    private static DogwoodCedarAuthorizer ForExport(
        DogwoodCedarExport export,
        string? source,
        DogwoodSchema? loweringSchema,
        string? providerDeclarations)
    {
        ArgumentNullException.ThrowIfNull(export);
        if (!export.ArtifactsExported)
        {
            throw new CedarBundleException(
                CedarBundleFailure.LoweringNotExported,
                "The lowering did not export a complete validated bundle.");
        }

        foreach (var rule in export.Rules)
        {
            if (string.IsNullOrEmpty(rule.CedarPolicyId))
            {
                throw new CedarBundleException(
                    CedarBundleFailure.MissingPolicyId, "A lowered rule has no generated Cedar policy id.");
            }

            if (string.IsNullOrEmpty(rule.CedarPolicyBody))
            {
                throw new CedarBundleException(
                    CedarBundleFailure.MissingPolicyBody,
                    $"Rule {rule.CedarPolicyId} has no exact Cedar policy body in the export.");
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in export.Rules)
        {
            if (!ids.Add(rule.CedarPolicyId))
            {
                throw new CedarBundleException(
                    CedarBundleFailure.DuplicatePolicyId,
                    $"The lowering exported a duplicate generated Cedar policy id: {rule.CedarPolicyId}.");
            }
        }

        if (export.CedarSchemaJson.Length == 0)
        {
            throw new CedarBundleException(
                CedarBundleFailure.MissingSchema, "The lowering exported no canonical augmented schema JSON.");
        }

        CedarPolicySet policySet;
        CedarSchema schema;
        try
        {
            // Build the identity-bearing policy set from exact (id, body) pairs. The source @id
            // annotation is never the stored id, and concatenated text is never reparsed here.
            policySet = CedarPolicySet.FromPolicies(
                export.Rules.Select(rule => (rule.CedarPolicyId, rule.CedarPolicyBody)).ToArray());
            schema = CedarSchema.FromJson(export.CedarSchemaJson);
        }
        catch (Exception exception)
        {
            throw new CedarBundleException(
                CedarBundleFailure.InvalidArtifact,
                "The exported policy set or canonical schema could not be built.", exception);
        }

        var built = new HashSet<string>(policySet.PolicyIds, StringComparer.Ordinal);
        if (!built.SetEquals(ids))
        {
            throw new CedarBundleException(
                CedarBundleFailure.PolicyIdMismatch,
                "The engine policy set ids differ from the exported rule ids.");
        }

        return new DogwoodCedarAuthorizer(
            new CedarEngine(), policySet, schema, export.CedarSchemaJson, export.Rules,
            export.GeneratedContextFields, source, loweringSchema, providerDeclarations);
    }

    /// <summary>The generated Cedar policy ids this authorizer owns, in a stable ordinal order.</summary>
    public IReadOnlyList<string> PolicyIds => policyIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    /// <summary>The canonical augmented schema JSON carried on every request.</summary>
    public string CanonicalSchemaJson => schemaJson;

    /// <summary>Whether this lowering is self-contained and therefore authorizable in this stage.</summary>
    public bool SelfContained => reservedContextFields.IsEmpty;

    /// <summary>
    /// The Dogwood source rule index for a generated Cedar policy id, or -1 when the id is not in
    /// the immutable bundle.
    /// </summary>
    public int RuleIndexFor(string cedarPolicyId) =>
        cedarPolicyId is not null && ruleIndexById.TryGetValue(cedarPolicyId, out var index) ? index : -1;

    /// <summary>Authorizes one request through the schema-carrying CedarSharp path.</summary>
    /// <remarks>
    /// Validation is always on. The caller entities are forwarded unchanged; no bare scope entity,
    /// attribute, parent, or relationship is synthesized. Only an error-free Allow authorizes an
    /// enforcement caller. Any schema/request validation failure, malformed input, native failure,
    /// unknown policy identity, or unavailable trusted binding is fail-closed and never authorizes.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The request is null.</exception>
    public CedarAuthorizationResult Authorize(CedarAuthorizationInput request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var contextDoc = JsonDocument.Parse(request.ContextJson);
            using var entitiesDoc = JsonDocument.Parse(request.EntitiesJson);

            var validation = ValidateRequestShape(contextDoc.RootElement, entitiesDoc.RootElement);
            if (validation is not null)
            {
                return validation;
            }

            if (!SelfContained)
            {
                // A non-self-contained lowering requires the separate trusted-runtime path. It is
                // never completed from caller context and never falls back to a missing Boolean.
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    "The lowering owns a trusted temporal/provider field; use the trusted-runtime authorization path.");
            }

            return Evaluate(request, request.ContextJson, request.EntitiesJson);
        }
        catch (JsonException exception)
        {
            return Fail(CedarAuthorizationStage.RequestConstruction, CedarDiagnosticCategory.OperationError,
                "The request JSON is malformed: " + exception.Message);
        }
        catch (Exception exception)
        {
            // Fail-closed boundary: a native/transport or response-decoding failure must never become
            // an unqualified successful authorization.
            return Fail(CedarAuthorizationStage.NativeTransport, CedarDiagnosticCategory.OperationError,
                "The native authorization call failed closed: " + exception.Message);
        }
    }

    /// <summary>
    /// Authorizes one request with an immutable trusted temporal/provider snapshot. The ordinary
    /// request record remains unable to carry logged history, temporal Booleans, or provider values.
    /// The pinned upstream authorizer computes the reserved bindings, and its complete enriched
    /// context is evaluated by CedarSharp.
    /// </summary>
    /// <remarks>
    /// Every trusted-runtime failure returns no decision and fails closed. Caller context containing
    /// a reserved generated field is rejected before the native binding call.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The request or trusted runtime is null.</exception>
    public CedarAuthorizationResult AuthorizeWithTrustedRuntime(
        CedarAuthorizationInput request, DogwoodTrustedRuntime trustedRuntime)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(trustedRuntime);

        if (SelfContained)
        {
            return Authorize(request);
        }

        if (source is null || loweringSchema is null)
        {
            return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                "This authorizer was built from an export without the source needed for trusted-runtime evaluation.");
        }

        try
        {
            using var contextDoc = JsonDocument.Parse(request.ContextJson);
            using var entitiesDoc = JsonDocument.Parse(request.EntitiesJson);

            var validation = ValidateRequestShape(contextDoc.RootElement, entitiesDoc.RootElement);
            if (validation is not null)
            {
                return validation;
            }

            var trustedValidation = ValidateTrustedRuntime(trustedRuntime);
            if (trustedValidation is not null)
            {
                return trustedValidation;
            }

            string runtimeJson = BuildRuntimeBindingJson(request, trustedRuntime);
            var runtimeExport = Dogwood.ExportCedarWithRuntime(
                source, loweringSchema, providerDeclarations, runtimeJson);

            if (!runtimeExport.ArtifactsExported)
            {
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    "The trusted-runtime operation did not export a complete validated bundle.");
            }

            var binding = runtimeExport.RuntimeBinding;
            if (binding is null || !binding.Bound)
            {
                var message = binding?.Error ?? "The trusted temporal/provider binding did not complete.";
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    "The trusted temporal/provider binding failed closed: " + message);
            }

            if (!SameBundle(runtimeExport))
            {
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    "The trusted-runtime export differs from the immutable authorizer bundle.");
            }

            var contextValidation = ValidateTrustedContext(binding.ContextJson);
            if (contextValidation is not null)
            {
                return contextValidation;
            }

            return Evaluate(request, binding.ContextJson, request.EntitiesJson);
        }
        catch (JsonException exception)
        {
            return Fail(CedarAuthorizationStage.RequestConstruction, CedarDiagnosticCategory.OperationError,
                "The trusted-runtime JSON is malformed: " + exception.Message);
        }
        catch (Exception exception)
        {
            return Fail(CedarAuthorizationStage.NativeTransport, CedarDiagnosticCategory.OperationError,
                "The trusted-runtime native call failed closed: " + exception.Message);
        }
    }

    private CedarAuthorizationResult? ValidateRequestShape(JsonElement context, JsonElement entities)
    {
        if (context.ValueKind != JsonValueKind.Object)
        {
            return Fail(CedarAuthorizationStage.RequestConstruction, CedarDiagnosticCategory.OperationError,
                "The request context must be a Cedar JSON record (object).");
        }

        if (entities.ValueKind != JsonValueKind.Array)
        {
            return Fail(CedarAuthorizationStage.RequestConstruction, CedarDiagnosticCategory.OperationError,
                "The request entities must be a Cedar JSON array.");
        }

        foreach (var reserved in reservedContextFields)
        {
            if (context.TryGetProperty(reserved, out _))
            {
                return Fail(CedarAuthorizationStage.SchemaOrRequestValidation, CedarDiagnosticCategory.ReservedContextField,
                    $"The request context carries the reserved generated field '{reserved}', which the trusted pipeline owns.");
            }
        }

        return null;
    }

    private CedarAuthorizationResult Evaluate(
        CedarAuthorizationInput request, string contextJson, string entitiesJson)
    {
        var cedarRequest = new CedarAuthorizationRequest(
            new CedarEntityUid(request.Principal.Type, request.Principal.Id),
            new CedarEntityUid(request.Action.Type, request.Action.Id),
            new CedarEntityUid(request.Resource.Type, request.Resource.Id),
            policySet,
            contextJson,
            entitiesJson,
            schema,
            true);

        var raw = engine.Authorize(cedarRequest);
        var policyErrors = raw.PolicyErrors
            .Select(error => new CedarPolicyError(error.PolicyId, Classify(error.Error.Message), error.Error.Message))
            .ToArray();
        var policyErrorIds = new HashSet<string>(policyErrors.Select(e => e.PolicyId), StringComparer.Ordinal);

        if (!raw.IsSuccess)
        {
            var errors = raw.Errors.Select(diagnostic => new CedarDiagnostic(Classify(diagnostic.Message), diagnostic.Message)).ToArray();
            var stage = ClassifyStage(errors);
            return new CedarAuthorizationResult(true, stage, null, false, false,
                Array.Empty<string>(), policyErrors, errors);
        }

        var determiningIds = (raw.DeterminingPolicies ?? Enumerable.Empty<string>())
            .Where(id => id is not null)
            .Select(id => id!)
            .ToArray();
        if (!policyErrorIds.IsSubsetOf(policyIds) || !determiningIds.ToHashSet(StringComparer.Ordinal).IsSubsetOf(policyIds))
        {
            return new CedarAuthorizationResult(true, CedarAuthorizationStage.UnknownPolicyIdentity, null, false, false,
                Array.Empty<string>(), policyErrors, new[]
                {
                    new CedarDiagnostic(CedarDiagnosticCategory.UnknownPolicyId,
                        "Evaluation named a policy identity not in the immutable bundle.")
                });
        }

        var decisionName = raw.Decision?.ToString();
        var decision = string.Equals(decisionName, "Allow", StringComparison.OrdinalIgnoreCase)
            ? CedarDecision.Allow
            : CedarDecision.Deny;
        var isCleanAllow = raw.IsCleanAllow;
        return new CedarAuthorizationResult(false, CedarAuthorizationStage.Evaluation, decision, isCleanAllow, isCleanAllow,
            determiningIds, policyErrors, Array.Empty<CedarDiagnostic>());
    }

    private CedarAuthorizationResult? ValidateTrustedRuntime(DogwoodTrustedRuntime runtime)
    {
        if (runtime.History.IsDefault || runtime.Providers.IsDefault)
        {
            return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                "The trusted-runtime snapshot has no immutable history or provider collection.");
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        long previousTimestamp = long.MinValue;
        foreach (var item in runtime.History)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Identity) || !identities.Add(item.Identity) ||
                string.IsNullOrWhiteSpace(item.ActionType) || string.IsNullOrWhiteSpace(item.ActionId) ||
                string.IsNullOrWhiteSpace(item.Kind))
            {
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    "The trusted history contains an invalid or duplicate event identity or action identity.");
            }

            if (previousTimestamp != long.MinValue && item.Timestamp <= previousTimestamp)
            {
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    "The trusted history timestamps are not strictly increasing.");
            }

            if (item.Timestamp >= runtime.Timestamp)
            {
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    "A trusted history event timestamp is not before the current decision timestamp.");
            }

            if (!IsObjectJson(NormalizeObjectJson(item.ContextJson)) ||
                !IsObjectJson(NormalizeObjectJson(item.LoggedJson)) ||
                !IsArrayJson(NormalizeArrayJson(item.EntitiesJson)) ||
                !IsValidEntityRef(item.Principal) || !IsValidEntityRef(item.Resource))
            {
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    "The trusted history contains malformed context, logged history, entities, or scope identity.");
            }

            previousTimestamp = item.Timestamp;
        }

        if (!IsObjectJson(NormalizeObjectJson(runtime.CurrentLoggedJson)))
        {
            return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                "The current trusted logged record must be a JSON object.");
        }

        var providerNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in runtime.Providers)
        {
            if (provider is null || string.IsNullOrWhiteSpace(provider.Name) || !providerNames.Add(provider.Name))
            {
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    "The trusted provider outcomes contain an invalid or duplicate provider name.");
            }

            switch (provider.Outcome)
            {
                case DogwoodTrustedProviderOutcomeKind.Value when !IsValidJson(provider.ValueJson):
                    return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                        $"The trusted provider '{provider.Name}' has a malformed value.");
                case DogwoodTrustedProviderOutcomeKind.Failure when string.IsNullOrWhiteSpace(provider.Message):
                    return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                        $"The trusted provider '{provider.Name}' has no failure message.");
                case DogwoodTrustedProviderOutcomeKind.Value:
                case DogwoodTrustedProviderOutcomeKind.Omitted:
                case DogwoodTrustedProviderOutcomeKind.Failure:
                    break;
                default:
                    return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                        $"The trusted provider '{provider.Name}' has an unknown outcome kind.");
            }
        }

        return null;
    }

    private CedarAuthorizationResult? ValidateTrustedContext(string contextJson)
    {
        using var document = JsonDocument.Parse(contextJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                "The trusted runtime did not produce a Cedar context object.");
        }

        foreach (var reserved in reservedContextFields)
        {
            if (!document.RootElement.TryGetProperty(reserved, out var value))
            {
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    $"The trusted runtime did not bind the reserved generated field '{reserved}'.");
            }

            if (reserved == "providers")
            {
                if (value.ValueKind != JsonValueKind.Object)
                {
                    return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                        "The trusted provider record is not a Cedar context object.");
                }
            }
            else if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    $"The trusted temporal field '{reserved}' is not a Boolean.");
            }
        }

        return null;
    }

    private bool SameBundle(DogwoodCedarExport candidate)
    {
        if (!string.Equals(candidate.CedarSchemaJson, schemaJson, StringComparison.Ordinal) ||
            candidate.Rules.Length != rules.Length ||
            !candidate.GeneratedContextFields.SequenceEqual(reservedContextFields, StringComparer.Ordinal))
        {
            return false;
        }

        for (int index = 0; index < rules.Length; index++)
        {
            if (candidate.Rules[index].RuleIndex != rules[index].RuleIndex ||
                !string.Equals(candidate.Rules[index].CedarPolicyId, rules[index].CedarPolicyId, StringComparison.Ordinal) ||
                !string.Equals(candidate.Rules[index].CedarPolicyBody, rules[index].CedarPolicyBody, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildRuntimeBindingJson(
        CedarAuthorizationInput request, DogwoodTrustedRuntime runtime)
    {
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("timestamp", runtime.Timestamp);
            writer.WriteStartArray("history");
            foreach (var item in runtime.History)
            {
                writer.WriteStartObject();
                writer.WriteString("identity", item.Identity);
                writer.WriteNumber("timestamp", item.Timestamp);
                writer.WriteString("actionType", item.ActionType);
                writer.WriteString("actionId", item.ActionId);
                writer.WriteString("kind", item.Kind);
                WriteEntityRef(writer, "principal", item.Principal);
                WriteEntityRef(writer, "resource", item.Resource);
                writer.WriteString("contextJson", NormalizeObjectJson(item.ContextJson));
                writer.WriteString("loggedJson", NormalizeObjectJson(item.LoggedJson));
                writer.WriteString("entitiesJson", NormalizeArrayJson(item.EntitiesJson));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("providers");
            foreach (var provider in runtime.Providers)
            {
                writer.WriteStartObject();
                writer.WriteString("name", provider.Name);
                writer.WriteString("outcome", provider.Outcome switch
                {
                    DogwoodTrustedProviderOutcomeKind.Value => "value",
                    DogwoodTrustedProviderOutcomeKind.Omitted => "omitted",
                    DogwoodTrustedProviderOutcomeKind.Failure => "failure",
                    _ => throw new InvalidOperationException("Unknown trusted provider outcome.")
                });
                if (provider.ValueJson is null)
                {
                    writer.WriteNull("valueJson");
                }
                else
                {
                    writer.WriteString("valueJson", provider.ValueJson);
                }

                if (provider.Message is null)
                {
                    writer.WriteNull("message");
                }
                else
                {
                    writer.WriteString("message", provider.Message);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartObject("current");
            writer.WriteString("actionType", request.Action.Type);
            writer.WriteString("actionId", request.Action.Id);
            writer.WriteString("kind", "request");
            WriteEntityRef(writer, "principal", request.Principal);
            WriteEntityRef(writer, "resource", request.Resource);
            writer.WriteString("contextJson", request.ContextJson);
            writer.WriteString("entitiesJson", request.EntitiesJson);
            // Preserve the empty sentinel: the native runtime uses it to mirror only the
            // current request's `input` group into the durable logged record, matching the
            // upstream request-event construction.
            writer.WriteString("loggedJson", runtime.CurrentLoggedJson);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    private static void WriteEntityRef(Utf8JsonWriter writer, string name, CedarEntityRef? entity)
    {
        if (entity is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("type", entity.Value.Type);
        writer.WriteString("id", entity.Value.Id);
        writer.WriteEndObject();
    }

    private static bool IsValidEntityRef(CedarEntityRef? entity) =>
        entity is null || (!string.IsNullOrWhiteSpace(entity.Value.Type) && !string.IsNullOrWhiteSpace(entity.Value.Id));

    private static bool IsObjectJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsArrayJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidJson(string? json)
    {
        if (json is null)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeObjectJson(string json) =>
        string.IsNullOrWhiteSpace(json) ? "{}" : json;

    private static string NormalizeArrayJson(string json) =>
        string.IsNullOrWhiteSpace(json) ? "[]" : json;

    private static CedarAuthorizationStage ClassifyStage(IReadOnlyList<CedarDiagnostic> errors)
    {
        foreach (var error in errors)
        {
            if (error.Category is CedarDiagnosticCategory.EntityConformance
                or CedarDiagnosticCategory.MissingAttribute
                or CedarDiagnosticCategory.ScopeMismatch
                or CedarDiagnosticCategory.UndeclaredAction
                or CedarDiagnosticCategory.MissingEntity)
            {
                return CedarAuthorizationStage.SchemaOrRequestValidation;
            }
        }

        return CedarAuthorizationStage.NativeTransport;
    }

    private static CedarDiagnosticCategory Classify(string message) => message switch
    {
        _ when message.Contains("does not conform", StringComparison.Ordinal) => CedarDiagnosticCategory.EntityConformance,
        _ when message.Contains("while parsing context", StringComparison.Ordinal)
            || message.Contains("record to have an attribute", StringComparison.Ordinal) => CedarDiagnosticCategory.MissingAttribute,
        _ when message.Contains("is not valid for", StringComparison.Ordinal) => CedarDiagnosticCategory.ScopeMismatch,
        _ when message.Contains("does not exist in the supplied schema", StringComparison.Ordinal) => CedarDiagnosticCategory.UndeclaredAction,
        _ when message.Contains("does not exist", StringComparison.Ordinal) => CedarDiagnosticCategory.MissingEntity,
        _ => CedarDiagnosticCategory.OperationError
    };

    private static CedarAuthorizationResult Fail(
        CedarAuthorizationStage stage, CedarDiagnosticCategory category, string message) =>
        new(true, stage, null, false, false, Array.Empty<string>(),
            Array.Empty<CedarPolicyError>(), new[] { new CedarDiagnostic(category, message) });

    /// <summary>Releases managed references only. It never unloads a native image.</summary>
    public void Dispose()
    {
        // Both native images remain resident for the process lifetime. The immutable snapshots this
        // authorizer holds stay valid after disposal; there is no native handle to release here.
    }
}
