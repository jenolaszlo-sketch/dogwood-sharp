using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace DogwoodSharp.Tests;

public sealed class D2ContractTests
{
    private const string Read = """action == App::Action::"read" """;
    private const string Group = """action in App::Action::"group" """;
    private const string Event = """when temporal { formerly within 1h App::Action::"read"::request { input.count: 3 } }""";
    private const string Field = "policy_0__temporal_0";
    private const string ValidSchema = """
        namespace App {
          entity User;
          entity Doc;
          action "read" appliesTo {
            principal: [User], resource: [Doc], context: { input: { count: Long } }
          };
        }
        """;

    [Theory]
    [InlineData("policy_0__temporal_0: String", null, Field)]
    [InlineData("policy_0__temporal_0: Bool", null, Field)]
    [InlineData("policy_0__temporal_0?: Bool", null, Field)]
    [InlineData("policy_0__temporal_0: Undefined", null, Field)]
    [InlineData("custom_0__temporal_0: Bool", "custom", "custom_0__temporal_0")]
    public void ExplicitScopeRejectsDeclaredGeneratedFields(string declaration, string? distincter, string field)
    {
        var schema = new DogwoodSchema(
            ValidSchema.Replace("count: Long } }", $"count: Long }}, {declaration} }}", StringComparison.Ordinal),
            Distincter: distincter);
        AssertCollision(Temporal(Read), schema, field, "read");
    }

    [Fact]
    public void ResolvesSharedContextAliasesInTheirDeclaringNamespace()
    {
        var actionSchema = ValidSchema
            .Replace("context: { input: { count: Long } }", "context: Common::Alias", StringComparison.Ordinal);
        var schema = new DogwoodSchema("\uFEFF" + """
            namespace Common {
              type Shared = { input: { count: Long }, policy_0__temporal_0: Bool };
              type Alias = Shared;
            }
            """ + "\n" + actionSchema);

        AssertCollision(Temporal(Read), schema, Field, "read");
    }

    [Theory]
    [InlineData(Group)]
    [InlineData("action")]
    [InlineData("""action in [App::Action::"read", App::Action::"write"] """)]
    public void ChecksEveryAffectedConcreteAction(string scope)
    {
        var schema = new DogwoodSchema(Actions(writeDeclaration: ", policy_0__temporal_0: Bool"));
        AssertCollision(Temporal(scope), schema, Field, "write");
    }

    [Fact]
    public void RepeatedCallsReportTheSameFirstCollisionAcrossAffectedActions()
    {
        var text = Actions(", policy_0__temporal_0: Bool")
            .Replace("count: Long } }", "count: Long }, policy_0__temporal_0: Bool }", StringComparison.Ordinal);
        var schema = new DogwoodSchema(text);
        for (int index = 0; index < 3; index++)
        {
            AssertCollision(Temporal(Group), schema, Field, "read");
        }
    }

    [Theory]
    [InlineData("policy_1__temporal_0", false)]
    [InlineData("policy_0__temporal_1", true)]
    public void ChecksEveryGeneratedField(string field, bool sameRule)
    {
        string source = sameRule
            ? $"permit (principal, {Read}, resource) {Event} {Event};"
            : $"{Temporal(Read)}\n{Temporal(Read)}";
        var schema = new DogwoodSchema(ValidSchema.Replace("count: Long } }",
            $"count: Long }}, {field}: Bool }}", StringComparison.Ordinal));
        AssertCollision(source, schema, field, "read");
    }

    [Fact]
    public void ValidTemporalExportIgnoresNamesOnUntargetedActionsAndSimilarNames()
    {
        string source = Temporal(Read);
        var schema = new DogwoodSchema(Actions(", custom_0__temporal_0: Bool")
            .Replace("count: Long } }", "count: Long }, custom_0__temporal_99: Bool }", StringComparison.Ordinal),
            Distincter: "custom");
        var result = Dogwood.Lower(source, schema);
        Assert.True(result.ArtifactsExported, Describe(result));
        Assert.False(result.SelfContainedCedar);
        Assert.Contains("context.custom_0__temporal_0", result.CedarPolicyText, StringComparison.Ordinal);
        Assert.Contains("custom_0__temporal_99", result.CedarSchemaText, StringComparison.Ordinal);
        Assert.Equal("custom_0", Assert.Single(result.Rules).CedarPolicyId);
        Assert.Equal("custom_0", Assert.Single(Dogwood.Validate(source, schema).Rules).CedarPolicyId);
    }

