using System.Text.Json;
using DogwoodSharp;
using DogwoodSharp.Experimental.CedarSharp;
using Xunit;

namespace DogwoodSharp.CedarSharp.Tests;

/// <summary>
/// Representation-invariance regression controls for the typed context values
/// affected by the pinned reference path's value-model ceiling
/// (<c>ipaddr</c>, <c>datetime</c> and <c>decimal</c>).
///
/// Cedar's reserved extension encoding has more than one JSON spelling for the
/// same typed value: the explicit <c>{"__extn":{"fn":...,"arg":...}}</c>
/// escape, the implicit <c>{"fn":...,"arg":...}</c> object and the bare
/// argument that the schema-directed constructor resolves. The authoritative
/// Cedar engine (evaluated directly, without the Dogwood conversion) treats
/// all three spellings of one argument as the same typed value, so D3 must as
/// well: the spelling may never change the decision, the determining rule or
/// the diagnostics, and a malformed argument may never gain validity through
/// an alternative spelling.
///
/// These controls are permanent production regressions, not evidence-only
/// controls. They do not consult the reference implementation and they do not
/// permit the adapter to coerce, default or synthesize a value.
/// </summary>
public sealed class C23TypedContextRepresentationInvarianceTests
{
    private const string IpaddrSchema =
        "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { ip: ipaddr } }; }";
    private const string DatetimeSchema =
        "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { ts: datetime } }; }";
    private const string DecimalSchema =
        "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { price: decimal } }; }";

    /// <summary>The three JSON spellings Cedar resolves to one typed value.</summary>
    private static readonly string[] Representations = ["explicit", "implicit", "short"];

    public static TheoryData<string, string, string, string> TypedValues => new()
    {
        // type, context attribute, extension constructor, matching argument
        { "ipaddr", "ip", "ip", "192.168.1.1" },
        { "datetime", "ts", "datetime", "2024-01-01T00:00:00Z" },
        { "decimal", "price", "decimal", "1.5" },
    };

    private static DogwoodCedarAuthorizer Plan(string source, string schemaText) =>
        DogwoodCedarAuthorizer.ForLoweredBundle(source, new DogwoodSchema(schemaText, Distincter: "c23repr"));

    private static CedarAuthorizationInput Request(string contextJson) =>
        new(
            new CedarEntityRef("App::User", "alice"),
            new CedarEntityRef("App::Action", "read"),
            new CedarEntityRef("App::Doc", "d1"),
            contextJson,
            "[]");

    private static string SchemaFor(string type) => type switch
    {
        "ipaddr" => IpaddrSchema,
        "datetime" => DatetimeSchema,
        "decimal" => DecimalSchema,
        _ => throw new InvalidOperationException($"unknown typed value `{type}`"),
    };

    private static string EqualsSource(string attribute, string constructor, string argument) =>
        $"permit(principal, action == App::Action::\"read\", resource)\n" +
        $"when {{ context.{attribute} == {constructor}(\"{argument}\") }};";

    /// <summary>
    /// One typed value in one of Cedar's JSON spellings. The explicit escape
    /// and the implicit extension object carry the constructor name; the bare
    /// argument is resolved by the schema-directed constructor.
    /// </summary>
    private static string Spelling(string attribute, string constructor, string argument, string representation)
    {
        var serialized = JsonSerializer.Serialize(argument);
        return representation switch
        {
            "explicit" => $"{{\"{attribute}\":{{\"__extn\":{{\"fn\":\"{constructor}\",\"arg\":{serialized}}}}}}}",
            "implicit" => $"{{\"{attribute}\":{{\"fn\":\"{constructor}\",\"arg\":{serialized}}}}}",
            "short" => $"{{\"{attribute}\":{serialized}}}",
            _ => throw new InvalidOperationException($"unknown representation `{representation}`"),
        };
    }

    [Theory]
    [MemberData(nameof(TypedValues))]
    public void EveryRepresentationOfAMatchingTypedValueAuthorizesIdentically(
        string type,
        string attribute,
        string constructor,
        string argument)
    {
        using var plan = Plan(EqualsSource(attribute, constructor, argument), SchemaFor(type));

        foreach (var representation in Representations)
        {
            var result = plan.Authorize(Request(Spelling(attribute, constructor, argument, representation)));

            Assert.False(result.OperationFailed);
            Assert.Equal(CedarAuthorizationStage.Evaluation, result.Stage);
            Assert.Equal(CedarDecision.Allow, result.Decision);
            Assert.True(result.IsCleanAllow);
            Assert.True(result.Authorized);
            var determining = Assert.Single(result.DeterminingPolicyIds);
            Assert.Equal(plan.PolicyIds[0], determining);
            Assert.Empty(result.Errors);
            Assert.Empty(result.PolicyErrors);
        }
    }

