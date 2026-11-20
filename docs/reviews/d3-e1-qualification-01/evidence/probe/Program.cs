// DOGWOOD-SHARP-D3-E1-Q1 independent reviewer probe harness (evidence tooling).
//
// Expectations below are hand-derived before execution from (a) the authored
// Cedar/Dogwood fixtures in this file and (b) source reading of the pinned
// upstream Dogwood authorizer (three-tier entity store, fail-closed pipeline).
// The Cedar 4.11 FFI reference binary is supporting evidence only: agreement
// with it does not prove equivalence with the upstream Dogwood authorizer.
//
// Usage: dotnet Probe.dll <results.json> [path\to\dogwoodsharp-e1-ref.exe]
//
// The Dogwood bridge asset must be staged beside the app first:
//   <app>\runtimes\win-x64\native\dogwoodsharp_bridge.dll (+ manifest),
// mirroring the D2 native asset discovery contract.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CedarSharp;
using DogwoodSharp;

var outputPath = args.Length > 0 ? args[0] : "q1-probe-results.json";
var referenceExe = args.Length > 1 ? args[1] : null;

var results = new List<Dictionary<string, object?>>();
var failures = 0;

void Record(string id, string name, string expectation, bool pass, Dictionary<string, object?> observed, string? note = null)
{
    var entry = new Dictionary<string, object?>
    {
        ["id"] = id,
        ["name"] = name,
        ["expectation"] = expectation,
        ["verdict"] = pass ? "pass" : "FAIL",
        ["observed"] = observed,
    };
    if (note is not null)
    {
        entry["note"] = note;
    }

    results.Add(entry);
    if (!pass)
    {
        failures++;
    }

    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {id} {name}");
}

// ---------------------------------------------------------------- fixtures.

const string CountSchema =
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

const string GroupSchema =
    """
    namespace App {
      entity User in Group { name?: String };
      entity Group;
      entity Doc { owner: User, secret?: Bool };
      action read appliesTo {
        principal: [User],
        resource: [Doc],
      };
    }
    """;

const string ActionHierarchySchema =
    """
    namespace App {
      entity User;
      entity Doc;
      action ReadGroup;
      action read2 in ReadGroup appliesTo {
        principal: [User],
        resource: [Doc],
      };
    }
    """;

const string SessionSchema =
    """
    namespace App {
      entity Session { token: String };
      entity Doc;
      action read appliesTo {
        principal: [Session],
        resource: [Doc],
      };
    }
    """;

const string ExtensionSchema =
    """
    namespace App {
      entity User;
      entity Doc;
      action read appliesTo {
        principal: [User],
        resource: [Doc],
        context: { ip: ipaddr, amount: decimal, at: datetime },
      };
    }
    """;

const string TemporalSchema =
    """
    namespace Test {
      entity Item;
      entity User;
      action Approve appliesTo {
        principal: [User],
        resource: [Item],
        context: { input: { key: String } },
      };
      action Read appliesTo {
        principal: [User],
        resource: [Item],
        context: { input: { key: String } },
      };
    }
    """;

// A generated-field collision control: the authored Read context declares the
// exact attribute name the "q1t" distincter generates for a temporal policy.
const string CollisionSchema =
    """
    namespace Test {
      entity Item;
      entity User;
      action Approve appliesTo {
        principal: [User],
        resource: [Item],
        context: { input: { key: String } },
      };
      action Read appliesTo {
        principal: [User],
        resource: [Item],
        context: { input: { key: String }, q1t_0__temporal_0: Bool },
      };
    }
    """;

const string TemporalSource =
    """permit(principal, action == Test::Action::"Read", resource) when temporal { formerly within 10s Test::Action::"Approve"::response{ input.key: context.input.key } };""";

const string EmptyEntities = "[]";
const string OwnerAlice =
    """[{"uid":{"type":"App::User","id":"alice"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"alice"}}},"parents":[]}]""";
const string OwnerBob =
    """[{"uid":{"type":"App::User","id":"alice"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"bob"}}},"parents":[]}]""";
const string Groups =
    """[{"uid":{"type":"App::User","id":"alice"},"attrs":{},"parents":[{"type":"App::Group","id":"admins"}]},{"uid":{"type":"App::Group","id":"admins"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"alice"}}},"parents":[]}]""";
const string NoGroups =
    """[{"uid":{"type":"App::User","id":"alice"},"attrs":{},"parents":[]},{"uid":{"type":"App::Group","id":"admins"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"alice"}}},"parents":[]}]""";
const string SessionWithoutToken =
    """[{"uid":{"type":"App::Session","id":"s1"},"attrs":{},"parents":[]}]""";
const string SessionWithToken =
    """[{"uid":{"type":"App::Session","id":"s1"},"attrs":{"token":"t"},"parents":[]}]""";
// Hand-written action-hierarchy entities matching ActionHierarchySchema, for
// the schema-less reconstruction path (upstream merges these explicitly).
const string ActionHierarchyEntities =
    """[{"uid":{"type":"App::Action","id":"ReadGroup"},"attrs":{},"parents":[]},{"uid":{"type":"App::Action","id":"read2"},"attrs":{},"parents":[{"type":"App::Action","id":"ReadGroup"}]}]""";

static CedarEntityUid Uid(string type, string id) => new(type, id);
static CedarSchema Schema(string text) => CedarSchema.FromText(text);

static Dictionary<string, object?> Summarize(CedarAuthorizationResult result) => new()
{
    ["success"] = result.IsSuccess,
    ["decision"] = result.Decision?.ToString(),
    ["determining"] = result.DeterminingPolicies.ToList(),
    ["policyErrors"] = result.PolicyErrors.Select(e => new Dictionary<string, object?>
    {
        ["policyId"] = e.PolicyId,
        ["message"] = e.Error.Message,
    }).ToList(),
    ["errors"] = result.Errors.Select(e => e.Message).ToList(),
    ["warnings"] = result.Warnings.Select(w => w.Message).ToList(),
    ["isCleanAllow"] = result.IsCleanAllow,
    ["isCleanDeny"] = result.IsCleanDeny,
    ["isErrorFree"] = result.IsErrorFree,
};

static Dictionary<string, object?> NormalizeSharp(CedarAuthorizationResult result) => new()
{
    ["failed"] = !result.IsSuccess,
    ["decision"] = result.Decision?.ToString()?.ToLowerInvariant(),
    ["determining"] = result.DeterminingPolicies.OrderBy(id => id, StringComparer.Ordinal).Cast<string?>().ToList(),
    ["policyErrorIds"] = result.PolicyErrors.Select(e => (string?)e.PolicyId).OrderBy(id => id, StringComparer.Ordinal).ToList(),
    ["policyErrorMessages"] = result.PolicyErrors.Select(e => e.Error.Message).ToList(),
    ["errors"] = result.Errors.Select(e => e.Message).ToList(),
};

static (bool Ok, string Output, string Error) RunRef(string exe, uint operation, string input)
{
    var path = Path.Combine(Path.GetTempPath(), $"q1-ref-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, input);
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(operation.ToString());
        psi.ArgumentList.Add(path);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("The reference binary could not be started.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode == 0, stdout.Trim(), stderr.Trim());
    }
    finally
    {
        File.Delete(path);
    }
}

static Dictionary<string, object?> NormalizeRef(bool ok, string output, string error)
{
    if (!ok)
    {
        return new Dictionary<string, object?> { ["failed"] = true, ["decision"] = null, ["error"] = error };
    }

    using var document = JsonDocument.Parse(output);
    var root = document.RootElement;
    var type = root.GetProperty("type").GetString();
    if (type == "success")
    {
        var response = root.GetProperty("response");
        var diagnostics = response.GetProperty("diagnostics");
        return new Dictionary<string, object?>
        {
            ["failed"] = false,
            ["decision"] = response.GetProperty("decision").GetString(),
            ["determining"] = diagnostics.GetProperty("reason").EnumerateArray()
                .Select(e => e.GetString()).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            ["policyErrorIds"] = diagnostics.GetProperty("errors").EnumerateArray()
                .Select(e => e.GetProperty("policyId").GetString()).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            ["policyErrorMessages"] = diagnostics.GetProperty("errors").EnumerateArray()
                .Select(e => e.GetProperty("error").GetProperty("message").GetString()).ToList(),
        };
    }

    return new Dictionary<string, object?>
    {
        ["failed"] = true,
        ["decision"] = null,
        ["determining"] = new List<string?>(),
        ["policyErrorIds"] = new List<string?>(),
        ["error"] = string.Join(" | ", root.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("message").GetString())),
    };
}

