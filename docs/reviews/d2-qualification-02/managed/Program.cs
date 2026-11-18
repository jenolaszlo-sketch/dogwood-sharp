using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using DogwoodSharp;

const string RaceSource = "permit(principal, action, resource);";
var empty = new DogwoodSchema("");
switch (args[0])
{
    case "real":
        RaceFirstAccess();
        using (var fixtures = JsonDocument.Parse(File.ReadAllBytes(args[1])))
        {
            int count = 0;
            foreach (var row in fixtures.RootElement.EnumerateArray())
            {
                var source = row.GetProperty("source").GetString()!;
                var schema = new DogwoodSchema(row.GetProperty("policySchema").GetString()!);
                var reply = row.GetProperty("reply");
                var validated = Dogwood.Validate(source, schema);
                var result = Dogwood.Lower(source, schema);
                Require(validated.IsValid && result.IsLowered, "valid schema export");
                Require(result.CedarSchemaJson == reply.GetProperty("cedarSchemaJson").GetString() &&
                    result.CedarSchemaText == reply.GetProperty("cedarSchemaText").GetString() &&
                    result.CedarPolicyText == reply.GetProperty("cedarPolicyText").GetString(), "native export bytes");
                var expectedRules = reply.GetProperty("rules");
                Require(result.Rules.Length == expectedRules.GetArrayLength(), "rule count");
                for (int index = 0; index < result.Rules.Length; index++)
                {
                    var rule = result.Rules[index];
                    Require(rule.RuleIndex == index &&
                        rule.CedarPolicyId == expectedRules[index].GetProperty("cedarPolicyId").GetString() &&
                        rule.SourceText == expectedRules[index].GetProperty("sourceText").GetString(), "rule mapping");
                }

                var expectedWarnings = reply.GetProperty("validationWarnings");
                foreach (var actual in new[] { validated, result.Validation })
                {
                    Require(actual.ValidationWarnings.Length == expectedWarnings.GetArrayLength(), "warning count");
                    for (int index = 0; index < actual.ValidationWarnings.Length; index++)
                    {
                        var warning = actual.ValidationWarnings[index];
                        Require(warning.Category == PolicyValidationCategory.Cedar && warning.Location is null &&
                            warning.Message == expectedWarnings[index].GetProperty("message").GetString(), "warning projection");
                    }
                }

                count++;
            }

            #if Q2_JIT
            CheckContext(true);
            CheckContext(false);
            #endif
            Parallel.For(0, 128, _ =>
            {
                Require(Dogwood.Parse(RaceSource).Success, "concurrent D1");
                Require(Dogwood.Validate("", empty).IsValid, "concurrent validation");
                Require(Dogwood.Lower("", empty).ArtifactsExported, "concurrent export");
            });
            if (args.Length == 3)
            {
                Require(ImageIsMapped(args[2]), "verified image remains mapped");
                Console.WriteLine("LOADER OBSERVATION image-mapped=yes");
            }
            Console.WriteLine($"REAL PASS first-contenders=8 repeat=128 schema-cases={count}");
        }
        break;
    case "faults":
        RaceFirstAccess();
        using (var fixtures = JsonDocument.Parse(File.ReadAllBytes(args[1])))
        {
            int accepted = 0;
            int rejected = 0;
            foreach (var row in fixtures.RootElement.EnumerateArray())
            {
                string id = row.GetProperty("id").GetString()!;
                string expected = row.GetProperty("expect").GetString()!;
                if (expected == "ok")
                {
                    Require(Dogwood.Lower(id, empty).ArtifactsExported, id);
                    accepted++;
                }
                else
                {
                    var failure = expected == "native" ? DogwoodBridgeFailure.NativeCall : DogwoodBridgeFailure.MalformedResponse;
                    var exception = Expect(() => Dogwood.Lower(id, empty), failure, id);
                    Require(exception.NativeStatus == (expected == "native" ? 8u : null), id);
                    rejected++;
                }
            }

            Console.WriteLine($"FAULTS PASS accepted={accepted} rejected={rejected}");
        }
        break;
    case "r1":
        var original = Dogwood.Lower("q1-original", empty);
        Require(original.ArtifactsExported && original.CedarSchemaText == "" &&
            original.CedarSchemaJson != "{}", "R1 original acceptance");
        Console.WriteLine("R1 REPRO QD2-01 accepted an incomplete export");
        break;
    case "bad-identity":
        string image = args[1];
        Require(!ImageIsMapped(image), "image initially absent");
        var first = Expect(() => Dogwood.GetNativeVersion(), DogwoodBridgeFailure.LoadOrIdentity, "invalid identity");
        Require(!ImageIsMapped(image), "failed initialization released temporary reference");
        File.Copy(args[2], args[3], overwrite: true);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var next = Expect(() => Dogwood.GetNativeVersion(), DogwoodBridgeFailure.LoadOrIdentity, "cached failure");
            Require(ReferenceEquals(first, next), "cached exception");
        }
        Require(!ImageIsMapped(image), "image remains absent");
        Console.WriteLine("FAILED INIT PASS attempts=9 abi=1 image-mapped=no");
        break;
    default:
        throw new ArgumentException("Unknown probe mode.");
}