    [Fact]
    public void ActionSchemaConstructionFailurePrecedesLowering()
    {
        string deep = new string('<', 100);
        var result = Dogwood.Lower("permit(principal, action, resource);", new DogwoodSchema(deep));
        Assert.True(result.Validation.ServiceSchemaBuilt);
        Assert.True(result.Parsed);
        Assert.False(result.Validation.ActionSchemaBuilt);
        Assert.False(result.Lowered);
        Assert.Equal(DogwoodStage.ActionSchema, result.StageError?.Stage);
        AssertEmptyExport(result);
    }

    [Fact]
    public void PreservesWarningsWithoutErrors()
    {
        const string source = "permit(principal, action, resource) when { false };";
        var result = Dogwood.Lower(source, new DogwoodSchema(ValidSchema));
        Assert.True(result.ArtifactsExported, Describe(result));
        Assert.Empty(result.Validation.ValidationErrors);
        Assert.Null(Assert.Single(result.Validation.ValidationWarnings).Location);
    }

    [Fact]
    public void FailureWithDistincterPreservesExactFindingsAndSuppressesExport()
    {
        var schema = new DogwoodSchema(ValidSchema, Distincter: "batch");
        const string source = "permit(principal, action, resource) when { context.unknown > 3 };";
        var validated = Dogwood.Validate(source, schema);
        var result = Dogwood.Lower(source, schema);
        Assert.True(result.Lowered);
        Assert.True(result.Validation.CollisionCheckPassed);
        Assert.False(result.Validation.IsValid);
        Assert.NotEmpty(result.Validation.ValidationErrors);
        Assert.Equal(validated.ValidationErrors.AsEnumerable(), result.Validation.ValidationErrors.AsEnumerable());
        AssertEmptyExport(result);
    }

