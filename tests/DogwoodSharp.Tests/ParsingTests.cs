using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace DogwoodSharp.Tests;

public sealed class ParsingTests
{
    [Fact]
    public void SharedFixtureManifestMatchesTheLoadedNativeBridge()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "parsing-v1.json")));
        var manifest = document.RootElement;
        var version = Dogwood.GetNativeVersion();
        Assert.Equal(1, manifest.GetProperty("fixtureVersion").GetInt32());
        Assert.Equal(1u, version.AbiVersion);
        Assert.Equal("0.1.0", version.BridgeVersion);
        Assert.Equal("1.1.2", version.DogwoodVersion);
        Assert.Equal("4.11.0", version.CedarVersion);
        Assert.Equal("1.99.1", version.RustVersion);
        Assert.Equal(NativeBridge.Platform().Target, version.Target);
        Assert.Equal(manifest.GetProperty("dogwoodRevision").GetString(), version.DogwoodRevision);

        foreach (var fixture in manifest.GetProperty("cases").EnumerateArray())
        {
            var id = fixture.GetProperty("id").GetString();
            var result = Dogwood.Parse(fixture.GetProperty("source").GetString()!);
            if (fixture.TryGetProperty("policyCount", out var count))
            {
                Assert.True(result.Success, id);
                Assert.Equal(count.GetInt32(), result.PolicyCount);
                Assert.Empty(result.Diagnostics);
            }
            else
            {
                Assert.False(result.Success, id);
                Assert.Null(result.PolicyCount);
                var category = fixture.GetProperty("category").GetString() == "syntax"
                    ? PolicyDiagnosticCategory.Syntax
                    : PolicyDiagnosticCategory.Macro;
                var expected = fixture.GetProperty("locations").EnumerateArray()
                    .Select(location => new PolicySourceLocation(location.GetProperty("offset").GetInt32(),
                        location.GetProperty("length").GetInt32()))
                    .ToArray();
                Assert.Equal(expected.Length, result.Diagnostics.Length);
                Assert.All(result.Diagnostics, diagnostic =>
                {
                    Assert.Equal(category, diagnostic.Category);
                    Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message), id);
                });
                Assert.Equal(expected.Select(location => (PolicySourceLocation?)location),
                    result.Diagnostics.Select(diagnostic => diagnostic.Location));
            }
        }
    }

    [Fact]
    public void ManagedArgumentChecksRunBeforeLoadingOrCallingNative()
    {
        Assert.Throws<ArgumentNullException>(() => Dogwood.Parse(null!));
        Assert.Throws<ArgumentException>(() => Dogwood.Parse("\ud800"));
        Assert.Throws<ArgumentException>(() => Dogwood.Parse(new string('é', Dogwood.MaximumSourceBytes / 2 + 1)));
        Assert.Equal(0, Dogwood.Parse(new string(' ', Dogwood.MaximumSourceBytes)).PolicyCount);
    }

    [Fact]
    public void RepeatedConcurrentCallsRetainIndependentSnapshots()
    {
        var initial = Dogwood.Parse("permit(");
        Parallel.For(0, 128, index =>
        {
            var result = Dogwood.Parse(index % 2 == 0 ? "permit(principal, action, resource);" : "forbid(");
            Assert.Equal(index % 2 == 0, result.Success);
            Assert.Equal(result.Success ? 0 : 1, result.Diagnostics.Length);
        });
        Assert.Equal(new PolicySourceLocation(7, 1), Assert.Single(initial.Diagnostics).Location);
    }

    [Theory]
    [InlineData("""{"success":true,"policyCount":null,"diagnostics":[]}""")]
    [InlineData("""{"success":false,"policyCount":0,"diagnostics":[]}""")]
    [InlineData("""{"success":false,"policyCount":null,"diagnostics":[]}""")]
    [InlineData("""{"success":true,"success":true,"policyCount":0,"diagnostics":[]}""")]
    [InlineData("""{"success":false,"policyCount":null,"diagnostics":[{"category":"syntax","message":"x","location":{"offset":8,"length":1}}]}""")]
    public void InvalidResponseContractsAreReportedSeparately(string response)
    {
        using var document = JsonDocument.Parse(response);
        var error = Assert.Throws<DogwoodBridgeException>(() => NativeBridge.ReadParseResult(document.RootElement, 7));
        Assert.Equal(DogwoodBridgeFailure.MalformedResponse, error.Failure);
        Assert.Null(error.NativeStatus);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("manifest-identity")]
    [InlineData("hash")]
    [InlineData("library-abi")]
    public void InvalidNativeAssetsAreRejected(string failure)
    {
        var root = Directory.CreateTempSubdirectory("dogwoodsharp-d1-");
        try
        {
            var (rid, _, file) = NativeBridge.Platform();
            var relative = Path.Combine("runtimes", rid, "native");
            var source = Path.Combine(AppContext.BaseDirectory, relative);
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, relative)).FullName;
            const string manifestFile = "dogwoodsharp-native.json";
            if (failure != "missing")
            {
                var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(source, manifestFile)))!;
                var image = Path.Combine(destination, file);
                if (failure == "library-abi")
                {
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "test-assets", file), image);
                    using var content = File.OpenRead(image);
                    manifest["sha256"] = Convert.ToHexString(SHA256.HashData(content));
                }
                else
                {
                    File.Copy(Path.Combine(source, file), image);
                    if (failure == "hash")
                    {
                        using var tampered = File.OpenWrite(image);
                        tampered.WriteByte(0);
                    }
                    else
                    {
                        manifest["dogwoodRevision"] = new string('0', 40);
                    }
                }

                File.WriteAllText(Path.Combine(destination, manifestFile), manifest.ToJsonString());
            }

            var load = typeof(NativeBridge).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)!;
            var invocation = Assert.Throws<TargetInvocationException>(() => load.Invoke(null, [root.FullName]));
            var error = Assert.IsType<DogwoodBridgeException>(invocation.InnerException);
            Assert.Equal(DogwoodBridgeFailure.LoadOrIdentity, error.Failure);
            if (failure == "library-abi")
            {
                Assert.Contains("ABI version mismatch", error.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void FlattenedNuGetNativeLayoutResolvesTheAdjacentAsset()
    {
        var root = Directory.CreateTempSubdirectory("dogwoodsharp-flat-");
        try
        {
            var (rid, _, file) = NativeBridge.Platform();
            var source = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native");
            File.Copy(Path.Combine(source, file), Path.Combine(root.FullName, file));
            File.Copy(
                Path.Combine(source, "dogwoodsharp-native.json"),
                Path.Combine(root.FullName, "dogwoodsharp-native.json"));

            var resolved = NativeBridge.ResolveNativePath(root.FullName, rid, file);
            Assert.Equal(Path.Combine(root.FullName, file), resolved, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void InvalidNestedNativeAssetDoesNotFallBackToAnAdjacentCopy()
    {
        var root = Directory.CreateTempSubdirectory("dogwoodsharp-no-fallback-");
        try
        {
            var (rid, _, file) = NativeBridge.Platform();
            var source = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native");
            var nested = Directory.CreateDirectory(Path.Combine(root.FullName, "runtimes", rid, "native"));
            File.Copy(Path.Combine(source, file), Path.Combine(nested.FullName, file));
            using (var tampered = File.OpenWrite(Path.Combine(nested.FullName, file)))
            {
                tampered.WriteByte(0);
            }

            File.Copy(Path.Combine(source, file), Path.Combine(root.FullName, file));
            File.Copy(
                Path.Combine(source, "dogwoodsharp-native.json"),
                Path.Combine(root.FullName, "dogwoodsharp-native.json"));
            File.Copy(
                Path.Combine(source, "dogwoodsharp-native.json"),
                Path.Combine(nested.FullName, "dogwoodsharp-native.json"));

            var load = typeof(NativeBridge).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)!;
            var invocation = Assert.Throws<TargetInvocationException>(() => load.Invoke(null, [root.FullName]));
            var error = Assert.IsType<DogwoodBridgeException>(invocation.InnerException);
            Assert.Equal(DogwoodBridgeFailure.LoadOrIdentity, error.Failure);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void RepeatedConcurrentAccessReusesTheProcessBridge()
    {
        var native = NativeBridge.Shared;
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(NativeBridge)));
        Parallel.For(0, 256, index =>
        {
            Assert.Same(native, NativeBridge.Shared);
            Assert.Same(native.Version, Dogwood.GetNativeVersion());
            Assert.True(Dogwood.Parse("").Success);
            Assert.True(Dogwood.Validate("", new DogwoodSchema("")).IsValid);
            Assert.True(Dogwood.Lower("", new DogwoodSchema("")).ArtifactsExported);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APrivateAssemblyLoadContextCannotInitializeAnotherBridge(bool collectible)
    {
        var context = new AssemblyLoadContext("DogwoodSharp private copy", isCollectible: collectible);
        try
        {
            var copy = context.LoadFromAssemblyPath(typeof(Dogwood).Assembly.Location);
            Assert.NotSame(typeof(Dogwood).Assembly, copy);
            var discover = copy.GetType(typeof(Dogwood).FullName!)!.GetMethod(nameof(Dogwood.GetNativeVersion))!;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var invocation = Assert.Throws<TargetInvocationException>(() => discover.Invoke(null, null));
                var error = invocation.InnerException!;
                Assert.Equal(typeof(DogwoodBridgeException).FullName, error.GetType().FullName);
                Assert.Equal(nameof(DogwoodBridgeFailure.LoadOrIdentity),
                    error.GetType().GetProperty(nameof(DogwoodBridgeException.Failure))!.GetValue(error)!.ToString());
                Assert.Contains("default assembly load context", error.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (collectible)
            {
                context.Unload();
            }
        }
    }
}
