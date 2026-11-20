using System.Text.RegularExpressions;
using CedarSharp;
using DogwoodSharp;
using Xunit;
using Xunit.Abstractions;

namespace CedarSharp.E1Prototype;

/// <summary>
/// E1-6 temporal boundary feasibility: one bounded, research-only wiring
/// experiment over a lowered temporal policy.
/// </summary>
/// <remarks>
/// <para>
/// TRUST BOUNDARY. The lowered temporal policy reads a generated context
/// field (for example <c>e1t_0__temporal_0</c>) that the augmented schema
/// declares but the authored action schema does not. Upstream computes that
/// value inside the trusted pipeline: <c>decide_at</c> asks the temporal
/// engine for the leaf booleans and binds them into the request context
/// before the policy engine decides, failing the decision closed when the
/// temporal evaluation fails.
/// </para>
/// <para>
/// The experiment below injects the generated boolean directly into the
/// request context. That is a test harness only. No public API may let an
/// untrusted caller supply an authoritative temporal result: the production
/// adapter must keep the generated fields reserved for the trusted Dogwood
/// execution pipeline, and a caller-supplied value must never be able to
/// stand in for a temporal evaluation.
/// </para>
/// </remarks>
public sealed class TemporalBoundaryTests
{
    private readonly ITestOutputHelper output;

    public TemporalBoundaryTests(ITestOutputHelper output) => this.output = output;

    private const string TemporalSchema =
        """
        namespace Test {
          entity Item;
          entity User;
          action Approve appliesTo {
            principal: [User],
            resource: [Item],
            context: {
              input: {
                key: String,
              },
            },
          };
          action Read appliesTo {
            principal: [User],
            resource: [Item],
            context: {
              input: {
                key: String,
              },
            },
          };
        }
        """;

    private const string TemporalSource =
        """permit(principal, action == Test::Action::"Read", resource) when temporal { formerly within 10s Test::Action::"Approve"::response{ input.key: context.input.key } };""";

    [Fact]
    public void LoweredTemporalPolicy_ExposesAGeneratedContextField()
    {
        var schema = new DogwoodSchema(TemporalSchema, Distincter: "e1t");
        var lowered = Dogwood.Lower(TemporalSource, schema);
        output.WriteLine($"Lowered: {lowered.Lowered}; self-contained: {lowered.SelfContainedCedar}");
        output.WriteLine(lowered.CedarPolicyText);
        output.WriteLine(lowered.CedarSchemaJson);
        Assert.True(lowered.ArtifactsExported);
        Assert.False(lowered.SelfContainedCedar);

        var field = GeneratedField(lowered.CedarPolicyText);
        output.WriteLine($"Generated temporal context field: {field}");
        Assert.Matches(new Regex("^[A-Za-z_][A-Za-z0-9_]*__temporal_[0-9]+$"), field);

        // The augmented schema declares the generated field as a Boolean; the
        // authored schema did not.
        Assert.Contains($"\"{field}\"", lowered.CedarSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"{field}\"", TemporalSchema, StringComparison.Ordinal);
    }

    [Fact]
    public void IndependentlySuppliedTemporalValues_DriveTheCedarDecision()
    {
        // Research-only harness: the two runs differ only in the generated
        // boolean. This demonstrates the wiring, not temporal evaluation.
        var schema = new DogwoodSchema(TemporalSchema, Distincter: "e1t");
        var bundle = E1Bundle.FromDogwood(TemporalSource, schema);
        var field = GeneratedField(bundle.Lowered.CedarPolicyText);

        var allowed = Authorize(bundle, field, true);
        var denied = Authorize(bundle, field, false);
        output.WriteLine($"field={field} true -> {allowed.Decision} (errors {allowed.PolicyErrors.Count})");
        output.WriteLine($"field={field} false -> {denied.Decision} (errors {denied.PolicyErrors.Count})");

        Assert.Equal(CedarSharp.CedarDecision.Allow, allowed.Decision);
        Assert.Equal(CedarSharp.CedarDecision.Deny, denied.Decision);
        Assert.Equal(["e1t_0"], allowed.DeterminingPolicies);
        Assert.Empty(denied.DeterminingPolicies);

        // The pinned Cedar 4.11 reference makes the same wiring decision.
        foreach (var value in new[] { true, false })
        {
            var input = E1ReferenceInput.Authorization(
                bundle,
                new CedarEntityUid("Test::User", "u"),
                new CedarEntityUid("Test::Action", "Read"),
                new CedarEntityUid("Test::Item", "i"),
                $"{{\"input\":{{\"key\":\"a\"}},\"{field}\":{(value ? "true" : "false")}}}",
                E1Corpus.EmptyEntities,
                validateRequest: true);
            using var document = Cedar11Reference.Call(Cedar11Reference.OperationAuthorize, input).Document;
            var reference = NormalizedAnswer.Normalize(document.RootElement);
            output.WriteLine($"reference {field}={value} -> {reference.Decision}");
            Assert.Equal(value ? "allow" : "deny", reference.Decision);
        }
    }

    private static CedarAuthorizationResult Authorize(E1Bundle bundle, string field, bool value)
    {
        var context = $"{{\"input\":{{\"key\":\"a\"}},\"{field}\":{(value ? "true" : "false")}}}";
        var request = new CedarAuthorizationRequest(
            new CedarEntityUid("Test::User", "u"),
            new CedarEntityUid("Test::Action", "Read"),
            new CedarEntityUid("Test::Item", "i"),
            bundle.PolicySet,
            contextJson: context,
            entitiesJson: E1Corpus.EmptyEntities,
            schema: bundle.SchemaJson);
        return E1Requests.Engine.Authorize(request);
    }

    private static string GeneratedField(string cedarPolicyText)
    {
        var match = Regex.Match(cedarPolicyText, @"context\.([A-Za-z_][A-Za-z0-9_]*__temporal_[0-9]+)");
        Assert.True(match.Success, $"No generated temporal field in: {cedarPolicyText}");
        return match.Groups[1].Value;
    }
}
