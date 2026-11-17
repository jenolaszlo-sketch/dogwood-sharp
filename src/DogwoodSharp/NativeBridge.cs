using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DogwoodSharp;

internal sealed unsafe class NativeBridge : IDisposable
{
    private const uint Abi = 1;
    private const string BridgeVersion = "0.1.0";
    private const string DogwoodRevision = "929f49e7305534ac61ef06cf8e47269c596dc851";
    private const string DogwoodVersion = "1.1.2";
    private const string CedarVersion = "4.11.0";
    private const string RustVersion = "1.99.1";
    private const int MaximumResponseBytes = 2 * Dogwood.MaximumSourceBytes;
    private const int MaximumManifestBytes = 4096;

    private static readonly Lazy<NativeBridge> Instance = new(() => Load(AppContext.BaseDirectory));
    private readonly delegate* unmanaged[Cdecl]<uint, uint, byte*, uint, Buffer*, uint> call;
    private readonly delegate* unmanaged[Cdecl]<Buffer*, void> free;
    private nint library;

    [StructLayout(LayoutKind.Sequential)]
    private struct Buffer
    {
        public byte* Data;
        public uint Length;
    }

    private NativeBridge(nint handle, string target)
    {
        library = handle;
        var abi = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(handle, "dogwoodsharp_abi_version");
        if (abi() != Abi)
        {
            throw Identity("Native ABI version mismatch.");
        }

        call = (delegate* unmanaged[Cdecl]<uint, uint, byte*, uint, Buffer*, uint>)NativeLibrary.GetExport(handle, "dogwoodsharp_call_v1");
        free = (delegate* unmanaged[Cdecl]<Buffer*, void>)NativeLibrary.GetExport(handle, "dogwoodsharp_free_v1");
        try
        {
            using var reply = Invoke(0, "{}"u8);
            Version = ReadIdentity(reply.RootElement, target, manifest: false);
        }
        catch (Exception exception) when (exception is DogwoodBridgeException or JsonException or InvalidOperationException or FormatException)
        {
            throw Identity("Native version discovery failed.", exception);
        }
    }

    internal static NativeBridge Shared => Instance.Value;

    internal DogwoodNativeVersion Version { get; }

    internal static NativeBridge Load(string root)
    {
        var (rid, target, file) = Platform();
        var path = Path.GetFullPath(Path.Combine(root, "runtimes", rid, "native", file));
        nint handle = 0;
        try
        {
            VerifyManifest(path, rid, target);
            handle = NativeLibrary.Load(path, typeof(NativeBridge).Assembly, DllImportSearchPath.System32);
            var bridge = new NativeBridge(handle, target);
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

        using var reply = Invoke(1, input.WrittenSpan);
        return ReadParseResult(reply.RootElement, sourceBytes);
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

    private JsonDocument Invoke(uint operation, ReadOnlySpan<byte> input)
    {
        ObjectDisposedException.ThrowIf(library == 0, this);
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

                if (output.Data == null || output.Length is 0 or > MaximumResponseBytes)
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

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref library, 0);
        if (handle != 0)
        {
            NativeLibrary.Free(handle);
        }
    }

    private static DogwoodBridgeException Identity(string message, Exception? inner = null) =>
        new(DogwoodBridgeFailure.LoadOrIdentity, message, inner);

    private static DogwoodBridgeException Malformed(Exception? inner = null) =>
        new(DogwoodBridgeFailure.MalformedResponse, "The native bridge returned a malformed response.", inner);
}
