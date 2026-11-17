using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DogwoodSharp;

namespace D1Qualification;

internal static class Program
{
    private const string DogwoodRevision = "929f49e7305534ac61ef06cf8e47269c596dc851";
    private static readonly string[] CedarFeatures = ["datetime", "decimal", "ipaddr"];

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--child")
        {
            try
            {
                Child(args[1]);
                Console.WriteLine($"CHILD {args[1]} PASS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        try
        {
            return Driver(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    private static int Driver(string[] args)
    {
        var options = Options.Parse(args);
        var (rid, target, file) = Platform();
        var cargoLockHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(options.CargoLock)))
            .ToLowerInvariant();
        var root = Path.Combine(options.Work, options.Framework);
        var results = new List<ScenarioResult>();
        var scenarios = BuildScenarios();

        foreach (var scenario in scenarios)
        {
            var directory = Path.Combine(root, scenario.Name);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            Directory.CreateDirectory(directory);
            CopyPayload(directory);
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["D1Q_TARGET"] = target,
            };

            switch (scenario.Kind)
            {
                case ScenarioKind.Real:
                    StageReal(directory, options.RealAssets, rid);
                    break;
                case ScenarioKind.NoAssets:
                    break;
                case ScenarioKind.CacheFailure:
                    environment["D1Q_REAL_ASSETS"] = options.RealAssets;
                    break;
                default:
                    StageFake(scenario, directory, rid, target, file, cargoLockHash,
                        options.FakeBridge, options.FakeMinimal, environment);
                    break;
            }

            var run = RunChild(options.Dotnet, directory, scenario.Name, environment);
            string? detail = run.ExitCode == 0 ? null : Truncate(run.StandardError + run.StandardOutput);
            if (detail is null && !run.StandardOutput.Contains($"CHILD {scenario.Name} PASS", StringComparison.Ordinal))
            {
                detail = "child did not report its pass marker";
            }

            if (detail is null && scenario.RequireMark)
            {
                detail = CheckMark(directory, scenario);
            }

            results.Add(new ScenarioResult(scenario.Name, detail is null, run.ExitCode, detail));
            Console.WriteLine($"{(detail is null ? "PASS" : "FAIL")} {scenario.Name}");
        }

        var failures = results.Where(result => !result.Pass).ToList();
        var report = new
        {
            framework = options.Framework,
            target,
            scenarioCount = results.Count,
            passed = results.Count - failures.Count,
            failed = failures.Count,
            results,
        };
        Directory.CreateDirectory(options.Report);
        var reportPath = Path.Combine(options.Report, "managed-report.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"MANAGED scenarios={results.Count} passed={results.Count - failures.Count} failed={failures.Count}");
        Console.WriteLine(failures.Count == 0 ? "RESULT OK" : "RESULT FAILED");
        foreach (var failure in failures)
        {
            Console.WriteLine($"FAILED {failure.Name}: {failure.Detail}");
        }

        return failures.Count == 0 ? 0 : 1;
    }

    private sealed record Options(
        string Dotnet, string RealAssets, string FakeBridge, string FakeMinimal,
        string CargoLock, string Report, string Work, string Framework)
    {
        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index += 2)
            {
                if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                {
                    throw new ArgumentException("Arguments must be --name value pairs.");
                }

                values[args[index][2..]] = args[index + 1];
            }

            string Get(string name) => values.TryGetValue(name, out var value)
                ? value
                : throw new ArgumentException($"Missing --{name}.");

            return new Options(
                Get("dotnet"), Path.GetFullPath(Get("real-assets")),
                Path.GetFullPath(Get("fake-bridge")), Path.GetFullPath(Get("fake-minimal")),
                Path.GetFullPath(Get("cargo-lock")), Path.GetFullPath(Get("report")),
                Path.GetFullPath(Get("work")), Get("framework"));
        }
    }

    private enum ScenarioKind
    {
        Real,
        NoAssets,
        CacheFailure,
        FakeParse,
        FakeVersion,
        ManifestVariant,
        ImageVariant,
        ParseStatus,
        StatusVersion,
    }

    private sealed record Scenario(string Name, ScenarioKind Kind, string Variant, bool RequireMark);

    private sealed record ScenarioResult(string Name, bool Pass, int ExitCode, string? Detail);

    private sealed record ChildRun(int ExitCode, string StandardOutput, string StandardError);

    private static List<Scenario> BuildScenarios()
    {
        var scenarios = new List<Scenario>
        {
            new("positive", ScenarioKind.Real, string.Empty, RequireMark: false),
            new("no-assets", ScenarioKind.NoAssets, string.Empty, RequireMark: false),
            new("cache-failure", ScenarioKind.CacheFailure, string.Empty, RequireMark: false),
        };

        string[] versionVariants =
        [
            "ok", "extra", "duplicate", "missing", "wrong-abi", "abi-string", "wrong-target",
            "features-order", "features-extra", "features-null", "dogwood-features", "wrong-bridge",
            "wrong-revision", "wrong-dogwood", "wrong-cedar", "wrong-rust", "empty-object",
            "invalid-json", "top-array", "invalid-utf8", "oversize", "null-data", "zero-length",
        ];
        foreach (var variant in versionVariants)
        {
            scenarios.Add(new Scenario($"fake-version-{variant}", ScenarioKind.FakeVersion, variant, RequireMark: true));
        }

        string[] parseVariants =
        [
            "success", "success-zero", "failure", "failure-null-location", "failure-eof",
            "success-extra-field", "success-missing-count", "success-count-null",
            "success-negative-count", "success-count-string", "success-count-overflow",
            "success-with-diagnostics", "failure-count-zero", "failure-empty-diagnostics",
            "failure-unknown-category", "failure-location-string", "failure-location-negative",
            "failure-location-past-eof", "failure-location-length-past-eof",
            "failure-location-eof-length-two", "failure-location-extra", "failure-location-missing",
            "failure-message-null", "failure-category-number", "failure-extra-field",
            "failure-diagnostic-extra", "failure-missing-location", "duplicate-property",
            "invalid-json", "top-array", "top-null", "empty", "invalid-utf8", "oversize",
            "null-data", "zero-length",
        ];
        foreach (var variant in parseVariants)
        {
            scenarios.Add(new Scenario($"fake-parse-{variant}", ScenarioKind.FakeParse, variant, RequireMark: true));
        }

        foreach (var status in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "255" })
        {
            scenarios.Add(new Scenario($"parse-status-{status}", ScenarioKind.ParseStatus, status, RequireMark: true));
            scenarios.Add(new Scenario($"status-version-{status}", ScenarioKind.StatusVersion, status, RequireMark: true));
        }

        string[] manifestVariants =
        [
            "missing", "empty", "oversize", "invalid-json", "invalid-utf8", "missing-field",
            "extra-field", "duplicate-field", "version", "rid", "file", "target", "abi", "bridge",
            "revision", "dogwood", "cedar", "rust", "dogwood-features", "cedar-order",
            "cedar-extra", "sha-length", "sha-hex", "cargo-length", "cargo-hex", "missing-image",
            "hash-mismatch", "uppercase-hash", "trailing-whitespace",
        ];
        foreach (var variant in manifestVariants)
        {
            scenarios.Add(new Scenario($"manifest-{variant}", ScenarioKind.ManifestVariant, variant, RequireMark: false));
        }

        foreach (var variant in new[] { "truncated", "wrong-abi", "missing-export" })
        {
            scenarios.Add(new Scenario($"image-{variant}", ScenarioKind.ImageVariant, variant, RequireMark: false));
        }

        return scenarios;
    }