    [Theory]
    [MemberData(nameof(TypedValues))]
    public void EveryRepresentationOfANonMatchingTypedValueDeniesIdentically(
        string type,
        string attribute,
        string constructor,
        string argument)
    {
        // A different argument of the same type: the decision must be a clean
        // Deny in every spelling, and it must never become an operation
        // failure or an Allow.
        var mismatching = type switch
        {
            "ipaddr" => "192.168.1.2",
            "datetime" => "2024-01-01T00:00:01Z",
            "decimal" => "2.5",
            _ => throw new InvalidOperationException($"unknown typed value `{type}`"),
        };
        using var plan = Plan(EqualsSource(attribute, constructor, argument), SchemaFor(type));

        foreach (var representation in Representations)
        {
            var result = plan.Authorize(Request(Spelling(attribute, constructor, mismatching, representation)));

            Assert.False(result.OperationFailed);
            Assert.Equal(CedarAuthorizationStage.Evaluation, result.Stage);
            Assert.Equal(CedarDecision.Deny, result.Decision);
            Assert.False(result.IsCleanAllow);
            Assert.False(result.Authorized);
            Assert.Empty(result.DeterminingPolicyIds);
            Assert.Empty(result.Errors);
            Assert.Empty(result.PolicyErrors);
        }
    }

    [Theory]
    [MemberData(nameof(TypedValues))]
    public void AMalformedTypedValueNeverGainsValidityInAnyRepresentation(
        string type,
        string attribute,
        string constructor,
        string argument)
    {
        var malformed = type switch
        {
            "ipaddr" => "not-an-ip",
            "datetime" => "not-a-datetime",
            "decimal" => "not-a-number",
            _ => throw new InvalidOperationException($"unknown typed value `{type}`"),
        };
        using var plan = Plan(EqualsSource(attribute, constructor, argument), SchemaFor(type));

        foreach (var representation in Representations)
        {
            var result = plan.Authorize(Request(Spelling(attribute, constructor, malformed, representation)));

            Assert.True(result.OperationFailed);
            Assert.Null(result.Decision);
            Assert.False(result.IsCleanAllow);
            Assert.False(result.Authorized);
            Assert.Empty(result.DeterminingPolicyIds);
            Assert.NotEmpty(result.Errors);
        }
    }

    [Theory]
    [MemberData(nameof(TypedValues))]
    public void APlainRecordNeverBecomesATypedValue(
        string type,
        string attribute,
        string constructor,
        string argument)
    {
        // A record is not a typed value. It must not be silently coerced into
        // one, and it must not be accepted as a value that simply does not
        // match the policy literal.
        using var plan = Plan(EqualsSource(attribute, constructor, argument), SchemaFor(type));

        var result = plan.Authorize(Request($"{{\"{attribute}\":{{\"unexpected\":1}}}}"));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.IsCleanAllow);
        Assert.False(result.Authorized);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [MemberData(nameof(TypedValues))]
    public void TheTypedValueIsNeverReplacedByThePolicyLiteralInAnyRepresentation(
        string type,
        string attribute,
        string constructor,
        string argument)
    {
        // If the adapter normalized the context to the policy literal, every
        // spelling would authorize. A different value of the same type must
        // still deny in every spelling.
        var mismatching = type switch
        {
            "ipaddr" => "10.0.0.1",
            "datetime" => "2025-01-01T00:00:00Z",
            "decimal" => "9.5",
            _ => throw new InvalidOperationException($"unknown typed value `{type}`"),
        };
        using var plan = Plan(EqualsSource(attribute, constructor, argument), SchemaFor(type));

        foreach (var representation in Representations)
        {
            var result = plan.Authorize(Request(Spelling(attribute, constructor, mismatching, representation)));

            Assert.False(result.OperationFailed);
            Assert.Equal(CedarDecision.Deny, result.Decision);
            Assert.False(result.IsCleanAllow);
            Assert.False(result.Authorized);
            Assert.Empty(result.Errors);
        }
    }

    [Fact]
    public void TheRepresentationOfATypedValueDoesNotChangeTheCanonicalSchema()
    {
        // The canonical schema is derived from the policy and the action
        // schema, never from a request payload, so no spelling of a typed
        // value can influence it.
        using var plan = Plan(EqualsSource("ip", "ip", "192.168.1.1"), IpaddrSchema);

        var before = plan.CanonicalSchemaJson;
        var result = plan.Authorize(Request(Spelling("ip", "ip", "192.168.1.1", "short")));

        Assert.True(result.Authorized);
        Assert.Equal(before, plan.CanonicalSchemaJson);
    }
}
