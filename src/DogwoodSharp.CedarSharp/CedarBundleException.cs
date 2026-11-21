using System;

namespace DogwoodSharp.Experimental.CedarSharp;

/// <summary>The reason a lowering bundle could not be built into an authorizer.</summary>
public enum CedarBundleFailure
{
    /// <summary>The lowering did not export a complete validated bundle.</summary>
    LoweringNotExported,

    /// <summary>A lowered rule is missing its generated Cedar policy id.</summary>
    MissingPolicyId,

    /// <summary>A lowered rule is missing its exact Cedar policy body.</summary>
    MissingPolicyBody,

    /// <summary>Two lowered rules share a generated Cedar policy id.</summary>
    DuplicatePolicyId,

    /// <summary>The lowering exported no canonical augmented schema JSON.</summary>
    MissingSchema,

    /// <summary>The exported policy set or schema could not be parsed by the engine.</summary>
    InvalidArtifact,

    /// <summary>The built policy set's ids differ from the exported rule ids.</summary>
    PolicyIdMismatch
}

/// <summary>
/// A bundle construction failure. Raised before any authorizer exists; a failed construction
/// never yields an empty policy set that continues.
/// </summary>
public sealed class CedarBundleException : Exception
{
    internal CedarBundleException(CedarBundleFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }

    /// <summary>The failure category.</summary>
    public CedarBundleFailure Failure { get; }
}