static (bool Same, string Detail) SameAnswer(Dictionary<string, object?> sharp, Dictionary<string, object?> reference)
{
    var failedSharp = sharp["failed"] is true;
    var failedRef = reference["failed"] is true;
    if (failedSharp != failedRef)
    {
        return (false, $"failed-ness differs: sharp={failedSharp} ref={failedRef}");
    }

    if (!failedSharp)
    {
        var decisionSharp = sharp["decision"] as string;
        var decisionRef = reference["decision"] as string;
        if (!string.Equals(decisionSharp, decisionRef, StringComparison.Ordinal))
        {
            return (false, $"decision differs: sharp={decisionSharp} ref={decisionRef}");
        }
    }

    var determiningSharp = (IEnumerable<string?>?)sharp["determining"] ?? [];
    var determiningRef = (IEnumerable<string?>?)reference["determining"] ?? [];
    if (!determiningSharp.SequenceEqual(determiningRef))
    {
        return (false, $"determining differs: sharp=[{string.Join(",", determiningSharp)}] ref=[{string.Join(",", determiningRef)}]");
    }

    var errorsSharp = (IEnumerable<string?>?)sharp["policyErrorIds"] ?? [];
    var errorsRef = (IEnumerable<string?>?)reference["policyErrorIds"] ?? [];
    if (!errorsSharp.SequenceEqual(errorsRef))
    {
        return (false, $"policy-error ids differ: sharp=[{string.Join(",", errorsSharp)}] ref=[{string.Join(",", errorsRef)}]");
    }

    return (true, "identical (decision, determining set, policy-error id set, failed-ness)");
}

static string J(string value) => JsonSerializer.Serialize(value);

static string RefAuthorization(
    IReadOnlyDictionary<string, string> policies,
    (string Type, string Id) principal,
    (string Type, string Id) action,
    (string Type, string Id) resource,
    string contextJson,
    string entitiesJson,
    string? schemaJson,
    bool validateRequest)
{
    var builder = new StringBuilder();
    builder.Append("{\"principal\":{\"type\":").Append(J(principal.Type)).Append(",\"id\":").Append(J(principal.Id)).Append('}');
    builder.Append(",\"action\":{\"type\":").Append(J(action.Type)).Append(",\"id\":").Append(J(action.Id)).Append('}');
    builder.Append(",\"resource\":{\"type\":").Append(J(resource.Type)).Append(",\"id\":").Append(J(resource.Id)).Append('}');
    builder.Append(",\"policies\":{\"staticPolicies\":{");
    var first = true;
    foreach (var pair in policies)
    {
        if (!first)
        {
            builder.Append(',');
        }

        first = false;
        builder.Append(J(pair.Key)).Append(':').Append(J(pair.Value));
    }

    builder.Append("}}");
    builder.Append(",\"context\":").Append(contextJson);
    builder.Append(",\"entities\":").Append(entitiesJson);
    builder.Append(",\"schema\":").Append(schemaJson ?? "null");
    builder.Append(",\"validateRequest\":").Append(validateRequest ? "true" : "false");
    builder.Append('}');
    return builder.ToString();
}

// Naive exported-text splitter: safe for fixture sources whose policy bodies
// contain no ';' inside strings or annotations (all fixtures here qualify).
// This demonstrates that ID-to-body pairing is recoverable from the export;
// it is not a proposed production algorithm.
static string[] SplitPolicies(string text) =>
    text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(chunk => chunk + ";")
        .ToArray();

// ------------------------------------------------------------- environment.

var meta = new Dictionary<string, object?>
{
    ["generatedAtUtc"] = DateTime.UtcNow.ToString("o"),
    ["dotNet"] = Environment.Version.ToString(),
    ["framework"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    ["rid"] = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
    ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
};

var engine = new CedarEngine();
try
{
    var version = engine.GetVersion();
    meta["cedarSharp"] = new Dictionary<string, object?>
    {
        ["abiVersion"] = version.AbiVersion,
        ["sdkVersion"] = version.SdkVersion,
        ["languageVersion"] = version.LanguageVersion,
        ["bridgeVersion"] = version.BridgeVersion,
        ["rustVersion"] = version.RustVersion,
        ["target"] = version.Target,
        ["features"] = version.Features.ToList(),
        ["nativePath"] = version.NativePath,
        ["sha256"] = version.Sha256,
    };
}
catch (Exception error)
{
    meta["cedarSharpError"] = error.Message;
}

try
{
    var dogwood = Dogwood.GetNativeVersion();
    meta["dogwoodBridge"] = new Dictionary<string, object?>
    {
        ["abiVersion"] = dogwood.AbiVersion,
        ["bridgeVersion"] = dogwood.BridgeVersion,
        ["dogwoodRevision"] = dogwood.DogwoodRevision,
        ["dogwoodVersion"] = dogwood.DogwoodVersion,
        ["cedarVersion"] = dogwood.CedarVersion,
        ["rustVersion"] = dogwood.RustVersion,
        ["target"] = dogwood.Target,
    };
}
catch (Exception error)
{
    meta["dogwoodBridgeError"] = error.Message;
}

string? referenceSdk = null;
if (referenceExe is not null)
{
    try
    {
        var (ok, output, error) = RunRef(referenceExe, 0, "{}");
        if (ok)
        {
            using var document = JsonDocument.Parse(output);
            referenceSdk = document.RootElement.GetProperty("sdkVersion").GetString();
            meta["reference"] = new Dictionary<string, object?>
            {
                ["binary"] = referenceExe,
                ["sdkVersion"] = referenceSdk,
                ["languageVersion"] = document.RootElement.GetProperty("languageVersion").GetString(),
            };
        }
        else
        {
            meta["referenceError"] = error;
        }
    }
    catch (Exception error)
    {
        meta["referenceError"] = error.Message;
    }
}

// ======================================================= Q1-01 identity.

// A1: FromPolicies preserves non-sequential caller-assigned IDs; the
// determining set names the caller ID, not a positional number.
{
    var set = CedarPolicySet.FromPolicies(
        ("z-rule-9", """permit(principal, action == App::Action::"read", resource) when { context.count > 4 };"""),
        ("a-rule-42", """permit(principal, action == App::Action::"read", resource) when { context.count > 6 };"""),
        ("m-rule-0", """permit(principal, action == App::Action::"read", resource) when { context.count > 8 };"""));
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{\"count\":5}", EmptyEntities, Schema(CountSchema), true));
    var pass = result.Decision == CedarDecision.Allow
        && result.DeterminingPolicies is ["z-rule-9"]
        && set.PolicyIds.SequenceEqual((IEnumerable<string>)["z-rule-9", "a-rule-42", "m-rule-0"]);
    Record(
        "Q1-01-A1",
        "FromPolicies preserves non-sequential caller policy IDs",
        "Allow; determining is exactly [z-rule-9] (count>4 matches at 5); PolicyIds keep supplied order",
        pass,
        new Dictionary<string, object?>
        {
            ["policyIds"] = set.PolicyIds.ToList(),
            ["result"] = Summarize(result),
        });
}

// A2: reordered supply changes only ID order, never the decision.
{
    var set = CedarPolicySet.FromPolicies(
        ("m-rule-0", """permit(principal, action == App::Action::"read", resource) when { context.count > 8 };"""),
        ("a-rule-42", """permit(principal, action == App::Action::"read", resource) when { context.count > 6 };"""),
        ("z-rule-9", """permit(principal, action == App::Action::"read", resource) when { context.count > 4 };"""));
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{\"count\":5}", EmptyEntities, Schema(CountSchema), true));
    var pass = result.Decision == CedarDecision.Allow
        && result.DeterminingPolicies is ["z-rule-9"]
        && set.PolicyIds.SequenceEqual((IEnumerable<string>)["m-rule-0", "a-rule-42", "z-rule-9"]);
    Record(
        "Q1-01-A2",
        "Reordered supply: decisions are identical, ID order follows supply",
        "Allow; determining [z-rule-9]; PolicyIds [m-rule-0, a-rule-42, z-rule-9]",
        pass,
        new Dictionary<string, object?>
        {
            ["policyIds"] = set.PolicyIds.ToList(),
            ["result"] = Summarize(result),
        });
}

// A3: FromText renumbers positionally and ignores @id annotations for
// identity; determining policies use the renumbered IDs.
{
    var text = """
        @id("custom-a")
        permit(principal, action == App::Action::"read", resource) when { context.count > 4 };
        @id("custom-b")
        permit(principal, action == App::Action::"read", resource) when { context.count > 6 };
        @id("custom-c")
        permit(principal, action == App::Action::"read", resource) when { context.count > 8 };
        """;
    var set = CedarPolicySet.FromText(text);
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{\"count\":5}", EmptyEntities, Schema(CountSchema), true));
    var pass = result.Decision == CedarDecision.Allow
        && result.DeterminingPolicies is ["policy0"];
    Record(
        "Q1-01-A3",
        "FromText renumbers to policy0..N and ignores @id for identity",
        "Allow; determining [policy0] (not custom-a); the PolicyIds property is empty for text sets (IDs are not materialized for plain-text sets), recorded as adapter evidence",
        pass,
        new Dictionary<string, object?>
        {
            ["policyIds"] = set.PolicyIds.ToList(),
            ["result"] = Summarize(result),
        },
        note: "Determining diagnostics still carry the positional policy0 identity, so a FromText set cannot report caller-meaningful IDs; the D3 adapter must use FromPolicies pairing.");
}

