using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DogwoodSharp;

internal sealed unsafe class NativeBridge
{
    private const uint Abi = 1;
    private const string BridgeVersion = "0.1.0";
    private const string DogwoodRevision = "929f49e7305534ac61ef06cf8e47269c596dc851";
    private const string DogwoodVersion = "1.1.2";
    private const string CedarVersion = "4.11.0";
    private const string RustVersion = "1.99.1";
    private const int MaximumResponseBytes = 2 * Dogwood.MaximumSourceBytes;
    private const int MaximumLoweringResponseBytes = 8 * Dogwood.MaximumSourceBytes;
    private const int MaximumManifestBytes = 4096;

    /// <summary>Identity and parsing operations. Their wire contract is unchanged from D1.</summary>
    private const uint OperationIdentity = 0;
    private const uint OperationParse = 1;

    /// <summary>D2 lowering and validation operations.</summary>
    private const uint OperationValidate = 2;
    private const uint OperationLower = 3;

    /// <summary>
    /// The D3 per-rule Cedar export operation. It runs the same pipeline and reports the same
    /// validated artifacts as operation 3, plus each rule's exact Cedar body and the trusted
    /// generated context-field names. An older native image answers this caller with status 3,
    /// which surfaces as a <see cref="DogwoodBridgeException"/>; the D3 reader never substitutes
    /// the operation 3 reply for an unavailable operation 4.
    /// </summary>
    private const uint OperationExport = 4;

    private static readonly Lazy<NativeBridge> Instance = new(() => Load(AppContext.BaseDirectory));
    private readonly delegate* unmanaged[Cdecl]<uint, uint, byte*, uint, Buffer*, uint> call;
    private readonly delegate* unmanaged[Cdecl]<Buffer*, void> free;

    [StructLayout(LayoutKind.Sequential)]
    private struct Buffer
    {
        public byte* Data;
        public uint Length;
    }

    private NativeBridge(nint handle, string target)
    {
        var abi = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(handle, "dogwoodsharp_abi_version");
        if (abi() != Abi)
        {
            throw Identity("Native ABI version mismatch.");
        }

        call = (delegate* unmanaged[Cdecl]<uint, uint, byte*, uint, Buffer*, uint>)NativeLibrary.GetExport(handle, "dogwoodsharp_call_v1");
        free = (delegate* unmanaged[Cdecl]<Buffer*, void>)NativeLibrary.GetExport(handle, "dogwoodsharp_free_v1");
        try
        {
            using var reply = Invoke(OperationIdentity, "{}"u8, MaximumResponseBytes);
            Version = ReadIdentity(reply.RootElement, target, manifest: false);
        }
        catch (Exception exception) when (exception is DogwoodBridgeException or JsonException or InvalidOperationException or FormatException)
        {
            throw Identity("Native version discovery failed.", exception);
        }
    }

    internal static NativeBridge Shared => Instance.Value;

    internal DogwoodNativeVersion Version { get; }

    private static NativeBridge Load(string root)
    {
        if (AssemblyLoadContext.GetLoadContext(typeof(NativeBridge).Assembly) != AssemblyLoadContext.Default)
        {
            throw Identity("Load DogwoodSharp in the default assembly load context.");
        }

        var (rid, target, file) = Platform();
        var path = Path.GetFullPath(Path.Combine(root, "runtimes", rid, "native", file));
        nint handle = 0;
        try
        {
            VerifyManifest(path, rid, target);
            handle = NativeLibrary.Load(path, typeof(NativeBridge).Assembly, DllImportSearchPath.System32);
            var bridge = new NativeBridge(handle, target);
            // The singleton keeps this loader reference for the process lifetime.
            // Only an unsuccessful initialization releases its reference.
            handle = 0;
            return bridge;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            BadImageFormatException or DllNotFoundException or EntryPointNotFoundException or
            JsonException or InvalidOperationException or FormatException or DecoderFallbackException)
        {
            throw Identity($"Could not load the verified DogwoodSharp asset for {rid}.", exception);
        }
        finally
        {
            if (handle != 0)
            {
                NativeLibrary.Free(handle);
            }
        }
    }

