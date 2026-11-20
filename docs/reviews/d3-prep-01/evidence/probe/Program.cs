// DOGWOOD-SHARP-D3-PREP-01 contract-resolution probe (evidence tooling).
//
// Executes one fixture set through two engines and classifies the result:
//
//   * the D2 lowering plus CedarSharp 1.0.0 (Cedar 4.13.0), schema-carrying
//     with request validation enabled, and
//   * the upstream Dogwood authorizer oracle (dogwood-language at the pinned
//     revision), which owns the three-tier entity-store construction, the
//     temporal pipeline and the provider pipeline.
//
// The expectations in fixtures.json are hand-derived before execution. The
// probe reports a case as a failure when the measured outcome departs from
// the hand-derived expectation, or when the measured cross-engine agreement
// departs from the declared agreement class.
//
// Usage: dotnet PrepProbe.dll <results.json> <oracle.exe> <fixtures.json> <differences.json>

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CedarSharp;
using DogwoodSharp;

var resultsPath = args.Length > 0 ? args[0] : "prep-probe-results.json";
var oraclePath = args.Length > 1 ? args[1] : null;
var fixturesPath = args.Length > 2 ? args[2] : "fixtures.json";
var differencesPath = args.Length > 3 ? args[3] : null;

if (oraclePath is null || differencesPath is null)
{
    Console.Error.WriteLine("the upstream oracle path and difference register path are required");
    return 2;
}

using var fixtureDocument = JsonDocument.Parse(File.ReadAllText(fixturesPath));
var fixtureRoot = fixtureDocument.RootElement;
var schemas = fixtureRoot.GetProperty("schemas").EnumerateObject()
    .ToDictionary(property => property.Name, property => property.Value.GetString()!);

var cases = fixtureRoot.GetProperty("cases").EnumerateArray().ToList();

using var differenceDocument = JsonDocument.Parse(File.ReadAllText(differencesPath));
var differenceRows = differenceDocument.RootElement.GetProperty("differences").EnumerateArray()
    .ToDictionary(
        row => row.GetProperty("id").GetString()!,
        row => row.Clone(),
        StringComparer.Ordinal);
if (!ValidateDifferenceCatalog(cases, differenceRows, out var differenceCatalogErrors))
{
    foreach (var error in differenceCatalogErrors)
    {
        Console.Error.WriteLine($"difference register error: {error}");
    }
    return 2;
}

// ------------------------------------------------------------ upstream oracle.

var oracleBatch = new JsonObject
{
    ["cases"] = new JsonArray(cases.Select(caseElement => (JsonNode)OracleEnvelope(caseElement, schemas)).ToArray()),
};
var oracleText = RunOracle(oraclePath, oracleBatch.ToJsonString());
var oracleResults = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
if (oracleText.ExitCode == 0)
{
    using var oracleDocument = JsonDocument.Parse(oracleText.Output);
    foreach (var entry in oracleDocument.RootElement.GetProperty("results").EnumerateArray())
    {
        oracleResults[entry.GetProperty("id").GetString()!] = entry.GetProperty("outcome").Clone();
    }
}
else
{
    Console.Error.WriteLine($"the upstream oracle failed: {oracleText.Error}");
    return 2;
}

// --------------------------------------------------------------- CedarSharp.

var engine = new CedarEngine();
var report = new List<Dictionary<string, object?>>();
var failures = 0;

