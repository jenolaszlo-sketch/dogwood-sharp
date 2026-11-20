using System.Text.Json;
using CedarSharp;
using Xunit;
using Xunit.Abstractions;

namespace CedarSharp.E1Prototype;

/// <summary>
/// E1-5 entity and request export: what a future adapter must transfer to
/// CedarSharp, and whether CedarSharp's existing inputs represent it without
/// loss.
/// </summary>
public sealed class EntityRequestExportTests
{
    private readonly ITestOutputHelper output;

    public EntityRequestExportTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void ActionHierarchyEntities_AreSynthesizableFromTheExportedSchema()
    {
        var bundle = E1Bundle.FromDogwood(
            "permit(principal, action in App::Action::\"ReadGroup\", resource);",
            E1Corpus.Hierarchy);
        using var schema = JsonDocument.Parse(bundle.Lowered.CedarSchemaJson);
        var actions = E1Requests.ActionEntities(schema.RootElement);
        output.WriteLine($"Synthesized action entities: {actions.Count}");
        foreach (var action in actions)
        {
            output.WriteLine(action);
        }

        Assert.Equal(2, actions.Count);

        // The merged entity payload parses against the exported schema.
        var merged = E1Requests.MergeActionEntities(E1Corpus.EmptyEntities, bundle);
        output.WriteLine($"Merged entities: {merged}");
        var check = E1Requests.Engine.CheckEntities(merged, bundle.SchemaJson);
        Assert.True(check.IsSuccess, check.ToString());
    }

    [Fact]
    public void ActionGroupMembership_ResolvesFromTheSchemaAlone()
    {
        // With the schema attached, Cedar adds the schema's action entities and
        // their ancestor edges, so `action in [Group]` resolves without the
        // adapter transporting action entities.
        var withSchema = E1Corpus.Cases.Single(candidate => candidate.Name == "action-hierarchy-with-schema");
        var (_, managed) = E1Requests.Run(withSchema);
        output.WriteLine($"With schema: {managed.Decision}, errors {managed.PolicyErrors.Count}");
        Assert.Equal(CedarSharp.CedarDecision.Allow, managed.Decision);
        Assert.Equal(["e1_0"], managed.DeterminingPolicies);

        // Merging the same action entities changes nothing.
        var merged = E1Corpus.Cases.Single(candidate => candidate.Name == "action-hierarchy-with-merged-entities");
        var (_, managedMerged) = E1Requests.Run(merged);
        Assert.Equal(managed.Decision, managedMerged.Decision);
        Assert.Equal(managed.DeterminingPolicies, managedMerged.DeterminingPolicies);

        // Without the schema the hierarchy is absent and the rule never fires.
        var withoutSchema = E1Corpus.Cases.Single(candidate => candidate.Name == "action-hierarchy-without-schema");
        var (_, managedWithout) = E1Requests.Run(withoutSchema);
        output.WriteLine($"Without schema: {managedWithout.Decision}, errors {managedWithout.PolicyErrors.Count}");
        Assert.Equal(CedarSharp.CedarDecision.Deny, managedWithout.Decision);
        Assert.Empty(managedWithout.DeterminingPolicies);
    }

    [Fact]
    public void TypedEntityApi_RoundTripsToTheSameDecision()
    {
        var testCase = E1Corpus.Cases.Single(candidate => candidate.Name == "namespaced-entity-match");
        var bundle = E1Bundle.FromDogwood(testCase.Source, testCase.Schema);
        var alice = new CedarEntityUid("App::User", "alice");
        var document = new CedarEntity(new CedarEntityUid("App::Doc", "d1"),
            $$"""{"owner":{{CedarValue.Entity(alice)}}}""");
        var entities = CedarEntity.ToJson([new CedarEntity(alice), document]);
        Assert.Equal(E1Corpus.EntitiesOwnerAlice, entities);

        var request = new CedarAuthorizationRequest(
            alice,
            new CedarEntityUid("App::Action", "read"),
            new CedarEntityUid("App::Doc", "d1"),
            bundle.PolicySet,
            contextJson: "{}",
            entitiesJson: entities,
            schema: bundle.SchemaJson);
        var result = E1Requests.Engine.Authorize(request);
        Assert.True(result.IsSuccess);
        Assert.Equal(CedarSharp.CedarDecision.Allow, result.Decision);
        Assert.Equal(["e1_0"], result.DeterminingPolicies);
    }

