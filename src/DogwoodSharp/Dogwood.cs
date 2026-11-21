using System.Collections.Immutable;
using System.Text;

namespace DogwoodSharp;

/// <summary>Discovers the native bridge, parses Dogwood policy source, and lowers and validates it.</summary>
public static class Dogwood
{
    /// <summary>The maximum policy source length in UTF-8 bytes.</summary>
    public const int MaximumSourceBytes = 1024 * 1024;

    internal static UTF8Encoding StrictUtf8 { get; } = new(false, true);

    /// <summary>Returns the verified identity of the loaded native bridge.</summary>
    public static DogwoodNativeVersion GetNativeVersion() => NativeBridge.Shared.Version;

    /// <summary>Parses source with the pinned Dogwood defaults and returns a snapshot of the findings.</summary>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="ArgumentException">The source contains an unpaired surrogate or exceeds the UTF-8 limit.</exception>
    /// <exception cref="DogwoodBridgeException">The native bridge could not load or complete the operation.</exception>
    public static PolicyParseResult Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int length = CheckSource(source);
        return NativeBridge.Shared.Parse(source, length);
    }

    /// <summary>Parses and lowers source, checks generated context fields, and validates the lowered set.</summary>
    /// <exception cref="ArgumentNullException">The source or schema is null.</exception>
    /// <exception cref="ArgumentException">The source, schema text, or distincter violates its format or size limit.</exception>
    /// <exception cref="DogwoodBridgeException">The native bridge could not load or complete the operation.</exception>
    public static DogwoodValidationResult Validate(string source, DogwoodSchema schema)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);
        int length = CheckSource(source);
        CheckSchema(schema);
        return NativeBridge.Shared.Validate(schema, source, length);
    }

    /// <summary>Lowers and validates source and exports the exact validated Cedar policy and schema.</summary>
    /// <remarks>Use <see cref="DogwoodLoweredPolicySet.ArtifactsExported"/> to check for a complete export.</remarks>
    /// <exception cref="ArgumentNullException">The source or schema is null.</exception>
    /// <exception cref="ArgumentException">The source, schema text, or distincter violates its format or size limit.</exception>
    /// <exception cref="DogwoodBridgeException">The native bridge could not load or complete the operation.</exception>
    public static DogwoodLoweredPolicySet Lower(string source, DogwoodSchema schema)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);
        int length = CheckSource(source);
        CheckSchema(schema);
        return NativeBridge.Shared.Lower(schema, source, length);
    }

    private static int CheckSource(string source)
    {
        int length;
        try
        {
            length = StrictUtf8.GetByteCount(source);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Policy source must be valid Unicode.", nameof(source), exception);
        }

        if (length > MaximumSourceBytes)
        {
            throw new ArgumentException("Policy source exceeds the UTF-8 byte limit.", nameof(source));
        }

        return length;
    }

    private static void CheckSchema(DogwoodSchema schema)
    {
        CheckText(schema.PolicySchema, nameof(schema), "The Cedar action schema is required.");
        if (schema.EventSchema is not null)
        {
            CheckText(schema.EventSchema, nameof(schema), "The event schema must be valid text.");
        }

        if (schema.Macros is not null)
        {
            CheckText(schema.Macros, nameof(schema), "The macro library must be valid text.");
        }

        if (schema.Distincter is not null)
        {
            CheckText(schema.Distincter, nameof(schema), "The distincter must be valid text.");
            if (!CedarIdentifier.IsMatch(schema.Distincter))
            {
                throw new ArgumentException(
                    "The distincter must be a Cedar identifier ([_A-Za-z][_A-Za-z0-9]*): it is interpolated into policy ids and context attribute names.",
                    nameof(schema));
            }

            if (schema.Distincter.Length > 256)
            {
                throw new ArgumentException("The distincter exceeds 256 UTF-8 bytes.", nameof(schema));
            }
        }
    }

    private static void CheckText(string text, string parameterName, string message)
    {
        int length;
        try
        {
            length = StrictUtf8.GetByteCount(text);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(message, parameterName, exception);
        }

        if (length > DogwoodSchema.MaximumSchemaBytes)
        {
            throw new ArgumentException($"{message} It exceeds {DogwoodSchema.MaximumSchemaBytes} UTF-8 bytes.", parameterName);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex CedarIdentifier = new(@"\A[_A-Za-z][_A-Za-z0-9]*\z");
}

/// <summary>The schema inputs for a lowering or validation operation.</summary>
/// <param name="PolicySchema">The Cedar <c>.cedarschema</c> action schema text.</param>
/// <param name="EventSchema">Optional event-schema DSL source. Omit for the pinned default.</param>
/// <param name="Macros">Optional reusable <c>def</c> macro library. Omit for the pinned default.</param>
/// <param name="Distincter">
/// Optional namespace for synthesized policy ids and generated <c>context</c> fields.
/// Both validation and export use this namespace and check the original schema for collisions.
/// </param>
public sealed record DogwoodSchema(
    string PolicySchema,
    string? EventSchema = null,
    string? Macros = null,
    string? Distincter = null)
{
    /// <summary>The maximum size in UTF-8 bytes of any single schema text field.</summary>
    public const int MaximumSchemaBytes = 1024 * 1024;
}

/// <summary>The verified identity of the loaded native bridge.</summary>
/// <param name="AbiVersion">The native ABI version.</param>
/// <param name="BridgeVersion">The bridge version.</param>
/// <param name="DogwoodRevision">The exact upstream Dogwood Git revision.</param>
/// <param name="DogwoodVersion">The upstream Dogwood language version.</param>
/// <param name="CedarVersion">The resolved Cedar policy crate version.</param>
/// <param name="RustVersion">The Rust compiler version.</param>
/// <param name="Target">The Rust target triple.</param>
public sealed record DogwoodNativeVersion(
    uint AbiVersion, string BridgeVersion, string DogwoodRevision, string DogwoodVersion,
    string CedarVersion, string RustVersion, string Target);

/// <summary>The result of parsing a policy source.</summary>
public sealed class PolicyParseResult
{
    internal PolicyParseResult(int? policyCount, ImmutableArray<PolicyDiagnostic> diagnostics)
    {
        PolicyCount = policyCount;
        Diagnostics = diagnostics;
    }

    /// <summary>Whether upstream parsing succeeded.</summary>
    public bool Success => PolicyCount.HasValue;

    /// <summary>The number of parsed policies, or null on failure.</summary>
    public int? PolicyCount { get; }

    /// <summary>The diagnostics in upstream order. Empty on success.</summary>
    public ImmutableArray<PolicyDiagnostic> Diagnostics { get; }
}

/// <summary>A finding from the upstream parser.</summary>
/// <param name="Category">A stable wrapper category.</param>
/// <param name="Message">The human-readable upstream message.</param>
/// <param name="Location">The UTF-8 source location, when available.</param>
public sealed record PolicyDiagnostic(PolicyDiagnosticCategory Category, string Message, PolicySourceLocation? Location);

/// <summary>Stable diagnostic categories for the parsing operation.</summary>
public enum PolicyDiagnosticCategory
{
    /// <summary>Syntax or a structural check performed during parsing.</summary>
    Syntax,

    /// <summary>Macro expansion failure.</summary>
    Macro
}

/// <summary>A zero-based byte range in the UTF-8 policy source.</summary>
/// <param name="Offset">The offset in bytes.</param>
/// <param name="Length">The length in bytes, possibly zero.</param>
public readonly record struct PolicySourceLocation(int Offset, int Length);

/// <summary>The pipeline stages a D2 operation runs. A stage that did not run left the outcome's stage flag false.</summary>
public enum DogwoodStage
{
    /// <summary>Building the service schema, before any policy source is parsed.</summary>
    ServiceSchema,

    /// <summary>Lowering with the supplied action schema and distincter.</summary>
    Lower,

    /// <summary>Constructing Dogwood's action-schema input.</summary>
    ActionSchema,

    /// <summary>Checking generated fields against the original action contexts.</summary>
    CollisionCheck,

    /// <summary>Producing the complete validated Cedar export.</summary>
    ArtifactExport
}

/// <summary>The reason a D2 pipeline stage stopped early.</summary>
public enum DogwoodFailureCategory
{
    /// <summary>The event-schema DSL could not be used.</summary>
    EventSchema,

    /// <summary>The policy could not be lowered, for an unsupported construct or an internal limit.</summary>
    Policy,

    /// <summary>The Cedar action schema was missing, malformed, or too deeply nested.</summary>
    Schema,

    /// <summary>The lowering distincter was rejected.</summary>
    Distincter,

    /// <summary>An unexpected upstream error was mapped defensively.</summary>
    Internal,

    /// <summary>A generated temporal field was already present in a target action's context.</summary>
    GeneratedContextFieldCollision
}

/// <summary>A fatal stage failure reported instead of partial output.</summary>
public sealed record DogwoodStageError(
    DogwoodStage Stage, DogwoodFailureCategory Category, string Message, PolicySourceLocation? Location);

/// <summary>A generated temporal field and the original action context that declares it.</summary>
/// <param name="Field">The generated Cedar attribute name.</param>
/// <param name="ActionNamespace">The Cedar namespace, or null for a top-level action.</param>
/// <param name="ActionId">The action's literal Cedar entity id.</param>
public sealed record DogwoodContextFieldCollision(string Field, string? ActionNamespace, string ActionId);

/// <summary>Stable validation-finding categories. Extension dialects can add categories that map to <see cref="Unspecified"/>.</summary>
public enum PolicyValidationCategory
{
    /// <summary>A Cedar schema-aware finding.</summary>
    Cedar,

    /// <summary>A Dogwood temporal dialect finding.</summary>
    Temporal,

    /// <summary>A Dogwood provider dialect finding.</summary>
    Provider,

    /// <summary>An extension finding whose dialect the wrapper does not name.</summary>
    Unspecified
}

/// <summary>One structured validation finding.</summary>
/// <param name="Category">The stable wrapper category.</param>
/// <param name="Message">The upstream message, which may change with upstream revisions.</param>
/// <param name="Location">The UTF-8 source location, when the finding carries one.</param>
public sealed record PolicyValidationFinding(
    PolicyValidationCategory Category, string Message, PolicySourceLocation? Location);

/// <summary>One lowered rule mapped back to its Dogwood source.</summary>
/// <param name="RuleIndex">The zero-based index of the Dogwood rule in source order.</param>
/// <param name="CedarPolicyId">The Cedar policy id the rule lowered to.</param>
/// <param name="SourceText">The macro-expanded Dogwood source of the rule, when the operation reported it.</param>
/// <param name="CedarPolicyBody">
/// The exact Cedar policy body stored under <paramref name="CedarPolicyId"/>, rendered from the
/// compiled Cedar policy, when the operation exported artifacts. This is the identity-bearing
/// body for building a per-rule policy set; it is never recovered by splitting concatenated text.
/// </param>
public sealed record PolicyRuleMapping(
    int RuleIndex, string CedarPolicyId, string? SourceText = null, string? CedarPolicyBody = null)
{
    // Equality is over the (index, id, source) identity triple, matching the D1/D2 contract. The
    // exact Cedar body is a transport value for the D3 per-rule export, not part of rule identity,
    // so existing records-compared consumers stay stable as the export is extended.
    /// <summary>Compares the (index, id, source) identity triple; the body is a transport value.</summary>
    public bool Equals(PolicyRuleMapping? other) =>
        other is not null &&
        RuleIndex == other.RuleIndex &&
        string.Equals(CedarPolicyId, other.CedarPolicyId, StringComparison.Ordinal) &&
        string.Equals(SourceText, other.SourceText, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(RuleIndex, CedarPolicyId, SourceText);
}

/// <summary>The result of checking and validating one lowered policy set.</summary>
public sealed class DogwoodValidationResult
{
    internal DogwoodValidationResult(
        bool serviceSchemaBuilt,
        bool parsed,
        int? policyCount,
        ImmutableArray<PolicyDiagnostic> parseDiagnostics,
        bool actionSchemaBuilt,
        bool lowered,
        int ruleCount,
        bool selfContainedCedar,
        bool collisionCheckPassed,
        DogwoodContextFieldCollision? collision,
        bool validationPassed,
        ImmutableArray<PolicyValidationFinding> validationErrors,
        ImmutableArray<PolicyValidationFinding> validationWarnings,
        DogwoodStageError? stageError,
        ImmutableArray<PolicyRuleMapping> rules)
    {
        ServiceSchemaBuilt = serviceSchemaBuilt;
        Parsed = parsed;
        PolicyCount = policyCount;
        ParseDiagnostics = parseDiagnostics;
        ActionSchemaBuilt = actionSchemaBuilt;
        Lowered = lowered;
        RuleCount = ruleCount;
        SelfContainedCedar = selfContainedCedar;
        CollisionCheckPassed = collisionCheckPassed;
        Collision = collision;
        ValidationPassed = validationPassed;
        ValidationErrors = validationErrors;
        ValidationWarnings = validationWarnings;
        StageError = stageError;
        Rules = rules;
    }

    /// <summary>Whether Dogwood constructed the service schema.</summary>
    public bool ServiceSchemaBuilt { get; }

    /// <summary>Whether upstream parsing succeeded.</summary>
    public bool Parsed { get; }

    /// <summary>The number of parsed policies, or null before a successful parse.</summary>
    public int? PolicyCount { get; }

    /// <summary>Diagnostics from a failed parse in upstream order.</summary>
    public ImmutableArray<PolicyDiagnostic> ParseDiagnostics { get; }

    /// <summary>Whether Dogwood constructed its action-schema input. Lowering resolves the schema.</summary>
    public bool ActionSchemaBuilt { get; }

    /// <summary>Whether the intermediate lowering stage succeeded.</summary>
    public bool Lowered { get; }

    /// <summary>The number of lowered rules, or zero before successful lowering.</summary>
    public int RuleCount { get; }

    /// <summary>Whether the lowered policies reproduce Dogwood semantics with no extra context.</summary>
    public bool SelfContainedCedar { get; }

    /// <summary>Whether the original action contexts passed the generated-field collision check.</summary>
    public bool CollisionCheckPassed { get; }

    /// <summary>The first conflicting generated field and target action, when detected.</summary>
    public DogwoodContextFieldCollision? Collision { get; }

    /// <summary>Whether validation ran and found zero errors.</summary>
    public bool ValidationPassed { get; }

    /// <summary>Validation errors in upstream order.</summary>
    public ImmutableArray<PolicyValidationFinding> ValidationErrors { get; }

    /// <summary>Validation warnings in upstream order, including on success.</summary>
    public ImmutableArray<PolicyValidationFinding> ValidationWarnings { get; }

    /// <summary>The fatal stage error, if reported.</summary>
    public DogwoodStageError? StageError { get; }

    /// <summary>Validated rules in source order. An unsuccessful operation leaves this empty.</summary>
    public ImmutableArray<PolicyRuleMapping> Rules { get; }

    /// <summary>Whether the intermediate policy and schema passed the collision check and validation.</summary>
    public bool IsValid => ValidationPassed;
}

/// <summary>The validation outcome and gated Cedar exports for one lowering call.</summary>
public sealed class DogwoodLoweredPolicySet
{
    internal DogwoodLoweredPolicySet(
        DogwoodValidationResult validation, bool exported, string policyText, string schemaJson,
        string schemaText, ImmutableArray<string> generatedContextFields)
    {
        Validation = validation;
        ArtifactsExported = exported;
        CedarPolicyText = policyText;
        CedarSchemaJson = schemaJson;
        CedarSchemaText = schemaText;
        GeneratedContextFields = generatedContextFields.IsDefault ? [] : generatedContextFields;
    }

    /// <summary>The stage outcomes and findings for this exact policy and schema.</summary>
    public DogwoodValidationResult Validation { get; }

    /// <inheritdoc cref="DogwoodValidationResult.Parsed"/>
    public bool Parsed => Validation.Parsed;

    /// <inheritdoc cref="DogwoodValidationResult.PolicyCount"/>
    public int? PolicyCount => Validation.PolicyCount;

    /// <inheritdoc cref="DogwoodValidationResult.ParseDiagnostics"/>
    public ImmutableArray<PolicyDiagnostic> ParseDiagnostics => Validation.ParseDiagnostics;

    /// <inheritdoc cref="DogwoodValidationResult.Lowered"/>
    public bool Lowered => Validation.Lowered;

    /// <inheritdoc cref="DogwoodValidationResult.RuleCount"/>
    public int RuleCount => Validation.RuleCount;

    /// <inheritdoc cref="DogwoodValidationResult.SelfContainedCedar"/>
    public bool SelfContainedCedar => Validation.SelfContainedCedar;

    /// <summary>The validated Cedar policy text. Empty when export failed.</summary>
    public string CedarPolicyText { get; }

    /// <summary>The validated augmented Cedar schema in JSON form. Empty when export failed.</summary>
    public string CedarSchemaJson { get; }

    /// <summary>The validated augmented Cedar schema in Cedar schema syntax. Empty when export failed.</summary>
    public string CedarSchemaText { get; }

    /// <summary>
    /// The trusted-pipeline-owned generated context field names this lowering requires: each hoisted
    /// temporal boolean's top-level field name, plus the reserved <c>providers</c> record when a provider
    /// field is hoisted. Empty exactly when <see cref="SelfContainedCedar"/> is true. An authorization
    /// request that carries any of these names in its context MUST be rejected before dispatch.
    /// </summary>
    public ImmutableArray<string> GeneratedContextFields { get; }

    /// <summary>The exported rule identities and expanded source, in source order.</summary>
    public ImmutableArray<PolicyRuleMapping> Rules => Validation.Rules;

    /// <inheritdoc cref="DogwoodValidationResult.StageError"/>
    public DogwoodStageError? StageError => Validation.StageError;

    /// <summary>Whether the complete validated Cedar bundle was exported.</summary>
    public bool ArtifactsExported { get; }

    /// <summary>Whether the complete validated Cedar bundle is available.</summary>
    public bool IsLowered => ArtifactsExported;
}

/// <summary>Identifies a managed native-bridge failure.</summary>
public enum DogwoodBridgeFailure
{
    /// <summary>The runtime asset was unavailable, invalid, or incompatible.</summary>
    LoadOrIdentity,

    /// <summary>The ABI operation returned a nonzero status.</summary>
    NativeCall,

    /// <summary>The native response violated its wire contract.</summary>
    MalformedResponse
}

/// <summary>A failure at the DogwoodSharp native boundary.</summary>
public sealed class DogwoodBridgeException : Exception
{
    internal DogwoodBridgeException(DogwoodBridgeFailure failure, string message, Exception? inner = null, uint? nativeStatus = null)
        : base(message, inner)
    {
        Failure = failure;
        NativeStatus = nativeStatus;
    }

    /// <summary>The failure category.</summary>
    public DogwoodBridgeFailure Failure { get; }

    /// <summary>The raw ABI status, when the native call returned an error.</summary>
    public uint? NativeStatus { get; }
}