foreach (var caseElement in cases)
{
    var id = caseElement.GetProperty("id").GetString()!;
    var question = caseElement.GetProperty("question").GetString()!;
    var source = caseElement.GetProperty("source").GetString()!;
    var schemaText = schemas[caseElement.GetProperty("schema").GetString()!];
    var distincter = caseElement.GetProperty("distincter").GetString()!;
    var expectation = caseElement.GetProperty("expectation");

    var lowering = Lower(source, schemaText, distincter);
    var cedarSharp = Evaluate(engine, caseElement, lowering);

    var upstream = oracleResults.TryGetValue(id, out var oracleOutcome)
        ? oracleOutcome
        : default;

    var measuredAgreement = Classify(upstream, cedarSharp);
    var declaredAgreement = expectation.GetProperty("agreement").GetString()!;

    var upstreamMatches = MatchesExpectation(upstream, expectation.GetProperty("upstream"));
    var cedarSharpMatches = MatchesCedarExpectation(cedarSharp, expectation.GetProperty("cedarSharp"));
    var loweringMatches = MatchesLoweringExpectation(lowering, expectation);
    var agreementMatches = measuredAgreement == declaredAgreement;
    var differenceManifestMatches = ValidateDifferenceCase(
        id,
        expectation,
        measuredAgreement,
        upstream,
        cedarSharp,
        differenceRows);

    var pass = upstreamMatches
        && cedarSharpMatches
        && loweringMatches
        && agreementMatches
        && differenceManifestMatches;
    if (!pass)
    {
        failures++;
    }

    report.Add(new Dictionary<string, object?>
    {
        ["id"] = id,
        ["question"] = question,
        ["expectation"] = expectation,
        ["verdict"] = pass ? "pass" : "FAIL",
        ["measuredAgreement"] = measuredAgreement,
        ["declaredAgreement"] = declaredAgreement,
        ["agreementMatches"] = agreementMatches,
        ["differenceManifestMatches"] = differenceManifestMatches,
        ["upstreamMatchesExpectation"] = upstreamMatches,
        ["cedarSharpMatchesExpectation"] = cedarSharpMatches,
        ["loweringMatchesExpectation"] = loweringMatches,
        ["lowering"] = lowering,
        ["upstream"] = upstream,
        ["upstreamPolicyErrorIds"] = UpstreamPolicyErrorIds(upstream),
        ["upstreamErrorCategories"] = UpstreamErrorCategories(upstream),
        ["cedarSharp"] = cedarSharp,
        ["cedarSharpPolicyErrorIds"] = CedarSharpPolicyErrorIds(cedarSharp),
        ["cedarSharpErrorCategories"] = cedarSharp["errorCategories"],
    });

    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {id} {question}");
}