// A4: determining-ID order is not a contract; the set is. Upstream maps
// determining IDs through its rule list, so an adapter must treat them as a
// set (or sort deterministically) and never rely on native order.
{
    var set = CedarPolicySet.FromPolicies(
        ("p0", """permit(principal, action == App::Action::"read", resource) when { context.count > 0 };"""),
        ("p1", """permit(principal, action == App::Action::"read", resource) when { context.count > 2 };"""),
        ("p2", """permit(principal, action == App::Action::"read", resource) when { context.count > 4 };"""));
    var orders = new List<List<string>>();
    var decisions = new List<string?>();
    for (var run = 0; run < 5; run++)
    {
        var result = engine.Authorize(new CedarAuthorizationRequest(
            Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
            set, "{\"count\":5}", EmptyEntities, Schema(CountSchema), true));
        orders.Add(result.DeterminingPolicies.ToList());
        decisions.Add(result.Decision?.ToString());
    }

    var stable = orders.All(order => order.SequenceEqual(orders[0]));
    var setCorrect = orders.All(order =>
        order.Count == 3 && order.All(id => id is "p0" or "p1" or "p2") && order.Distinct().Count() == 3);
    Record(
        "Q1-01-A4",
        "Determining-ID order is unordered; the set is stable",
        "Every run Allow with determining set {p0,p1,p2}; order stability recorded, not required",
        setCorrect && decisions.All(decision => decision == "Allow"),
        new Dictionary<string, object?>
        {
            ["runs"] = orders,
            ["orderStableAcrossRuns"] = stable,
        });
}

// A5: forbid precedence with clean diagnostics.
{
    var set = CedarPolicySet.FromPolicies(
        ("allow-all", """permit(principal, action == App::Action::"read", resource);"""),
        ("deny-all", """forbid(principal, action == App::Action::"read", resource);"""));
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{}", EmptyEntities, Schema(GroupSchema), true));
    var pass = result.Decision == CedarDecision.Deny
        && result.DeterminingPolicies is ["deny-all"]
        && result.IsErrorFree;
    Record(
        "Q1-01-A5",
        "Forbid precedence over a satisfied permit",
        "Deny; determining [deny-all]; no policy errors",
        pass,
        Summarize(result));
}

// A6: policy evaluation errors carry the failing policy's caller-assigned ID
// and fail closed; RequireAllow/EnsureNoErrors reject the result.
{
    var set = CedarPolicySet.FromPolicies(
        ("counting", """permit(principal, action == App::Action::"read", resource) when { context.count > 4 };"""),
        ("broken", """permit(principal, action == App::Action::"read", resource) when { context.missing == 1 };"""));
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{\"count\":5}", EmptyEntities, Schema(CountSchema), true));
    string requireAllow = "did-not-throw";
    string ensureNoErrors = "did-not-throw";
    try
    {
        _ = result.RequireAllow();
    }
    catch (Exception error)
    {
        requireAllow = error.GetType().Name;
    }

    try
    {
        result.EnsureNoErrors();
    }
    catch (Exception error)
    {
        ensureNoErrors = error.GetType().Name;
    }

    var pass = result.Decision == CedarDecision.Allow
        && result.PolicyErrors.Select(e => e.PolicyId).SequenceEqual((IEnumerable<string?>)["broken"])
        && !result.IsErrorFree
        && requireAllow == "CedarAuthorizationException"
        && ensureNoErrors == "CedarAuthorizationException";
    Record(
        "Q1-01-A6",
        "Policy evaluation errors carry the failing policy's caller-assigned ID; enforcement gates reject the result",
        "Allow determined by the good permit (a failing permit does not fire, per-policy error semantics); policy error on [broken]; not error-free; RequireAllow/EnsureNoErrors throw CedarAuthorizationException",
        pass,
        new Dictionary<string, object?>
        {
            ["result"] = Summarize(result),
            ["requireAllow"] = requireAllow,
            ["ensureNoErrors"] = ensureNoErrors,
        });
}

// A7: operation-level rejections fail closed (no Allow can be surfaced).
{
    var good = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource);"""));
    string malformedEntities;
    try
    {
        var result = engine.Authorize(new CedarAuthorizationRequest(
            Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
            good, "{}", "{not json", null, false));
        malformedEntities = $"no-exception: {Summarize(result)["decision"]}";
    }
    catch (Exception error)
    {
        malformedEntities = error.GetType().FullName + ": " + error.Message;
    }

    string duplicateIds;
    try
    {
        _ = CedarPolicySet.FromPolicies(
            ("dup", """permit(principal, action == App::Action::"read", resource);"""),
            ("dup", """forbid(principal, action == App::Action::"read", resource);"""));
        duplicateIds = "no-exception";
    }
    catch (Exception error)
    {
        duplicateIds = error.GetType().FullName + ": " + error.Message;
    }

    Record(
        "Q1-01-A7",
        "Operation failures are rejected before any Allow",
        "Malformed entities JSON throws (no result); duplicate policy IDs are rejected",
        !malformedEntities.StartsWith("no-exception", StringComparison.Ordinal) && !duplicateIds.Contains("no-exception", StringComparison.Ordinal),
        new Dictionary<string, object?>
        {
            ["malformedEntities"] = malformedEntities,
            ["duplicateIds"] = duplicateIds,
        });
}

// A8: a raw Allow can carry policy errors (direct-Cedar semantics). The
// upstream Dogwood pipeline forwards evaluation errors in diagnostics without
// flipping the decision, so the adapter surface must expose both and force an
// explicit product choice (IsCleanAllow / RequireAllow) instead of Decision.
{
    var set = CedarPolicySet.FromPolicies(
        ("good", """permit(principal, action == App::Action::"read", resource) when { context.count > 4 };"""),
        ("broken", """permit(principal, action == App::Action::"read", resource) when { context.missing == 1 };"""));
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{\"count\":5}", EmptyEntities, Schema(CountSchema), true));
    string requireAllow = "did-not-throw";
    try
    {
        _ = result.RequireAllow();
    }
    catch (Exception error)
    {
        requireAllow = error.GetType().Name;
    }

    var pass = result.Decision == CedarDecision.Allow
        && result.PolicyErrors.Select(e => e.PolicyId).SequenceEqual((IEnumerable<string?>)["broken"])
        && !result.IsCleanAllow
        && requireAllow == "CedarAuthorizationException";
    Record(
        "Q1-01-A8",
        "Raw Allow can carry policy errors; clean-Allow is the enforcement gate",
        "Allow with policy error on [broken]; IsCleanAllow false; RequireAllow throws",
        pass,
        new Dictionary<string, object?>
        {
            ["result"] = Summarize(result),
            ["requireAllow"] = requireAllow,
        },
        note: "Direct-Cedar semantics; upstream forwards evaluation errors in diagnostics without flipping the decision (api.rs decide/decide_at).");
}

// =================================================== Q1-02 semantics.

// B1: attribute equality against a present entity owner.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource) when { resource.owner == principal };"""));
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{}", OwnerAlice, Schema(GroupSchema), true));
    Record(
        "Q1-02-B1",
        "Owner attribute matches the principal",
        "Allow; determining [p]; no errors",
        result.Decision == CedarDecision.Allow && result.IsErrorFree && result.DeterminingPolicies is ["p"],
        Summarize(result));
}

// B2: owner mismatch denies silently (no error).
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource) when { resource.owner == principal };"""));
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{}", OwnerBob, Schema(GroupSchema), true));
    Record(
        "Q1-02-B2",
        "Owner mismatch denies without error",
        "Deny; no determining; no policy errors",
        result.Decision == CedarDecision.Deny && result.IsErrorFree && result.DeterminingPolicies.Count == 0,
        Summarize(result));
}

// B3: condition true/false on the same policy.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource) when { context.count > 2 };"""));
    var allowed = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{\"count\":5}", EmptyEntities, Schema(CountSchema), true));
    var denied = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{\"count\":1}", EmptyEntities, Schema(CountSchema), true));
    Record(
        "Q1-02-B3",
        "Condition true allows; false denies cleanly",
        "count=5 -> Allow [p]; count=1 -> Deny, no errors, no determining",
        allowed.Decision == CedarDecision.Allow && allowed.DeterminingPolicies is ["p"]
            && denied.Decision == CedarDecision.Deny && denied.IsErrorFree && denied.DeterminingPolicies.Count == 0,
        new Dictionary<string, object?> { ["count5"] = Summarize(allowed), ["count1"] = Summarize(denied) });
}

// B4: principal hierarchy membership through parent edges, and its absence.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource) when { principal in App::Group::"admins" };"""));
    var matched = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{}", Groups, Schema(GroupSchema), true));
    var missed = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{}", NoGroups, Schema(GroupSchema), true));
    Record(
        "Q1-02-B4",
        "Principal membership: parent edge matches; missing edge denies without error",
        "With edge -> Allow [p]; without edge -> Deny, no errors",
        matched.Decision == CedarDecision.Allow && matched.DeterminingPolicies is ["p"]
            && missed.Decision == CedarDecision.Deny && missed.IsErrorFree,
        new Dictionary<string, object?> { ["withEdge"] = Summarize(matched), ["withoutEdge"] = Summarize(missed) });
}