    [Fact]
    public async Task IndependentConcurrentCallsKeepImmutableSnapshots()
    {
        string source = Temporal(Read);
        var colliding = new DogwoodSchema(ValidSchema.Replace("count: Long } }",
            "count: Long }, policy_0__temporal_0: Bool }", StringComparison.Ordinal));
        var failed = Dogwood.Lower(source, colliding);
        AssertEmptyExport(failed);
        var success = Dogwood.Lower(source, new DogwoodSchema(ValidSchema));
        Assert.True(success.ArtifactsExported, Describe(success));
        var altered = success.Rules.SetItem(0, success.Rules[0] with { CedarPolicyId = "other" });
        Assert.NotEqual(altered[0], success.Rules[0]);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            Dogwood.Lower(source, index % 2 == 0 ? colliding :
                new DogwoodSchema(ValidSchema, Distincter: $"batch_{index}")))));

        for (int index = 0; index < results.Length; index++)
        {
            if (index % 2 == 0)
            {
                Assert.Equal(failed.Validation.Collision, results[index].Validation.Collision);
                AssertEmptyExport(results[index]);
            }
            else
            {
                Assert.True(results[index].ArtifactsExported, Describe(results[index]));
                Assert.Equal($"batch_{index}_0", Assert.Single(results[index].Rules).CedarPolicyId);
            }
        }

        Assert.Equal("policy_0", Assert.Single(success.Rules).CedarPolicyId);
        Assert.Equal(new DogwoodContextFieldCollision(Field, "App", "read"), failed.Validation.Collision);
    }

    [Theory]
    [InlineData("parsed", "\"true\"")]
    [InlineData("lowered", "false")]
    [InlineData("actionSchemaBuilt", "false")]
    [InlineData("collisionCheckPassed", "false")]
    [InlineData("validationPassed", "false")]
    [InlineData("artifactsExported", "false")]
    [InlineData("ruleCount", "-1")]
    [InlineData("rules", "[]")]
    [InlineData("cedarPolicyText", "null")]
    [InlineData("cedarSchemaJson", "\"[]\"")]
    [InlineData("validationErrors", """[{"category":"cedar","message":"error","location":null}]""")]
    [InlineData("collision", """{"field":"f","actionNamespace":null,"actionId":"read"}""")]
    public void RejectsContradictoryNativeReplies(string property, string replacement)
    {
        var reply = JsonNode.Parse("""
            {"serviceSchemaBuilt":true,"parsed":true,"policyCount":1,"parseDiagnostics":[],
             "actionSchemaBuilt":true,"lowered":true,"ruleCount":1,"selfContainedCedar":true,
             "collisionCheckPassed":true,"collision":null,"validationPassed":true,
             "validationErrors":[],"validationWarnings":[],"artifactsExported":true,"stageError":null,
             "rules":[{"ruleIndex":0,"cedarPolicyId":"policy_0","sourceText":"permit(principal,action,resource);"}],
             "cedarPolicyText":"permit(principal,action,resource);","cedarSchemaJson":"{}","cedarSchemaText":""}
            """)!.AsObject();
        using (var positive = JsonDocument.Parse(reply.ToJsonString()))
        {
            Assert.True(NativeBridge.ReadLoweredResult(positive.RootElement, 40).ArtifactsExported);
        }

        reply[property] = JsonNode.Parse(replacement);
        using var malformed = JsonDocument.Parse(reply.ToJsonString());
        var exception = Assert.Throws<DogwoodBridgeException>(() =>
            NativeBridge.ReadLoweredResult(malformed.RootElement, 40));
        Assert.Equal(DogwoodBridgeFailure.MalformedResponse, exception.Failure);
    }

    private static string Temporal(string action) => $"permit (principal, {action}, resource) {Event};";

    private static string Actions(string writeDeclaration)
    {
        string schema = ValidSchema.Replace("action \"read\" appliesTo",
            "action \"group\"; action \"nested\" in [\"group\"]; action \"read\" in [\"nested\"] appliesTo",
            StringComparison.Ordinal);
        return schema.Insert(schema.LastIndexOf('}'), $$"""
            action "write" in ["nested"] appliesTo {
              principal: [User], resource: [Doc], context: { input: { count: Long }{{writeDeclaration}} }
            };
            """);
    }

    private static void AssertCollision(string source, DogwoodSchema schema, string field, string action)
    {
        var lowered = Dogwood.Lower(source, schema);
        AssertEmptyExport(lowered);
        foreach (var result in new[] { lowered.Validation, Dogwood.Validate(source, schema) })
        {
            Assert.True(result.Lowered, Describe(lowered));
            Assert.False(result.CollisionCheckPassed);
            Assert.False(result.ValidationPassed);
            Assert.Equal(DogwoodStage.CollisionCheck, result.StageError?.Stage);
            Assert.Equal(DogwoodFailureCategory.GeneratedContextFieldCollision, result.StageError?.Category);
            Assert.Equal(new DogwoodContextFieldCollision(field, "App", action), result.Collision);
            Assert.Empty(result.Rules);
            Assert.Empty(result.ValidationErrors);
        }
    }

    private static string Describe(DogwoodLoweredPolicySet result) => result.StageError?.Message ??
        string.Join("\n", result.Validation.ValidationErrors.Select(finding => finding.Message));

    private static void AssertEmptyExport(DogwoodLoweredPolicySet result)
    {
        Assert.False(result.ArtifactsExported);
        Assert.False(result.IsLowered);
        Assert.Empty(result.CedarPolicyText);
        Assert.Empty(result.CedarSchemaJson);
        Assert.Empty(result.CedarSchemaText);
        Assert.Empty(result.Rules);
    }
}