var output = new Dictionary<string, object?>
{
    ["meta"] = new Dictionary<string, object?>
    {
        ["generatedAtUtc"] = DateTime.UtcNow.ToString("o"),
        ["framework"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        ["rid"] = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
        ["oracle"] = oraclePath,
        ["fixtures"] = fixturesPath,
        ["fixturesSha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fixturesPath))),
        ["differenceRegister"] = differencesPath,
        ["differenceRegisterSha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(differencesPath))),
        ["differenceRegisterSchemaVersion"] = differenceDocument.RootElement.GetProperty("schemaVersion").GetInt32(),
        ["fixtureCount"] = cases.Count,
    },
    ["summary"] = new Dictionary<string, object?>
    {
        ["total"] = report.Count,
        ["failed"] = failures,
        ["passed"] = report.Count - failures,
    },
    ["cases"] = report,
};

File.WriteAllText(resultsPath, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"results: {report.Count} cases, {failures} failures -> {resultsPath}");
return failures == 0 ? 0 : 1;

// ------------------------------------------------------------------ helpers.

JsonObject OracleEnvelope(JsonElement caseElement, IReadOnlyDictionary<string, string> schemas)
{
    var eventElement = caseElement.GetProperty("event");
    return new JsonObject
    {
        ["id"] = caseElement.GetProperty("id").GetString(),
        ["source"] = caseElement.GetProperty("source").GetString(),
        ["policySchema"] = schemas[caseElement.GetProperty("schema").GetString()!],
        ["distincter"] = caseElement.GetProperty("distincter").GetString(),
        ["event"] = JsonNode.Parse(eventElement.GetRawText()),
    };
}

Dictionary<string, object?> Lower(string source, string schemaText, string distincter)
{
    DogwoodLoweredPolicySet? lowered = null;
    string? error = null;
    try
    {
        lowered = Dogwood.Lower(source, new DogwoodSchema(schemaText, Distincter: distincter));
    }
    catch (Exception failure)
    {
        error = failure.GetType().Name + ": " + failure.Message;
    }

    var observation = new Dictionary<string, object?>
    {
        ["artifactsExported"] = lowered?.ArtifactsExported,
        ["stageError"] = lowered?.StageError is null ? null : new Dictionary<string, object?>
        {
            ["stage"] = lowered.StageError.Stage.ToString(),
            ["category"] = lowered.StageError.Category.ToString(),
            ["message"] = lowered.StageError.Message,
        },
        ["exception"] = error,
        ["exportedRuleIds"] = lowered?.Rules.Select(rule => rule.CedarPolicyId).ToList(),
    };
    return observation;
}

Dictionary<string, object?> Evaluate(CedarEngine engine, JsonElement caseElement, Dictionary<string, object?> lowering)
{
    var observation = new Dictionary<string, object?>
    {
        ["operationFailed"] = true,
        ["decision"] = null,
        ["hasErrors"] = true,
        ["determining"] = new List<string?>(),
        ["policyErrors"] = new List<string?>(),
        ["errors"] = new List<string?>(),
        ["errorCategories"] = new List<string>(),
        ["isCleanAllow"] = false,
        ["exception"] = null,
    };

    if (lowering["artifactsExported"] is not true)
    {
        return observation;
    }

    var source = caseElement.GetProperty("source").GetString()!;
    var distincter = caseElement.GetProperty("distincter").GetString()!;
    var eventElement = caseElement.GetProperty("event");
    var actionNamespace = string.Join("::", eventElement.GetProperty("actionNamespace").EnumerateArray().Select(item => item.GetString()!));
    var actionId = eventElement.GetProperty("actionId").GetString()!;
    var principal = eventElement.GetProperty("principal");
    var resource = eventElement.GetProperty("resource");
    var contextJson = eventElement.GetProperty("context").GetRawText();
    var entitiesJson = eventElement.GetProperty("entities").GetRawText();

    try
    {
        // The current D2 response exports one concatenated text value. The
        // bounded probe splits that value and pairs it with the exported IDs;
        // the source text is never reparsed as a CedarPolicySet. The future
        // D3-1 export must supply exact per-rule bodies instead of this
        // calibration-only reconstruction.
        var lowered = Dogwood.Lower(source, new DogwoodSchema(
            schemasFor(caseElement), Distincter: distincter))!;
        var bodies = SplitPolicyBodies(lowered.CedarPolicyText);
        if (bodies.Count != lowered.Rules.Length)
        {
            throw new InvalidOperationException(
                $"The exported Cedar text contains {bodies.Count} bodies for {lowered.Rules.Length} rules.");
        }
        var set = CedarPolicySet.FromPolicies(
            lowered.Rules.Select((rule, index) => (rule.CedarPolicyId, bodies[index])).ToArray());
        var schema = CedarSchema.FromJson(lowered.CedarSchemaJson);
        var result = engine.Authorize(new CedarAuthorizationRequest(
            new CedarEntityUid(principal.GetProperty("type").GetString()!, principal.GetProperty("id").GetString()!),
            new CedarEntityUid(actionNamespace, actionId),
            new CedarEntityUid(resource.GetProperty("type").GetString()!, resource.GetProperty("id").GetString()!),
            set,
            contextJson,
            entitiesJson,
            schema,
            true));

        observation["operationFailed"] = !result.IsSuccess;
        observation["decision"] = result.IsSuccess
            ? result.Decision?.ToString().ToLowerInvariant()
            : null;
        observation["hasErrors"] = !result.IsErrorFree;
        observation["determining"] = result.DeterminingPolicies.ToList();
        observation["policyErrors"] = result.PolicyErrors.Select(error => $"{error.PolicyId}: {error.Error.Message}").ToList();
        observation["errors"] = result.Errors.ToList();
        observation["errorCategories"] = result.Errors
            .Select(error => ClassifyCedarError(error.Message))
            .Concat(result.PolicyErrors.Select(error => ClassifyCedarError(error.Error.Message)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        observation["isCleanAllow"] = result.IsCleanAllow;
    }
    catch (Exception failure)
    {
        observation["operationFailed"] = true;
        observation["decision"] = null;
        observation["hasErrors"] = true;
        observation["exception"] = failure.GetType().Name + ": " + failure.Message;
    }

    return observation;
}

// The schema text for a case, resolved through the fixture's schema table.
string schemasFor(JsonElement caseElement) => schemas[caseElement.GetProperty("schema").GetString()!];

string Classify(JsonElement upstream, Dictionary<string, object?> cedarSharp)
{
    if (upstream.ValueKind == JsonValueKind.Undefined)
    {
        return "missing-upstream-result";
    }
    if (upstream.GetProperty("failed").GetBoolean())
    {
        return cedarSharp["operationFailed"] is true ? "match" : "expected-difference";
    }

    var upstreamDecision = upstream.GetProperty("decision").ValueKind == JsonValueKind.Null
        ? null
        : upstream.GetProperty("decision").GetString();
    var upstreamHasErrors = upstream.GetProperty("errors").GetArrayLength() > 0;
    var cedarDecision = (string?)cedarSharp["decision"];
    var cedarHasErrors = cedarSharp["hasErrors"] is true;
    var cedarFailed = cedarSharp["operationFailed"] is true;

    if (upstreamDecision == null && cedarFailed)
    {
        return "expected-difference";
    }

    var decisionMatches = upstreamDecision == cedarDecision;
    var errorPresenceMatches = upstreamHasErrors == cedarHasErrors;
    var determiningMatches = UpstreamDeterminingIds(upstream).SetEquals(CedarSharpDeterminingIds(cedarSharp));
    var policyErrorIdsMatch = UpstreamPolicyErrorIds(upstream).SetEquals(CedarSharpPolicyErrorIds(cedarSharp));
    var errorCategoriesMatch = UpstreamErrorCategories(upstream)
        .SetEquals(CedarSharpErrorCategories(cedarSharp));
    var cedarCleanAllow = cedarSharp.TryGetValue("isCleanAllow", out var cleanAllowValue)
        && cleanAllowValue is true;
    var upstreamCleanAllow = upstreamDecision == "allow" && !upstreamHasErrors;
    var cleanAllowMatches = cedarCleanAllow == upstreamCleanAllow;

    return decisionMatches
        && errorPresenceMatches
        && determiningMatches
        && policyErrorIdsMatch
        && errorCategoriesMatch
        && cleanAllowMatches
        ? "match"
        : "expected-difference";
}

HashSet<string> UpstreamDeterminingIds(JsonElement upstream)
{
    if (upstream.ValueKind == JsonValueKind.Undefined
        || !upstream.TryGetProperty("determiningRules", out var determining))
    {
        return new HashSet<string>(StringComparer.Ordinal);
    }
    return determining.EnumerateArray()
        .Select(rule => rule.GetProperty("cedarPolicyId").GetString()!)
        .ToHashSet(StringComparer.Ordinal);
}

HashSet<string> CedarSharpDeterminingIds(Dictionary<string, object?> cedarSharp)
{
    var determining = cedarSharp["determining"] as IEnumerable<string?>;
    return determining is null
        ? new HashSet<string>(StringComparer.Ordinal)
        : determining.Where(id => id is not null).Select(id => id!).ToHashSet(StringComparer.Ordinal);
}

HashSet<string> UpstreamPolicyErrorIds(JsonElement upstream)
{
    if (upstream.ValueKind == JsonValueKind.Undefined
        || !upstream.TryGetProperty("errors", out var errors))
    {
        return new HashSet<string>(StringComparer.Ordinal);
    }
    var ids = new HashSet<string>(StringComparer.Ordinal);
    foreach (var error in errors.EnumerateArray())
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            error.GetString() ?? string.Empty,
            "policy `(?<id>[^`]+)`");
        if (match.Success)
        {
            ids.Add(match.Groups["id"].Value);
        }
    }
    return ids;
}

HashSet<string> CedarSharpPolicyErrorIds(Dictionary<string, object?> cedarSharp)
{
    var policyErrors = cedarSharp["policyErrors"] as IEnumerable<string?>;
    if (policyErrors is null)
    {
        return new HashSet<string>(StringComparer.Ordinal);
    }
    return policyErrors
        .Where(error => error is not null)
        .Select(error => error!.Split(':', 2)[0])
        .ToHashSet(StringComparer.Ordinal);
}

HashSet<string> UpstreamErrorCategories(JsonElement upstream)
{
    if (upstream.ValueKind == JsonValueKind.Undefined
        || !upstream.TryGetProperty("errors", out var errors))
    {
        return new HashSet<string>(StringComparer.Ordinal);
    }
    return errors.EnumerateArray()
        .Select(error => ClassifyUpstreamError(error.GetString() ?? string.Empty))
        .ToHashSet(StringComparer.Ordinal);
}

HashSet<string> CedarSharpErrorCategories(Dictionary<string, object?> cedarSharp)
{
    var categories = cedarSharp["errorCategories"] as IEnumerable<string?>;
    return categories is null
        ? new HashSet<string>(StringComparer.Ordinal)
        : categories.Where(category => category is not null).Select(category => category!)
            .ToHashSet(StringComparer.Ordinal);
}

string ClassifyUpstreamError(string message)
{
    if (message.Contains("entity conformance", StringComparison.Ordinal))
    {
        return "entity-conformance";
    }
    if (message.Contains("does not have the attribute", StringComparison.Ordinal)
        || message.Contains("record does not have the attribute", StringComparison.Ordinal))
    {
        return "missing-attribute";
    }
    if (message.Contains("does not exist", StringComparison.Ordinal))
    {
        return "missing-entity";
    }
    return "policy-evaluation-error";
}

string ClassifyCedarError(string message)
{
    if (message.Contains("does not conform", StringComparison.Ordinal))
    {
        return "entity-conformance";
    }
    if (message.Contains("while parsing context", StringComparison.Ordinal)
        || message.Contains("record to have an attribute", StringComparison.Ordinal))
    {
        return "missing-attribute";
    }
    if (message.Contains("is not valid for", StringComparison.Ordinal))
    {
        return "scope-mismatch";
    }
    if (message.Contains("does not exist in the supplied schema", StringComparison.Ordinal))
    {
        return "undeclared-action";
    }
    if (message.Contains("does not exist", StringComparison.Ordinal))
    {
        return "missing-entity";
    }
    return "operation-error";
}

bool MatchesExpectation(JsonElement measured, JsonElement expectation)
{
    if (measured.ValueKind == JsonValueKind.Undefined)
    {
        return false;
    }

    var measuredFailed = measured.GetProperty("failed").GetBoolean();
    var expectedFailed = expectation.TryGetProperty("failed", out var failedValue)
        && failedValue.GetBoolean();
    if (measuredFailed != expectedFailed)
    {
        return false;
    }

    var decision = measured.GetProperty("decision").ValueKind == JsonValueKind.Null
        ? null
        : measured.GetProperty("decision").GetString();
    var expectedDecision = expectation.GetProperty("decision").ValueKind == JsonValueKind.Null
        ? null
        : expectation.GetProperty("decision").GetString();
    if (decision != expectedDecision)
    {
        return false;
    }

    var hasErrors = measured.GetProperty("errors").GetArrayLength() > 0;
    if (hasErrors != expectation.GetProperty("hasErrors").GetBoolean())
    {
        return false;
    }

    if (expectation.TryGetProperty("determiningRules", out var expectedRules)
        && !MatchesDeterminingRules(measured.GetProperty("determiningRules"), expectedRules))
    {
        return false;
    }
    if (expectation.TryGetProperty("policyErrorIds", out var expectedPolicyErrors)
        && !MatchesStringSet(UpstreamPolicyErrorIds(measured), expectedPolicyErrors))
    {
        return false;
    }
    if (expectation.TryGetProperty("errorCategories", out var expectedCategories)
        && !MatchesStringSet(UpstreamErrorCategories(measured), expectedCategories))
    {
        return false;
    }

    return true;
}

bool MatchesCedarExpectation(Dictionary<string, object?> measured, JsonElement expectation)
{
    var decision = (string?)measured["decision"];
    var expectedDecision = expectation.GetProperty("decision").ValueKind == JsonValueKind.Null
        ? null
        : expectation.GetProperty("decision").GetString();
    if (decision != expectedDecision)
    {
        return false;
    }
    if ((measured["hasErrors"] is true) != expectation.GetProperty("hasErrors").GetBoolean())
    {
        return false;
    }
    if ((measured["operationFailed"] is true)
        != expectation.GetProperty("operationFailed").GetBoolean())
    {
        return false;
    }
    if ((measured["isCleanAllow"] is true)
        != expectation.GetProperty("isCleanAllow").GetBoolean())
    {
        return false;
    }
    if (expectation.TryGetProperty("determiningIds", out var expectedDetermining)
        && !MatchesStringSet(CedarSharpDeterminingIds(measured), expectedDetermining))
    {
        return false;
    }
    if (expectation.TryGetProperty("policyErrorIds", out var expectedPolicyErrors)
        && !MatchesStringSet(CedarSharpPolicyErrorIds(measured), expectedPolicyErrors))
    {
        return false;
    }
    if (expectation.TryGetProperty("errorCategories", out var expectedCategories)
        && !MatchesStringSet(CedarSharpErrorCategories(measured), expectedCategories))
    {
        return false;
    }

    return true;
}

bool MatchesLoweringExpectation(
    Dictionary<string, object?> measured,
    JsonElement expectation)
{
    var artifactsExported = measured["artifactsExported"] is true;
    if (artifactsExported != expectation.GetProperty("loweringArtifactsExported").GetBoolean())
    {
        return false;
    }
    if (!expectation.TryGetProperty("loweringExportedRuleIds", out var expectedIds))
    {
        return true;
    }

    var actualIds = (measured["exportedRuleIds"] as IEnumerable<string?> ?? Array.Empty<string?>())
        .Where(id => id is not null)
        .Select(id => id!)
        .ToHashSet(StringComparer.Ordinal);
    return MatchesStringSet(actualIds, expectedIds);
}

bool MatchesDeterminingRules(JsonElement actual, JsonElement expected)
{
    var actualIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var rule in actual.EnumerateArray())
    {
        actualIds.Add($"{rule.GetProperty("ruleIndex").GetInt32()}:{rule.GetProperty("cedarPolicyId").GetString()}");
    }
    var expectedIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var rule in expected.EnumerateArray())
    {
        expectedIds.Add($"{rule.GetProperty("ruleIndex").GetInt32()}:{rule.GetProperty("cedarPolicyId").GetString()}");
    }
    return actualIds.Count == actual.GetArrayLength()
        && expectedIds.Count == expected.GetArrayLength()
        && actualIds.SetEquals(expectedIds);
}

bool MatchesStringSet(HashSet<string> actual, JsonElement expected)
{
    if (expected.ValueKind != JsonValueKind.Array)
    {
        return false;
    }
    var expectedIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var value in expected.EnumerateArray())
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        expectedIds.Add(value.GetString()!);
    }
    return expectedIds.Count == expected.GetArrayLength()
        && actual.Count == expectedIds.Count
        && actual.SetEquals(expectedIds);
}