    [Fact]
    public void ParentRelationships_SurviveTheEntityPayload()
    {
        var testCase = E1Corpus.Cases.Single(candidate => candidate.Name == "entity-parents-match");
        var bundle = E1Bundle.FromDogwood(testCase.Source, testCase.Schema);
        var alice = new CedarEntity(new CedarEntityUid("App::User", "alice"), "{}",
            [new CedarEntityUid("App::Group", "admins")]);
        var group = new CedarEntity(new CedarEntityUid("App::Group", "admins"));
        var document = new CedarEntity(
            new CedarEntityUid("App::Doc", "d1"),
            $$"""{"owner":{{CedarValue.Entity(new CedarEntityUid("App::User", "alice"))}}}""");
        var entities = CedarEntity.ToJson([alice, group, document]);
        var request = new CedarAuthorizationRequest(
            new CedarEntityUid("App::User", "alice"),
            new CedarEntityUid("App::Action", "read"),
            new CedarEntityUid("App::Doc", "d1"),
            bundle.PolicySet,
            contextJson: "{}",
            entitiesJson: entities,
            schema: bundle.SchemaJson);
        var result = E1Requests.Engine.Authorize(request);
        output.WriteLine($"Raw: {result.Raw}");
        Assert.Equal(CedarSharp.CedarDecision.Allow, result.Decision);
        Assert.Equal(["e1_0"], result.DeterminingPolicies);
    }

    [Fact]
    public void ExtensionValues_RoundTripThroughTheCedarValueHelpers()
    {
        var testCase = E1Corpus.Cases.Single(candidate => candidate.Name == "extension-values");
        var bundle = E1Bundle.FromDogwood(testCase.Source, testCase.Schema);
        var context = JsonSerializer.Serialize(new Dictionary<string, JsonElement>
        {
            ["ip"] = CedarValue.Ip("127.0.0.1"),
            ["amount"] = CedarValue.Decimal("25.50"),
            ["at"] = CedarValue.Datetime("2026-06-01T00:00:00Z"),
        });
        output.WriteLine($"Context: {context}");
        var request = new CedarAuthorizationRequest(
            new CedarEntityUid("App::User", "alice"),
            new CedarEntityUid("App::Action", "write"),
            new CedarEntityUid("App::Doc", "d1"),
            bundle.PolicySet,
            contextJson: context,
            entitiesJson: E1Corpus.EmptyEntities,
            schema: bundle.SchemaJson);
        var result = E1Requests.Engine.Authorize(request);
        output.WriteLine($"Raw: {result.Raw}");
        Assert.Equal(CedarSharp.CedarDecision.Allow, result.Decision);
        Assert.Equal(["e1_0"], result.DeterminingPolicies);
    }

    [Fact]
    public void BareScopeEntityAndConformanceModes_AreRecorded()
    {
        // Upstream adds the scope principal and resource bare (no conformance,
        // no attributes) so identity reads never hit "entity does not exist",
        // and rejects caller-supplied entities that fail conformance. The
        // observed CedarSharp modes decide what an adapter must do.
        foreach (var name in new[] { "bare-scope-entity-match", "bare-scope-required-attr-read", "supplied-incomplete-entity" })
        {
            var testCase = E1Corpus.Cases.Single(candidate => candidate.Name == name);
            var (bundle, managed) = E1Requests.Run(testCase);
            output.WriteLine($"{name}: success={managed.IsSuccess} decision={managed.Decision} " +
                $"errors={managed.PolicyErrors.Count} operationErrors={managed.Errors.Count} raw={managed.Raw}");

            var referenceInput = E1ReferenceInput.Authorization(
                bundle,
                new CedarEntityUid(testCase.Principal.Type, testCase.Principal.Id),
                new CedarEntityUid(testCase.Action.Type, testCase.Action.Id),
                new CedarEntityUid(testCase.Resource.Type, testCase.Resource.Id),
                testCase.ContextJson,
                testCase.EntitiesJson,
                testCase.ValidateRequest,
                testCase.PassSchema);
            using var referenceDocument = Cedar11Reference.Call(Cedar11Reference.OperationAuthorize, referenceInput).Document;
            var reference = NormalizedAnswer.Normalize(referenceDocument.RootElement);
            output.WriteLine($"{name}: reference success={reference.IsSuccess} decision={reference.Decision} " +
                $"errors={reference.PolicyErrors.Count} operationErrors={reference.OperationErrors.Count}");
            Assert.Equal(managed.IsSuccess, reference.IsSuccess);
        }
    }
}
