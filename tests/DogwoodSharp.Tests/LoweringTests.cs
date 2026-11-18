using System.Text.Json;
using Xunit;

namespace DogwoodSharp.Tests;

/// <summary>
/// Compares the managed D2 operations against the pinned differential fixtures.
/// The fixture expectations are derived from the pinned Dogwood crate by
/// <c>native/dogwoodsharp-d2-diff</c> and are re-checked against upstream by the
/// native differential, so this suite closes the loop through the managed API.
/// </summary>
public sealed class LoweringTests
{
    [Fact]
    public void FixtureManifestMatchesTheLoadedNativeBridge()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "lowering-v1.json")));
        var manifest = document.RootElement;
        Assert.Equal(1, manifest.GetProperty("fixtureVersion").GetInt32());
        Assert.Equal(NativeBridge.Platform().Target, Dogwood.GetNativeVersion().Target);
        Assert.Equal(Dogwood.GetNativeVersion().DogwoodRevision,
            manifest.GetProperty("dogwoodRevision").GetString());
    }

    [Fact]
    public void FixtureCasesMatchValidationAndLowering()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "lowering-v1.json")));
        var shared = document.RootElement.GetProperty("defaultPolicySchema").GetString()
            ?? throw new InvalidOperationException("defaultPolicySchema is null");
        var failures = new List<string>();

        foreach (var fixture in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var id = fixture.GetProperty("id").GetString()!;
            var source = fixture.GetProperty("source").GetString()!;
            var schema = new DogwoodSchema(
                Optional(fixture, "policySchema") ?? shared,
                Optional(fixture, "eventSchema"),
                Optional(fixture, "macros"),
                Optional(fixture, "distincter"));
            var expected = fixture.GetProperty("expect");

            try
            {
                // A distincter that is not a Cedar identifier is rejected by the
                // managed layer before any native call. The pinned stage error
                // records that same boundary from the bridge's side.
                if (schema.Distincter is not null && !ValidDistincter(schema.Distincter))
                {
                    var error = Assert.Throws<ArgumentException>(() => Dogwood.Lower(source, schema));
                    Assert.Contains("Cedar identifier", error.Message, StringComparison.Ordinal);
                    var stage = expected.GetProperty("stageError");
                    Assert.Equal("lower", stage.GetProperty("stage").GetString());
                    Assert.Equal("distincter", stage.GetProperty("category").GetString());
                    continue;
                }

                var lowered = Dogwood.Lower(source, schema);
                Check(id, "lower", expected, lowered.Parsed, lowered.PolicyCount,
                    lowered.Lowered, lowered.RuleCount, lowered.SelfContainedCedar,
                    lowered.StageError is null ? null : $"{StageName(lowered.StageError.Stage)}/{CategoryName(lowered.StageError.Category)}",
                    lowered.Rules, lowered.CedarPolicyText, lowered.CedarSchemaJson, lowered.CedarSchemaText,
                    failures);

                if (schema.Distincter is null)
                {
                    var validated = Dogwood.Validate(source, schema);
                    Check(id, "validate", expected, validated.Parsed, validated.PolicyCount,
                        validated.Lowered, validated.RuleCount, validated.SelfContainedCedar,
                        validated.StageError is null ? null : $"{StageName(validated.StageError.Stage)}/{CategoryName(validated.StageError.Category)}",
                        validated.Rules, null, null, null, failures);

                    Assert.Equal(expected.GetProperty("validationPassed").GetBoolean(),
                        validated.ValidationPassed);
                    AssertFindings(expected.GetProperty("validationErrors"),
                        validated.ValidationErrors, id, "validationErrors");
                    AssertFindings(expected.GetProperty("validationWarnings"),
                        validated.ValidationWarnings, id, "validationWarnings");
                }
                else
                {
                    var validateError = Assert.Throws<ArgumentException>(() => Dogwood.Validate(source, schema));
                    Assert.Contains("distincter", validateError.Message, StringComparison.Ordinal);
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{id}: {exception.GetType().Name}: {exception.Message} @ {exception.StackTrace}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static string StageName(DogwoodStage stage) => stage switch
    {
        DogwoodStage.ServiceSchema => "service-schema",
        DogwoodStage.Lower => "lower",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown pipeline stage."),
    };

    private static string CategoryName(DogwoodFailureCategory category) => category switch
    {
        DogwoodFailureCategory.EventSchema => "event-schema",
        DogwoodFailureCategory.Policy => "policy",
        DogwoodFailureCategory.Schema => "schema",
        DogwoodFailureCategory.Distincter => "distincter",
        DogwoodFailureCategory.Internal => "internal",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown failure category."),
    };

    private static bool ValidDistincter(string distincter) =>
        distincter.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(
            distincter, "^[_A-Za-z][_A-Za-z0-9]*$");

    private static string? Optional(JsonElement fixture, string name)
    {
        return fixture.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;
    }

    private static void Check(
        string id,
        string operation,
        JsonElement expected,
        bool parsed,
        int? policyCount,
        bool lowered,
        int ruleCount,
        bool selfContainedCedar,
        string? stageError,
        System.Collections.Immutable.ImmutableArray<PolicyRuleMapping> rules,
        string? cedarPolicyText,
        string? cedarSchemaJson,
        string? cedarSchemaText,
        List<string> failures)
    {
        void Compare(string name, bool want, bool actual)
        {
            if (want != actual)
            {
                failures.Add($"{id}/{operation}: {name} is {actual}, fixture says {want}");
            }
        }

        void CompareInt(string name, int want, int actual)
        {
            if (want != actual)
            {
                failures.Add($"{id}/{operation}: {name} is {actual}, fixture says {want}");
            }
        }

        Compare("parsed", expected.GetProperty("parsed").GetBoolean(), parsed);
        Compare("lowered", expected.GetProperty("lowered").GetBoolean(), lowered);
        Compare("selfContainedCedar",
            expected.GetProperty("selfContainedCedar").GetBoolean(), selfContainedCedar);
        CompareInt("ruleCount", expected.GetProperty("ruleCount").GetInt32(), ruleCount);

        var expectedCount = expected.GetProperty("policyCount");
        if (expectedCount.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(policyCount);
        }
        else
        {
            Assert.Equal(expectedCount.GetInt32(), policyCount);
        }

        var expectedStage = expected.GetProperty("stageError");
        if (expectedStage.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(stageError);
        }
        else
        {
            var want = $"{expectedStage.GetProperty("stage").GetString()}/" +
                       $"{expectedStage.GetProperty("category").GetString()}";
            Assert.Equal(want, stageError);
        }

        var expectedRules = expected.GetProperty("rules").GetArrayLength();
        Assert.Equal(expectedRules, rules.Length);
        for (int index = 0; index < expectedRules; index++)
        {
            var expectedRule = expected.GetProperty("rules")[index];
            var rule = rules[index];
            Assert.Equal(expectedRule.GetProperty("ruleIndex").GetInt32(), rule.RuleIndex);
            Assert.Equal(expectedRule.GetProperty("cedarPolicyId").GetString(), rule.CedarPolicyId);
        }

        // Only the lowering operation reports artifacts, and only for a completed
        // lowering, so compare them only when this call returned them.
        if (operation == "lower" && lowered)
        {
            Assert.Equal(expected.GetProperty("cedarPolicyText").GetString(), cedarPolicyText);
            AssertJson(expected.GetProperty("cedarSchemaJson"), cedarSchemaJson!);
            Assert.Equal(expected.GetProperty("cedarSchemaText").GetString(), cedarSchemaText);
            foreach (var expectedRule in expected.GetProperty("rules").EnumerateArray())
            {
                var mapping = Assert.Single(rules,
                    rule => rule.CedarPolicyId == expectedRule.GetProperty("cedarPolicyId").GetString());
                Assert.Equal(expectedRule.GetProperty("sourceText").GetString(), mapping.SourceText);
            }
        }
    }

    private static void AssertFindings(
        JsonElement expected,
        System.Collections.Immutable.ImmutableArray<PolicyValidationFinding> actual,
        string id,
        string name)
    {
        var expectedFindings = expected.GetArrayLength();
        Assert.Equal(expectedFindings, actual.Length);
        for (int index = 0; index < expectedFindings; index++)
        {
            var want = expected[index];
            var finding = actual[index];
            Assert.Equal(want.GetProperty("category").GetString(),
                finding.Category.ToString().ToLowerInvariant());
            var location = want.GetProperty("location");
            if (location.ValueKind == JsonValueKind.Null)
            {
                Assert.Null(finding.Location);
            }
            else
            {
                Assert.Equal(new PolicySourceLocation(
                    location.GetProperty("offset").GetInt32(),
                    location.GetProperty("length").GetInt32()), finding.Location);
            }

            Assert.False(string.IsNullOrEmpty(finding.Message), $"{id}/{name}");
        }
    }

    private static void AssertJson(JsonElement expected, string actual)
    {
        using var actualDocument = JsonDocument.Parse(actual);
        Assert.True(JsonEquals(expected, actualDocument.RootElement),
            $"the augmented Cedar schema differs:\nexpected {expected.GetRawText()}\nactual {actualDocument.RootElement.GetRawText()}");
    }

    private static bool JsonEquals(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return false;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedProperties = expected.EnumerateObject().ToDictionary(x => x.Name, x => x.Value);
                var actualProperties = actual.EnumerateObject().ToDictionary(x => x.Name, x => x.Value);
                if (expectedProperties.Count != actualProperties.Count)
                {
                    return false;
                }

                foreach (var (name, value) in expectedProperties)
                {
                    if (!actualProperties.TryGetValue(name, out var other) || !JsonEquals(value, other))
                    {
                        return false;
                    }
                }

                return true;
            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToArray();
                var actualItems = actual.EnumerateArray().ToArray();
                if (expectedItems.Length != actualItems.Length)
                {
                    return false;
                }

                for (int index = 0; index < expectedItems.Length; index++)
                {
                    if (!JsonEquals(expectedItems[index], actualItems[index]))
                    {
                        return false;
                    }
                }

                return true;
            case JsonValueKind.String:
                return expected.GetString() == actual.GetString();
            case JsonValueKind.Number:
                return expected.GetRawText() == actual.GetRawText();
            case JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null:
                return true;
            default:
                return false;
        }
    }
}