// B5: action-group membership with the schema attached and no action
// entities in the store (Cedar synthesizes them from the schema).
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action in App::Action::"ReadGroup", resource);"""));
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read2"), Uid("App::Doc", "d1"),
        set, "{}", EmptyEntities, Schema(ActionHierarchySchema), true));
    Record(
        "Q1-02-B5",
        "Action group membership resolves from the schema alone",
        "Allow; determining [p]; no errors; no action entities supplied",
        result.Decision == CedarDecision.Allow && result.IsErrorFree && result.DeterminingPolicies is ["p"],
        Summarize(result));
}

// B6: reading an attribute of an entity referenced but not present in the
// store is an evaluation error that fails closed (Deny with a policy error),
// not a silent deny and never an Allow.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource) when { resource.owner.name == "n" };"""));
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{}", OwnerBob, Schema(GroupSchema), true));
    Record(
        "Q1-02-B6",
        "Missing referenced entity fails closed with a policy error",
        "Deny; policy error on [p] (entity does not exist); never Allow",
        result.Decision == CedarDecision.Deny
            && result.PolicyErrors.Select(e => e.PolicyId).SequenceEqual((IEnumerable<string?>)["p"]),
        Summarize(result));
}

// B7: `has` on a present entity distinguishes absent vs present attributes.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource) when { !(resource has secret) };"""));
    var withoutSecret = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{}", OwnerAlice, Schema(GroupSchema), true));
    var withSecret = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        set, "{}",
        """[{"uid":{"type":"App::User","id":"alice"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"alice"}},"secret":true},"parents":[]}]""",
        Schema(GroupSchema), true));
    Record(
        "Q1-02-B7",
        "has on a present entity: absent attribute allows, present attribute denies, both error-free",
        "no secret -> Allow [p] no errors; secret=true -> Deny no errors",
        withoutSecret.Decision == CedarDecision.Allow && withoutSecret.IsErrorFree
            && withSecret.Decision == CedarDecision.Deny && withSecret.IsErrorFree,
        new Dictionary<string, object?> { ["withoutSecret"] = Summarize(withoutSecret), ["withSecret"] = Summarize(withSecret) });
}

// B8: THE upstream-vs-direct probe. Upstream inserts a bare scope principal
// for uids the caller did not supply, so `!(principal has token)` evaluates
// has=false. Direct Cedar evaluates against the store as supplied; the
// behavior with the scope principal absent is recorded, not assumed.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource) when { !(principal has token) };"""));
    var session = Uid("App::Session", "s1");
    var read = Uid("App::Action", "read");
    var doc = Uid("App::Doc", "d1");

    var absent = engine.Authorize(new CedarAuthorizationRequest(session, read, doc, set, "{}", EmptyEntities, null, false));
    var bare = engine.Authorize(new CedarAuthorizationRequest(session, read, doc, set, "{}", SessionWithoutToken, null, false));
    var withToken = engine.Authorize(new CedarAuthorizationRequest(session, read, doc, set, "{}", SessionWithToken, null, false));
    var schemaAbsent = engine.Authorize(new CedarAuthorizationRequest(session, read, doc, set, "{}", EmptyEntities, Schema(SessionSchema), false));

    var bareOk = bare.Decision == CedarDecision.Allow && bare.IsErrorFree;
    var withTokenOk = withToken.Decision == CedarDecision.Deny && withToken.IsErrorFree;
    Record(
        "Q1-02-B8",
        "Scope-principal `has` probes: absent vs bare vs attributed (schema-less)",
        "bare principal -> Allow no errors (upstream-equivalent); principal with token -> Deny no errors; absent-principal and schema-carrying-absent runs are recorded evidence, not assumed",
        bareOk && withTokenOk,
        new Dictionary<string, object?>
        {
            ["principalAbsent"] = Summarize(absent),
            ["barePrincipal"] = Summarize(bare),
            ["principalWithToken"] = Summarize(withToken),
            ["schemaCarryingPrincipalAbsent"] = Summarize(schemaAbsent),
        },
        note: "Upstream build_entities inserts a bare scope principal for absent uids (api.rs three tiers); direct Cedar with the scope entity absent is engine behavior, recorded as evidence.");
}

// B9: validateRequest scope conformance and the silent bypass without it.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource) when { context.count > 1 };"""));
    var ghost = Uid("App::Ghost", "g");
    var validated = engine.Authorize(new CedarAuthorizationRequest(
        ghost, Uid("App::Action", "read"), Uid("App::Doc", "d1"), set, "{\"count\":5}", EmptyEntities, Schema(CountSchema), true));
    var unvalidated = engine.Authorize(new CedarAuthorizationRequest(
        ghost, Uid("App::Action", "read"), Uid("App::Doc", "d1"), set, "{\"count\":5}", EmptyEntities, null, false));
    var validatedNotAllow = validated.Decision != CedarDecision.Allow;
    Record(
        "Q1-02-B9",
        "validateRequest rejects a scope type outside the action's appliesTo; without validation the request is silently evaluated",
        "validated run is NOT an Allow (fail closed); unvalidated run observed (expected Allow: no scope constraint is evaluated schema-less)",
        validatedNotAllow && unvalidated.Decision == CedarDecision.Allow,
        new Dictionary<string, object?> { ["validated"] = Summarize(validated), ["unvalidated"] = Summarize(unvalidated) },
        note: "The D3 adapter must always carry the augmented schema or pre-validate the scope; a schema-less pass-through would authorize principals outside the action's appliesTo.");
}

// B10: a required context attribute missing at request time.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource) when { context.count > 1 };"""));
    var validated = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"), set, "{}", EmptyEntities, Schema(CountSchema), true));
    var unvalidatedWithSchema = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"), set, "{}", EmptyEntities, Schema(CountSchema), false));
    var unvalidated = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"), set, "{}", EmptyEntities, null, false));
    var validatedNotAllow = validated.Decision != CedarDecision.Allow;
    var unvalidatedNotAllow = unvalidated.Decision != CedarDecision.Allow;
    Record(
        "Q1-02-B10",
        "Missing required context attribute fails closed on every path",
        "validated: not Allow; schema-carrying unvalidated: observed (expected Deny with evaluation error); schema-less: Deny with evaluation error on context.count",
        validatedNotAllow && unvalidatedNotAllow && unvalidatedWithSchema.Decision != CedarDecision.Allow,
        new Dictionary<string, object?>
        {
            ["validated"] = Summarize(validated),
            ["unvalidatedWithSchema"] = Summarize(unvalidatedWithSchema),
            ["unvalidated"] = Summarize(unvalidated),
        });
}

// B11: extension values flow through context; malformed extension input
// fails closed at request validation.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource) when { context.ip.isIpv4() && context.amount == decimal("2.50") && context.at == datetime("2024-06-01T12:00:00Z") };"""));
    var extensionSchema = Schema(ExtensionSchema);
    var allowed = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"), set,
        """{"ip":"10.1.2.3","amount":"2.50","at":"2024-06-01T12:00:00Z"}""", EmptyEntities, extensionSchema, true));
    var ipv6 = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"), set,
        """{"ip":"2001:db8::1","amount":"2.50","at":"2024-06-01T12:00:00Z"}""", EmptyEntities, extensionSchema, true));
    string malformed;
    try
    {
        var result = engine.Authorize(new CedarAuthorizationRequest(
            Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"), set,
            """{"ip":"not-an-ip","amount":"2.50","at":"2024-06-01T12:00:00Z"}""", EmptyEntities, extensionSchema, true));
        malformed = $"no-exception: {result.Decision}";
    }
    catch (Exception error)
    {
        malformed = error.GetType().Name + ": " + error.Message;
    }

    Record(
        "Q1-02-B11",
        "ipaddr/decimal/datetime extensions evaluate; malformed extension input is rejected",
        "valid ipv4/decimal/datetime -> Allow [p]; ipv6 -> Deny no errors; invalid ip -> rejected (no Allow)",
        allowed.Decision == CedarDecision.Allow && allowed.DeterminingPolicies is ["p"]
            && ipv6.Decision == CedarDecision.Deny && ipv6.IsErrorFree
            && !malformed.Contains("no-exception: Allow", StringComparison.Ordinal),
        new Dictionary<string, object?> { ["allowed"] = Summarize(allowed), ["ipv6"] = Summarize(ipv6), ["malformed"] = malformed });
}

