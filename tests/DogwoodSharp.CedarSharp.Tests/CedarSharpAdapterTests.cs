using System.Collections.Concurrent;
using System.Text.Json;
using DogwoodSharp;
using DogwoodSharp.Experimental.CedarSharp;
using Xunit;

namespace DogwoodSharp.CedarSharp.Tests;

/// <summary>
/// Focused, fail-closed tests for the experimental schema-carrying CedarSharp adapter. Expected
/// outcomes are derived from Cedar semantics and the D2-generated rule identities, independently of
/// the adapter's own output. Covers the decision, scope, entity, identity, temporal-guard,
/// malformed-input, clean-Allow, concurrency, and lifecycle families of the D3 contract.
/// </summary>
public sealed class CedarSharpAdapterTests
{
    // User/Doc/Session with a Boolean attribute read and a contextual action.
    private const string Schema =
        "namespace App { entity User; entity Doc { secret: Bool }; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { count: Long } }; }";

    private static DogwoodCedarAuthorizer Plan(string source, string schemaText = Schema)
    {
        var lowered = Dogwood.Lower(source, new DogwoodSchema(schemaText));
        Assert.True(lowered.ArtifactsExported, "the test lowering must export a complete bundle");
        return DogwoodCedarAuthorizer.ForLoweredBundle(lowered);
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
        var lowered = Dogwood.Lower(
            "permit(principal, action == App::Action::\"read\", resource) when { context.nope > 3 };",
            new DogwoodSchema(Schema));
        Assert.False(lowered.ArtifactsExported);
        var exception = Assert.Throws<CedarBundleException>(
            () => DogwoodCedarAuthorizer.ForLoweredBundle(lowered));
        Assert.Equal(CedarBundleFailure.LoweringNotExported, exception.Failure);
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
        var lowered = Dogwood.Lower(
            "def temporal was_read(?count) { formerly within 1h App::Action::\"read\"::request { context.count: ?count } };\n" +
            "permit (principal, action == App::Action::\"read\", resource) when temporal { was_read(context.count) };",
            new DogwoodSchema(D2Fixture.DefaultPolicySchema));
        if (!lowered.ArtifactsExported)
        {
            Assert.Throws<CedarBundleException>(() => DogwoodCedarAuthorizer.ForLoweredBundle(lowered));
            return;
        }

        Assert.False(lowered.SelfContainedCedar);
        var reserved = Assert.Single(lowered.GeneratedContextFields);

        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(lowered);
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
