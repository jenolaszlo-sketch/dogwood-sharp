using System;
using System.Collections.Generic;

namespace DogwoodSharp.Experimental.CedarSharp;

/// <summary>The raw Cedar decision. Null when an operation failure produced no decision.</summary>
public enum CedarDecision
{
    /// <summary>Cedar allowed the request.</summary>
    Allow,

    /// <summary>Cedar denied the request.</summary>
    Deny
}

/// <summary>The pipeline stage an authorization outcome is attributed to.</summary>
public enum CedarAuthorizationStage
{
    /// <summary>A usable decision was produced.</summary>
    None,

    /// <summary>Snapshot construction or request JSON parsing failed.</summary>
    RequestConstruction,

    /// <summary>Schema or request validation rejected the request before a decision.</summary>
    SchemaOrRequestValidation,

    /// <summary>Native evaluation completed and produced its result.</summary>
    Evaluation,

    /// <summary>A native load, ABI, transport, or response-decoding failure occurred.</summary>
    NativeTransport,

    /// <summary>Evaluation named a policy identity that is not in the immutable bundle.</summary>
    UnknownPolicyIdentity,

    /// <summary>The lowering owns a trusted pipeline field whose trusted binding is unavailable.</summary>
    TrustedBindingUnavailable
}

/// <summary>
/// Stable diagnostic categories. Wording is informative only; the category is the normative
/// contract and is preserved across engine versions.
/// </summary>
public enum CedarDiagnosticCategory
{
    /// <summary>An entity did not conform to the schema.</summary>
    EntityConformance,

    /// <summary>A required entity or context attribute was absent.</summary>
    MissingAttribute,

    /// <summary>A principal or resource type is outside the action's <c>appliesTo</c>.</summary>
    ScopeMismatch,

    /// <summary>The action is not declared by the schema.</summary>
    UndeclaredAction,

    /// <summary>A referenced entity is absent.</summary>
    MissingEntity,

    /// <summary>A policy failed during evaluation.</summary>
    PolicyEvaluationError,

    /// <summary>An operation-level failure that is not a policy error.</summary>
    OperationError,

    /// <summary>Evaluation named a policy identity not in the bundle.</summary>
    UnknownPolicyId,

    /// <summary>The caller context carried a reserved generated field.</summary>
    ReservedContextField,

    /// <summary>A trusted temporal/provider binding was required but unavailable.</summary>
    TrustedBindingUnavailable
}

/// <summary>One operation diagnostic with a stable category and an informative message.</summary>
/// <param name="Category">The stable diagnostic category.</param>
/// <param name="Message">The human-readable diagnostic, which may change with an engine version.</param>
public sealed record CedarDiagnostic(CedarDiagnosticCategory Category, string Message);

/// <summary>One policy evaluation error, keyed by its D2-generated Cedar policy id.</summary>
/// <param name="PolicyId">The D2-generated Cedar policy id whose evaluation erred.</param>
/// <param name="Category">The stable diagnostic category.</param>
/// <param name="Message">The human-readable diagnostic, which may change with an engine version.</param>
public sealed record CedarPolicyError(string PolicyId, CedarDiagnosticCategory Category, string Message);

/// <summary>
/// The outcome of one schema-carrying CedarSharp authorization. The raw Cedar decision and the
/// enforcement decision are distinct: only an error-free Allow authorizes an enforcement caller.
/// </summary>
public sealed class CedarAuthorizationResult
{
    internal CedarAuthorizationResult(
        bool operationFailed,
        CedarAuthorizationStage stage,
        CedarDecision? decision,
        bool authorized,
        bool isCleanAllow,
        IReadOnlyList<string> determiningPolicyIds,
        IReadOnlyList<CedarPolicyError> policyErrors,
        IReadOnlyList<CedarDiagnostic> errors)
    {
        OperationFailed = operationFailed;
        Stage = stage;
        Decision = decision;
        Authorized = authorized;
        IsCleanAllow = isCleanAllow;
        DeterminingPolicyIds = determiningPolicyIds;
        PolicyErrors = policyErrors;
        Errors = errors;
    }

    /// <summary>Whether no usable authorization result was produced.</summary>
    public bool OperationFailed { get; }

    /// <summary>The stage this outcome is attributed to.</summary>
    public CedarAuthorizationStage Stage { get; }

    /// <summary>The raw Cedar decision, or null when the operation failed before a decision.</summary>
    public CedarDecision? Decision { get; }

    /// <summary>The enforcement decision: true only for an error-free Allow. Never authorize on any failure.</summary>
    public bool Authorized { get; }

    /// <summary>Whether the decision is an error-free Allow.</summary>
    public bool IsCleanAllow { get; }

    /// <summary>The determining policy identities as a set (order is not a contract).</summary>
    public IReadOnlyList<string> DeterminingPolicyIds { get; }

    /// <summary>Policy evaluation errors, keyed by D2-generated Cedar policy id.</summary>
    public IReadOnlyList<CedarPolicyError> PolicyErrors { get; }

    /// <summary>Operation errors with stable categories.</summary>
    public IReadOnlyList<CedarDiagnostic> Errors { get; }
}
