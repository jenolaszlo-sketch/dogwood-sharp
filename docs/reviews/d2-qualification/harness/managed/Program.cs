using System.Text.Json;
using DogwoodSharp;
#if TEST_DECODER
using System.Reflection;
using System.Text.Json.Nodes;
#endif

if (args[0] == "transport")
{
    ExpectMalformed(() => Dogwood.Lower("", new DogwoodSchema("")));
    Console.WriteLine($"TRANSPORT PASS {args[1]}");
    return;
}

using var document = JsonDocument.Parse(File.ReadAllBytes(args[1]));
var cases = document.RootElement.EnumerateArray().ToArray();
if (args[0] == "corpus")
{
    var version = Dogwood.GetNativeVersion();
    Require(version.AbiVersion == 1 && version.DogwoodRevision == "929f49e7305534ac61ef06cf8e47269c596dc851",
        "identity");
    foreach (var test in cases)
    {
        var (source, schema) = Request(test);
        var id = test.GetProperty("id").GetString();
        try
        {
            if (id == "invalid-distincter")
            {
                ExpectArgument(() => Dogwood.Validate(source, schema));
                ExpectArgument(() => Dogwood.Lower(source, schema));
                continue;
            }
            CheckOutcome(Dogwood.Validate(source, schema), test.GetProperty("validate"));
            var result = Dogwood.Lower(source, schema);
            var expect = test.GetProperty("lower");
            CheckOutcome(result.Validation, expect);
            Require(result.IsLowered == expect.GetProperty("artifactsExported").GetBoolean() &&
                result.ArtifactsExported == result.IsLowered, "export gate");
            Require(result.CedarPolicyText == expect.GetProperty("cedarPolicyText").GetString() &&
                result.CedarSchemaJson == expect.GetProperty("cedarSchemaJson").GetString() &&
                result.CedarSchemaText == expect.GetProperty("cedarSchemaText").GetString(), "export bytes");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Managed case {id} failed.", exception);
        }
    }

    var success = Request(cases[0]);
    var original = Dogwood.Lower(success.Source, success.Schema);
    var snapshot = original.Rules[0];
    var copy = original.Rules.SetItem(0, snapshot with { CedarPolicyId = "copy-only" });
    for (int index = 1; index <= 8; index++)
    {
        var request = Request(cases[index]);
        _ = Dogwood.Lower(request.Source, request.Schema);
        _ = Dogwood.Parse("permit(");
    }
    Require(copy[0] != snapshot && original.Rules[0] == snapshot &&
        original.CedarPolicyText == cases[0].GetProperty("lower").GetProperty("cedarPolicyText").GetString(),
        "immutable snapshot");
    Console.WriteLine($"MANAGED CORPUS PASS cases={cases.Length} immutable=1 target={version.Target}");
    return;
}