    private static (string Rid, string Target, string File) Platform()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 when OperatingSystem.IsWindows() =>
                ("win-x64", "x86_64-pc-windows-msvc", "dogwoodsharp_bridge.dll"),
            Architecture.X64 when OperatingSystem.IsLinux() =>
                ("linux-x64", "x86_64-unknown-linux-gnu", "libdogwoodsharp_bridge.so"),
            Architecture.Arm64 when OperatingSystem.IsMacOS() =>
                ("osx-arm64", "aarch64-apple-darwin", "libdogwoodsharp_bridge.dylib"),
            _ => throw new PlatformNotSupportedException("No D1 qualification asset for this process."),
        };
    }

    private static void CopyPayload(string destination)
    {
        foreach (var source in Directory.GetFiles(AppContext.BaseDirectory))
        {
            File.Copy(source, Path.Combine(destination, Path.GetFileName(source)), overwrite: true);
        }
    }

    private static void StageReal(string directory, string realAssets, string rid)
    {
        var source = Path.Combine(realAssets, "runtimes", rid, "native");
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Real asset directory not found: {source}");
        }

        CopyDirectory(source, Path.Combine(directory, "runtimes", rid, "native"));
    }

    private static void StageFake(
        Scenario scenario, string directory, string rid, string target, string file,
        string cargoLockHash, string fakeBridge, string fakeMinimal,
        Dictionary<string, string> environment)
    {
        var nativeDirectory = Path.Combine(directory, "runtimes", rid, "native");
        Directory.CreateDirectory(nativeDirectory);
        var imagePath = Path.Combine(nativeDirectory, file);
        var manifestPath = Path.Combine(nativeDirectory, "dogwoodsharp-native.json");
        var imageBytes = File.ReadAllBytes(fakeBridge);
        var writeImage = true;
        var writeManifest = true;

        environment["D1_FAKE_TARGET"] = target;
        environment["D1_FAKE_MARK"] = Path.Combine(directory, "mark.txt");

        switch (scenario.Kind)
        {
            case ScenarioKind.FakeParse:
                environment["D1_FAKE_VERSION"] = "ok";
                environment["D1_FAKE_PARSE"] = scenario.Variant;
                break;
            case ScenarioKind.FakeVersion:
                environment["D1_FAKE_VERSION"] = scenario.Variant;
                break;
            case ScenarioKind.ParseStatus:
                environment["D1_FAKE_VERSION"] = "ok";
                environment["D1_FAKE_PARSE_STATUS"] = scenario.Variant;
                break;
            case ScenarioKind.StatusVersion:
                environment["D1_FAKE_STATUS"] = scenario.Variant;
                break;
            case ScenarioKind.ManifestVariant:
                environment["D1_FAKE_VERSION"] = "ok";
                break;
            case ScenarioKind.ImageVariant:
                environment["D1_FAKE_VERSION"] = "ok";
                break;
            default:
                throw new InvalidOperationException($"Unexpected scenario kind {scenario.Kind}.");
        }

        if (scenario.Kind == ScenarioKind.ImageVariant)
        {
            switch (scenario.Variant)
            {
                case "truncated":
                    imageBytes = imageBytes[..16];
                    break;
                case "wrong-abi":
                    environment["D1_FAKE_ABI"] = "2";
                    break;
                case "missing-export":
                    imageBytes = File.ReadAllBytes(fakeMinimal);
                    break;
            }
        }

        if (scenario.Kind == ScenarioKind.ManifestVariant)
        {
            switch (scenario.Variant)
            {
                case "missing":
                    writeManifest = false;
                    break;
                case "missing-image":
                    writeImage = false;
                    break;
            }
        }

        if (writeImage)
        {
            File.WriteAllBytes(imagePath, imageBytes);
        }

        if (!writeManifest)
        {
            return;
        }

        if (scenario.Kind == ScenarioKind.ManifestVariant)
        {
            switch (scenario.Variant)
            {
                case "empty":
                    File.WriteAllBytes(manifestPath, []);
                    return;
                case "invalid-json":
                    File.WriteAllText(manifestPath, "{", new UTF8Encoding(false));
                    return;
                case "invalid-utf8":
                    File.WriteAllBytes(manifestPath, [(byte)'{', 0xff, (byte)'}']);
                    return;
                case "oversize":
                    {
                        var bytes = Encoding.UTF8.GetBytes(SerializeManifest(
                            CorrectManifest(rid, target, file, imageBytes, cargoLockHash)));
                        File.WriteAllBytes(manifestPath, PadTo(bytes, 4097));
                        return;
                    }
                case "trailing-whitespace":
                    {
                        var text = SerializeManifest(CorrectManifest(rid, target, file, imageBytes, cargoLockHash));
                        File.WriteAllText(manifestPath, text + "\n\t  ", new UTF8Encoding(false));
                        return;
                    }
                case "duplicate-field":
                    {
                        var text = SerializeManifest(CorrectManifest(rid, target, file, imageBytes, cargoLockHash));
                        File.WriteAllText(manifestPath, text.Insert(1, $"\"rid\":\"{rid}\","), new UTF8Encoding(false));
                        return;
                    }
            }
        }

        var manifest = CorrectManifest(rid, target, file, imageBytes, cargoLockHash);
        if (scenario.Kind == ScenarioKind.ManifestVariant)
        {
            MutateManifest(manifest, scenario.Variant);
        }

        File.WriteAllText(manifestPath, SerializeManifest(manifest), new UTF8Encoding(false));

        if (scenario.Kind == ScenarioKind.ManifestVariant && scenario.Variant == "hash-mismatch")
        {
            using var stream = new FileStream(imagePath, FileMode.Append, FileAccess.Write);
            stream.WriteByte(0);
        }
    }

    private static Dictionary<string, object?> CorrectManifest(
        string rid, string target, string file, byte[] image, string cargoLockHash)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["manifestVersion"] = 1,
            ["rid"] = rid,
            ["file"] = file,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant(),
            ["cargoLockSha256"] = cargoLockHash,
            ["abiVersion"] = 1,
            ["bridgeVersion"] = "0.1.0",
            ["dogwoodRevision"] = DogwoodRevision,
            ["dogwoodVersion"] = "1.1.2",
            ["cedarVersion"] = "4.11.0",
            ["rustVersion"] = "1.99.1",
            ["target"] = target,
            ["dogwoodFeatures"] = Array.Empty<string>(),
            ["cedarFeatures"] = CedarFeatures,
        };
    }

    private static void MutateManifest(Dictionary<string, object?> manifest, string variant)
    {
        switch (variant)
        {
            case "missing-field":
                manifest.Remove("rustVersion");
                break;
            case "extra-field":
                manifest["extra"] = 1;
                break;
            case "version":
                manifest["manifestVersion"] = 2;
                break;
            case "rid":
                manifest["rid"] = "other-rid";
                break;
            case "file":
                manifest["file"] = "other-image";
                break;
            case "target":
                manifest["target"] = "other-target";
                break;
            case "abi":
                manifest["abiVersion"] = 2;
                break;
            case "bridge":
                manifest["bridgeVersion"] = "9.9.9";
                break;
            case "revision":
                manifest["dogwoodRevision"] = new string('0', 40);
                break;
            case "dogwood":
                manifest["dogwoodVersion"] = "0.0.0";
                break;
            case "cedar":
                manifest["cedarVersion"] = "0.0.0";
                break;
            case "rust":
                manifest["rustVersion"] = "0.0.0";
                break;
            case "dogwood-features":
                manifest["dogwoodFeatures"] = new[] { "extra" };
                break;
            case "cedar-order":
                manifest["cedarFeatures"] = new[] { "ipaddr", "decimal", "datetime" };
                break;
            case "cedar-extra":
                manifest["cedarFeatures"] = new[] { "datetime", "decimal", "ipaddr", "extra" };
                break;
            case "sha-length":
                manifest["sha256"] = new string('0', 63);
                break;
            case "sha-hex":
                manifest["sha256"] = new string('g', 64);
                break;
            case "cargo-length":
                manifest["cargoLockSha256"] = new string('0', 63);
                break;
            case "cargo-hex":
                manifest["cargoLockSha256"] = new string('g', 64);
                break;
            case "uppercase-hash":
                manifest["sha256"] = ((string)manifest["sha256"]!).ToUpperInvariant();
                manifest["cargoLockSha256"] = ((string)manifest["cargoLockSha256"]!).ToUpperInvariant();
                break;
            case "hash-mismatch":
            case "missing-image":
                break;
            default:
                throw new InvalidOperationException($"Unknown manifest variant {variant}.");
        }
    }

    private static byte[] PadTo(byte[] bytes, int length)
    {
        if (bytes.Length >= length)
        {
            return bytes;
        }

        var padded = new byte[length];
        bytes.CopyTo(padded, 0);
        padded.AsSpan(bytes.Length).Fill((byte)' ');
        return padded;
    }

    private static string SerializeManifest(Dictionary<string, object?> manifest) =>
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = false });

    private static ChildRun RunChild(
        string dotnet, string directory, string scenario, Dictionary<string, string> environment)
    {
        var entryName = $"{typeof(Program).Assembly.GetName().Name}.dll";
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(directory, entryName));
        start.ArgumentList.Add("--child");
        start.ArgumentList.Add(scenario);
        foreach (var pair in environment)
        {
            start.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start child.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ChildRun(process.ExitCode, standardOutput, standardError);
    }

    private static string? CheckMark(string directory, Scenario scenario)
    {
        var mark = Path.Combine(directory, "mark.txt");
        if (!File.Exists(mark))
        {
            return "fake native library was not called";
        }

        var lines = File.ReadAllLines(mark);
        var calls = lines.Count(line => line.StartsWith("C:", StringComparison.Ordinal));
        var frees = lines.Count(line => line == "F");
        if (calls == 0 || calls != frees)
        {
            return $"fake call/free imbalance: calls={calls}, frees={frees}";
        }

        if (scenario.Kind is ScenarioKind.FakeParse or ScenarioKind.ParseStatus &&
            !lines.Contains("C:1", StringComparer.Ordinal))
        {
            return "parse operation was not called";
        }

        return null;
    }

    private static void Child(string scenario)
    {
        switch (scenario)
        {
            case "positive":
                Positive(aot: false);
                return;
            case "aot-smoke":
                Positive(aot: true);
                return;
            case "no-assets":
                NoAssets();
                return;
            case "cache-failure":
                CacheFailure();
                return;
        }

        if (scenario.StartsWith("fake-parse-", StringComparison.Ordinal))
        {
            FakeParse(scenario["fake-parse-".Length..]);
            return;
        }

        if (scenario.StartsWith("fake-version-", StringComparison.Ordinal))
        {
            FakeVersion(scenario["fake-version-".Length..]);
            return;
        }

        if (scenario.StartsWith("parse-status-", StringComparison.Ordinal))
        {
            ParseStatus(scenario["parse-status-".Length..]);
            return;
        }

        if (scenario.StartsWith("status-version-", StringComparison.Ordinal))
        {
            ExpectBridge(DogwoodBridgeFailure.LoadOrIdentity, null, () => _ = Dogwood.GetNativeVersion());
            return;
        }

        if (scenario.StartsWith("manifest-", StringComparison.Ordinal))
        {
            var variant = scenario["manifest-".Length..];
            if (variant is "uppercase-hash" or "trailing-whitespace")
            {
                VersionOk();
            }
            else
            {
                ExpectBridge(DogwoodBridgeFailure.LoadOrIdentity, null, () => _ = Dogwood.GetNativeVersion());
            }

            return;
        }

        if (scenario.StartsWith("image-", StringComparison.Ordinal))
        {
            ExpectBridge(DogwoodBridgeFailure.LoadOrIdentity, null, () => _ = Dogwood.GetNativeVersion());
            return;
        }

        throw new InvalidOperationException($"Unknown child scenario {scenario}.");
    }

    private static void Positive(bool aot)
    {
        VersionOk();
        Ensure(Dogwood.MaximumSourceBytes == 1024 * 1024, "MaximumSourceBytes changed.");

        var permit = Dogwood.Parse("permit(principal, action, resource);");
        Ensure(permit.Success && permit.PolicyCount == 1 && permit.Diagnostics.IsEmpty,
            "simple permit did not produce one policy.");

        var empty = Dogwood.Parse(string.Empty);
        Ensure(empty.Success && empty.PolicyCount == 0 && empty.Diagnostics.IsEmpty,
            "empty policy set did not parse as zero policies.");

        var comment = Dogwood.Parse("// comment only\n");
        Ensure(comment.Success && comment.PolicyCount == 0, "comment-only policy set did not parse.");

        var two = Dogwood.Parse(
            "permit(principal, action, resource);\nforbid(principal, action, resource);");
        Ensure(two.Success && two.PolicyCount == 2, "permit+forbid did not produce two policies.");

        ExpectFailure("permit(", 7, 1);
        ExpectFailure("\uFEFFpermit(", 10, 1);
        ExpectFailure("// é😀\npermit(", 17, 1);
        ExpectFailure("allow(principal, action, resource);", 0, 5);

        Expect<ArgumentNullException>(() => Dogwood.Parse(null!));
        Expect<ArgumentException>(() => Dogwood.Parse("\ud800"));
        Expect<ArgumentException>(() => Dogwood.Parse("\udc00"));
        Expect<ArgumentException>(() => Dogwood.Parse("a\ud800b"));

        var maximumComment = "//" + new string('a', Dogwood.MaximumSourceBytes - 2);
        Ensure(Encoding.UTF8.GetByteCount(maximumComment) == Dogwood.MaximumSourceBytes,
            "maximum source construction is not exactly the limit.");
        var maximumResult = Dogwood.Parse(maximumComment);
        Ensure(maximumResult.Success && maximumResult.PolicyCount == 0,
            "maximum-size comment policy set did not parse.");
        Expect<ArgumentException>(() => Dogwood.Parse(maximumComment + "a"));
        Ensure(!Dogwood.Parse(new string('a', Dogwood.MaximumSourceBytes)).Success,
            "oversized-identifier source unexpectedly parsed.");

        var multibyte = "//" + new string('\u00e9', (Dogwood.MaximumSourceBytes - 2) / 2);
        Ensure(Encoding.UTF8.GetByteCount(multibyte) == Dogwood.MaximumSourceBytes,
            "multibyte maximum source construction is not exactly the limit.");
        Ensure(Dogwood.Parse(multibyte).Success, "multibyte maximum-size comment did not parse.");
        Expect<ArgumentException>(() => Dogwood.Parse(multibyte + "\u00e9"));
        Expect<ArgumentException>(() => Dogwood.Parse(new string('\u00e9', Dogwood.MaximumSourceBytes / 2 + 1)));

        var failed = Dogwood.Parse("permit(");
        Ensure(!failed.Diagnostics.IsDefault, "diagnostics array is default.");
        Expect<NotSupportedException>(() =>
            ((ICollection<PolicyDiagnostic>)failed.Diagnostics).Add(failed.Diagnostics[0]));
        Ensure(typeof(PolicyParseResult).IsSealed && typeof(PolicyDiagnostic).IsSealed &&
            typeof(DogwoodNativeVersion).IsSealed && typeof(DogwoodBridgeException).IsSealed &&
            typeof(PolicySourceLocation).IsValueType,
            "A public result type is not an immutable sealed snapshot.");

        if (!aot)
        {
            var errors = new ConcurrentQueue<Exception>();
            Parallel.For(0, 800, index =>
            {
                try
                {
                    switch (index % 4)
                    {
                        case 0:
                            var parsed = Dogwood.Parse("permit(principal, action, resource);");
                            Ensure(parsed.Success && parsed.PolicyCount == 1, "concurrent success result changed.");
                            break;
                        case 1:
                            var bad = Dogwood.Parse("permit(");
                            Ensure(!bad.Success && bad.Diagnostics.Length > 0 &&
                                bad.Diagnostics[0].Location == new PolicySourceLocation(7, 1),
                                "concurrent failure result changed.");
                            break;
                        case 2:
                            var version = Dogwood.GetNativeVersion();
                            Ensure(version.AbiVersion == 1, "concurrent version result changed.");
                            break;
                        default:
                            var zero = Dogwood.Parse(string.Empty);
                            Ensure(zero.Success && zero.PolicyCount == 0, "concurrent empty result changed.");
                            break;
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
            });
            Ensure(errors.IsEmpty, $"concurrent calls failed: {errors.Count}");
        }
    }

    private static void ExpectFailure(string source, int offset, int length)
    {
        var result = Dogwood.Parse(source);
        Ensure(!result.Success && result.PolicyCount is null, $"source did not fail: {source}");
        Ensure(result.Diagnostics.Length > 0, $"failure has no diagnostics: {source}");
        var diagnostic = result.Diagnostics[0];
        Ensure(diagnostic.Category == PolicyDiagnosticCategory.Syntax,
            $"failure category is {diagnostic.Category}: {source}");
        Ensure(diagnostic.Location == new PolicySourceLocation(offset, length),
            $"failure location is {diagnostic.Location}: {source}");
    }

    private static void NoAssets()
    {
        Expect<ArgumentNullException>(() => Dogwood.Parse(null!));
        Expect<ArgumentException>(() => Dogwood.Parse("\ud800"));
        ExpectBridge(DogwoodBridgeFailure.LoadOrIdentity, null,
            () => _ = Dogwood.Parse("permit(principal, action, resource);"));
        ExpectBridge(DogwoodBridgeFailure.LoadOrIdentity, null, () => _ = Dogwood.GetNativeVersion());
    }

    private static void CacheFailure()
    {
        ExpectBridge(DogwoodBridgeFailure.LoadOrIdentity, null,
            () => _ = Dogwood.Parse("permit(principal, action, resource);"));

        var source = Environment.GetEnvironmentVariable("D1Q_REAL_ASSETS")
            ?? throw new InvalidOperationException("D1Q_REAL_ASSETS is missing.");
        CopyDirectory(Path.Combine(source, "runtimes"), Path.Combine(AppContext.BaseDirectory, "runtimes"));

        ExpectBridge(DogwoodBridgeFailure.LoadOrIdentity, null,
            () => _ = Dogwood.Parse("permit(principal, action, resource);"));
        ExpectBridge(DogwoodBridgeFailure.LoadOrIdentity, null, () => _ = Dogwood.GetNativeVersion());
    }

    private static void FakeVersion(string variant)
    {
        if (variant == "ok")
        {
            VersionOk();
            return;
        }

        ExpectBridge(DogwoodBridgeFailure.LoadOrIdentity, null, () => _ = Dogwood.GetNativeVersion());
    }

    private static void FakeParse(string variant)
    {
        VersionOk();
        var source = variant == "failure-eof"
            ? "permit("
            : "permit(principal, action, resource);";

        switch (variant)
        {
            case "success":
                {
                    var result = Dogwood.Parse(source);
                    Ensure(result.Success && result.PolicyCount == 1 && result.Diagnostics.IsEmpty,
                        "fake success response was not accepted.");
                    return;
                }
            case "success-zero":
                {
                    var result = Dogwood.Parse(source);
                    Ensure(result.Success && result.PolicyCount == 0 && result.Diagnostics.IsEmpty,
                        "fake zero-policy response was not accepted.");
                    return;
                }
            case "failure":
                {
                    var result = Dogwood.Parse(source);
                    Ensure(!result.Success && result.PolicyCount is null && result.Diagnostics.Length == 1,
                        "fake failure response was not accepted.");
                    Ensure(result.Diagnostics[0].Category == PolicyDiagnosticCategory.Syntax &&
                        result.Diagnostics[0].Location == new PolicySourceLocation(0, 5) &&
                        result.Diagnostics[0].Message == "fake failure",
                        "fake failure diagnostic was not preserved.");
                    return;
                }
            case "failure-null-location":
                {
                    var result = Dogwood.Parse(source);
                    Ensure(!result.Success && result.Diagnostics.Length == 1 &&
                        result.Diagnostics[0].Location is null,
                        "fake null-location failure was not accepted.");
                    return;
                }
            case "failure-eof":
                {
                    var result = Dogwood.Parse(source);
                    Ensure(!result.Success && result.Diagnostics.Length == 1 &&
                        result.Diagnostics[0].Location == new PolicySourceLocation(7, 1),
                        "fake EOF failure was not accepted.");
                    return;
                }
            default:
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    ExpectBridge(DogwoodBridgeFailure.MalformedResponse, null, () => _ = Dogwood.Parse(source));
                }

                return;
        }
    }

    private static void ParseStatus(string status)
    {
        VersionOk();
        var expected = uint.Parse(status, System.Globalization.CultureInfo.InvariantCulture);
        ExpectBridge(DogwoodBridgeFailure.NativeCall, expected,
            () => _ = Dogwood.Parse("permit(principal, action, resource);"));
    }

    private static void VersionOk()
    {
        var target = Environment.GetEnvironmentVariable("D1Q_TARGET")
            ?? throw new InvalidOperationException("D1Q_TARGET is missing.");
        var version = Dogwood.GetNativeVersion();
        var expected = new DogwoodNativeVersion(
            1, "0.1.0", DogwoodRevision, "1.1.2", "4.11.0", "1.99.1", target);
        Ensure(version == expected, $"native version mismatch: {version}");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(T).Name}, got {exception.GetType().Name}.", exception);
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}, but no exception was thrown.");
    }

    private static void ExpectBridge(DogwoodBridgeFailure failure, uint? status, Action action)
    {
        try
        {
            action();
        }
        catch (DogwoodBridgeException exception)
        {
            Ensure(exception.Failure == failure,
                $"expected bridge failure {failure}, got {exception.Failure}: {exception.Message}");
            Ensure(exception.NativeStatus == status,
                $"expected native status {status?.ToString() ?? "null"}, got " +
                $"{exception.NativeStatus?.ToString() ?? "null"}.");
            return;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Expected DogwoodBridgeException, got {exception.GetType().Name}.", exception);
        }

        throw new InvalidOperationException("Expected DogwoodBridgeException, but no exception was thrown.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 4000 ? value : value[^4000..];

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