bool ValidateDifferenceCatalog(
    List<JsonElement> cases,
    Dictionary<string, JsonElement> rows,
    out List<string> errors)
{
    errors = [];
    var fixtureIds = new HashSet<string>(StringComparer.Ordinal);
    var fixtureReferences = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

    foreach (var caseElement in cases)
    {
        var caseId = caseElement.GetProperty("id").GetString()!;
        if (!fixtureIds.Add(caseId))
        {
            errors.Add($"duplicate fixture id {caseId}");
        }
        var expectation = caseElement.GetProperty("expectation");
        var agreement = expectation.GetProperty("agreement").GetString()!;
        var differenceIds = expectation.TryGetProperty("differenceIds", out var idsElement)
            ? idsElement.EnumerateArray().Select(id => id.GetString()!).ToList()
            : new List<string>();
        var uniqueIds = new HashSet<string>(differenceIds, StringComparer.Ordinal);
        if (uniqueIds.Count != differenceIds.Count)
        {
            errors.Add($"{caseId} repeats a difference id");
        }
        if (agreement == "expected-difference" && uniqueIds.Count == 0)
        {
            errors.Add($"{caseId} is an expected difference without a registered difference id");
        }
        if (agreement != "expected-difference" && uniqueIds.Count != 0)
        {
            errors.Add($"{caseId} is not an expected difference but names difference ids");
        }
        foreach (var differenceId in uniqueIds)
        {
            if (!rows.ContainsKey(differenceId))
            {
                errors.Add($"{caseId} names unknown difference id {differenceId}");
            }
        }
        fixtureReferences[caseId] = uniqueIds;
    }

    foreach (var (differenceId, row) in rows)
    {
        foreach (var required in new[]
        {
            "dimension",
            "agreementClass",
            "cases",
            "upstreamObservation",
            "d3Observation",
            "failClosedEffect",
            "caseObservations"
        })
        {
            if (!row.TryGetProperty(required, out _))
            {
                errors.Add($"{differenceId} is missing {required}");
            }
        }

        var agreementClass = row.GetProperty("agreementClass").GetString()!;
        if (agreementClass is not ("match" or "expected-difference"))
        {
            errors.Add($"{differenceId} has invalid agreement class {agreementClass}");
        }
        if (string.IsNullOrWhiteSpace(row.GetProperty("upstreamObservation").GetString())
            || string.IsNullOrWhiteSpace(row.GetProperty("d3Observation").GetString())
            || string.IsNullOrWhiteSpace(row.GetProperty("failClosedEffect").GetString()))
        {
            errors.Add($"{differenceId} has an empty semantic observation");
        }

        var rowCases = row.GetProperty("cases").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToList();
        var uniqueRowCases = new HashSet<string>(rowCases, StringComparer.Ordinal);
        if (uniqueRowCases.Count != rowCases.Count || rowCases.Count == 0)
        {
            errors.Add($"{differenceId} has an empty or repeated case list");
        }

        var observations = row.GetProperty("caseObservations");
        var observationIds = observations.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!observationIds.SetEquals(uniqueRowCases))
        {
            errors.Add($"{differenceId} case observations do not match its case list");
        }

        foreach (var caseId in uniqueRowCases)
        {
            if (!fixtureReferences.TryGetValue(caseId, out var references))
            {
                errors.Add($"{differenceId} names unknown fixture {caseId}");
                continue;
            }
            if (!references.Contains(differenceId))
            {
                errors.Add($"{differenceId} is not referenced by fixture {caseId}");
            }
            var fixtureAgreement = cases
                .First(candidate => candidate.GetProperty("id").GetString() == caseId)
                .GetProperty("expectation")
                .GetProperty("agreement")
                .GetString();
            if (fixtureAgreement != agreementClass)
            {
                errors.Add($"{differenceId} agreement class does not match fixture {caseId}");
            }
        }
    }

    foreach (var (caseId, references) in fixtureReferences)
    {
        foreach (var differenceId in references)
        {
            if (!rows.TryGetValue(differenceId, out var row))
            {
                continue;
            }
            var rowCases = row.GetProperty("cases").EnumerateArray()
                .Select(value => value.GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            if (!rowCases.Contains(caseId))
            {
                errors.Add($"{differenceId} does not list fixture {caseId}");
            }
        }
    }

    return errors.Count == 0;
}