static void RaceFirstAccess()
{
    const int contenders = 8;
    var errors = new Exception?[contenders];
    var versions = new DogwoodNativeVersion?[contenders];
    using var barrier = new Barrier(contenders);
    var workers = new Thread[contenders];
    for (int index = 0; index < contenders; index++)
    {
        int slot = index;
        workers[slot] = new Thread(() =>
        {
            try
            {
                barrier.SignalAndWait();
                versions[slot] = Dogwood.GetNativeVersion();
            }
            catch (Exception error)
            {
                errors[slot] = error;
            }
        });
        workers[slot].Start();
    }

    foreach (var worker in workers)
    {
        worker.Join();
    }

    Require(errors.All(error => error is null), string.Join(", ", errors.Select(e => e?.Message)));
    Require(versions[0]?.AbiVersion == 1 && versions.All(version => ReferenceEquals(version, versions[0])),
        "single version after concurrent first access");
}

#if Q2_JIT
static void CheckContext(bool collectible)
{
    var current = Dogwood.GetNativeVersion();
    var context = new AssemblyLoadContext("Independent private plugin", isCollectible: collectible);
    try
    {
        var copy = context.LoadFromAssemblyPath(typeof(Dogwood).Assembly.Location);
        Require(AssemblyLoadContext.GetLoadContext(copy) == context, "private assembly identity");
        var discover = copy.GetType(typeof(Dogwood).FullName!)!.GetMethod(nameof(Dogwood.GetNativeVersion))!;
        try
        {
            discover.Invoke(null, null);
            throw new InvalidOperationException("Q2_GUARD_CONTROL private context initialized");
        }
        catch (TargetInvocationException invocation)
        {
            var exception = invocation.InnerException!;
            Require(exception.GetType().FullName == typeof(DogwoodBridgeException).FullName &&
                exception.GetType().GetProperty(nameof(DogwoodBridgeException.Failure))!.GetValue(exception)?.ToString() ==
                    nameof(DogwoodBridgeFailure.LoadOrIdentity), "private context rejected");
        }
    }
    finally
    {
        if (collectible)
        {
            context.Unload();
        }
    }

    Require(ReferenceEquals(current, Dogwood.GetNativeVersion()) && Dogwood.Parse("").Success, "default context survives");
    Console.WriteLine($"CUSTOM CONTEXT PASS collectible={collectible}");
}
#endif

static DogwoodBridgeException Expect(Action perform, DogwoodBridgeFailure failure, string label)
{
    try
    {
        perform();
    }
    catch (DogwoodBridgeException exception) when (exception.Failure == failure)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {failure} for {label}");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static bool ImageIsMapped(string path)
{
    if (OperatingSystem.IsWindows())
    {
        return Loader.WindowsHandle(Path.GetFileName(path)) != 0;
    }
    // RTLD_NOW | RTLD_NOLOAD. Balance the temporary observation if mapped.
    nint handle = OperatingSystem.IsLinux() ? Loader.LinuxOpen(path, 6) : Loader.MacOpen(path, 18);
    if (handle == 0)
    {
        return false;
    }

    int status = OperatingSystem.IsLinux() ? Loader.LinuxClose(handle) : Loader.MacClose(handle);
    Require(status == 0, "balance loader observation");
    return true;
}

internal static class Loader
{
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern nint WindowsHandle(string file);

    [DllImport("libdl.so.2", EntryPoint = "dlopen", ExactSpelling = true)]
    internal static extern nint LinuxOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libdl.so.2", EntryPoint = "dlclose", ExactSpelling = true)]
    internal static extern int LinuxClose(nint handle);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlopen", ExactSpelling = true)]
    internal static extern nint MacOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlclose", ExactSpelling = true)]
    internal static extern int MacClose(nint handle);
}
