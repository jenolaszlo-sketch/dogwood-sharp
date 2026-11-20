using CedarSharp;
using DogwoodSharp;
using Xunit;
using Xunit.Abstractions;

namespace CedarSharp.E1Prototype;

/// <summary>Negative controls: every failure mode must fail closed and stay classifiable.</summary>
public sealed class NegativeControlTests
{
    private readonly ITestOutputHelper output;

    public NegativeControlTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SchemaInvalidPolicy_IsRejectedBeforeAnyExport()
    {
        // context.count is not declared by the action schema, so the lowered
        // policy does not validate and no artifacts are exported.
        var lowered = Dogwood.Lower(
            "permit(principal, action == App::Action::\"read\", resource) when { context.count > 1 };",
            E1Corpus.Base);
        output.WriteLine($"exported={lowered.ArtifactsExported} errors={lowered.Validation.ValidationErrors.Length}");
        foreach (var error in lowered.Validation.ValidationErrors)
        {
            output.WriteLine($"{error.Category}: {error.Message}");
        }

        Assert.False(lowered.ArtifactsExported);
        Assert.False(lowered.Validation.IsValid);
        Assert.NotEmpty(lowered.Validation.ValidationErrors);
        Assert.Equal(string.Empty, lowered.CedarPolicyText);
        Assert.Equal(string.Empty, lowered.CedarSchemaJson);
    }

    [Fact]
    public void UnguardedOptionalAttributeAccess_IsRejectedByTheValidator()
    {
        // Cedar's validator refuses an unguarded read of an optional attribute,
        // so this policy never reaches CedarSharp at all.
        var lowered = Dogwood.Lower(
            "permit(principal, action == App::Action::\"read\", resource) when { resource.secret == true };",
            E1Corpus.Base);
        output.WriteLine($"exported={lowered.ArtifactsExported} errors={lowered.Validation.ValidationErrors.Length}");
        foreach (var error in lowered.Validation.ValidationErrors)
        {
            output.WriteLine($"{error.Category}: {error.Message}");
        }

        Assert.False(lowered.ArtifactsExported);
        Assert.NotEmpty(lowered.Validation.ValidationErrors);
    }

    [Fact]
    public void SyntaxError_IsReportedAsAParseDiagnostic()
    {
        var parse = Dogwood.Parse("permit(principal, action");
        Assert.False(parse.Success);
        Assert.NotEmpty(parse.Diagnostics);
        output.WriteLine(string.Join(Environment.NewLine, parse.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Fact]
    public void MalformedPolicyBody_IsRejectedWithPolicyAttribution()
    {
        var policies = CedarPolicySet.FromPolicies(
            ("good", "permit(principal, action, resource);"),
            ("bad", "permit(principal, action, resource"));
        var check = E1Requests.Engine.CheckPolicies(policies);
        output.WriteLine($"check success={check.IsSuccess} errors={check.Errors.Count}");
        foreach (var error in check.Errors)
        {
            output.WriteLine(error.Message);
        }

        Assert.False(check.IsSuccess);
        Assert.NotEmpty(check.Errors);
    }

    [Fact]
    public void MatchingPermitWithAnInvalidPolicySet_DoesNotProduceAnAllow()
    {
        // A valid permit next to an unparsable policy: the call must fail
        // closed as an operation failure, never as an Allow.
        var policies = CedarPolicySet.FromPolicies(
            ("good", "permit(principal, action, resource);"),
            ("bad", "permit(principal, action, resource"));
        var request = new CedarAuthorizationRequest(
            new CedarEntityUid("App::User", "alice"),
            new CedarEntityUid("App::Action", "read"),
            new CedarEntityUid("App::Doc", "d1"),
            policies,
            contextJson: "{}",
            entitiesJson: E1Corpus.EmptyEntities);
        var result = E1Requests.Engine.Authorize(request);
        output.WriteLine($"success={result.IsSuccess} decision={result.Decision} errors={result.Errors.Count}");
        foreach (var error in result.Errors)
        {
            output.WriteLine(error.Message);
        }

        Assert.False(result.IsSuccess);
        Assert.Null(result.Decision);
        Assert.False(result.IsCleanAllow);
    }

    [Fact]
    public void PolicyTextSplitter_RejectsContentAfterTheFinalPolicy()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CedarPolicyTextSplitter.Split("permit(principal, action, resource); trailing"));
    }

    [Fact]
    public void PolicyTextSplitter_KeepsSemicolonsInsideStringsAndBlocks()
    {
        var text =
            """
            permit(principal == App::User::"a;b", action, resource) when { resource.note == "x;y" };
            forbid(principal, action, resource);
            """;
        var bodies = CedarPolicyTextSplitter.Split(text);
        Assert.Equal(2, bodies.Count);
        Assert.Contains("a;b", bodies[0], StringComparison.Ordinal);
        Assert.Contains("x;y", bodies[0], StringComparison.Ordinal);
        Assert.StartsWith("forbid(", bodies[1], StringComparison.Ordinal);
    }
}