bool ValidateDifferenceCase(
    string caseId,
    JsonElement expectation,
    string measuredAgreement,
    JsonElement upstream,
    Dictionary<string, object?> cedarSharp,
    Dictionary<string, JsonElement> rows)
{
    var differenceIds = expectation.TryGetProperty("differenceIds", out var idsElement)
        ? idsElement.EnumerateArray().Select(id => id.GetString()!).ToList()
        : [];
    if (differenceIds.Count == 0)
    {
        return measuredAgreement == "match";
    }

    foreach (var differenceId in differenceIds)
    {
        if (!rows.TryGetValue(differenceId, out var row))
        {
            return false;
        }
        if (row.GetProperty("agreementClass").GetString() != measuredAgreement)
        {
            return false;
        }
        var rowCases = row.GetProperty("cases").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        if (!rowCases.Contains(caseId)
            || !row.GetProperty("caseObservations").TryGetProperty(caseId, out var observation))
        {
            return false;
        }
        if (!MatchesDifferenceObservation(observation, upstream, cedarSharp))
        {
            return false;
        }
    }

    return true;
}

bool MatchesDifferenceObservation(
    JsonElement expected,
    JsonElement upstream,
    Dictionary<string, object?> cedarSharp)
{
    var expectedUpstream = expected.GetProperty("upstream");
    var upstreamDecision = upstream.GetProperty("decision").ValueKind == JsonValueKind.Null
        ? null
        : upstream.GetProperty("decision").GetString();
    var expectedUpstreamDecision = expectedUpstream.GetProperty("decision").ValueKind == JsonValueKind.Null
        ? null
        : expectedUpstream.GetProperty("decision").GetString();
    if (upstreamDecision != expectedUpstreamDecision
        || (upstream.GetProperty("errors").GetArrayLength() > 0)
            != expectedUpstream.GetProperty("hasErrors").GetBoolean()
        || !MatchesStringSet(
            UpstreamErrorCategories(upstream),
            expectedUpstream.GetProperty("errorCategories")))
    {
        return false;
    }

    var expectedD3 = expected.GetProperty("d3");
    var d3Decision = (string?)cedarSharp["decision"];
    var expectedD3Decision = expectedD3.GetProperty("decision").ValueKind == JsonValueKind.Null
        ? null
        : expectedD3.GetProperty("decision").GetString();
    return d3Decision == expectedD3Decision
        && (cedarSharp["hasErrors"] is true) == expectedD3.GetProperty("hasErrors").GetBoolean()
        && (cedarSharp["operationFailed"] is true) == expectedD3.GetProperty("operationFailed").GetBoolean()
        && MatchesStringSet(
            CedarSharpErrorCategories(cedarSharp),
            expectedD3.GetProperty("errorCategories"));
}