#if TEST_DECODER
if (args[0] == "decoder")
{
    var bridge = typeof(Dogwood).Assembly.GetType("DogwoodSharp.NativeBridge", throwOnError: true)!;
    var flags = BindingFlags.NonPublic | BindingFlags.Static;
    var lower = bridge.GetMethod("ReadLoweredResult", flags)!;
    var validate = bridge.GetMethod("ReadValidationResult", flags)!;
    var validLower = JsonNode.Parse(cases[0].GetProperty("lower").GetRawText())!.AsObject();
    var validValidate = JsonNode.Parse(cases[0].GetProperty("validate").GetRawText())!.AsObject();
    var two = JsonNode.Parse(cases.Single(f => f.GetProperty("id").GetString() == "namespace-and-repeated-id-annotations")
        .GetProperty("lower").GetRawText())!.AsObject();
    var invalid = JsonNode.Parse(cases.Single(f => f.GetProperty("id").GetString() == "validation-utf8")
        .GetProperty("lower").GetRawText())!.AsObject();
    _ = Decode(lower, validLower);
    _ = Decode(validate, validValidate);
    _ = Decode(lower, JsonNode.Parse(cases[1].GetProperty("lower").GetRawText())!.AsObject());
    int rejected = 0;

    void Reject(MethodInfo decoder, JsonObject baseline, Action<JsonObject> mutation)
    {
        var copy = baseline.DeepClone().AsObject();
        mutation(copy);
        ExpectMalformed(() => Decode(decoder, copy));
        rejected++;
    }

    Reject(lower, validLower, m => m.Remove("collisionCheckPassed"));
    Reject(lower, validLower, m => m["actionSchemaBuilt"] = false);
    Reject(lower, validLower, m => m["validationPassed"] = false);
    Reject(lower, validLower, m => m["stageError"] = JsonNode.Parse("""
        {"stage":"collision-check","category":"internal","message":"qualification control","location":null}
        """));
    Reject(lower, validLower, m => m.Remove("cedarSchemaText"));
    Reject(lower, validLower, m => m["cedarPolicyText"] = "");
    Reject(lower, validLower, m => m["cedarSchemaJson"] = "[]");
    Reject(lower, validLower, m => m["ruleCount"] = 37);
    Reject(lower, validLower, m => m["rules"]![0]!["ruleIndex"] = 1);
    Reject(lower, validLower, m => m["rules"]![0]!["cedarPolicyId"] = "");
    Reject(lower, two, m => m["rules"]![1]!["cedarPolicyId"] = m["rules"]![0]!["cedarPolicyId"]!.DeepClone());
    Reject(lower, invalid, m => m["validationErrors"]![0]!["location"] = JsonNode.Parse("""{"offset":-1,"length":1}"""));
    Reject(lower, invalid, m => m["validationErrors"]![0]!["location"] = JsonNode.Parse("""{"offset":9999,"length":1}"""));
    Reject(lower, invalid, m => m["cedarSchemaText"] = "entity Phantom;");
    Reject(validate, validValidate, m => m["cedarPolicyText"] = "");
    Reject(validate, validValidate, m => m["artifactsExported"] = true);
    Reject(validate, validValidate, m => m["rules"]![0]!["sourceText"] = "permit(principal,action,resource);");
    ExpectMalformed(() => Decode(lower, validValidate));
    rejected++;

    // The independent test records this case either way for findings triage.
    var missingText = validLower.DeepClone().AsObject();
    missingText["cedarSchemaText"] = "";
    bool partialAccepted;
    try
    {
        var result = (DogwoodLoweredPolicySet)Decode(lower, missingText);
        partialAccepted = result.ArtifactsExported && result.CedarSchemaText.Length == 0;
    }
    catch (DogwoodBridgeException ex) when (ex.Failure == DogwoodBridgeFailure.MalformedResponse)
    {
        partialAccepted = false;
    }
    Console.WriteLine($"DECODER controls=3 rejected={rejected} partial-schema-text-accepted={partialAccepted}");
    return;
}
#endif
throw new ArgumentException("Unknown qualification mode.");

static (string Source, DogwoodSchema Schema) Request(JsonElement test)
{
    var request = test.GetProperty("request");
    return (request.GetProperty("source").GetString()!, new DogwoodSchema(
        request.GetProperty("policySchema").GetString()!, Optional(request, "eventSchema"),
        Optional(request, "macros"), Optional(request, "distincter")));
}

static string? Optional(JsonElement row, string name) => row.TryGetProperty(name, out var v) ? v.GetString() : null;

