using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

    private DogwoodCedarAuthorizer(
        CedarEngine engine,
        CedarPolicySet policySet,
        CedarSchema schema,
        string schemaJson,
        ImmutableArray<CedarPolicyRule> rules,
        ImmutableArray<string> reservedContextFields)
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
    public static DogwoodCedarAuthorizer ForLoweredBundle(string source, DogwoodSchema schema)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);
        return ForExport(Dogwood.ExportCedar(source, schema));
    }

    /// <summary>
    /// Builds an authorizer from one complete, validated D3 export bundle. The bundle is the
    /// native operation 4 export; a lowering result without a per-rule body is rejected rather than
    /// completed, and there is no fallback path to the operation 3 lowering reply.
    /// </summary>
    /// <exception cref="ArgumentNullException">The export is null.</exception>
    /// <exception cref="CedarBundleException">The export is incomplete, mispaired, or inconsistent.</exception>
    internal static DogwoodCedarAuthorizer ForLoweredBundle(DogwoodCedarExport export) =>
        ForExport(export);

    private static DogwoodCedarAuthorizer ForExport(DogwoodCedarExport export)
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
            new CedarEngine(), policySet, schema, export.CedarSchemaJson, export.Rules, export.GeneratedContextFields);
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

            if (contextDoc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Fail(CedarAuthorizationStage.RequestConstruction, CedarDiagnosticCategory.OperationError,
                    "The request context must be a Cedar JSON record (object).");
            }

            if (entitiesDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Fail(CedarAuthorizationStage.RequestConstruction, CedarDiagnosticCategory.OperationError,
                    "The request entities must be a Cedar JSON array.");
            }

            foreach (var reserved in reservedContextFields)
            {
                if (contextDoc.RootElement.TryGetProperty(reserved, out _))
                {
                    return Fail(CedarAuthorizationStage.SchemaOrRequestValidation, CedarDiagnosticCategory.ReservedContextField,
                        $"The request context carries the reserved generated field '{reserved}', which the trusted pipeline owns.");
                }
            }

            if (!SelfContained)
            {
                // The trusted temporal/provider binding is a separate, not-yet-authorized commission
                // (D3-5). Until it exists, a non-self-contained lowering is fail-closed here rather
                // than authorized on a caller-substituted value.
                return Fail(CedarAuthorizationStage.TrustedBindingUnavailable, CedarDiagnosticCategory.TrustedBindingUnavailable,
                    "The lowering owns a trusted temporal/provider field; its trusted binding is not available through this public request path.");
            }

            var cedarRequest = new CedarAuthorizationRequest(
                new CedarEntityUid(request.Principal.Type, request.Principal.Id),
                new CedarEntityUid(request.Action.Type, request.Action.Id),
                new CedarEntityUid(request.Resource.Type, request.Resource.Id),
                policySet,
                request.ContextJson,
                request.EntitiesJson,
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
