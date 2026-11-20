using System.Diagnostics;
using System.Text.Json;

namespace CedarSharp.E1Prototype;

/// <summary>One normalized answer used for cross-engine comparison.</summary>
/// <remarks>
/// Normalization preserves semantics: the success marker, the raw decision,
/// the set of determining IDs, every per-policy error with its policy ID and
/// message, and operation-level errors. Only the JSON envelope shape is
/// abstracted; message wording is carried through unchanged.
/// </remarks>
public sealed record NormalizedAnswer(
    bool IsSuccess,
    string? Decision,
    IReadOnlyCollection<string> Reason,
    IReadOnlyList<NormalizedPolicyError> PolicyErrors,
    IReadOnlyList<string> OperationErrors,
    IReadOnlyList<string> Warnings)
{
    public static NormalizedAnswer Normalize(JsonElement raw)
    {
        var type = raw.GetProperty("type").GetString();
        switch (type)
        {
            case "success":
                {
                    var response = raw.GetProperty("response");
                    var diagnostics = response.GetProperty("diagnostics");
                    var decision = response.GetProperty("decision").GetString();
                    var reason = diagnostics.GetProperty("reason").EnumerateArray()
                        .Select(id => id.GetString() ?? throw new JsonException("Null determining policy ID."))
                        .ToArray();
                    var errors = diagnostics.GetProperty("errors").EnumerateArray()
                        .Select(NormalizedPolicyError.Read)
                        .ToArray();
                    var warnings = raw.TryGetProperty("warnings", out var warningElement)
                        ? warningElement.EnumerateArray()
                            .Select(warning => warning.GetProperty("message").GetString() ?? string.Empty)
                            .ToArray()
                        : [];
                    return new NormalizedAnswer(true, decision, reason, errors, [], warnings);
                }
            case "failure":
                {
                    var errors = raw.GetProperty("errors").EnumerateArray()
                        .Select(error => error.GetProperty("message").GetString() ?? string.Empty)
                        .ToArray();
                    return new NormalizedAnswer(false, null, [], [], errors, []);
                }
            default:
                throw new JsonException($"Unknown answer type '{type}'.");
        }
    }
}

/// <summary>One policy-level error: the policy that errored and its message.</summary>
public sealed record NormalizedPolicyError(string PolicyId, string Message)
{
    public static NormalizedPolicyError Read(JsonElement raw) =>
        new(
            raw.GetProperty("policyId").GetString() ?? throw new JsonException("Null policy ID."),
            raw.GetProperty("error").GetProperty("message").GetString() ?? string.Empty);
}

/// <summary>
/// Runs the pinned Cedar 4.11.0 reference binary (the same Cedar SDK version
/// the Dogwood bridge pins) on the same JSON envelopes the CedarSharp bridge
/// consumes. Test tooling only; it is never on a production path.
/// </summary>
public static class Cedar11Reference
{
    internal const uint OperationVersion = 0;
    internal const uint OperationAuthorize = 1;
    internal const uint OperationValidate = 2;
    internal const uint OperationCheckPolicies = 3;
    internal const uint OperationCheckSchema = 4;
    internal const uint OperationCheckEntities = 5;
    internal const uint OperationCheckContext = 6;

    private static readonly Lazy<string?> Binary = new(FindBinary);

    /// <summary>The reference binary path, or null when it has not been built.</summary>
    public static string? BinaryPath => Binary.Value;

    /// <summary>The reference SDK identity (sdkVersion and languageVersion).</summary>
    public static (string SdkVersion, string LanguageVersion) Version()
    {
        using var document = Call(OperationVersion, "{}").Document;
        return (
            document.RootElement.GetProperty("sdkVersion").GetString()!,
            document.RootElement.GetProperty("languageVersion").GetString()!);
    }

    public static (JsonDocument Document, string Raw) Call(uint operation, string inputJson)
    {
        var path = Binary.Value
            ?? throw new InvalidOperationException(
                "The Cedar 4.11 reference binary is missing. Build it with: " +
                "cargo build --locked --release --manifest-path native/dogwoodsharp-e1-ref/Cargo.toml");
        var input = Path.Combine(Path.GetTempPath(), $"dogwoodsharp-e1-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(input, inputJson);
            var start = new ProcessStartInfo
            {
                FileName = path,
                ArgumentList = { operation.ToString(), input },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("The reference binary could not be started.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Reference operation {operation} failed with exit code {process.ExitCode}: {error.Trim()}");
            }

            return (JsonDocument.Parse(output), output);
        }
        finally
        {
            File.Delete(input);
        }
    }

    private static string? FindBinary()
    {
        var name = OperatingSystem.IsWindows() ? "dogwoodsharp-e1-ref.exe" : "dogwoodsharp-e1-ref";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "native", "dogwoodsharp-e1-ref", "target", "release", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent!;
        }

        return null;
    }
}
