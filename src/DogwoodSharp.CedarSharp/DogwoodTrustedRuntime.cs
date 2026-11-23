using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace DogwoodSharp.Experimental.CedarSharp;

/// <summary>
/// One event in the trusted temporal history supplied to the D3 runtime. Request context and the
/// durable logged record are separate: <see cref="ContextJson"/> is request-only data, while
/// <see cref="LoggedJson"/> is the durable event record read by temporal predicates.
/// </summary>
/// <param name="Identity">The stable, nonempty event identity assigned by the trusted source.</param>
/// <param name="Timestamp">The trusted monotonic timestamp, in seconds.</param>
/// <param name="ActionType">
/// The qualified action type path (for example <c>App::Action</c>), not the action id.
/// </param>
/// <param name="ActionId">The action id (for example <c>read</c>).</param>
/// <param name="Kind">The upstream event kind (for example <c>request</c> or <c>response</c>).</param>
/// <param name="Principal">The optional request principal for a request-wrapping event.</param>
/// <param name="Resource">The optional request resource for a request-wrapping event.</param>
/// <param name="ContextJson">The request-only context JSON object.</param>
/// <param name="LoggedJson">The durable logged-event JSON object.</param>
/// <param name="EntitiesJson">The Cedar entity JSON array supplied to this event.</param>
public sealed record DogwoodTrustedEvent(
    string Identity,
    long Timestamp,
    string ActionType,
    string ActionId,
    string Kind,
    CedarEntityRef? Principal,
    CedarEntityRef? Resource,
    string ContextJson,
    string LoggedJson,
    string EntitiesJson);

/// <summary>The outcome of one trusted provider evaluation.</summary>
public enum DogwoodTrustedProviderOutcomeKind
{
    /// <summary>The provider produced a value.</summary>
    Value,

    /// <summary>The provider was evaluated but deliberately produced no value.</summary>
    Omitted,

    /// <summary>The provider evaluation failed.</summary>
    Failure
}

/// <summary>
/// The result of one trusted provider evaluation. A successful value is Cedar JSON in the upstream
/// provider output shape; an omitted or failed outcome has no value and can never fall back to
/// caller context or to a declared provider implementation.
/// </summary>
/// <param name="Name">The fully qualified provider name (for example <c>Strings::Matches</c>).</param>
/// <param name="Outcome">The trusted outcome kind.</param>
/// <param name="ValueJson">The provider value JSON for a successful outcome; otherwise null.</param>
/// <param name="Message">The failure or omission message; otherwise null.</param>
public sealed record DogwoodTrustedProviderOutcome(
    string Name,
    DogwoodTrustedProviderOutcomeKind Outcome,
    string? ValueJson,
    string? Message)
{
    /// <summary>Creates a successful provider outcome.</summary>
    public static DogwoodTrustedProviderOutcome Succeeded(string name, string valueJson)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(valueJson);
        return new(name, DogwoodTrustedProviderOutcomeKind.Value, valueJson, null);
    }

    /// <summary>Creates an omitted provider outcome.</summary>
    public static DogwoodTrustedProviderOutcome Omitted(string name, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new(name, DogwoodTrustedProviderOutcomeKind.Omitted, null, message);
    }

    /// <summary>Creates a failed provider outcome.</summary>
    public static DogwoodTrustedProviderOutcome Failed(string name, string message)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(message);
        return new(name, DogwoodTrustedProviderOutcomeKind.Failure, null, message);
    }
}

/// <summary>
/// An immutable trusted-runtime snapshot for one authorization attempt. This is deliberately a
/// separate surface from <see cref="CedarAuthorizationInput"/>: ordinary callers cannot populate
/// durable logged history or provider outcomes through the public request record.
/// </summary>
/// <param name="Timestamp">The current decision's trusted monotonic timestamp, in seconds.</param>
/// <param name="CurrentLoggedJson">
/// The current decision event's trusted durable logged record. Empty means <c>{}</c>. When it is
/// empty, the native runtime mirrors only the request context's <c>input</c> group into the logged
/// record, matching upstream request-event construction.
/// </param>
/// <param name="History">The complete ordered trusted history before the current decision.</param>
/// <param name="Providers">The trusted provider outcomes required by the current request.</param>
public sealed record DogwoodTrustedRuntime(
    long Timestamp,
    string CurrentLoggedJson,
    ImmutableArray<DogwoodTrustedEvent> History,
    ImmutableArray<DogwoodTrustedProviderOutcome> Providers)
{
    /// <summary>Creates a trusted runtime snapshot and copies its input collections.</summary>
    public DogwoodTrustedRuntime(
        long timestamp,
        string currentLoggedJson,
        IEnumerable<DogwoodTrustedEvent> history,
        IEnumerable<DogwoodTrustedProviderOutcome> providers)
        : this(
            timestamp,
            currentLoggedJson,
            history?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(history)),
            providers?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(providers)))
    {
    }
}
