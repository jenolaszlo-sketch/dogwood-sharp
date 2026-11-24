using System.Collections.Concurrent;
using System.Text.Json;
using CedarPolicySet = CedarSharp.CedarPolicySet;
using DogwoodSharp;
using DogwoodSharp.Experimental.CedarSharp;
using Xunit;

namespace DogwoodSharp.CedarSharp.Tests;

/// <summary>
/// Focused, fail-closed tests for the experimental schema-carrying CedarSharp adapter. Expected
/// outcomes are derived from Cedar semantics and the D2-generated rule identities, independently of
/// the adapter's own output. Covers the decision, scope, entity, identity, temporal-guard,
/// malformed-input, clean-Allow, concurrency, and lifecycle families of the D3 contract, plus the
/// operation 3/operation 4 surface boundary that keeps the frozen D2 contract intact.
/// </summary>
public sealed class CedarSharpAdapterTests
{
    // User/Doc/Session with a Boolean attribute read and a contextual action.
    private const string Schema =
        "namespace App { entity User; entity Doc { secret: Bool }; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { count: Long } }; }";

    // A validated temporal lowering: the distincter and the nested request record let the generated
    // temporal field pass upstream validation, so the export stage runs and reports the reserved name.
    private const string ValidatedTemporalSource =
        "permit (principal, action == App::Action::\"read\", resource)\n" +
        "when temporal { formerly within 1h App::Action::\"read\"::request { input.count: 3 } };";
    private const string ValidatedTemporalSchema =
        "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], " +
        "context: { input: { count: Long } } }; }";

    private static DogwoodCedarAuthorizer Plan(string source, string schemaText = Schema)
    {
        // The public construction path performs the operation 4 export itself. An incomplete
        // export fails construction, so a test lowering that does not export throws here.
        return DogwoodCedarAuthorizer.ForLoweredBundle(source, new DogwoodSchema(schemaText));
    }

    private static CedarEntityRef User(string id) => new("App::User", id);
    private static CedarEntityRef Doc(string id) => new("App::Doc", id);
    private static CedarEntityRef Action(string id) => new("App::Action", id);

    private static CedarAuthorizationInput Request(
        CedarEntityRef principal, CedarEntityRef action, CedarEntityRef resource,
        string contextJson = "{}", string entitiesJson = "[]") =>
        new(principal, action, resource, contextJson, entitiesJson);