// B12: the D2 lowering's input space. `name` is optional on App::User, so an
// unguarded read of `resource.owner.name` is rejected by Cedar's strict
// validation and the lowering exports no artifacts; the guarded form lowers
// cleanly. CedarSharp evaluates the unguarded policy when it is supplied
// directly, and then fails closed.
{
    const string unguarded = """permit(principal, action == App::Action::"read", resource) when { resource.owner.name == "n" };""";
    const string guarded = """permit(principal, action == App::Action::"read", resource) when { resource.owner has name && resource.owner.name == "n" };""";

    DogwoodLoweredPolicySet? unguardedLowered = null;
    string? unguardedException = null;
    try
    {
        unguardedLowered = Dogwood.Lower(unguarded, new DogwoodSchema(GroupSchema, Distincter: "q1"));
    }
    catch (Exception error)
    {
        unguardedException = error.GetType().Name + ": " + error.Message;
    }

    DogwoodLoweredPolicySet? guardedLowered = null;
    string? guardedException = null;
    try
    {
        guardedLowered = Dogwood.Lower(guarded, new DogwoodSchema(GroupSchema, Distincter: "q1"));
    }
    catch (Exception error)
    {
        guardedException = error.GetType().Name + ": " + error.Message;
    }

    Dictionary<string, object?>? unguardedStageError = unguardedLowered?.StageError is { } stageError
        ? new Dictionary<string, object?>
        {
            ["stage"] = stageError.Stage.ToString(),
            ["category"] = stageError.Category.ToString(),
            ["message"] = stageError.Message,
        }
        : null;

    var direct = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
        CedarPolicySet.FromPolicies(("p", unguarded)), "{}", OwnerBob, Schema(GroupSchema), true));

    Record(
        "Q1-02-B12",
        "D2 lowering input space: unguarded optional-attribute access is not exportable",
        "Unguarded form -> no artifacts exported (strict Cedar validation); guarded form -> artifacts exported; direct CedarSharp evaluation of the unguarded policy fails closed",
        unguardedLowered is not null && !unguardedLowered.ArtifactsExported
            && guardedLowered is not null && guardedLowered.ArtifactsExported
            && direct.Decision == CedarDecision.Deny,
        new Dictionary<string, object?>
        {
            ["unguarded"] = new Dictionary<string, object?>
            {
                ["artifactsExported"] = unguardedLowered?.ArtifactsExported,
                ["stageError"] = unguardedStageError,
                ["exception"] = unguardedException,
            },
            ["guarded"] = new Dictionary<string, object?>
            {
                ["artifactsExported"] = guardedLowered?.ArtifactsExported,
                ["stageError"] = guardedLowered?.StageError?.ToString(),
                ["exception"] = guardedException,
                ["exportedRuleIds"] = guardedLowered?.Rules.Select(rule => rule.CedarPolicyId).ToList(),
            },
            ["directEvaluation"] = Summarize(direct),
        },
        note: "Cedar's strict validation rejects an unguarded read of an optional attribute, so this policy is outside the D2-exportable set; a missing lowering must surface as an error, not as an empty evaluation.");
}

// ================================ Q1-03 schema/entity interoperability.

// C1: schema-less authorization with explicit action entities reproduces the
// upstream action-hierarchy merge.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action in App::Action::"ReadGroup", resource);"""));
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read2"), Uid("App::Doc", "d1"),
        set, "{}", ActionHierarchyEntities, null, false));
    Record(
        "Q1-03-C1",
        "Schema-less with explicit action entities: action-group membership resolves",
        "Allow; determining [p]; no errors",
        result.Decision == CedarDecision.Allow && result.IsErrorFree && result.DeterminingPolicies is ["p"],
        Summarize(result),
        note: "Hand-written action entities mirror the upstream pipeline's explicit merge of schema action entities (api.rs build_entities tier 3).");
}

// C2: schema-less without action entities silently does not match.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action in App::Action::"ReadGroup", resource);"""));
    var result = engine.Authorize(new CedarAuthorizationRequest(
        Uid("App::User", "alice"), Uid("App::Action", "read2"), Uid("App::Doc", "d1"),
        set, "{}", EmptyEntities, null, false));
    Record(
        "Q1-03-C2",
        "Schema-less without action entities denies silently",
        "Deny; no determining; no errors (membership unresolvable)",
        result.Decision == CedarDecision.Deny && result.IsErrorFree && result.DeterminingPolicies.Count == 0,
        Summarize(result));
}

// C3: the upstream-equivalent reconstruction path: pre-validate the supplied
// request against the augmented schema, then authorize schema-less with the
// bare scope principal inserted. Expected to match upstream bare-entity
// semantics (Allow, no errors) — this is the path a D3 adapter would take.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource) when { !(principal has token) };"""));
    var schema = Schema(SessionSchema);
    var principal = Uid("App::Session", "s1");
    var read = Uid("App::Action", "read");
    var doc = Uid("App::Doc", "d1");

    var entitiesCheck = engine.CheckEntities(EmptyEntities, schema);
    var contextCheck = engine.CheckContext("{}", schema, read);
    var fullCheck = engine.CheckFullRequest(principal, read, doc, JsonDocument.Parse("{}").RootElement.Clone(), JsonDocument.Parse(EmptyEntities).RootElement.Clone(), schema);

    var result = engine.Authorize(new CedarAuthorizationRequest(principal, read, doc, set, "{}", SessionWithoutToken, null, false));
    var pass = entitiesCheck.IsSuccess && contextCheck.IsSuccess && fullCheck.IsSuccess
        && result.Decision == CedarDecision.Allow && result.IsErrorFree && result.DeterminingPolicies is ["p"];
    Record(
        "Q1-03-C3",
        "Pre-validate with the augmented schema, then authorize schema-less with a bare scope principal",
        "CheckEntities/CheckContext/CheckFullRequest all succeed; schema-less authorize -> Allow [p] no errors (matches upstream bare-entity semantics)",
        pass,
        new Dictionary<string, object?>
        {
            ["checkEntities"] = entitiesCheck.IsSuccess,
            ["checkContext"] = contextCheck.IsSuccess,
            ["checkFullRequest"] = fullCheck.IsSuccess,
            ["result"] = Summarize(result),
        });
}