    internal static (string Rid, string Target, string File) Platform()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 when OperatingSystem.IsWindows() =>
                ("win-x64", "x86_64-pc-windows-msvc", "dogwoodsharp_bridge.dll"),
            Architecture.X64 when OperatingSystem.IsLinux() =>
                ("linux-x64", "x86_64-unknown-linux-gnu", "libdogwoodsharp_bridge.so"),
            Architecture.Arm64 when OperatingSystem.IsMacOS() =>
                ("osx-arm64", "aarch64-apple-darwin", "libdogwoodsharp_bridge.dylib"),
            _ => throw Identity("This operating system and process architecture have no D1 native asset.")
        };
    }

    private static void VerifyManifest(string path, string rid, string target)
    {
        using var manifest = File.OpenRead(Path.Combine(Path.GetDirectoryName(path)!, "dogwoodsharp-native.json"));
        if (manifest.Length is <= 0 or > MaximumManifestBytes)
        {
            throw Identity("Invalid native manifest size.");
        }

        var bytes = new byte[(int)manifest.Length];
        manifest.ReadExactly(bytes);
        _ = Dogwood.StrictUtf8.GetCharCount(bytes);
        using var document = JsonDocument.Parse(bytes);
        var json = document.RootElement;
        _ = ReadIdentity(json, target, manifest: true);
        if (json.GetProperty("manifestVersion").GetInt32() != 1 ||
            json.GetProperty("rid").GetString() != rid ||
            json.GetProperty("file").GetString() != Path.GetFileName(path))
        {
            throw Identity("Native manifest platform or schema mismatch.");
        }

        _ = ReadHash(json, "cargoLockSha256");
        var expectedHash = ReadHash(json, "sha256");
        using var image = File.OpenRead(path);
        if (!SHA256.HashData(image).AsSpan().SequenceEqual(expectedHash))
        {
            throw Identity("Native asset SHA-256 differs from the manifest.");
        }
    }

    private static byte[] ReadHash(JsonElement json, string name)
    {
        var hex = json.GetProperty(name).GetString();
        if (hex?.Length != 64)
        {
            throw Identity($"Invalid SHA-256 field {name}.");
        }

        return Convert.FromHexString(hex);
    }

    private static DogwoodNativeVersion ReadIdentity(JsonElement json, string target, bool manifest)
    {
        string[] properties = ["abiVersion", "bridgeVersion", "dogwoodRevision", "dogwoodVersion",
            "cedarVersion", "rustVersion", "target", "dogwoodFeatures", "cedarFeatures"];
        if (manifest)
        {
            properties = [.. properties, "manifestVersion", "rid", "file", "sha256", "cargoLockSha256"];
        }

        RequireProperties(json, properties);
        if (json.GetProperty("abiVersion").GetUInt32() != Abi ||
            json.GetProperty("bridgeVersion").GetString() != BridgeVersion ||
            json.GetProperty("dogwoodRevision").GetString() != DogwoodRevision ||
            json.GetProperty("dogwoodVersion").GetString() != DogwoodVersion ||
            json.GetProperty("cedarVersion").GetString() != CedarVersion ||
            json.GetProperty("rustVersion").GetString() != RustVersion ||
            json.GetProperty("target").GetString() != target ||
            json.GetProperty("dogwoodFeatures").GetArrayLength() != 0 ||
            !json.GetProperty("cedarFeatures").EnumerateArray().Select(x => x.GetString())
                .SequenceEqual(new[] { "datetime", "decimal", "ipaddr" }))
        {
            throw Identity("Incompatible native bridge identity or feature set.");
        }

        return new(Abi, BridgeVersion, DogwoodRevision, DogwoodVersion, CedarVersion, RustVersion, target);
    }

    internal PolicyParseResult Parse(string source, int sourceBytes)
    {
        var input = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(input))
        {
            writer.WriteStartObject();
            writer.WriteString("source", source);
            writer.WriteEndObject();
        }

        using var reply = Invoke(OperationParse, input.WrittenSpan, MaximumResponseBytes);
        return ReadParseResult(reply.RootElement, sourceBytes);
    }

    internal DogwoodValidationResult Validate(DogwoodSchema schema, string source, int sourceBytes)
    {
        var input = BuildD2Request(source, schema);
        using var reply = Invoke(OperationValidate, input.WrittenSpan, MaximumLoweringResponseBytes);
        return ReadValidationResult(reply.RootElement, sourceBytes);
    }

    internal DogwoodLoweredPolicySet Lower(DogwoodSchema schema, string source, int sourceBytes)
    {
        var input = BuildD2Request(source, schema);
        using var reply = Invoke(OperationLower, input.WrittenSpan, MaximumLoweringResponseBytes);
        return ReadLoweredResult(reply.RootElement, sourceBytes);
    }

    /// <summary>Runs the D3 per-rule Cedar export operation and reads its completed bundle.</summary>
    internal DogwoodCedarExport Export(DogwoodSchema schema, string source, int sourceBytes)
    {
        var input = BuildD2Request(source, schema);
        using var reply = Invoke(OperationExport, input.WrittenSpan, MaximumLoweringResponseBytes);
        return ReadExportedResult(reply.RootElement, sourceBytes);
    }

    private static ArrayBufferWriter<byte> BuildD2Request(string source, DogwoodSchema schema)
    {
        var input = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(input))
        {
            writer.WriteStartObject();
            writer.WriteString("source", source);
            writer.WriteString("policySchema", schema.PolicySchema);
            if (schema.EventSchema is not null)
            {
                writer.WriteString("eventSchema", schema.EventSchema);
            }

            if (schema.Macros is not null)
            {
                writer.WriteString("macros", schema.Macros);
            }

            if (schema.Distincter is not null)
            {
                writer.WriteString("distincter", schema.Distincter);
            }

            writer.WriteEndObject();
        }

        return input;
    }

    private static DogwoodNativeVersion ReadVersion(JsonElement json, string target)
    {
        return ReadIdentity(json, target, manifest: false);
    }

    internal static DogwoodValidationResult ReadValidationResult(JsonElement json, int sourceBytes)
    {
        try
        {
            return ReadPipeline(json, sourceBytes, export: false, d3: false).Validation;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw Malformed(exception);
        }
    }

    internal static DogwoodLoweredPolicySet ReadLoweredResult(JsonElement json, int sourceBytes)
    {
        try
        {
            var (validation, exported) = ReadPipeline(json, sourceBytes, export: true, d3: false);
            string policy = ReadText(json, "cedarPolicyText");
            string schemaJson = ReadText(json, "cedarSchemaJson");
            string schemaText = ReadText(json, "cedarSchemaText");
            if (exported)
            {
                using var schema = JsonDocument.Parse(schemaJson);
                bool schemaHasText = SchemaHasText(schema.RootElement);
                if ((schemaHasText ? string.IsNullOrWhiteSpace(schemaText) : schemaText.Length != 0) ||
                    string.IsNullOrWhiteSpace(policy) != (validation.RuleCount == 0))
                {
                    throw new JsonException("Invalid validated artifacts.");
                }
            }
            else if (policy.Length != 0 || schemaJson.Length != 0 || schemaText.Length != 0)
            {
                throw new JsonException("Unexpected artifacts without a successful export.");
            }

            return new DogwoodLoweredPolicySet(validation, exported, policy, schemaJson, schemaText);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw Malformed(exception);
        }
    }

    /// <summary>
    /// Reads the D3 export operation's reply. The reply carries the frozen D2 fields, the three
    /// artifact strings, the generated context-field names, and per rule the exact Cedar body under
    /// its generated id. Every field is required: an incomplete export is rejected rather than
    /// completed from the operation 3 reply.
    /// </summary>
    internal static DogwoodCedarExport ReadExportedResult(JsonElement json, int sourceBytes)
    {
        try
        {
            var (validation, exported) = ReadPipeline(json, sourceBytes, export: true, d3: true);
            string policy = ReadText(json, "cedarPolicyText");
            string schemaJson = ReadText(json, "cedarSchemaJson");
            string schemaText = ReadText(json, "cedarSchemaText");
            ImmutableArray<string> generatedFields = ReadGeneratedContextFields(json);
            ImmutableArray<CedarPolicyRule> rules = ReadCedarRules(json.GetProperty("rules"), sourceBytes);
            if (exported)
            {
                using var schema = JsonDocument.Parse(schemaJson);
                bool schemaHasText = SchemaHasText(schema.RootElement);
                if ((schemaHasText ? string.IsNullOrWhiteSpace(schemaText) : schemaText.Length != 0) ||
                    string.IsNullOrWhiteSpace(policy) != (validation.RuleCount == 0) ||
                    rules.Length != validation.RuleCount ||
                    generatedFields.IsEmpty != validation.SelfContainedCedar)
                {
                    throw new JsonException("Invalid exported artifacts.");
                }
            }
            else if (policy.Length != 0 || schemaJson.Length != 0 || schemaText.Length != 0 ||
                rules.Length != 0 || !generatedFields.IsEmpty)
            {
                throw new JsonException("Unexpected artifacts without a successful export.");
            }

            return new DogwoodCedarExport(
                validation, exported, policy, schemaJson, schemaText, rules, generatedFields);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw Malformed(exception);
        }
    }

    private static bool SchemaHasText(JsonElement schema)
    {
        // Cedar 4.11 emits every named namespace, including empty ones.
        // The unnamed namespace emits only its common types, entities and actions.
        bool hasText = false;
        foreach (var entry in schema.EnumerateObject())
        {
            var definition = entry.Value;
            if (!definition.TryGetProperty("entityTypes", out var entities) ||
                !definition.TryGetProperty("actions", out var actions))
            {
                throw new JsonException("Missing Cedar schema declaration map.");
            }

            bool hasEntities = entities.EnumerateObject().MoveNext();
            bool hasActions = actions.EnumerateObject().MoveNext();
            bool hasTypes = definition.TryGetProperty("commonTypes", out var types) &&
                types.EnumerateObject().MoveNext();
            hasText |= entry.Name.Length != 0 || hasEntities || hasActions || hasTypes;
        }

        return hasText;
    }

    private static (DogwoodValidationResult Validation, bool Exported) ReadPipeline(
        JsonElement json, int sourceBytes, bool export, bool d3)
    {
        string[] properties = ["serviceSchemaBuilt", "parsed", "policyCount", "parseDiagnostics",
            "actionSchemaBuilt", "lowered", "ruleCount", "selfContainedCedar", "collisionCheckPassed",
            "collision", "validationPassed", "validationErrors", "validationWarnings",
            "artifactsExported", "stageError", "rules"];
        RequireProperties(
            json,
            d3
                ? [.. properties, "cedarPolicyText", "cedarSchemaJson", "cedarSchemaText", "generatedContextFields"]
                : export
                    ? [.. properties, "cedarPolicyText", "cedarSchemaJson", "cedarSchemaText"]
                    : properties);

        bool service = json.GetProperty("serviceSchemaBuilt").GetBoolean();
        var (parsed, policyCount, parseDiagnostics) = ReadStage(json, sourceBytes);
        bool action = json.GetProperty("actionSchemaBuilt").GetBoolean();
        bool lowered = json.GetProperty("lowered").GetBoolean();
        bool checkedCollisions = json.GetProperty("collisionCheckPassed").GetBoolean();
        bool validated = json.GetProperty("validationPassed").GetBoolean();
        bool exported = json.GetProperty("artifactsExported").GetBoolean();
        if ((parsed && !service) || (action && !parsed) || (lowered && !action) ||
            (checkedCollisions && !lowered) || (validated && !checkedCollisions) ||
            (exported && (!validated || !export)))
        {
            throw new JsonException("Invalid D2 pipeline stage order.");
        }

        int ruleCount = json.GetProperty("ruleCount").GetInt32();
        bool selfContained = json.GetProperty("selfContainedCedar").GetBoolean();
        var stageError = ReadStageError(json.GetProperty("stageError"), sourceBytes);
        var collision = ReadCollision(json.GetProperty("collision"));
        var errors = ReadFindings(json.GetProperty("validationErrors"), sourceBytes);
        var warnings = ReadFindings(json.GetProperty("validationWarnings"), sourceBytes);
        // The D3 export reports the exact body stored under each generated id; the frozen
        // operation 2/3 rule mapping carries the identity and expanded source only.
        var rules = d3
            ? ReadCedarRules(json.GetProperty("rules"), sourceBytes)
                .Select(rule => new PolicyRuleMapping(rule.RuleIndex, rule.CedarPolicyId, rule.SourceText))
                .ToImmutableArray()
            : ReadRules(json.GetProperty("rules"), withSource: export);
        DogwoodStage? expectedFailure = (service, parsed, action, lowered, checkedCollisions, validated, export, exported) switch
        {
            (false, _, _, _, _, _, _, _) => DogwoodStage.ServiceSchema,
            (true, false, _, _, _, _, _, _) => null,
            (_, _, false, _, _, _, _, _) => DogwoodStage.ActionSchema,
            (_, _, _, false, _, _, _, _) => DogwoodStage.Lower,
            (_, _, _, _, false, _, _, _) => DogwoodStage.CollisionCheck,
            (_, _, _, _, _, true, true, false) => DogwoodStage.ArtifactExport,
            _ => null
        };
        bool collisionReported = stageError?.Category == DogwoodFailureCategory.GeneratedContextFieldCollision;
        if (stageError?.Stage != expectedFailure ||
            parseDiagnostics.IsEmpty == (service && !parsed) ||
            collisionReported != (collision is not null) ||
            (collisionReported && expectedFailure != DogwoodStage.CollisionCheck) ||
            (expectedFailure == DogwoodStage.CollisionCheck && !collisionReported &&
                stageError?.Category != DogwoodFailureCategory.Internal) ||
            (expectedFailure == DogwoodStage.ArtifactExport && stageError?.Category != DogwoodFailureCategory.Internal) ||
            (lowered ? ruleCount != policyCount : ruleCount != 0 || selfContained) ||
            (checkedCollisions ? validated != errors.IsEmpty : !errors.IsEmpty || !warnings.IsEmpty) ||
            rules.Length != ((export ? exported : validated) ? ruleCount : 0))
        {
            throw new JsonException("Contradictory D2 pipeline result.");
        }

        return (new DogwoodValidationResult(service, parsed, policyCount, parseDiagnostics, action, lowered,
            ruleCount, selfContained, checkedCollisions, collision, validated, errors, warnings, stageError, rules), exported);
    }

    private static DogwoodContextFieldCollision? ReadCollision(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireProperties(json, "field", "actionNamespace", "actionId");
        var field = ReadText(json, "field");
        var actionNamespace = json.GetProperty("actionNamespace").GetString();
        if (field.Length == 0 || actionNamespace is "")
        {
            throw new JsonException("Invalid generated field or action namespace.");
        }

        return new DogwoodContextFieldCollision(field, actionNamespace, ReadText(json, "actionId"));
    }

    private static (bool Parsed, int? PolicyCount, ImmutableArray<PolicyDiagnostic> ParseDiagnostics)
        ReadStage(JsonElement json, int sourceBytes)
    {
        var parsed = json.GetProperty("parsed").GetBoolean();
        var parsedCount = json.GetProperty("policyCount");
        var findings = json.GetProperty("parseDiagnostics");
        int size = findings.GetArrayLength();
        if (parsed)
        {
            if (size != 0 || parsedCount.ValueKind != JsonValueKind.Number || parsedCount.GetInt32() < 0)
            {
                throw new JsonException("Invalid successful parse stage.");
            }

            return (true, parsedCount.GetInt32(), []);
        }

        if (parsedCount.ValueKind != JsonValueKind.Null)
        {
            throw new JsonException("Invalid failed parse stage.");
        }

        var builder = ImmutableArray.CreateBuilder<PolicyDiagnostic>(size);
        foreach (var diagnostic in findings.EnumerateArray())
        {
            RequireProperties(diagnostic, "category", "message", "location");
            var category = diagnostic.GetProperty("category").GetString() switch
            {
                "syntax" => PolicyDiagnosticCategory.Syntax,
                "macro" => PolicyDiagnosticCategory.Macro,
                _ => throw new JsonException("Invalid diagnostic category.")
            };
            var message = diagnostic.GetProperty("message").GetString() ?? throw new JsonException("Missing diagnostic message.");
            builder.Add(new PolicyDiagnostic(category, message, ReadLocation(diagnostic, sourceBytes)));
        }

        return (false, null, builder.MoveToImmutable());
    }

    private static string ReadText(JsonElement json, string name) =>
        json.GetProperty(name).GetString() ?? throw new JsonException($"Missing string {name}.");

    private static PolicySourceLocation? ReadLocation(JsonElement json, int sourceBytes)
    {
        var location = json.GetProperty("location");
        if (location.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireProperties(location, "offset", "length");
        int offset = location.GetProperty("offset").GetInt32();
        int length = location.GetProperty("length").GetInt32();
        if (offset < 0 || length < 0 || offset > sourceBytes ||
            (length > sourceBytes - offset && !(offset == sourceBytes && length == 1)))
        {
            throw new JsonException("Diagnostic location is outside the source.");
        }

        return new PolicySourceLocation(offset, length);
    }

    private static ImmutableArray<PolicyValidationFinding> ReadFindings(JsonElement json, int sourceBytes)
    {
        var builder = ImmutableArray.CreateBuilder<PolicyValidationFinding>(json.GetArrayLength());
        foreach (var finding in json.EnumerateArray())
        {
            RequireProperties(finding, "category", "message", "location");
            var category = finding.GetProperty("category").GetString() switch
            {
                "cedar" => PolicyValidationCategory.Cedar,
                "temporal" => PolicyValidationCategory.Temporal,
                "provider" => PolicyValidationCategory.Provider,
                _ => PolicyValidationCategory.Unspecified
            };
            var message = finding.GetProperty("message").GetString() ?? throw new JsonException("Missing validation message.");
            builder.Add(new PolicyValidationFinding(category, message, ReadLocation(finding, sourceBytes)));
        }

        return builder.MoveToImmutable();
    }

    private static DogwoodStageError? ReadStageError(JsonElement json, int sourceBytes)
    {
        if (json.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireProperties(json, "stage", "category", "message", "location");
        var stage = json.GetProperty("stage").GetString() switch
        {
            "service-schema" => DogwoodStage.ServiceSchema,
            "action-schema" => DogwoodStage.ActionSchema,
            "lower" => DogwoodStage.Lower,
            "collision-check" => DogwoodStage.CollisionCheck,
            "artifact-export" => DogwoodStage.ArtifactExport,
            _ => throw new JsonException("Invalid pipeline stage.")
        };
        var category = json.GetProperty("category").GetString() switch
        {
            "schema" => DogwoodFailureCategory.Schema,
            "policy" => DogwoodFailureCategory.Policy,
            "distincter" => DogwoodFailureCategory.Distincter,
            "generated-context-field-collision" => DogwoodFailureCategory.GeneratedContextFieldCollision,
            "internal" => DogwoodFailureCategory.Internal,
            _ => throw new JsonException("Invalid stage error category.")
        };
        var message = json.GetProperty("message").GetString() ?? throw new JsonException("Missing stage error message.");
        return new DogwoodStageError(stage, category, message, ReadLocation(json, sourceBytes));
    }

    private static ImmutableArray<PolicyRuleMapping> ReadRules(JsonElement json, bool withSource)
    {
        var builder = ImmutableArray.CreateBuilder<PolicyRuleMapping>(json.GetArrayLength());
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in json.EnumerateArray())
        {
            if (withSource)
            {
                RequireProperties(rule, "ruleIndex", "cedarPolicyId", "sourceText");
            }
            else
            {
                RequireProperties(rule, "ruleIndex", "cedarPolicyId");
            }

            int index = rule.GetProperty("ruleIndex").GetInt32();
            string id = ReadText(rule, "cedarPolicyId");
            string? source = withSource ? ReadText(rule, "sourceText") : null;
            if (index != builder.Count || id.Length == 0 || !ids.Add(id) || source is "")
            {
                throw new JsonException("Invalid rule mapping.");
            }

            builder.Add(new PolicyRuleMapping(index, id, source));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Reads the D3 export's per-rule records. Each record carries exactly its rule index, the
    /// generated Cedar policy id, the expanded source, and the exact Cedar body the lowered set
    /// stores under that id. A missing, empty, duplicated, or misordered record is rejected: the
    /// export is all-or-nothing, and no rule may be completed from concatenated policy text.
    /// </summary>
    private static ImmutableArray<CedarPolicyRule> ReadCedarRules(JsonElement json, int sourceBytes)
    {
        var builder = ImmutableArray.CreateBuilder<CedarPolicyRule>(json.GetArrayLength());
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in json.EnumerateArray())
        {
            RequireProperties(rule, "ruleIndex", "cedarPolicyId", "sourceText", "cedarBody");
            int index = rule.GetProperty("ruleIndex").GetInt32();
            string id = ReadText(rule, "cedarPolicyId");
            string source = ReadText(rule, "sourceText");
            string body = ReadText(rule, "cedarBody");
            if (index != builder.Count || id.Length == 0 || !ids.Add(id) ||
                source.Length == 0 || body.Length == 0)
            {
                throw new JsonException("Invalid exported rule mapping.");
            }

            builder.Add(new CedarPolicyRule(index, id, source, body));
        }

        return builder.MoveToImmutable();
    }

    private static ImmutableArray<string> ReadGeneratedContextFields(JsonElement json)
    {
        var builder = ImmutableArray.CreateBuilder<string>(json.GetProperty("generatedContextFields").GetArrayLength());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in json.GetProperty("generatedContextFields").EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Invalid generated context field kind.");
            }

            string name = field.GetString() ?? throw new JsonException("Missing generated context field.");
            if (name.Length == 0 || !seen.Add(name))
            {
                throw new JsonException("Invalid generated context field.");
            }

            builder.Add(name);
        }

        return builder.MoveToImmutable();
    }

    internal static PolicyParseResult ReadParseResult(JsonElement json, int sourceBytes)
    {
        try
        {
            RequireProperties(json, "success", "policyCount", "diagnostics");
            var success = json.GetProperty("success").GetBoolean();
            var count = json.GetProperty("policyCount");
            var findings = json.GetProperty("diagnostics");
            int size = findings.GetArrayLength();
            if (success)
            {
                int policyCount = count.GetInt32();
                if (size != 0 || policyCount < 0)
                {
                    throw new JsonException("Invalid successful parse result.");
                }

                return new(policyCount, []);
            }

            if (count.ValueKind != JsonValueKind.Null || size == 0)
            {
                throw new JsonException("Invalid failed parse result.");
            }

            var diagnostics = ImmutableArray.CreateBuilder<PolicyDiagnostic>(size);
            foreach (var diagnostic in findings.EnumerateArray())
            {
                RequireProperties(diagnostic, "category", "message", "location");
                var category = diagnostic.GetProperty("category").GetString() switch
                {
                    "syntax" => PolicyDiagnosticCategory.Syntax,
                    "macro" => PolicyDiagnosticCategory.Macro,
                    _ => throw new JsonException("Invalid diagnostic category.")
                };
                var message = diagnostic.GetProperty("message").GetString() ?? throw new JsonException("Missing diagnostic message.");
                var location = diagnostic.GetProperty("location");
                PolicySourceLocation? span = null;
                if (location.ValueKind != JsonValueKind.Null)
                {
                    RequireProperties(location, "offset", "length");
                    int offset = location.GetProperty("offset").GetInt32();
                    int length = location.GetProperty("length").GetInt32();
                    if (offset < 0 || offset > sourceBytes || length < 0 || (length > sourceBytes - offset && !(offset == sourceBytes && length == 1)))
                    {
                        throw new JsonException("Diagnostic location is outside the source.");
                    }

                    span = new PolicySourceLocation(offset, length);
                }

                diagnostics.Add(new PolicyDiagnostic(category, message, span));
            }

            return new(null, diagnostics.MoveToImmutable());
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw Malformed(exception);
        }
    }

    private static void RequireProperties(JsonElement json, params string[] names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in json.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            {
                throw new JsonException("Unexpected or duplicate JSON property.");
            }
        }

        if (seen.Count != names.Length)
        {
            throw new JsonException("Missing required JSON property.");
        }
    }

    private JsonDocument Invoke(uint operation, ReadOnlySpan<byte> input, int maximumResponseBytes)
    {
        Buffer output = default;
        fixed (byte* bytes = input)
        {
            try
            {
                uint status = call(Abi, operation, bytes, (uint)input.Length, &output);
                if (status != 0)
                {
                    throw new DogwoodBridgeException(DogwoodBridgeFailure.NativeCall,
                        $"Native operation {operation} failed with ABI status {status}.", nativeStatus: status);
                }

                if (output.Data == null || output.Length == 0 || output.Length > (uint)maximumResponseBytes)
                {
                    throw Malformed();
                }

                var copy = new ReadOnlySpan<byte>(output.Data, (int)output.Length).ToArray();
                _ = Dogwood.StrictUtf8.GetCharCount(copy);
                return JsonDocument.Parse(copy);
            }
            catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
            {
                throw Malformed(exception);
            }
            finally
            {
                free(&output);
            }
        }
    }

    private static DogwoodBridgeException Identity(string message, Exception? inner = null) =>
        new(DogwoodBridgeFailure.LoadOrIdentity, message, inner);

    private static DogwoodBridgeException Malformed(Exception? inner = null) =>
        new(DogwoodBridgeFailure.MalformedResponse, "The native bridge returned a malformed response.", inner);
}
