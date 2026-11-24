using System.Text.Json;
using DogwoodSharp;
using DogwoodSharp.Experimental.CedarSharp;
using Xunit;

namespace DogwoodSharp.CedarSharp.Tests;

/// <summary>
/// Targeted C-23 semantic regression controls for typed <c>ipaddr</c> and <c>datetime</c>
/// context values. The pinned upstream Dogwood 1.1.2 value model has no <c>ipaddr</c> or
/// <c>datetime</c> variant, so upstream cannot represent a typed context value of either
/// type; D3 evaluates the same caller value through CedarSharp. These controls pin the D3
/// side of that owner-approved ceiling (ED-06) from the Cedar semantics rather than from
/// the adapter's own output: a valid typed value authorizes exactly when it equals the
/// policy literal, the Cedar ipaddr and datetime operations observe a real value of that
/// type, the boundary values are evaluated correctly, an invalid or malformed typed value
/// fails closed, and the caller's value is never substituted, defaulted or synthesized.
/// </summary>
public sealed class C23TypedContextTests
{
    private const string IpaddrSchema =
        "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { ip: ipaddr } }; }";
    private const string DatetimeSchema =
        "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { ts: datetime } }; }";

    private const string IpaddrEqualsSource =
        "permit(principal, action == App::Action::\"read\", resource)\n" +
        "when { context.ip == ip(\"192.168.1.1\") };";
    private const string DatetimeEqualsSource =
        "permit(principal, action == App::Action::\"read\", resource)\n" +
        "when { context.ts == datetime(\"2024-01-01T00:00:00Z\") };";

    private static DogwoodCedarAuthorizer Plan(string source, string schemaText) =>
        DogwoodCedarAuthorizer.ForLoweredBundle(source, new DogwoodSchema(schemaText, Distincter: "c23typed"));

    private static CedarAuthorizationInput Request(string contextJson) =>
        new(
            new CedarEntityRef("App::User", "alice"),
            new CedarEntityRef("App::Action", "read"),
            new CedarEntityRef("App::Doc", "d1"),
            contextJson,
            "[]");

    private static string TypedIpaddr(string argument) =>
        "{\"ip\":{\"__extn\":{\"fn\":\"ip\",\"arg\":" + JsonSerializer.Serialize(argument) + "}}}";

    private static string TypedDatetime(string argument) =>
        "{\"ts\":{\"__extn\":{\"fn\":\"datetime\",\"arg\":" + JsonSerializer.Serialize(argument) + "}}}";

    [Fact]
    public void AValidTypedIpaddrContextValueAuthorizesWithTheIntendedCedarSemantics()
    {
        using var plan = Plan(IpaddrEqualsSource, IpaddrSchema);
        var result = plan.Authorize(Request(TypedIpaddr("192.168.1.1")));

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

    [Fact]
    public void ATypedIpaddrContextValueThatDiffersFromThePolicyLiteralIsACleanDeny()
    {
        using var plan = Plan(IpaddrEqualsSource, IpaddrSchema);
        var result = plan.Authorize(Request(TypedIpaddr("192.168.1.2")));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Deny, result.Decision);
        Assert.False(result.IsCleanAllow);
        Assert.False(result.Authorized);
        Assert.Empty(result.DeterminingPolicyIds);
        Assert.Empty(result.Errors);
        Assert.Empty(result.PolicyErrors);
    }

    [Fact]
    public void TheCedarIpaddrOperationsObserveARealIpaddrValue()
    {
        // A string comparison would not distinguish these two policies: only a real ipaddr
        // value answers isIpv4() and isIpv6() the way Cedar defines them.
        using var ipv4 = Plan(
            "permit(principal, action == App::Action::\"read\", resource)\nwhen { context.ip.isIpv4() };",
            IpaddrSchema);
        using var ipv6 = Plan(
            "permit(principal, action == App::Action::\"read\", resource)\nwhen { context.ip.isIpv6() };",
            IpaddrSchema);

        var ipv4Result = ipv4.Authorize(Request(TypedIpaddr("192.168.1.1")));
        var ipv6Result = ipv6.Authorize(Request(TypedIpaddr("192.168.1.1")));

        Assert.False(ipv4Result.OperationFailed);
        Assert.Equal(CedarDecision.Allow, ipv4Result.Decision);
        Assert.True(ipv4Result.IsCleanAllow);
        Assert.False(ipv6Result.OperationFailed);
        Assert.Equal(CedarDecision.Deny, ipv6Result.Decision);
        Assert.False(ipv6Result.IsCleanAllow);
        Assert.Empty(ipv6Result.Errors);
    }