// Split the formatter-rendered Cedar export at top-level semicolons. This is
// calibration-only: the current D2 response exposes one concatenated text
// export rather than the per-rule body export required by the future D3
// contract. The probe pairs the split bodies with exported IDs only to drive
// this bounded, hand-authored corpus. It does not qualify production identity
// pairing, and D3-1 must replace this path with the exact per-rule export.
List<string> SplitPolicyBodies(string text)
{
    var bodies = new List<string>();
    var start = 0;
    var depth = 0;
    var inString = false;
    var escaped = false;
    for (var index = 0; index < text.Length; index++)
    {
        var current = text[index];
        if (inString)
        {
            if (escaped)
            {
                escaped = false;
            }
            else if (current == '\\')
            {
                escaped = true;
            }
            else if (current == '"')
            {
                inString = false;
            }
            continue;
        }

        switch (current)
        {
            case '"':
                inString = true;
                break;
            case '{':
            case '[':
            case '(':
                depth++;
                break;
            case '}':
            case ']':
            case ')':
                depth--;
                break;
            case ';' when depth == 0:
                bodies.Add(text[start..(index + 1)]);
                start = index + 1;
                break;
        }
    }
    if (inString || depth != 0)
    {
        throw new InvalidOperationException("The exported Cedar policy text is not balanced.");
    }
    if (start < text.Length && !string.IsNullOrWhiteSpace(text[start..]))
    {
        throw new InvalidOperationException("The exported Cedar policy text has content after its final semicolon.");
    }
    return bodies;
}

(int ExitCode, string Output, string Error) RunOracle(string exe, string input)
{
    var start = new ProcessStartInfo(exe)
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    using var process = Process.Start(start)!;
    process.StandardInput.Write(input);
    process.StandardInput.Close();
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, output, error);
}
