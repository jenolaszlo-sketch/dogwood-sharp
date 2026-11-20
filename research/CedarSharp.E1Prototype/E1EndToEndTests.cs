using DogwoodSharp;
using Xunit;
using Xunit.Abstractions;

namespace CedarSharp.E1Prototype;

/// <summary>
/// The E1-3 prototype walk-through: parse, validate, lower, export, package,
/// request, authorize, inspect — using only the existing public APIs of
/// DogwoodSharp and CedarSharp, plus the pinned Cedar 4.11 reference.
/// </summary>
public sealed class E1EndToEndTests
{
    private readonly ITestOutputHelper output;

    public E1EndToEndTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void VersionPair_IsExecutableEvidence()
    {
        var dogwood = Dogwood.GetNativeVersion();
        var cedarSharp = E1Requests.Engine.GetVersion();
        var reference = Cedar11Reference.Version();
        output.WriteLine($"Dogwood bridge: {dogwood}");
        output.WriteLine($"  Dogwood {dogwood.DogwoodVersion} @ {dogwood.DogwoodRevision}, Cedar {dogwood.CedarVersion}, {dogwood.Target}");
        output.WriteLine($"CedarSharp native: {cedarSharp}");
        output.WriteLine($"Cedar 4.11 reference: sdk {reference.SdkVersion}, language {reference.LanguageVersion}");
        Assert.Equal("4.11.0", dogwood.CedarVersion);
        Assert.Equal("4.13.0", cedarSharp.SdkVersion);
        Assert.Equal("4.5", cedarSharp.LanguageVersion);
        Assert.Equal(("4.11.0", "4.5"), reference);
    }

    [Fact]
    public void LoweredCorpusCase_FullFlow_ReachesACedarSharpDecision()
    {
        // Steps 1–2: parse and validate the Dogwood source.
        var source = "permit(principal, action == App::Action::\"read\", resource); forbid(principal, action == App::Action::\"write\", resource);";
        var parse = Dogwood.Parse(source);
        Assert.True(parse.Success);
        Assert.Equal(2, parse.PolicyCount);

        // Step 2: validate against the action schema.
        var validated = Dogwood.Validate(source, E1Corpus.Base);
        Assert.True(validated.IsValid);
        Assert.Empty(validated.ValidationErrors);

        // Step 3: lower to Cedar.
        var lowered = Dogwood.Lower(source, E1Corpus.Base);
        Assert.True(lowered.IsLowered);
        Assert.True(lowered.ArtifactsExported);
        Assert.True(lowered.SelfContainedCedar);
        Assert.Equal(2, lowered.RuleCount);

        // Step 4: the validated Cedar artifacts and the rule mapping.
        output.WriteLine("Exported Cedar policy text:");
        output.WriteLine(lowered.CedarPolicyText);
        output.WriteLine("Exported augmented Cedar schema (JSON):");
        output.WriteLine(lowered.CedarSchemaJson);
        output.WriteLine("Exported augmented Cedar schema (text):");
        output.WriteLine(lowered.CedarSchemaText);
        foreach (var rule in lowered.Rules)
        {
            output.WriteLine($"Rule {rule.RuleIndex} -> Cedar policy '{rule.CedarPolicyId}'");
        }

        Assert.Equal(["e1_0", "e1_1"], lowered.Rules.Select(rule => rule.CedarPolicyId).ToArray());

        // Steps 5–6: package for CedarSharp and build a request.
        var bundle = E1Bundle.FromDogwood(source, E1Corpus.Base);
        var request = new CedarAuthorizationRequest(
            new CedarEntityUid("App::User", "alice"),
            new CedarEntityUid("App::Action", "read"),
            new CedarEntityUid("App::Doc", "d1"),
            bundle.PolicySet,
            contextJson: "{}",
            entitiesJson: E1Corpus.EmptyEntities,
            schema: bundle.SchemaJson,
            validateRequest: true);

        // Step 7: authorize through CedarSharp.
        var result = E1Requests.Engine.Authorize(request);

        // Step 8: inspect the raw result and diagnostics.
        output.WriteLine($"Decision: {result.Decision}; clean: {result.IsCleanAllow}");
        output.WriteLine($"Determining: [{string.Join(", ", result.DeterminingPolicies)}]");
        output.WriteLine($"Policy errors: {result.PolicyErrors.Count}");
        output.WriteLine($"Raw: {result.Raw}");

        Assert.True(result.IsSuccess);
        Assert.Equal(CedarSharp.CedarDecision.Allow, result.Decision);
        Assert.True(result.IsCleanAllow);
        Assert.Equal(["e1_0"], result.DeterminingPolicies);
        Assert.Empty(result.PolicyErrors);

        // The rule identity maps back through the exported mapping.
        Assert.Equal(0, bundle.RuleIndexFor(result.DeterminingPolicies[0]));
    }

    [Fact]
    public void ExportedArtifacts_AreRoundTrippableThroughCedarSharpChecks()
    {
        var bundle = E1Bundle.FromDogwood(
            "permit(principal, action == App::Action::\"read\", resource);",
            E1Corpus.Base);
        var policies = E1Requests.Engine.CheckPolicies(bundle.PolicySet);
        Assert.True(policies.IsSuccess, policies.ToString());

        var schemaJson = E1Requests.Engine.CheckSchema(bundle.SchemaJson);
        Assert.True(schemaJson.IsSuccess, schemaJson.ToString());
        var schemaText = E1Requests.Engine.CheckSchema(bundle.SchemaText);
        Assert.True(schemaText.IsSuccess, schemaText.ToString());

        // The same corpus entities parse against the exported schema.
        var entities = E1Requests.Engine.CheckEntities(E1Corpus.EntitiesOwnerAlice, bundle.SchemaJson);
        Assert.True(entities.IsSuccess, entities.ToString());
    }
}
