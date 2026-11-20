using CedarSharp;
using Xunit;
using Xunit.Abstractions;

namespace CedarSharp.E1Prototype;

/// <summary>
/// E1-5 entity-model comparison: the upstream Dogwood entity store and the
/// CedarSharp request inputs do not fail in the same way, so an adapter must
/// reproduce the upstream store construction rather than forward entities
/// verbatim.
/// </summary>
/// <remarks>
/// <para>
/// Upstream builds the store in three tiers (verified in the pinned upstream
/// source): caller-supplied entities are schema-validated; the scope principal
/// and resource are then added bare and unchecked for any uid the caller did
/// not supply; finally the schema's action-hierarchy entities are merged.
/// </para>
/// <para>
/// A CedarSharp request carries a flat entity list plus an optional schema.
/// The schema performs conformance checking on every supplied entity, so an
/// incomplete caller-supplied entity fails the whole call instead of being
/// skipped. The scope entities are not injected at all, and the action
/// hierarchy is carried by the schema rather than the store.
/// </para>
/// </remarks>
public sealed class EntityModelDifferenceTests
{
    private readonly ITestOutputHelper output;

    public EntityModelDifferenceTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SuppliedIncompleteEntity_FailsTheWholeCall()
    {
        var testCase = E1Corpus.Cases.Single(candidate => candidate.Name == "supplied-incomplete-entity");
        var (_, managed) = E1Requests.Run(testCase);
        output.WriteLine($"success={managed.IsSuccess} decision={managed.Decision} errors={managed.Errors.Count}");
        foreach (var error in managed.Errors)
        {
            output.WriteLine(error.Message);
        }

        // Fail closed: no decision is produced at all.
        Assert.False(managed.IsSuccess);
        Assert.Null(managed.Decision);
        Assert.False(managed.IsCleanAllow);
        Assert.NotEmpty(managed.Errors);
    }

    [Fact]
    public void AbsentScopeEntity_AndPresentEntityMissingTheAttribute_FailDifferently()
    {
        var absent = E1Corpus.Cases.Single(candidate => candidate.Name == "bare-scope-required-attr-read");
        var (_, absentResult) = E1Requests.Run(absent);
        output.WriteLine($"absent: decision={absentResult.Decision} errors={absentResult.PolicyErrors.Count}");
        foreach (var error in absentResult.PolicyErrors)
        {
            output.WriteLine($"  {error.PolicyId}: {error.Error.Message}");
        }

        var present = E1Corpus.Cases.Single(candidate => candidate.Name == "scope-entity-present-missing-attribute");
        var (_, presentResult) = E1Requests.Run(present);
        output.WriteLine($"present: decision={presentResult.Decision} errors={presentResult.PolicyErrors.Count}");
        foreach (var error in presentResult.PolicyErrors)
        {
            output.WriteLine($"  {error.PolicyId}: {error.Error.Message}");
        }

        // Both fail closed with a policy error, but the diagnostics differ:
        // an absent entity and a present-but-attribute-less entity are not the
        // same condition in Cedar.
        Assert.True(absentResult.IsSuccess);
        Assert.Equal(CedarSharp.CedarDecision.Deny, absentResult.Decision);
        Assert.Single(absentResult.PolicyErrors);
        Assert.Equal("e1_0", absentResult.PolicyErrors[0].PolicyId);

        Assert.True(presentResult.IsSuccess);
        Assert.Equal(CedarSharp.CedarDecision.Deny, presentResult.Decision);
        Assert.Single(presentResult.PolicyErrors);
        Assert.Equal("e1_0", presentResult.PolicyErrors[0].PolicyId);

        Assert.NotEqual(absentResult.PolicyErrors[0].Error.Message, presentResult.PolicyErrors[0].Error.Message);
    }

    [Fact]
    public void IdentityComparison_SucceedsWithoutTheScopeEntity()
    {
        // Upstream injects the bare scope entity so that identity comparisons
        // never hit "entity does not exist". Cedar compares entity references
        // without requiring presence, so the comparison already succeeds.
        var testCase = E1Corpus.Cases.Single(candidate => candidate.Name == "bare-scope-entity-match");
        var (_, managed) = E1Requests.Run(testCase);
        output.WriteLine($"decision={managed.Decision} errors={managed.PolicyErrors.Count}");
        Assert.True(managed.IsSuccess);
        Assert.Equal(CedarSharp.CedarDecision.Allow, managed.Decision);
        Assert.Empty(managed.PolicyErrors);
    }
}
