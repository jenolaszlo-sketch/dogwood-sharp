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

    /// <summary>Parses, lowers and validates source against the supplied schema.</summary>
    /// <remarks>
    /// The result reports parsing, lowering and validation separately. <see cref="DogwoodValidationResult.IsValid"/>
    /// is true only when all three succeeded, so a failed validation is never presented as a validated policy.
    /// Authorization is not implemented and is not implied by a passing result.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The source or schema is null.</exception>
    /// <exception cref="ArgumentException">The source, a schema text, or the distincter is invalid, oversized, or not a Cedar identifier, or validation was given a distincter.</exception>
    /// <exception cref="DogwoodBridgeException">The native bridge could not load or complete the operation.</exception>
    public static DogwoodValidationResult Validate(string source, DogwoodSchema schema)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);
        int length = CheckSource(source);
        CheckSchema(schema);
        if (schema.Distincter is not null)
        {
            throw new ArgumentException(
                "Validation does not name hoisted fields, so a distincter is not accepted here.", nameof(schema));
        }

        return NativeBridge.Shared.Validate(schema, source, length);
    }

    /// <summary>Parses and lowers source against the supplied schema and returns the lowered artifacts.</summary>
    /// <remarks>
    /// The returned set holds Cedar policy text, the augmented Cedar schema and the rule/source mapping.
    /// They are populated only when parsing and lowering both succeeded; a failed stage is reported through
    /// <see cref="DogwoodLoweredPolicySet.StageError"/> with <see cref="DogwoodLoweredPolicySet.IsLowered"/> false.
    /// A passing result is a lowered artifact, not an authorization decision.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The source or schema is null.</exception>
    /// <exception cref="ArgumentException">The source, a schema text, or the distincter is invalid, oversized, or not a Cedar identifier.</exception>
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

    private static readonly System.Text.RegularExpressions.Regex CedarIdentifier = new("^[_A-Za-z][_A-Za-z0-9]*$");
}

/// <summary>The schema inputs for a lowering or validation operation.</summary>
/// <param name="PolicySchema">The Cedar <c>.cedarschema</c> action schema text.</param>
/// <param name="EventSchema">Optional event-schema DSL source. Omit for the pinned default.</param>
/// <param name="Macros">Optional reusable <c>def</c> macro library. Omit for the pinned default.</param>
/// <param name="Distincter">
/// Optional lowering namespace. Every synthesized policy id and hoisted <c>context</c> field is
/// prefixed with it, so lowering several sets separately and combining them cannot collide.
/// Ignored by validation, which does not name hoisted fields.
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

    /// <summary>Parsing succeeded and the action schema was usable.</summary>
    Lower
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
    Internal
}

/// <summary>A fatal stage failure reported instead of partial output.</summary>
public sealed record DogwoodStageError(
    DogwoodStage Stage, DogwoodFailureCategory Category, string Message, PolicySourceLocation? Location);

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
public sealed record PolicyRuleMapping(int RuleIndex, string CedarPolicyId, string? SourceText);

/// <summary>The outcome of parse, lower and validate for one source and schema pair.</summary>
public sealed class DogwoodValidationResult
{
    internal DogwoodValidationResult(
        bool parsed,
        int? policyCount,
        ImmutableArray<PolicyDiagnostic> parseDiagnostics,
        bool lowered,
        int ruleCount,
        bool selfContainedCedar,
        bool validationPassed,
        ImmutableArray<PolicyValidationFinding> validationErrors,
        ImmutableArray<PolicyValidationFinding> validationWarnings,
        DogwoodStageError? stageError,
        ImmutableArray<PolicyRuleMapping> rules)
    {
        Parsed = parsed;
        PolicyCount = policyCount;
        ParseDiagnostics = parseDiagnostics;
        Lowered = lowered;
        RuleCount = ruleCount;
        SelfContainedCedar = selfContainedCedar;
        ValidationPassed = validationPassed;
        ValidationErrors = validationErrors;
        ValidationWarnings = validationWarnings;
        StageError = stageError;
        Rules = rules;
    }

    /// <summary>Whether upstream parsing succeeded. Meaningful only on its own.</summary>
    public bool Parsed { get; }