// C4: an incomplete supplied entity (required attribute missing) fails the
// schema-carrying call closed, and CheckEntities rejects it as well.
{
    var set = CedarPolicySet.FromPolicies(("p", """permit(principal, action == App::Action::"read", resource);"""));
    string authorizeOutcome;
    try
    {
        var result = engine.Authorize(new CedarAuthorizationRequest(
            Uid("App::Session", "s1"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
            set, "{}", SessionWithoutToken, Schema(SessionSchema), true));
        authorizeOutcome = JsonSerializer.Serialize(Summarize(result));
    }
    catch (Exception error)
    {
        authorizeOutcome = error.GetType().Name + ": " + error.Message;
    }

    var check = engine.CheckEntities(SessionWithoutToken, Schema(SessionSchema));
    Record(
        "Q1-03-C4",
        "Incomplete supplied entity fails closed in both the authorize and pre-validation paths",
        "Schema-carrying authorize does NOT Allow (rejects or denies with error); CheckEntities fails",
        !authorizeOutcome.Contains("\"Decision\":\"Allow\"", StringComparison.Ordinal) && !check.IsSuccess,
        new Dictionary<string, object?>
        {
            ["authorize"] = authorizeOutcome,
            ["checkEntitiesSuccess"] = check.IsSuccess,
            ["checkEntitiesErrors"] = check.Errors.Select(e => e.Message).ToList(),
        });
}

// C5: the exported augmented schema round-trips between JSON and text form
// with value equality and identical authorization outcomes.
{
    DogwoodLoweredPolicySet? lowered = null;
    string? lowerError = null;
    try
    {
        lowered = Dogwood.Lower("""permit(principal, action == App::Action::"read", resource) when { context.count > 1 };""", new DogwoodSchema(CountSchema, Distincter: "q1"));
    }
    catch (Exception error)
    {
        lowerError = error.GetType().Name + ": " + error.Message;
    }

    if (lowered is null || !lowered.ArtifactsExported)
    {
        Record("Q1-03-C5", "Exported schema round-trip", "Lowering exports artifacts", false, new Dictionary<string, object?> { ["stageError"] = lowered?.StageError?.ToString(), ["exception"] = lowerError });
    }
    else
    {
        var fromJson = CedarSchema.FromJson(lowered.CedarSchemaJson);
        var fromText = CedarSchema.FromText(lowered.CedarSchemaText);
        var set = CedarPolicySet.FromPolicies((lowered.Rules[0].CedarPolicyId, lowered.CedarPolicyText));
        var viaJson = engine.Authorize(new CedarAuthorizationRequest(
            Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"), set, "{\"count\":5}", EmptyEntities, fromJson, true));
        var viaText = engine.Authorize(new CedarAuthorizationRequest(
            Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"), set, "{\"count\":5}", EmptyEntities, fromText, true));
        var behaviorallyEqual = viaJson.Decision == viaText.Decision
            && viaJson.DeterminingPolicies.SequenceEqual(viaText.DeterminingPolicies)
            && viaJson.PolicyErrors.Select(e => e.PolicyId).SequenceEqual(viaText.PolicyErrors.Select(e => e.PolicyId));
        var pass = behaviorallyEqual
            && viaJson.Decision == CedarDecision.Allow
            && viaJson.PolicyErrors.Count == 0;
        Record(
            "Q1-03-C5",
            "Exported schema JSON/text round-trip with identical outcomes",
            "Both export forms authorize identically (same decision, determining set, policy-error set); value equality of the two CedarSchema snapshots is recorded but not required",
            pass,
            new Dictionary<string, object?>
            {
                ["valueEqual"] = fromJson.Equals(fromText),
                ["behaviorallyEqual"] = behaviorallyEqual,
                ["viaJson"] = Summarize(viaJson),
                ["viaText"] = Summarize(viaText),
            },
            note: "CedarSharp schema equality is by upstream JSON representation; the two export forms of the same augmented schema are not value-equal even though they authorize identically. A D3 adapter must cache/compare using one export form consistently.");
    }
}

// C6: the exported per-rule IDs must be paired with bodies explicitly;
// FromText renumbers and loses the exported IDs.
{
    var source = string.Join(" ", new[] { 4, 6, 8 }.Select(count =>
        $"permit(principal, action == App::Action::\"read\", resource) when {{ context.count > {count} }};"));
    DogwoodLoweredPolicySet? lowered = null;
    string? lowerError = null;
    try
    {
        lowered = Dogwood.Lower(source, new DogwoodSchema(CountSchema, Distincter: "q1"));
    }
    catch (Exception error)
    {
        lowerError = error.GetType().Name + ": " + error.Message;
    }

    if (lowered is null || !lowered.ArtifactsExported)
    {
        Record("Q1-03-C6", "Exported rule IDs pair with bodies", "Lowering exports artifacts", false, new Dictionary<string, object?> { ["stageError"] = lowered?.StageError?.ToString(), ["exception"] = lowerError });
    }
    else
    {
        var exportedIds = lowered.Rules.Select(rule => rule.CedarPolicyId).ToList();
        var renumbered = CedarPolicySet.FromText(lowered.CedarPolicyText);
        var renumberedOutcome = engine.Authorize(new CedarAuthorizationRequest(
            Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
            renumbered, "{\"count\":5}", EmptyEntities, CedarSchema.FromJson(lowered.CedarSchemaJson), true));

        var bodies = SplitPolicies(lowered.CedarPolicyText);
        var paired = CedarPolicySet.FromPolicies(exportedIds.Zip(bodies, (id, body) => (id, body)).ToArray());
        var pairedOutcome = engine.Authorize(new CedarAuthorizationRequest(
            Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
            paired, "{\"count\":5}", EmptyEntities, CedarSchema.FromJson(lowered.CedarSchemaJson), true));

        var pass = exportedIds.SequenceEqual((IEnumerable<string>)["q1_0", "q1_1", "q1_2"])
            && renumberedOutcome.DeterminingPolicies is ["policy0"]
            && pairedOutcome.DeterminingPolicies is ["q1_0"]
            && paired.PolicyIds.SequenceEqual((IEnumerable<string>)["q1_0", "q1_1", "q1_2"]);
        Record(
            "Q1-03-C6",
            "Exported IDs: FromText renumbers; FromPolicies with paired bodies preserves them",
            "Exported ids [q1_0, q1_1, q1_2]; FromText determining [policy0] (renumbered, PolicyIds property empty for text sets); paired FromPolicies keeps IDs and reports determining [q1_0]",
            pass,
            new Dictionary<string, object?>
            {
                ["exportedIds"] = exportedIds,
                ["fromTextIds"] = renumbered.PolicyIds.ToList(),
                ["fromText"] = Summarize(renumberedOutcome),
                ["pairedIds"] = paired.PolicyIds.ToList(),
                ["paired"] = Summarize(pairedOutcome),
            });
    }
}

// C7: a generated temporal field that collides with an authored context
// attribute is rejected at lowering, before any export.
{
    var lowered = Dogwood.Lower(TemporalSource, new DogwoodSchema(CollisionSchema, Distincter: "q1t"));
    var pass = !lowered.ArtifactsExported
        && lowered.StageError is not null
        && lowered.StageError.Stage == DogwoodStage.CollisionCheck
        && lowered.StageError.Category == DogwoodFailureCategory.GeneratedContextFieldCollision
        && lowered.Validation.Collision is not null
        && lowered.Validation.Collision.Field == "q1t_0__temporal_0";
    Record(
        "Q1-03-C7",
        "Generated-field collision is rejected at lowering (fail closed)",
        "No artifacts; StageError at CollisionCheck with GeneratedContextFieldCollision; collision field q1t_0__temporal_0",
        pass,
        new Dictionary<string, object?>
        {
            ["artifactsExported"] = lowered.ArtifactsExported,
            ["stageError"] = lowered.StageError is null ? null : new Dictionary<string, object?>
            {
                ["stage"] = lowered.StageError.Stage.ToString(),
                ["category"] = lowered.StageError.Category.ToString(),
                ["message"] = lowered.StageError.Message,
            },
            ["collision"] = lowered.Validation.Collision?.ToString(),
        });
}

// ====================================================== Q1-04 temporal.

string? temporalField = null;
CedarPolicySet? temporalSet = null;
CedarSchema? temporalSchema = null;
string? temporalSchemaJson = null;
string? temporalBody = null;
try
{
    var lowered = Dogwood.Lower(TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "q1t"));
    if (lowered.ArtifactsExported)
    {
        var match = Regex.Match(lowered.CedarPolicyText, @"context\.([A-Za-z_][A-Za-z0-9_]*__temporal_[0-9]+)");
        temporalField = match.Success ? match.Groups[1].Value : null;
        temporalSet = CedarPolicySet.FromPolicies((lowered.Rules[0].CedarPolicyId, lowered.CedarPolicyText));
        temporalSchema = CedarSchema.FromJson(lowered.CedarSchemaJson);
        temporalSchemaJson = lowered.CedarSchemaJson;
        temporalBody = lowered.CedarPolicyText;

        var fieldPattern = Regex.IsMatch(temporalField ?? string.Empty, "^[A-Za-z_][A-Za-z0-9_]*__temporal_[0-9]+$");
        var d1Pass = fieldPattern
            && !lowered.SelfContainedCedar
            && lowered.CedarSchemaJson.Contains(temporalField ?? "\u0000not-present", StringComparison.Ordinal)
            && !TemporalSchema.Contains(temporalField ?? "\u0000", StringComparison.Ordinal);
        Record(
            "Q1-04-D1",
            "Lowered temporal policy exposes a generated Boolean context field",
            "Artifacts exported; policy reads exactly one generated <distincter>__temporal_N field; the augmented schema declares it; the authored schema does not; not self-contained",
            d1Pass,
            new Dictionary<string, object?>
            {
                ["generatedField"] = temporalField,
                ["selfContained"] = lowered.SelfContainedCedar,
                ["augmentedSchemaDeclaresField"] = lowered.CedarSchemaJson.Contains(temporalField ?? "\u0000not-present", StringComparison.Ordinal),
                ["authoredSchemaDeclaresField"] = false,
                ["exportedRuleIds"] = lowered.Rules.Select(rule => rule.CedarPolicyId).ToList(),
            });
    }
    else
    {
        Record("Q1-04-D1", "Lowered temporal policy exposes a generated context field", "Lowering exports artifacts", false,
            new Dictionary<string, object?> { ["stageError"] = lowered.StageError?.ToString() });
    }
}
catch (Exception error)
{
    Record("Q1-04-D1", "Lowered temporal policy exposes a generated context field", "Lowering exports artifacts", false,
        new Dictionary<string, object?> { ["exception"] = error.GetType().Name + ": " + error.Message });
}

if (temporalSet is not null && temporalSchema is not null && temporalField is not null && temporalSchemaJson is not null && temporalBody is not null)
{
    // D2/D4: the decision follows the supplied temporal boolean (research-only
    // trust-boundary wiring: the caller supplies the generated field here,
    // which no public API may allow in production).
    CedarAuthorizationResult AuthorizeTemporal(bool value) => engine.Authorize(new CedarAuthorizationRequest(
        Uid("Test::User", "u"), Uid("Test::Action", "Read"), Uid("Test::Item", "i"),
        temporalSet, $"{{\"input\":{{\"key\":\"a\"}},\"{temporalField}\":{(value ? "true" : "false")}}}",
        EmptyEntities, temporalSchema, true));

    var allowed = AuthorizeTemporal(true);
    var denied = AuthorizeTemporal(false);
    var temporalId = temporalSet.PolicyIds.Single();
    var d2Pass = allowed.Decision == CedarDecision.Allow && allowed.DeterminingPolicies is [_]
        && allowed.PolicyErrors.Count == 0 && allowed.DeterminingPolicies[0] == temporalId
        && denied.Decision == CedarDecision.Deny && denied.DeterminingPolicies.Count == 0 && denied.PolicyErrors.Count == 0;
    Record(
        "Q1-04-D2",
        "Harness-injected temporal boolean drives the decision",
        "true -> Allow with determining [exported id]; false -> Deny with no determining and no errors",
        d2Pass,
        new Dictionary<string, object?> { ["true"] = Summarize(allowed), ["false"] = Summarize(denied), ["exportedRuleId"] = temporalId },
        note: "Research-only wiring: the generated field is caller-supplied here to demonstrate the trust boundary; production must compute it inside the trusted pipeline.");

    var d4Pass = allowed.Decision != denied.Decision;
    Record(
        "Q1-04-D4",
        "Caller-supplied temporal value flips the decision (trust-boundary evidence)",
        "The two runs differ only in the caller-supplied generated boolean and produce opposite decisions",
        d4Pass,
        new Dictionary<string, object?> { ["true"] = allowed.Decision.ToString(), ["false"] = denied.Decision.ToString() });

    // D3: a missing generated field fails closed on every path.
    var missingValidated = engine.Authorize(new CedarAuthorizationRequest(
        Uid("Test::User", "u"), Uid("Test::Action", "Read"), Uid("Test::Item", "i"),
        temporalSet, "{\"input\":{\"key\":\"a\"}}", EmptyEntities, temporalSchema, true));
    var missingUnvalidated = engine.Authorize(new CedarAuthorizationRequest(
        Uid("Test::User", "u"), Uid("Test::Action", "Read"), Uid("Test::Item", "i"),
        temporalSet, "{\"input\":{\"key\":\"a\"}}", EmptyEntities, temporalSchema, false));
    var d3Pass = missingValidated.Decision != CedarDecision.Allow && missingUnvalidated.Decision != CedarDecision.Allow;
    Record(
        "Q1-04-D3",
        "Missing generated temporal field fails closed on every path",
        "validated: not Allow (request validation); unvalidated: not Allow (evaluation error); observed categories recorded",
        d3Pass,
        new Dictionary<string, object?>
        {
            ["validated"] = Summarize(missingValidated),
            ["unvalidated"] = Summarize(missingUnvalidated),
        });

    // D5: cross-request isolation on one engine instance.
    var sequence = new List<CedarDecision?> { AuthorizeTemporal(true).Decision, AuthorizeTemporal(false).Decision, AuthorizeTemporal(true).Decision };
    var d5Pass = sequence[0] == CedarDecision.Allow && sequence[1] == CedarDecision.Deny && sequence[2] == CedarDecision.Allow;
    Record(
        "Q1-04-D5",
        "Cross-request isolation on a shared engine",
        "true, false, true in sequence -> Allow, Deny, Allow (no state bleed)",
        d5Pass,
        new Dictionary<string, object?> { ["sequence"] = sequence.Select(decision => decision?.ToString()).ToList() });

    // D6: the pinned Cedar 4.11 reference agrees on the same envelopes.
    if (referenceExe is not null && temporalSet.PolicyIds.Count == 1)
    {
        var exportedId = temporalSet.PolicyIds.Single();
        foreach (var value in new[] { true, false })
        {
            var input = RefAuthorization(
                new Dictionary<string, string> { [exportedId] = temporalBody },
                ("Test::User", "u"), ("Test::Action", "Read"), ("Test::Item", "i"),
                $"{{\"input\":{{\"key\":\"a\"}},\"{temporalField}\":{(value ? "true" : "false")}}}",
                EmptyEntities,
                temporalSchemaJson,
                validateRequest: true);
            var (ok, output, error) = RunRef(referenceExe, 1, input);
            Dictionary<string, object?> normalized;
            if (ok)
            {
                try
                {
                    normalized = NormalizeRef(ok, output, error);
                }
                catch (Exception parseError)
                {
                    normalized = new Dictionary<string, object?> { ["parseError"] = parseError.Message, ["raw"] = output };
                }
            }
            else
            {
                normalized = new Dictionary<string, object?> { ["failed"] = true, ["error"] = error };
            }

            var expected = value ? "allow" : "deny";
            var refDecision = normalized.TryGetValue("decision", out var decisionObject) ? decisionObject as string : null;
            Record(
                $"Q1-04-D6-{(value ? "true" : "false")}",
                "Cedar 4.11 reference agrees on the temporal wiring envelope",
                $"Reference decision {expected}",
                string.Equals(refDecision, expected, StringComparison.Ordinal),
                normalized);
        }
    }
}

// ==================================================== Q1-05 coexistence.

{
    // Both native modules live in one process: the D2 bridge (Cedar 4.11) and
    // the CedarSharp native (Cedar 4.13) must keep independent identities and
    // a full lower->authorize flow must work end to end.
    string? identity = null;
    string? flow = null;
    var identityPass = false;
    var flowPass = false;
    try
    {
        var dogwood = Dogwood.GetNativeVersion();
        var cedar = engine.GetVersion();
        identity = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["bridgeCedar"] = dogwood.CedarVersion,
            ["bridgeRevision"] = dogwood.DogwoodRevision,
            ["cedarSharpSdk"] = cedar.SdkVersion,
            ["cedarSharpSha256"] = cedar.Sha256,
        });
        identityPass = dogwood.CedarVersion == "4.11.0"
            && dogwood.DogwoodRevision.StartsWith("929f49e", StringComparison.Ordinal)
            && cedar.SdkVersion == "4.13.0"
            && cedar.Sha256 == "77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895";
    }
    catch (Exception error)
    {
        identity = error.GetType().Name + ": " + error.Message;
    }

    try
    {
        var lowered = Dogwood.Lower("""permit(principal, action == App::Action::"read", resource) when { context.count > 1 };""", new DogwoodSchema(CountSchema, Distincter: "q1"));
        var set = CedarPolicySet.FromPolicies((lowered.Rules[0].CedarPolicyId, lowered.CedarPolicyText));
        var result = engine.Authorize(new CedarAuthorizationRequest(
            Uid("App::User", "alice"), Uid("App::Action", "read"), Uid("App::Doc", "d1"),
            set, "{\"count\":5}", EmptyEntities, CedarSchema.FromJson(lowered.CedarSchemaJson), true));
        flow = JsonSerializer.Serialize(Summarize(result));
        flowPass = lowered.ArtifactsExported && result.Decision == CedarDecision.Allow && result.DeterminingPolicies is [_];
    }
    catch (Exception error)
    {
        flow = error.GetType().Name + ": " + error.Message;
    }

    Record(
        "Q1-05-E1",
        "D2 bridge and CedarSharp native coexist in one process",
        "Bridge Cedar 4.11.0 at revision 929f49e*; CedarSharp SDK 4.13.0 with the recorded SHA-256; a full lower->authorize flow succeeds in-process",
        identityPass && flowPass,
        new Dictionary<string, object?> { ["identity"] = identity, ["flow"] = flow });
}

