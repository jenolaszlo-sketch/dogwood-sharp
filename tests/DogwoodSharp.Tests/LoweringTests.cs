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
        var shared = document.RootElement.GetProperty("defaultPolicySchema").GetString()!;
        var failures = new List<string>();
        foreach (var fixture in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var id = fixture.GetProperty("id").GetString()!;
            var source = fixture.GetProperty("source").GetString()!;
            var schema = new DogwoodSchema(
                Optional(fixture, "policySchema") ?? shared, Optional(fixture, "eventSchema"),
                Optional(fixture, "macros"), Optional(fixture, "distincter"));
            try
            {
                CheckFixture(id, fixture, source, schema);
            }
            catch (Exception exception)
            {
                failures.Add($"{id}: {exception}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static void CheckFixture(string id, JsonElement fixture, string source, DogwoodSchema schema)
    {
        var expected = fixture.GetProperty("expect");
        if (id == "invalid-distincter")
        {
            Assert.Throws<ArgumentException>(() => Dogwood.Validate(source, schema));
            Assert.Throws<ArgumentException>(() => Dogwood.Lower(source, schema));
            return;
        }

        var validated = Dogwood.Validate(source, schema);
        var lowered = Dogwood.Lower(source, schema);
        bool hasCollision = fixture.TryGetProperty("wrapperCollision", out var collision);
        bool valid = expected.GetProperty("validationPassed").GetBoolean() && !hasCollision;
        Assert.Equal(valid, lowered.ArtifactsExported);
        Assert.Equal(valid, lowered.IsLowered);
        foreach (var outcome in new[] { validated, lowered.Validation })
        {
            Assert.Equal(expected.GetProperty("parsed").GetBoolean(), outcome.Parsed);
            Assert.Equal(expected.GetProperty("lowered").GetBoolean(), outcome.Lowered);
            Assert.Equal(expected.GetProperty("policyCount").Deserialize<int?>(), outcome.PolicyCount);
            Assert.Equal(expected.GetProperty("ruleCount").GetInt32(), outcome.RuleCount);
            Assert.Equal(expected.GetProperty("selfContainedCedar").GetBoolean(), outcome.SelfContainedCedar);
            Assert.Equal(expected.GetProperty("parseDiagnostics").GetArrayLength(), outcome.ParseDiagnostics.Length);
            var upstreamStage = expected.GetProperty("stageError");
            Assert.Equal(upstreamStage.ValueKind == JsonValueKind.Null ||
                upstreamStage.GetProperty("stage").GetString() != "service-schema", outcome.ServiceSchemaBuilt);
            Assert.Equal(outcome.Parsed, outcome.ActionSchemaBuilt);
            Assert.Equal(outcome.Lowered && !hasCollision, outcome.CollisionCheckPassed);
            Assert.Equal(valid, outcome.ValidationPassed);
            Assert.Equal(valid, outcome.IsValid);
            if (hasCollision)
            {
                Assert.Equal(DogwoodStage.CollisionCheck, outcome.StageError?.Stage);
                Assert.Equal(DogwoodFailureCategory.GeneratedContextFieldCollision, outcome.StageError?.Category);
                Assert.Equal(new DogwoodContextFieldCollision(collision.GetProperty("field").GetString()!,
                    collision.GetProperty("actionNamespace").GetString(), collision.GetProperty("actionId").GetString()!),
                    outcome.Collision);
                Assert.Empty(outcome.ValidationErrors);
                Assert.Empty(outcome.ValidationWarnings);
            }
            else
            {
                Assert.Null(outcome.Collision);
                if (upstreamStage.ValueKind == JsonValueKind.Null)
                {
                    Assert.Null(outcome.StageError);
                }
                else
                {
                    var stage = upstreamStage.GetProperty("stage").GetString() == "service-schema"
                        ? DogwoodStage.ServiceSchema
                        : outcome.ActionSchemaBuilt ? DogwoodStage.Lower : DogwoodStage.ActionSchema;
                    Assert.Equal(stage, outcome.StageError?.Stage);
                    Assert.Equal(upstreamStage.GetProperty("category").GetString(),
                        outcome.StageError?.Category.ToString().ToLowerInvariant());
                }

                AssertFindings(expected.GetProperty("validationErrors"), outcome.ValidationErrors, id, "errors");
                AssertFindings(expected.GetProperty("validationWarnings"), outcome.ValidationWarnings, id, "warnings");
            }
        }

        if (!valid)
        {
            Assert.Empty(validated.Rules);
            Assert.Empty(lowered.Rules);
            Assert.Empty(lowered.CedarPolicyText);
            Assert.Empty(lowered.CedarSchemaJson);
            Assert.Empty(lowered.CedarSchemaText);
            return;
        }

        var expectedRules = expected.GetProperty("rules");
        Assert.Equal(expectedRules.GetArrayLength(), validated.Rules.Length);
        Assert.Equal(expectedRules.GetArrayLength(), lowered.Rules.Length);
        foreach (var expectedRule in expectedRules.EnumerateArray())
        {
            int index = expectedRule.GetProperty("ruleIndex").GetInt32();
            string policyId = expectedRule.GetProperty("cedarPolicyId").GetString()!;
            Assert.Equal(new PolicyRuleMapping(index, policyId, null), validated.Rules[index]);
            Assert.Equal(new PolicyRuleMapping(index, policyId, expectedRule.GetProperty("sourceText").GetString(),
                    Optional(expectedRule, "cedarBody")),
                lowered.Rules[index]);
        }

        Assert.Equal(expected.GetProperty("cedarPolicyText").GetString(), lowered.CedarPolicyText);
        AssertJson(expected.GetProperty("cedarSchemaJson"), lowered.CedarSchemaJson);
        Assert.Equal(expected.GetProperty("cedarSchemaText").GetString(), lowered.CedarSchemaText);
        Assert.Equal(
            expected.GetProperty("generatedContextFields").EnumerateArray().Select(field => field.GetString()!).ToList(),
            lowered.GeneratedContextFields);
    }

    private static string? Optional(JsonElement fixture, string name) =>
        fixture.TryGetProperty(name, out var value) ? value.GetString() : null;

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