    /// <summary>The number of parsed policies, or null when parsing failed.</summary>
    public int? PolicyCount { get; }

    /// <summary>Parsing diagnostics in upstream order. Empty when parsing succeeded.</summary>
    public ImmutableArray<PolicyDiagnostic> ParseDiagnostics { get; }

    /// <summary>Whether lowering succeeded. Lowering runs only after a successful parse.</summary>
    public bool Lowered { get; }

    /// <summary>The number of lowered rules, or zero when lowering did not run.</summary>
    public int RuleCount { get; }

    /// <summary>Whether the lowered artifacts reproduce Dogwood semantics with no extra context.</summary>
    public bool SelfContainedCedar { get; }

    /// <summary>Whether validation produced no errors. Warnings never fail validation.</summary>
    public bool ValidationPassed { get; }

    /// <summary>Validation errors in upstream order.</summary>
    public ImmutableArray<PolicyValidationFinding> ValidationErrors { get; }

    /// <summary>Validation warnings in upstream order.</summary>
    public ImmutableArray<PolicyValidationFinding> ValidationWarnings { get; }

    /// <summary>The stage that stopped the pipeline, or null when it completed.</summary>
    public DogwoodStageError? StageError { get; }

    /// <summary>Rule index to Cedar policy id, in source order. Empty unless lowering succeeded.</summary>
    public ImmutableArray<PolicyRuleMapping> Rules { get; }

    /// <summary>True only when parsing, lowering and validation all succeeded.</summary>
    public bool IsValid => Parsed && Lowered && ValidationPassed;
}

/// <summary>The outcome of parse and lower for one source and schema pair.</summary>
public sealed class DogwoodLoweredPolicySet
{
    internal DogwoodLoweredPolicySet(
        bool parsed,
        int? policyCount,
        ImmutableArray<PolicyDiagnostic> parseDiagnostics,
        bool lowered,
        int ruleCount,
        bool selfContainedCedar,
        string cedarPolicyText,
        string cedarSchemaJson,
        string cedarSchemaText,
        ImmutableArray<PolicyRuleMapping> rules,
        DogwoodStageError? stageError)
    {
        Parsed = parsed;
        PolicyCount = policyCount;
        ParseDiagnostics = parseDiagnostics;
        Lowered = lowered;
        RuleCount = ruleCount;
        SelfContainedCedar = selfContainedCedar;
        CedarPolicyText = cedarPolicyText;
        CedarSchemaJson = cedarSchemaJson;
        CedarSchemaText = cedarSchemaText;
        Rules = rules;
        StageError = stageError;
    }

    /// <summary>Whether upstream parsing succeeded. Meaningful only on its own.</summary>
    public bool Parsed { get; }

    /// <summary>The number of parsed policies, or null when parsing failed.</summary>
    public int? PolicyCount { get; }

    /// <summary>Parsing diagnostics in upstream order. Empty when parsing succeeded.</summary>
    public ImmutableArray<PolicyDiagnostic> ParseDiagnostics { get; }

    /// <summary>Whether lowering succeeded.</summary>
    public bool Lowered { get; }

    /// <summary>The number of lowered rules, or zero when lowering did not run.</summary>
    public int RuleCount { get; }

    /// <summary>Whether the lowered artifacts reproduce Dogwood semantics with no extra context.</summary>
    public bool SelfContainedCedar { get; }

    /// <summary>The lowered Cedar policy text. Empty unless lowering succeeded.</summary>
    public string CedarPolicyText { get; }

    /// <summary>The augmented Cedar schema in the Cedar JSON schema form. Empty unless lowering succeeded.</summary>
    public string CedarSchemaJson { get; }

    /// <summary>The augmented Cedar schema as <c>.cedarschema</c> text. Empty unless lowering succeeded.</summary>
    public string CedarSchemaText { get; }

    /// <summary>Rule index, Cedar policy id and macro-expanded Dogwood source, in source order.</summary>
    public ImmutableArray<PolicyRuleMapping> Rules { get; }

    /// <summary>The stage that stopped the pipeline, or null when it completed.</summary>
    public DogwoodStageError? StageError { get; }

    /// <summary>True only when parsing and lowering both succeeded, so the artifacts are authoritative.</summary>
    public bool IsLowered => Parsed && Lowered;
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