// ============================================= Q1-06 differential limits.

// F1: the reference identity (supporting evidence only).
if (referenceSdk is not null)
{
    Record(
        "Q1-06-F1",
        "Cedar 4.11 reference identity",
        "Reference sdkVersion 4.11.0 (the bridge's pinned Cedar); language 4.5",
        referenceSdk == "4.11.0",
        new Dictionary<string, object?> { ["sdkVersion"] = referenceSdk },
        note: "The reference wraps cedar_policy::ffi directly; agreement with it is supporting evidence, not proof of upstream Dogwood-authorizer equivalence.");
}

// F2: identical envelopes through both engines. All fixtures are Dogwood-
// lowered bundles so both engines receive the same exported schema JSON.
{
    var cases = new List<(string Id, string Name, string Source, DogwoodSchema Schema, string Context, string Entities, (string, string) Principal, (string, string) Action, (string, string) Resource, bool SchemaCarried, bool Validate, string? Note)>();

    cases.Add(("count-true", "count condition true", """permit(principal, action == App::Action::"read", resource) when { context.count > 1 };""",
        new DogwoodSchema(CountSchema, Distincter: "q1"), "{\"count\":5}", EmptyEntities,
        ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true, true, null));
    cases.Add(("count-false", "count condition false", """permit(principal, action == App::Action::"read", resource) when { context.count > 1 };""",
        new DogwoodSchema(CountSchema, Distincter: "q1"), "{\"count\":1}", EmptyEntities,
        ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true, true, null));
    cases.Add(("owner-match", "owner matches", """permit(principal, action == App::Action::"read", resource) when { resource.owner == principal };""",
        new DogwoodSchema(GroupSchema, Distincter: "q1"), "{}", OwnerAlice,
        ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true, true, null));
    cases.Add(("owner-missing-entity", "owner references absent entity", """permit(principal, action == App::Action::"read", resource) when { resource.owner has name && resource.owner.name == "n" };""",
        new DogwoodSchema(GroupSchema, Distincter: "q1"), "{}", OwnerBob,
        ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true, true,
        "the has-guard is required by the D2 lowering's strict validation of optional attributes"));
    cases.Add(("parents-match", "principal hierarchy match", """permit(principal, action == App::Action::"read", resource) when { principal in App::Group::"admins" };""",
        new DogwoodSchema(GroupSchema, Distincter: "q1"), "{}", Groups,
        ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true, true, null));
    cases.Add(("action-group-schema", "action group with schema", """permit(principal, action in App::Action::"ReadGroup", resource);""",
        new DogwoodSchema(ActionHierarchySchema, Distincter: "q1"), "{}", EmptyEntities,
        ("App::User", "alice"), ("App::Action", "read2"), ("App::Doc", "d1"), true, true, null));
    cases.Add(("action-group-schemaless", "action group without schema", """permit(principal, action in App::Action::"ReadGroup", resource);""",
        new DogwoodSchema(ActionHierarchySchema, Distincter: "q1"), "{}", ActionHierarchyEntities,
        ("App::User", "alice"), ("App::Action", "read2"), ("App::Doc", "d1"), false, false, "explicit action entities, schema-less"));
    cases.Add(("has-absent-principal", "scope principal absent", """permit(principal, action == App::Action::"read", resource) when { !(principal has token) };""",
        new DogwoodSchema(SessionSchema, Distincter: "q1"), "{}", EmptyEntities,
        ("App::Session", "s1"), ("App::Action", "read"), ("App::Doc", "d1"), false, false, "the upstream-vs-direct probe envelope"));
    cases.Add(("has-bare-principal", "bare scope principal present", """permit(principal, action == App::Action::"read", resource) when { !(principal has token) };""",
        new DogwoodSchema(SessionSchema, Distincter: "q1"), "{}", SessionWithoutToken,
        ("App::Session", "s1"), ("App::Action", "read"), ("App::Doc", "d1"), false, false, null));
    cases.Add(("has-attributed-principal", "scope principal with token", """permit(principal, action == App::Action::"read", resource) when { !(principal has token) };""",
        new DogwoodSchema(SessionSchema, Distincter: "q1"), "{}", SessionWithToken,
        ("App::Session", "s1"), ("App::Action", "read"), ("App::Doc", "d1"), false, false, null));
    cases.Add(("extensions", "extension values", """permit(principal, action == App::Action::"read", resource) when { context.ip.isIpv4() && context.amount == decimal("2.50") && context.at == datetime("2024-06-01T12:00:00Z") };""",
        new DogwoodSchema(ExtensionSchema, Distincter: "q1"), """{"ip":"10.1.2.3","amount":"2.50","at":"2024-06-01T12:00:00Z"}""", EmptyEntities,
        ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true, true, null));

    if (referenceExe is not null)
    {
        var total = 0;
        var identical = 0;
        foreach (var (id, name, source, schema, context, entities, principal, action, resource, schemaCarried, validate, note) in cases)
        {
            try
            {
            var lowered = Dogwood.Lower(source, schema);
            if (!lowered.ArtifactsExported)
            {
                Record($"Q1-06-F2-{id}", name, "Lowering exports artifacts", false,
                    new Dictionary<string, object?> { ["stageError"] = lowered.StageError?.ToString() });
                continue;
            }

            var bodies = lowered.Rules.Length == 1
                ? new Dictionary<string, string> { [lowered.Rules[0].CedarPolicyId] = lowered.CedarPolicyText }
                : lowered.Rules.Zip(SplitPolicies(lowered.CedarPolicyText), (rule, body) => (rule.CedarPolicyId, body))
                    .ToDictionary(pair => pair.Item1, pair => pair.Item2);

            var set = CedarPolicySet.FromPolicies(bodies.ToArray());
            var schemaJson = lowered.CedarSchemaJson;
            var sharpResult = engine.Authorize(new CedarAuthorizationRequest(
                Uid(principal.Item1, principal.Item2), Uid(action.Item1, action.Item2), Uid(resource.Item1, resource.Item2),
                set, context, entities, schemaCarried ? CedarSchema.FromJson(schemaJson) : null, validate));
            var sharp = NormalizeSharp(sharpResult);

            var input = RefAuthorization(bodies, principal, action, resource, context, entities, schemaCarried ? schemaJson : null, validate);
            var (ok, output, error) = RunRef(referenceExe, 1, input);
            var reference = ok ? NormalizeRef(ok, output, error) : new Dictionary<string, object?> { ["failed"] = true, ["decision"] = null, ["error"] = error };

            var (same, detail) = SameAnswer(sharp, reference);
            total++;
            if (same)
            {
                identical++;
            }

            Record(
                $"Q1-06-F2-{id}",
                name,
                "CedarSharp (4.13) and the Cedar 4.11 reference produce identical normalized answers for the identical envelope",
                same,
                new Dictionary<string, object?>
                {
                    ["cedarSharp"] = sharp,
                    ["reference"] = reference,
                    ["detail"] = detail,
                    ["messageWordingIdentical"] = SameWording(sharp, reference),
                },
                note);
            }
            catch (Exception caseError)
            {
                Record($"Q1-06-F2-{id}", name, "Case executes on both engines", false,
                    new Dictionary<string, object?> { ["exception"] = caseError.GetType().Name + ": " + caseError.Message });
            }
        }

        // F2-raw-allow-with-error: an otherwise allowing set with one failing
        // policy. The D2 lowering cannot export a policy that reads an
        // attribute outside the augmented schema, so this envelope is built
        // directly for both engines (schema-less, no request validation).
        var directTotal = 0;
        var directMatched = 0;
        {
            var bodies = new Dictionary<string, string>
            {
                ["raw-good"] = """permit(principal, action == App::Action::"read", resource) when { context.count > 4 };""",
                ["raw-broken"] = """permit(principal, action == App::Action::"read", resource) when { context.missing == 1 };""",
            };
            var principal = ("App::User", "alice");
            var action = ("App::Action", "read");
            var resource = ("App::Doc", "d1");
            const string contextJson = "{\"count\":5}";
            var set = CedarPolicySet.FromPolicies(bodies.ToArray());
            var sharpResult = engine.Authorize(new CedarAuthorizationRequest(
                Uid(principal.Item1, principal.Item2), Uid(action.Item1, action.Item2), Uid(resource.Item1, resource.Item2),
                set, contextJson, EmptyEntities, null, false));
            var sharp = NormalizeSharp(sharpResult);
            var input = RefAuthorization(bodies, principal, action, resource, contextJson, EmptyEntities, null, false);
            var (ok, output, error) = RunRef(referenceExe, 1, input);
            var reference = ok
                ? NormalizeRef(ok, output, error)
                : new Dictionary<string, object?>
                {
                    ["failed"] = true,
                    ["decision"] = null,
                    ["determining"] = new List<string?>(),
                    ["policyErrorIds"] = new List<string?>(),
                    ["error"] = error,
                };
            var (same, detail) = SameAnswer(sharp, reference);
            directTotal++;
            if (same)
            {
                directMatched++;
            }

            Record(
                "Q1-06-F2-raw-allow-with-error",
                "raw allow with a policy error",
                "CedarSharp (4.13) and the Cedar 4.11 reference produce identical normalized answers for the identical envelope",
                same,
                new Dictionary<string, object?>
                {
                    ["cedarSharp"] = sharp,
                    ["reference"] = reference,
                    ["detail"] = detail,
                    ["messageWordingIdentical"] = SameWording(sharp, reference),
                });
        }

        Record(
            "Q1-06-F2-summary",
            "Differential subset summary",
            "Every executed envelope matches",
            identical + directMatched == total + directTotal && total + directTotal == cases.Count + 1,
            new Dictionary<string, object?> { ["matched"] = identical + directMatched, ["executed"] = total + directTotal, ["defined"] = cases.Count + 1 });
    }
    else
    {
        Record("Q1-06-F2", "Differential subset", "Reference binary available", false,
            new Dictionary<string, object?> { ["error"] = "dogwoodsharp-e1-ref path not supplied" });
    }
}

static bool SameWording(Dictionary<string, object?> sharp, Dictionary<string, object?> reference)
{
    var sharpMessages = ((IEnumerable<string?>?)sharp["policyErrorMessages"] ?? [])
        .Select(m => m ?? string.Empty).OrderBy(m => m, StringComparer.Ordinal).ToList();
    var referenceMessages = reference.TryGetValue("policyErrorMessages", out var value)
        ? ((IEnumerable<string?>?)value ?? []).Select(m => m ?? string.Empty).OrderBy(m => m, StringComparer.Ordinal).ToList()
        : [];
    return sharpMessages.SequenceEqual(referenceMessages, StringComparer.Ordinal);
}

// --------------------------------------------------------------- summary.

var payload = new Dictionary<string, object?>
{
    ["meta"] = meta,
    ["summary"] = new Dictionary<string, object?>
    {
        ["total"] = results.Count,
        ["failed"] = failures,
        ["passed"] = results.Count - failures,
    },
    ["cases"] = results,
};

var options = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(outputPath, JsonSerializer.Serialize(payload, options));
Console.WriteLine();
Console.WriteLine($"results: {results.Count} cases, {failures} failures -> {outputPath}");
return failures == 0 ? 0 : 1;
