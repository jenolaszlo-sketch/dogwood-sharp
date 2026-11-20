// DOGWOOD-SHARP-D3-E1-Q1 NativeAOT smoke consumer (reviewer evidence tooling).
//
// Exercises the D2 Dogwood bridge (Cedar 4.11) and the CedarSharp native
// engine (Cedar 4.13) inside one NativeAOT process and writes a hand-built
// JSON result (no reflection, AOT-safe). The Dogwood bridge asset must be
// staged at <app>\runtimes\win-x64\native before running; a run without it
// must fail closed with DogwoodBridgeException (the aot.ps1 negative control).
//
// Usage: AotProbe <results.json>

using CedarSharp;
using DogwoodSharp;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: AotProbe <results.json>");
    return 2;
}

var outputPath = args[0];
var notes = new List<(string Key, string Value)>();
var ok = true;

static string Safe(string value) =>
    value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");

void Note(string key, string value) => notes.Add((key, Safe(value)));

bool Check(string name, bool condition)
{
    if (!condition)
    {
        ok = false;
    }

    Note($"check_{name}", condition ? "pass" : "FAIL");
    return condition;
}

try
{
    var dogwood = Dogwood.GetNativeVersion();
    Note("bridge_version", dogwood.BridgeVersion);
    Note("bridge_cedar", dogwood.CedarVersion);
    Note("bridge_revision", dogwood.DogwoodRevision);
    Check("bridge_cedar_4_11_0", dogwood.CedarVersion == "4.11.0");
    Check("bridge_revision_929f49e", dogwood.DogwoodRevision.StartsWith("929f49e", StringComparison.Ordinal));
}
catch (Exception error)
{
    ok = false;
    Note("bridge_error", $"{error.GetType().Name}: {error.Message}");
}

try
{
    var cedar = new CedarEngine().GetVersion();
    Note("cedarsharp_sdk", cedar.SdkVersion);
    Note("cedarsharp_language", cedar.LanguageVersion);
    Note("cedarsharp_sha256", cedar.Sha256);
    Check("cedarsharp_sdk_4_13_0", cedar.SdkVersion == "4.13.0");
    Check("cedarsharp_sha256_matches_manifest",
        cedar.Sha256 == "77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895");
}
catch (Exception error)
{
    ok = false;
    Note("cedarsharp_error", $"{error.GetType().Name}: {error.Message}");
}

try
{
    const string schemaText =
        """
        namespace App {
          entity User;
          entity Doc;
          action read appliesTo {
            principal: [User],
            resource: [Doc],
            context: { count: Long },
          };
        }
        """;

    var lowered = Dogwood.Lower(
        """permit(principal, action == App::Action::"read", resource) when { context.count > 1 };""",
        new DogwoodSchema(schemaText, Distincter: "aotq1"));
    Check("lowered", lowered.ArtifactsExported);
    if (lowered.ArtifactsExported)
    {
        var engine = new CedarEngine();
        var set = CedarPolicySet.FromPolicies((lowered.Rules[0].CedarPolicyId, lowered.CedarPolicyText));
        var schema = CedarSchema.FromJson(lowered.CedarSchemaJson);
        var allowed = engine.Authorize(new CedarAuthorizationRequest(
            new CedarEntityUid("App::User", "alice"),
            new CedarEntityUid("App::Action", "read"),
            new CedarEntityUid("App::Doc", "d1"),
            set, """{"count":5}""", "[]", schema, true));
        var denied = engine.Authorize(new CedarAuthorizationRequest(
            new CedarEntityUid("App::User", "alice"),
            new CedarEntityUid("App::Action", "read"),
            new CedarEntityUid("App::Doc", "d1"),
            set, """{"count":1}""", "[]", schema, true));
        Note("allow_decision", allowed.Decision?.ToString() ?? "null");
        Note("allow_determining", string.Join(",", allowed.DeterminingPolicies));
        Note("deny_decision", denied.Decision?.ToString() ?? "null");
        Note("exported_rule_id", lowered.Rules[0].CedarPolicyId);
        Check("allow", allowed.Decision == CedarDecision.Allow && allowed.IsErrorFree);
        Check("deny", denied.Decision == CedarDecision.Deny && denied.IsErrorFree);
        Check("determining_is_exported_id",
            allowed.DeterminingPolicies.SequenceEqual((IEnumerable<string>)[lowered.Rules[0].CedarPolicyId]));
    }
}
catch (Exception error)
{
    ok = false;
    Note("flow_error", $"{error.GetType().Name}: {error.Message}");
}

Note("dotnet", Environment.Version.ToString());
Note("outcome", ok ? "ok" : "failed");

var body = string.Join("\n", notes.Select(note => $"  \"{note.Key}\": \"{note.Value}\""));
File.WriteAllText(outputPath, "{\n" + body + "\n}\n");
Console.WriteLine($"AotProbe outcome={(ok ? "ok" : "failed")} -> {outputPath}");
return ok ? 0 : 1;