static void CheckOutcome(DogwoodValidationResult result, JsonElement expect)
{
    Require(result.ServiceSchemaBuilt == Flag("serviceSchemaBuilt") && result.Parsed == Flag("parsed") &&
        result.ActionSchemaBuilt == Flag("actionSchemaBuilt") && result.Lowered == Flag("lowered") &&
        result.CollisionCheckPassed == Flag("collisionCheckPassed") && result.ValidationPassed == Flag("validationPassed") &&
        result.IsValid == result.ValidationPassed && result.SelfContainedCedar == Flag("selfContainedCedar"), "stages");
    var count = expect.GetProperty("policyCount");
    Require(result.PolicyCount == (count.ValueKind == JsonValueKind.Null ? null : count.GetInt32()) &&
        result.RuleCount == expect.GetProperty("ruleCount").GetInt32(), "counts");
    var error = expect.GetProperty("stageError");
    if (error.ValueKind == JsonValueKind.Null)
        Require(result.StageError == null, "unexpected stage error");
    else
    {
        var stage = error.GetProperty("stage").GetString() switch
        {
            "service-schema" => DogwoodStage.ServiceSchema,
            "action-schema" => DogwoodStage.ActionSchema,
            "lower" => DogwoodStage.Lower,
            "collision-check" => DogwoodStage.CollisionCheck,
            _ => DogwoodStage.ArtifactExport
        };
        var actual = result.StageError;
        Require(actual is not null && actual.Stage == stage && actual.Message == error.GetProperty("message").GetString()
            && actual.Location == Location(error), "fatal diagnostic");
    }
    var collision = expect.GetProperty("collision");
    Require(result.Collision == (collision.ValueKind == JsonValueKind.Null ? null : new DogwoodContextFieldCollision(
        collision.GetProperty("field").GetString()!, Optional(collision, "actionNamespace"),
        collision.GetProperty("actionId").GetString()!)), "collision diagnostic");
    CheckSequence(result.ParseDiagnostics.Select(d => (d.Category.ToString(), d.Message, d.Location)), "parseDiagnostics");
    CheckSequence(result.ValidationErrors.Select(d => (d.Category.ToString(), d.Message, d.Location)), "validationErrors");
    CheckSequence(result.ValidationWarnings.Select(d => (d.Category.ToString(), d.Message, d.Location)), "validationWarnings");
    Require(result.Rules.SequenceEqual(expect.GetProperty("rules").EnumerateArray().Select(row => new PolicyRuleMapping(
        row.GetProperty("ruleIndex").GetInt32(), row.GetProperty("cedarPolicyId").GetString()!, Optional(row, "sourceText"))),
        "rule correspondence");

    bool Flag(string name) => expect.GetProperty(name).GetBoolean();
    void CheckSequence(IEnumerable<(string, string, PolicySourceLocation?)> actual, string channel)
    {
        Require(actual.SequenceEqual(expect.GetProperty(channel).EnumerateArray().Select(row =>
        {
            string category = row.GetProperty("category").GetString()!;
            return (char.ToUpperInvariant(category[0]) + category[1..], row.GetProperty("message").GetString()!, Location(row));
        })), channel);
    }
}

static PolicySourceLocation? Location(JsonElement row)
{
    var loc = row.GetProperty("location");
    return loc.ValueKind == JsonValueKind.Null ? null : new PolicySourceLocation(
        loc.GetProperty("offset").GetInt32(), loc.GetProperty("length").GetInt32());
}

#if TEST_DECODER
static object Decode(MethodInfo method, JsonObject json)
{
    using var check = JsonDocument.Parse(json.ToJsonString());
    try
    {
        return method.Invoke(null, [check.RootElement, 256])!;
    }
    catch (TargetInvocationException ex) when (ex.InnerException is DogwoodBridgeException error)
    {
        throw error;
    }
}
#endif

static void ExpectMalformed(Func<object> operation)
{
    try { _ = operation(); }
    catch (DogwoodBridgeException ex) when (ex.Failure == DogwoodBridgeFailure.MalformedResponse) { return; }
    throw new InvalidOperationException("Expected a malformed-response exception.");
}

static void ExpectArgument(Func<object> operation)
{
    try { _ = operation(); }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException("Expected an argument exception.");
}

static void Require(bool condition, string context)
{
    if (!condition)
        throw new InvalidOperationException(context);
}