    [Fact]
    public void AValidTypedDatetimeContextValueAuthorizesWithTheIntendedCedarSemantics()
    {
        using var plan = Plan(DatetimeEqualsSource, DatetimeSchema);
        var result = plan.Authorize(Request(TypedDatetime("2024-01-01T00:00:00Z")));

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

    [Fact]
    public void ATypedDatetimeContextValueThatDiffersFromThePolicyLiteralIsACleanDeny()
    {
        using var plan = Plan(DatetimeEqualsSource, DatetimeSchema);
        var result = plan.Authorize(Request(TypedDatetime("2024-01-01T00:00:01Z")));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Deny, result.Decision);
        Assert.False(result.IsCleanAllow);
        Assert.False(result.Authorized);
        Assert.Empty(result.DeterminingPolicyIds);
        Assert.Empty(result.Errors);
        Assert.Empty(result.PolicyErrors);
    }

    [Fact]
    public void TheCedarDatetimeOrderingOperatorObservesARealDatetimeValue()
    {
        // The ordering operators are only defined for the Cedar datetime type. A string
        // comparison would answer both requests the same way.
        using var plan = Plan(
            "permit(principal, action == App::Action::\"read\", resource)\n" +
            "when { context.ts < datetime(\"2025-01-01T00:00:00Z\") };",
            DatetimeSchema);

        var earlier = plan.Authorize(Request(TypedDatetime("2024-01-01T00:00:00Z")));
        var later = plan.Authorize(Request(TypedDatetime("2025-01-01T00:00:00Z")));

        Assert.False(earlier.OperationFailed);
        Assert.Equal(CedarDecision.Allow, earlier.Decision);
        Assert.True(earlier.IsCleanAllow);
        Assert.False(later.OperationFailed);
        Assert.Equal(CedarDecision.Deny, later.Decision);
        Assert.False(later.IsCleanAllow);
        Assert.Empty(later.Errors);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("192.168.1.0/24")]
    public void TheTypedIpaddrBoundaryValuesAreEvaluatedCorrectly(string address)
    {
        using var plan = Plan(
            $"permit(principal, action == App::Action::\"read\", resource)\nwhen {{ context.ip == ip(\"{address}\") }};",
            IpaddrSchema);
        var result = plan.Authorize(Request(TypedIpaddr(address)));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Allow, result.Decision);
        Assert.True(result.IsCleanAllow);
        Assert.True(result.Authorized);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("1970-01-01T00:00:00Z")]
    [InlineData("2024-02-29T00:00:00Z")]
    public void TheTypedDatetimeBoundaryValuesAreEvaluatedCorrectly(string instant)
    {
        using var plan = Plan(
            $"permit(principal, action == App::Action::\"read\", resource)\nwhen {{ context.ts == datetime(\"{instant}\") }};",
            DatetimeSchema);
        var result = plan.Authorize(Request(TypedDatetime(instant)));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Allow, result.Decision);
        Assert.True(result.IsCleanAllow);
        Assert.True(result.Authorized);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void TheTypedDatetimeValueImmediatelyBeforeThePolicyLiteralIsACleanDeny()
    {
        using var plan = Plan(DatetimeEqualsSource, DatetimeSchema);
        var result = plan.Authorize(Request(TypedDatetime("2023-12-31T23:59:59Z")));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Deny, result.Decision);
        Assert.False(result.IsCleanAllow);
        Assert.False(result.Authorized);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("{\"ip\":{\"__extn\":{\"fn\":\"ip\",\"arg\":\"not-an-ip\"}}}")]
    [InlineData("{\"ip\":{\"__extn\":{\"fn\":\"ip\",\"arg\":\"256.256.256.256\"}}}")]
    [InlineData("{\"ip\":{\"__extn\":{\"fn\":\"ip\"}}}")]
    [InlineData("{\"ip\":{\"__extn\":\"ip\"}}")]
    [InlineData("{\"ip\":{\"__extn\":{\"fn\":\"ip\",\"arg\":5}}}")]
    [InlineData("{\"ip\":{\"__extn\":{\"fn\":\"unknown\",\"arg\":\"192.168.1.1\"}}}")]
    public void AnInvalidOrMalformedTypedIpaddrValueFailsClosed(string contextJson)
    {
        using var plan = Plan(IpaddrEqualsSource, IpaddrSchema);
        var result = plan.Authorize(Request(contextJson));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.IsCleanAllow);
        Assert.False(result.Authorized);
        Assert.Empty(result.DeterminingPolicyIds);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("{\"ts\":{\"__extn\":{\"fn\":\"datetime\",\"arg\":\"not-a-datetime\"}}}")]
    [InlineData("{\"ts\":{\"__extn\":{\"fn\":\"datetime\",\"arg\":\"2023-02-29T00:00:00Z\"}}}")]
    [InlineData("{\"ts\":{\"__extn\":{\"fn\":\"datetime\"}}}")]
    [InlineData("{\"ts\":{\"__extn\":\"datetime\"}}")]
    public void AnInvalidOrMalformedTypedDatetimeValueFailsClosed(string contextJson)
    {
        using var plan = Plan(DatetimeEqualsSource, DatetimeSchema);
        var result = plan.Authorize(Request(contextJson));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.IsCleanAllow);
        Assert.False(result.Authorized);
        Assert.Empty(result.DeterminingPolicyIds);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void AMissingTypedContextAttributeFailsClosedInsteadOfBeingDefaulted()
    {
        using var plan = Plan(IpaddrEqualsSource, IpaddrSchema);
        var result = plan.Authorize(Request("{}"));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.IsCleanAllow);
        Assert.False(result.Authorized);
        Assert.Contains(result.Errors, error => error.Category == CedarDiagnosticCategory.MissingAttribute);
    }

    [Fact]
    public void TheTypedContextValueIsNeverSubstitutedByThePolicyLiteral()
    {
        using var plan = Plan(IpaddrEqualsSource, IpaddrSchema);
        var matching = plan.Authorize(Request(TypedIpaddr("192.168.1.1")));
        var substituted = plan.Authorize(Request(TypedIpaddr("10.0.0.1")));

        // If the adapter replaced the caller's typed value with the policy literal, both
        // requests would authorize. The second one must deny.
        Assert.True(matching.Authorized);
        Assert.False(substituted.OperationFailed);
        Assert.Equal(CedarDecision.Deny, substituted.Decision);
        Assert.False(substituted.Authorized);
        Assert.Empty(substituted.Errors);
    }

    [Fact]
    public void TheTypedContextLoweringSynthesizesNoContextField()
    {
        // A typed context value is caller data. The lowering must not generate a trusted
        // context field for it, so the value can only ever come from the request.
        var export = Dogwood.ExportCedar(IpaddrEqualsSource, new DogwoodSchema(IpaddrSchema, Distincter: "c23typed"));

        Assert.True(export.ArtifactsExported);
        Assert.True(export.SelfContainedCedar);
        Assert.Empty(export.GeneratedContextFields);
    }

    [Fact]
    public void AnExtraCallerFieldBesideTheTypedValueIsRejectedBeforeDispatch()
    {
        using var plan = Plan(IpaddrEqualsSource, IpaddrSchema);
        var result = plan.Authorize(Request(
            "{\"ip\":{\"__extn\":{\"fn\":\"ip\",\"arg\":\"192.168.1.1\"}},\"extra\":1}"));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
        Assert.Contains(result.Errors, error => error.Category == CedarDiagnosticCategory.MissingAttribute);
    }

    [Fact]
    public void TheTrustedBindingChannelCannotBeReachedFromTheTypedContext()
    {
        // The trusted pipeline owns the generated context fields. A caller that supplies a
        // field with that name in a typed context is rejected before dispatch, never merged.
        using var plan = Plan(IpaddrEqualsSource, IpaddrSchema);
        var result = plan.Authorize(Request(
            "{\"ip\":{\"__extn\":{\"fn\":\"ip\",\"arg\":\"192.168.1.1\"}},\"providers\":{\"Http::Fetch\":\"value\"}}"));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
        Assert.Contains(result.Errors, error => error.Category == CedarDiagnosticCategory.MissingAttribute);
    }
}
