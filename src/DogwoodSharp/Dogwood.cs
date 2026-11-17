using System.Collections.Immutable;
using System.Text;

namespace DogwoodSharp;

/// <summary>Discovers the native bridge and parses Dogwood policy source.</summary>
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

        return NativeBridge.Shared.Parse(source, length);
    }
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