    [Fact]
    public void DirectPermitIsACleanAllowThatAuthorizes()
    {
        using var plan = Plan("permit(principal, action == App::Action::\"read\", resource);");
        var result = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), "{ \"count\": 5 }"));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarAuthorizationStage.Evaluation, result.Stage);
        Assert.Equal(CedarDecision.Allow, result.Decision);
        Assert.True(result.IsCleanAllow);
        Assert.True(result.Authorized);
        var id = plan.PolicyIds;
        Assert.Single(result.DeterminingPolicyIds);
        Assert.Contains(id[0], result.DeterminingPolicyIds);
        Assert.Empty(result.PolicyErrors);
    }

    [Fact]
    public void UnsatisfiedConditionIsACleanDefaultDeny()
    {
        using var plan = Plan("permit(principal, action == App::Action::\"read\", resource) when { context.count > 100 };");
        var result = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), "{ \"count\": 5 }"));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Deny, result.Decision);
        Assert.False(result.Authorized);
        Assert.Empty(result.DeterminingPolicyIds);
        Assert.Empty(result.PolicyErrors);
    }

    [Fact]
    public void SatisfiedForbidWinsOverPermitByIdentity()
    {
        using var plan = Plan(
            "forbid(principal, action == App::Action::\"read\", resource) when { context.count > 3 };" +
            "permit(principal, action == App::Action::\"read\", resource);");
        var forbidId = plan.PolicyIds[0]; // rule 0 lowered to policy_0
        var result = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), "{ \"count\": 5 }"));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Deny, result.Decision);
        Assert.False(result.Authorized);
        Assert.Equal(new[] { forbidId }, result.DeterminingPolicyIds);
        Assert.Equal(0, plan.RuleIndexFor(forbidId));
    }

    [Fact]
    public void MultiplePermitsPreserveTheDeterminingIdSet()
    {
        using var plan = Plan(
            "permit(principal, action == App::Action::\"read\", resource) when { context.count > 3 };" +
            "permit(principal, action == App::Action::\"read\", resource) when { resource == App::Doc::\"d1\" };");
        var ids = plan.PolicyIds; // policy_0, policy_1
        var result = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), "{ \"count\": 5 }"));

        Assert.Equal(CedarDecision.Allow, result.Decision);
        Assert.True(result.IsCleanAllow);
        Assert.True(result.Authorized);
        Assert.Equal(ids.OrderBy(x => x, StringComparer.Ordinal),
            result.DeterminingPolicyIds.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void RawAllowBesideAPolicyErrorDoesNotAuthorize()
    {
        // The first permit allows; the second reads an attribute of an absent entity and errors.
        using var plan = Plan(
            "permit(principal, action == App::Action::\"read\", resource) when { principal == principal };" +
            "permit(principal, action == App::Action::\"read\", resource) when { resource.secret };");
        var erroringId = plan.PolicyIds[1];
        var result = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), "{ \"count\": 5 }"));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Allow, result.Decision);
        Assert.False(result.IsCleanAllow);
        Assert.False(result.Authorized, "a raw Allow carrying a policy error must not authorize");
        var errorIds = result.PolicyErrors.Select(e => e.PolicyId).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(erroringId, errorIds);
    }

    [Fact]
    public void PrincipalOutsideAppliesToIsRejectedByValidation()
    {
        using var plan = Plan("permit(principal, action == App::Action::\"read\", resource);");
        // A Doc principal is outside read's appliesTo principal types.
        var result = plan.Authorize(Request(Doc("d1"), Action("read"), Doc("d2"), "{ \"count\": 5 }"));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
        Assert.Equal(CedarAuthorizationStage.SchemaOrRequestValidation, result.Stage);
    }

    [Fact]
    public void ResourceOutsideAppliesToIsRejectedByValidation()
    {
        using var plan = Plan("permit(principal, action == App::Action::\"read\", resource);");
        var result = plan.Authorize(Request(User("alice"), Action("read"), User("bob"), "{ \"count\": 5 }"));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.Equal(CedarAuthorizationStage.SchemaOrRequestValidation, result.Stage);
    }

    [Fact]
    public void UndeclaredActionIsRejectedByValidation()
    {
        using var plan = Plan("permit(principal, action == App::Action::\"read\", resource);");
        var result = plan.Authorize(Request(User("alice"), Action("delete"), Doc("d1"), "{ \"count\": 5 }"));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.Equal(CedarAuthorizationStage.SchemaOrRequestValidation, result.Stage);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void MalformedContextFailsClosedInConstruction(string contextJson)
    {
        using var plan = Plan("permit(principal, action == App::Action::\"read\", resource);");
        var result = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), contextJson));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
        Assert.Equal(CedarAuthorizationStage.RequestConstruction, result.Stage);
    }

    [Fact]
    public void EntitiesMustBeAJsonArray()
    {
        using var plan = Plan("permit(principal, action == App::Action::\"read\", resource);");
        var result = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), "{}", "{}"));

        Assert.True(result.OperationFailed);
        Assert.Equal(CedarAuthorizationStage.RequestConstruction, result.Stage);
    }

    [Fact]
    public void AnUnexportedLoweringThrowsRatherThanBuildingAnEmptySet()
    {
        // The operation 4 export ran but did not commit, so no bundle exists and nothing is built.
        var export = Dogwood.ExportCedar(
            "permit(principal, action == App::Action::\"read\", resource) when { context.nope > 3 };",
            new DogwoodSchema(Schema));
        Assert.False(export.ArtifactsExported);
        var exception = Assert.Throws<CedarBundleException>(
            () => DogwoodCedarAuthorizer.ForLoweredBundle(export));
        Assert.Equal(CedarBundleFailure.LoweringNotExported, exception.Failure);

        // The public construction path fails the same way rather than building an empty set.
        var publicFailure = Assert.Throws<CedarBundleException>(
            () => DogwoodCedarAuthorizer.ForLoweredBundle(
                "permit(principal, action == App::Action::\"read\", resource) when { context.nope > 3 };",
                new DogwoodSchema(Schema)));
        Assert.Equal(CedarBundleFailure.LoweringNotExported, publicFailure.Failure);
    }

    [Fact]
    public void OperationFourExportsWhatTheFrozenLoweringResultDoesNotCarry()
    {
        // Operations 3 and 4 run one pipeline, so their shared results agree exactly. Only operation
        // 4 reports the per-rule (id, body) pairs and the trusted generated context-field names; the
        // frozen operation 3 surface has no per-rule body property at all, which is what keeps the
        // archived D2 corpus green.
        const string Source =
            "permit(principal, action == App::Action::\"read\", resource) when { context.count > 3 };";
        var schema = new DogwoodSchema(Schema);
        var lowered = Dogwood.Lower(Source, schema);
        var export = Dogwood.ExportCedar(Source, schema);

        Assert.True(lowered.ArtifactsExported);
        Assert.True(export.ArtifactsExported);
        Assert.Equal(lowered.CedarPolicyText, export.CedarPolicyText);
        Assert.Equal(lowered.CedarSchemaJson, export.CedarSchemaJson);
        Assert.Equal(lowered.CedarSchemaText, export.CedarSchemaText);
        Assert.Equal(lowered.RuleCount, export.RuleCount);
        Assert.Equal(lowered.SelfContainedCedar, export.SelfContainedCedar);
        Assert.Equal(lowered.Validation.ValidationPassed, export.Validation.ValidationPassed);
        Assert.Equal(lowered.StageError, export.StageError);
        Assert.Equal(lowered.Rules.Length, export.Rules.Length);
        for (int index = 0; index < lowered.Rules.Length; index++)
        {
            Assert.Equal(lowered.Rules[index].RuleIndex, export.Rules[index].RuleIndex);
            Assert.Equal(lowered.Rules[index].CedarPolicyId, export.Rules[index].CedarPolicyId);
            Assert.Equal(lowered.Rules[index].SourceText, export.Rules[index].SourceText);
            Assert.False(string.IsNullOrEmpty(export.Rules[index].CedarPolicyBody));
        }

        Assert.Empty(export.GeneratedContextFields);
        Assert.Null(typeof(PolicyRuleMapping).GetProperty("CedarPolicyBody"));
        Assert.Null(typeof(DogwoodLoweredPolicySet).GetProperty("GeneratedContextFields"));

        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(export);
        Assert.Equal(
            export.Rules.Select(rule => rule.CedarPolicyId).OrderBy(id => id, StringComparer.Ordinal),
            plan.PolicyIds);
    }

    [Fact]
    public void DeterminingIdentityMapsBackToTheSourceRuleIndex()
    {
        using var plan = Plan(
            "permit(principal, action == App::Action::\"read\", resource) when { context.count > 3 };" +
            "forbid(principal, action == App::Action::\"read\", resource) when { context.count > 100 };");
        var permitId = plan.PolicyIds[0];
        var forbidId = plan.PolicyIds[1];
        Assert.Equal(0, plan.RuleIndexFor(permitId));
        Assert.Equal(1, plan.RuleIndexFor(forbidId));
        Assert.Equal(-1, plan.RuleIndexFor("not-in-bundle"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATemporalLoweringNeverAuthorizesThroughThePublicRequestPath(bool injectReservedField)
    {
        // The trusted temporal/provider pipeline is a separate, not-yet-authorized commission, so a
        // temporal lowering is never a clean authorization through this public path. It is fail-closed
        // at the lowering boundary when it cannot produce the trusted field, or refused by the adapter
        // when it can; both authorize nothing.
        var unexported = Dogwood.ExportCedar(
            "def temporal was_read(?count) { formerly within 1h App::Action::\"read\"::request { context.count: ?count } };\n" +
            "permit (principal, action == App::Action::\"read\", resource) when temporal { was_read(context.count) };",
            new DogwoodSchema(D2Fixture.DefaultPolicySchema));
        if (!unexported.ArtifactsExported)
        {
            // The lowering failed upstream validation, so no bundle exists and nothing is built.
            Assert.Throws<CedarBundleException>(() => DogwoodCedarAuthorizer.ForLoweredBundle(unexported));
            return;
        }

        Assert.False(unexported.SelfContainedCedar);
        var reserved = Assert.Single(unexported.GeneratedContextFields);

        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(unexported);
        var contextJson = injectReservedField
            ? "{\"count\": 5, \"" + reserved + "\": true}"
            : "{\"count\": 5}";
        var result = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), contextJson, "[]"));

        Assert.True(result.OperationFailed);
        Assert.False(result.Authorized);
        if (injectReservedField)
        {
            Assert.Contains(CedarDiagnosticCategory.ReservedContextField, result.Errors.Select(e => e.Category));
        }
        else
        {
            Assert.Equal(CedarAuthorizationStage.TrustedBindingUnavailable, result.Stage);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AValidatedTemporalExportRejectsTheReservedFieldAndNeverAuthorizes(bool injectReservedField)
    {
        // This temporal lowering passes upstream validation, so the operation 4 export reports the
        // reserved context field the trusted pipeline owns. The name is trusted pipeline output: a
        // caller that injects it is rejected before dispatch, and without it the trusted binding is
        // unavailable. Neither case authorizes, and neither substitutes a caller value.
        var export = Dogwood.ExportCedar(
            ValidatedTemporalSource, new DogwoodSchema(ValidatedTemporalSchema, Distincter: "verified"));
        Assert.True(export.ArtifactsExported);
        Assert.False(export.SelfContainedCedar);
        var reserved = Assert.Single(export.GeneratedContextFields);

        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(export);
        Assert.False(plan.SelfContained);
        var contextJson = injectReservedField
            ? "{\"input\": {\"count\": 5}, \"" + reserved + "\": true}"
            : "{\"input\": {\"count\": 5}}";
        var result = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), contextJson, "[]"));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
        if (injectReservedField)
        {
            Assert.Contains(CedarDiagnosticCategory.ReservedContextField, result.Errors.Select(e => e.Category));
        }
        else
        {
            Assert.Equal(CedarAuthorizationStage.TrustedBindingUnavailable, result.Stage);
        }
    }

    [Fact]
    public void TheExportedBodiesRebuildTheExportedPolicySetByIdentity()
    {
        // The per-rule bodies are the identity-bearing export: rebuilding a set from them reproduces
        // exactly the exported ids, while the concatenated text route is never used for identity.
        const string Source =
            "permit(principal, action == App::Action::\"read\", resource) when { context.count > 3 };" +
            "forbid(principal, action == App::Action::\"read\", resource) when { resource == App::Doc::\"d1\" };";
        var export = Dogwood.ExportCedar(Source, new DogwoodSchema(Schema));
        Assert.True(export.ArtifactsExported);

        var rebuilt = CedarPolicySet.FromPolicies(
            export.Rules.Select(rule => (rule.CedarPolicyId, rule.CedarPolicyBody)).ToArray());
        Assert.Equal(
            export.Rules.Select(rule => rule.CedarPolicyId).OrderBy(id => id, StringComparer.Ordinal),
            rebuilt.PolicyIds.OrderBy(id => id, StringComparer.Ordinal));
        foreach (var rule in export.Rules)
        {
            Assert.Contains(rule.CedarPolicyId, rebuilt.PolicyIds);
        }

        // Each exported body is one complete policy statement in its own right, which is what makes
        // the (id, body) pair a complete identity-bearing export.
        foreach (var rule in export.Rules)
        {
            Assert.True(
                rule.CedarPolicyBody.TrimStart().StartsWith("permit(", StringComparison.Ordinal) ||
                rule.CedarPolicyBody.TrimStart().StartsWith("forbid(", StringComparison.Ordinal),
                $"the exported body for {rule.CedarPolicyId} is not a policy statement");
        }
    }

    [Fact]
    public void ConcurrentAuthorizationsAreDeterministic()
    {
        using var plan = Plan("permit(principal, action == App::Action::\"read\", resource);");
        var expected = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), "{ \"count\": 5 }"));
        Assert.True(expected.Authorized);

        var results = new ConcurrentBag<CedarAuthorizationResult>();
        Parallel.For(0, 64, _ =>
        {
            var result = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), "{ \"count\": 5 }"));
            results.Add(result);
        });

        foreach (var result in results)
        {
            Assert.False(result.OperationFailed);
            Assert.Equal(CedarDecision.Allow, result.Decision);
            Assert.True(result.IsCleanAllow);
            Assert.True(result.Authorized);
            Assert.Equal(expected.DeterminingPolicyIds, result.DeterminingPolicyIds);
        }
    }

    [Fact]
    public void RepeatedUseAndDisposalDoNotChangeOutcomes()
    {
        using var plan = Plan("permit(principal, action == App::Action::\"read\", resource);");
        var request = Request(User("alice"), Action("read"), Doc("d1"), "{ \"count\": 5 }");
        var first = plan.Authorize(request);
        for (var i = 0; i < 32; i++)
        {
            var result = plan.Authorize(request);
            Assert.Equal(first.Authorized, result.Authorized);
            Assert.Equal(first.Decision, result.Decision);
            Assert.Equal(first.DeterminingPolicyIds, result.DeterminingPolicyIds);
        }

        plan.Dispose();
        plan.Dispose(); // idempotent, managed references only
        Assert.True(plan.Authorize(request).Authorized);
    }

    /// <summary>
    /// C-19: an errored forbid must not silently become a clean Deny. The rule identity and the
    /// error are preserved, and the raw Allow beside it is not enforcement.
    /// </summary>
    [Fact]
    public void ErroredForbidPreservesItsErrorAndIdentity()
    {
        const string sessionSchema =
            "namespace App { entity User; entity Session { token: String }; entity Doc; " +
            "action \"read\" appliesTo { principal: [Session], resource: [Doc], context: { count: Long } }; }";
        using var plan = Plan(
            "forbid(principal, action == App::Action::\"read\", resource) when { principal.token == \"t\" };" +
            "permit(principal, action == App::Action::\"read\", resource);",
            sessionSchema);
        var forbidId = plan.PolicyIds[0];
        var result = plan.Authorize(Request(
            new CedarEntityRef("App::Session", "s1"), Action("read"), Doc("d1"), "{ \"count\": 5 }"));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Allow, result.Decision);
        Assert.False(result.IsCleanAllow, "an errored forbid must not produce a clean Allow");
        Assert.False(result.Authorized, "a raw Allow beside an errored forbid must not authorize");
        Assert.Contains(forbidId, result.PolicyErrors.Select(error => error.PolicyId));
        Assert.Contains(
            CedarDiagnosticCategory.MissingEntity,
            result.PolicyErrors.Select(error => error.Category));
    }

    /// <summary>
    /// C-23: valid ipaddr, decimal and datetime extension values evaluate, and an invalid
    /// extension literal fails the validated lowering closed instead of authorizing.
    /// </summary>
    [Theory]
    [InlineData("ip(\"192.168.1.1\") == ip(\"192.168.1.1\")")]
    [InlineData("decimal(\"19.99\") == decimal(\"19.99\")")]
    [InlineData("datetime(\"2024-01-01T00:00:00Z\") == datetime(\"2024-01-01T00:00:00Z\")")]
    public void ValidExtensionValuesEvaluateIdentically(string comparison)
    {
        using var plan = Plan(
            $"permit(principal, action == App::Action::\"read\", resource) when {{ {comparison} }};");
        var result = plan.Authorize(Request(User("alice"), Action("read"), Doc("d1"), "{ \"count\": 5 }"));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Allow, result.Decision);
        Assert.True(result.IsCleanAllow);
        Assert.True(result.Authorized);
        Assert.Empty(result.PolicyErrors);
    }

    [Theory]
    [InlineData("ip(\"not-an-ip\") == ip(\"192.168.1.1\")")]
    [InlineData("decimal(\"not-a-decimal\") == decimal(\"1.00\")")]
    [InlineData("datetime(\"not-a-datetime\") == datetime(\"2024-01-01T00:00:00Z\")")]
    public void InvalidExtensionValuesFailClosedAtBundleConstruction(string comparison)
    {
        Assert.Throws<CedarBundleException>(() => Plan(
            $"permit(principal, action == App::Action::\"read\", resource) when {{ {comparison} }};"));
    }
}

/// <summary>Reads the pinned D2 lowering fixture's shared default action schema.</summary>
internal static class D2Fixture
{
    public static string DefaultPolicySchema { get; } = ReadSchema();

    private static string ReadSchema()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "lowering-v1.json");
        var manifest = JsonDocument.Parse(File.ReadAllBytes(path)).RootElement;
        if (manifest.GetProperty("fixtureVersion").GetInt32() != 1)
        {
            throw new InvalidOperationException("Unexpected lowering fixture version.");
        }

        return manifest.GetProperty("defaultPolicySchema").GetString()!;
    }
}
