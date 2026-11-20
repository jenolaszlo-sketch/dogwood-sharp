using CedarSharp;
using DogwoodSharp;
using Xunit;
using Xunit.Abstractions;

namespace CedarSharp.E1Prototype;

/// <summary>
/// E1 policy-ID preservation evidence. Dogwood exports generated Cedar policy
/// IDs separately from the rendered policy text; reparsing concatenated text
/// renumbers positionally (Cedar's text parser assigns policy0..policyN and
/// ignores @id for identity), so only the ID-to-body pairing preserves them.
/// </summary>
public sealed class PolicyIdPreservationTests
{
    private readonly ITestOutputHelper output;

    public PolicyIdPreservationTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void FromPolicies_PreservesEveryExportedId()
    {
        var bundle = E1Bundle.FromDogwood(
            string.Join(" ", Enumerable.Range(0, 12).Select(count =>
                $"permit(principal, action == App::Action::\"read\", resource) when {{ context.count > {count} }};")),
            E1Corpus.Count);
        Assert.Equal(12, bundle.PolicyIds.Length);

        // The exported IDs are in Dogwood rule order.
        var exported = Enumerable.Range(0, 12).Select(count => $"e1_{count}").ToArray();
        Assert.Equal(exported, bundle.PolicyIds.ToArray());

        // CedarSharp keeps every ID, and the managed policy set enumerates them
        // in the order they were supplied. The native Cedar policy map is
        // ordered by ID internally, so an adapter must still map Cedar policy
        // IDs back to Dogwood rules by identity and never by position.
        Assert.Equal(exported, bundle.PolicySet.PolicyIds.ToArray());
        Assert.Equal(
            exported.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            bundle.PolicySet.PolicyIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void DeterminingIds_MapBackToDogwoodRuleIndices()
    {
        var (bundle, result) = E1Requests.Run(
            E1Corpus.Cases.Single(testCase => testCase.Name == "distinct-ids-12"));
        Assert.True(result.IsSuccess);
        Assert.Equal(CedarSharp.CedarDecision.Allow, result.Decision);
        var indices = result.DeterminingPolicies
            .Select(id => bundle.RuleIndexFor(id) ?? throw new InvalidOperationException($"Unmapped ID {id}"))
            .OrderBy(index => index)
            .ToArray();
        Assert.Equal([0, 1, 2, 3, 4], indices);
        foreach (var rule in bundle.Lowered.Rules)
        {
            Assert.Equal(rule.RuleIndex, bundle.RuleIndexFor(rule.CedarPolicyId));
        }
    }

    [Fact]
    public void FromText_RenumbersAndLosesTheExportedIds()
    {
        // The naive alternative — reparsing the concatenated export — cannot
        // preserve IDs: Cedar's text parser assigns policy0..policyN by
        // position, and the exported text carries no ID annotations.
        var (bundle, _) = E1Requests.Run(
            E1Corpus.Cases.Single(testCase => testCase.Name == "permit-forbid"));
        var reparsed = CedarPolicySet.FromText(bundle.Lowered.CedarPolicyText);
        var request = new CedarAuthorizationRequest(
            new CedarEntityUid("App::User", "alice"),
            new CedarEntityUid("App::Action", "read"),
            new CedarEntityUid("App::Doc", "d1"),
            reparsed,
            schema: bundle.SchemaJson);
        var result = E1Requests.Engine.Authorize(request);
        output.WriteLine($"Reparsed determining IDs: [{string.Join(", ", result.DeterminingPolicies)}]");
        Assert.Equal(CedarSharp.CedarDecision.Deny, result.Decision);
        // The deny is determined by the forbid; under renumbering it reports
        // the positional name, which maps to no exported rule identity.
        Assert.Equal(["policy1"], result.DeterminingPolicies);
        Assert.DoesNotContain(result.DeterminingPolicies, id => bundle.RuleIndexFor(id) is not null);
    }

    [Fact]
    public void SourceIdAnnotations_DoNotBecomePolicyIds()
    {
        var source =
            """
            @id("audit-note") permit(principal, action == App::Action::"read", resource) when { context.count > 0 };
            forbid(principal, action == App::Action::"read", resource) when { context.count > 100 };
            """;
        var bundle = E1Bundle.FromDogwood(source, E1Corpus.Count);
        Assert.Equal(["e1_0", "e1_1"], bundle.PolicyIds.ToArray());
        Assert.NotEqual("audit-note", bundle.PolicyIds[0]);

        // The annotation rides along as an ordinary annotation on exactly the
        // body that is paired to the annotated rule's exported ID.
        var annotated = bundle.PolicyBodies.Where(body => body.Contains("audit-note", StringComparison.Ordinal)).ToArray();
        Assert.Single(annotated);
        Assert.Contains("audit-note", bundle.PoliciesById["e1_0"], StringComparison.Ordinal);
        Assert.DoesNotContain("audit-note", bundle.PoliciesById["e1_1"], StringComparison.Ordinal);

        // Decisions name the stored policy ID, never the source annotation.
        var request = new CedarAuthorizationRequest(
            new CedarEntityUid("App::User", "alice"),
            new CedarEntityUid("App::Action", "read"),
            new CedarEntityUid("App::Doc", "d1"),
            bundle.PolicySet,
            contextJson: "{\"count\":5}",
            schema: bundle.SchemaJson);
        var result = E1Requests.Engine.Authorize(request);
        output.WriteLine($"Determining: [{string.Join(", ", result.DeterminingPolicies)}]");
        Assert.Equal(CedarSharp.CedarDecision.Allow, result.Decision);
        Assert.Equal(["e1_0"], result.DeterminingPolicies);
        Assert.DoesNotContain("audit-note", result.DeterminingPolicies);
    }

    [Fact]
    public void DefaultDistincter_KeepsItsDistinctNaming()
    {
        var bundle = E1Bundle.FromDogwood(
            "permit(principal, action == App::Action::\"read\", resource); forbid(principal, action == App::Action::\"read\", resource);",
            E1Corpus.DefaultDistincter);
        Assert.Equal(["policy_0", "policy_1"], bundle.PolicyIds.ToArray());
        var request = new CedarAuthorizationRequest(
            new CedarEntityUid("App::User", "alice"),
            new CedarEntityUid("App::Action", "read"),
            new CedarEntityUid("App::Doc", "d1"),
            bundle.PolicySet,
            schema: bundle.SchemaJson);
        var result = E1Requests.Engine.Authorize(request);
        Assert.Equal(CedarSharp.CedarDecision.Deny, result.Decision);
        Assert.Equal(["policy_1"], result.DeterminingPolicies);
    }
}
