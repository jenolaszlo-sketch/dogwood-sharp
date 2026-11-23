using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DogwoodSharp;
using DogwoodSharp.Experimental.CedarSharp;

internal static class Program
{
    private const string Schema =
        "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { count: Long } }; }";

    private const string Source =
        "permit (principal, action == App::Action::\"read\", resource) when { context.count > 3 };";

    private const string TemporalSchema =
        """
        namespace Test {
          entity User;
          entity Item;
          action Approve appliesTo {
            principal: [User],
            resource: [Item],
            context: { input: { stock: String } }
          };
          action Sell appliesTo {
            principal: [User],
            resource: [Item],
            context: { input: { stock: String } }
          };
        }
        """;

    private const string TemporalSource =
        """
        permit (principal, action == Test::Action::"Sell", resource)
        when temporal { formerly within 1h Test::Action::"Approve"::request { input.stock: context.input.stock } };
        """;

    public static int Main()
    {
        try
        {
            Phase("consumption-mode", "start");
            Console.WriteLine("D3_CONSUMER_MODE=PACKAGE_ONLY");
            Phase("consumption-mode", "ok");

            Phase("native-identity", "start");
            VerifyNativeIdentity();
            Phase("native-identity", "ok");

            Phase("lower", "start");
            using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(Source, new DogwoodSchema(Schema));
            Require(authorizer.SelfContained, "The ordinary lowering unexpectedly requires a trusted runtime.");
            Require(authorizer.PolicyIds.Count == 1, "The lowering must produce exactly one policy identity.");
            Require(!string.IsNullOrWhiteSpace(authorizer.PolicyIds[0]), "The generated policy identity must not be empty.");
            Require(authorizer.CanonicalSchemaJson.Length != 0, "The lowered canonical schema must not be empty.");
            Phase("lower", "ok");

            Phase("authorize-allow", "start");
            var request = new CedarAuthorizationInput(
                new CedarEntityRef("App::User", "alice"),
                new CedarEntityRef("App::Action", "read"),
                new CedarEntityRef("App::Doc", "doc-1"),
                BuildContextJson(5),
                BuildEntitiesJson());
            var result = authorizer.Authorize(request);
            Require(!result.OperationFailed, "The qualifying authorization operation failed.");
            Require(result.Stage == CedarAuthorizationStage.Evaluation, "The authorization did not reach evaluation.");
            Require(result.Decision == CedarDecision.Allow, "The qualifying request was not allowed.");
            Require(result.Errors.Count == 0, "The qualifying authorization returned diagnostics.");
            Require(result.PolicyErrors.Count == 0, "The qualifying authorization returned policy errors.");
            Require(result.IsCleanAllow, "The qualifying Allow was not clean.");
            Require(result.Authorized, "The clean Allow did not authorize.");
            Phase("authorize-allow", "ok");

            Phase("authorize-deny", "start");
            var denied = authorizer.Authorize(request with { ContextJson = BuildContextJson(2) });
            Require(!denied.OperationFailed, "The ordinary Deny was reported as an operation failure.");
            Require(denied.Stage == CedarAuthorizationStage.Evaluation, "The ordinary Deny did not reach evaluation.");
            Require(denied.Decision == CedarDecision.Deny, "The non-qualifying request was not denied.");
            Require(!denied.Authorized, "The ordinary Deny authorized the request.");
            Require(!denied.IsCleanAllow, "The ordinary Deny was reported as a clean Allow.");
            Require(denied.DeterminingPolicyIds.Count == 0, "The ordinary Deny named a determining policy.");
            Require(denied.Errors.Count == 0, "The ordinary Deny returned diagnostics.");
            Require(denied.PolicyErrors.Count == 0, "The ordinary Deny returned policy errors.");
            Phase("authorize-deny", "ok");

            Phase("identity", "start");
            var expectedPolicyId = authorizer.PolicyIds[0];
            Require(result.DeterminingPolicyIds.Count == 1, "The clean Allow must identify exactly one determining policy.");
            Require(result.DeterminingPolicyIds[0] == expectedPolicyId, "The determining policy identity differs from the lowered identity.");
            Require(authorizer.RuleIndexFor(expectedPolicyId) == 0, "The determining policy identity does not map to source rule zero.");
            Phase("identity", "ok");

            Phase("temporal", "start");
            VerifyTemporalRuntime();
            Phase("temporal", "ok");

            Phase("fail-closed", "start");
            var wrongScope = authorizer.Authorize(request with
            {
                Principal = new CedarEntityRef("App::Doc", "not-a-user")
            });
            Require(wrongScope.OperationFailed, "An out-of-scope principal did not fail closed.");
            Require(wrongScope.Decision is null, "An out-of-scope principal produced a decision.");
            Require(!wrongScope.Authorized, "An out-of-scope principal authorized.");
            Require(!wrongScope.IsCleanAllow, "An out-of-scope principal produced a clean Allow.");

            var malformed = authorizer.Authorize(request with { ContextJson = "not-json" });
            Require(malformed.OperationFailed, "Malformed JSON did not fail closed.");
            Require(malformed.Decision is null, "Malformed JSON produced a decision.");
            Require(!malformed.Authorized, "Malformed JSON authorized.");
            Phase("fail-closed", "ok");

            Console.WriteLine("D3_PACKAGE_CONSUMER_RESULT=PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("D3_PACKAGE_CONSUMER_RESULT=FAIL");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyNativeIdentity()
    {
        var identity = Dogwood.GetNativeVersion();
        Require(identity.AbiVersion == 1, "Unexpected native ABI version.");
        Require(identity.BridgeVersion == "0.1.0", "Unexpected native bridge version.");
        Require(identity.DogwoodRevision == "929f49e7305534ac61ef06cf8e47269c596dc851", "Unexpected Dogwood revision.");
        Require(identity.DogwoodVersion == "1.1.2", "Unexpected Dogwood version.");
        Require(identity.CedarVersion == "4.11.0", "Unexpected native Cedar version.");
        Require(identity.RustVersion == "1.99.1", "Unexpected native Rust version.");
        Require(identity.Target == ExpectedNativeTarget(), "The loaded native image has the wrong target for this host.");
    }

    private static string ExpectedNativeTarget()
    {
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return "x86_64-pc-windows-msvc";
        }

        if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return "x86_64-unknown-linux-gnu";
        }

        if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return "aarch64-apple-darwin";
        }

        throw new InvalidOperationException(
            $"This operating system and process architecture are not supported: {RuntimeInformation.OSDescription} / {RuntimeInformation.ProcessArchitecture}.");
    }

    private static void VerifyTemporalRuntime()
    {
        using var temporal = DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource,
            new DogwoodSchema(TemporalSchema, Distincter: "pkg2t"));
        Require(!temporal.SelfContained, "The temporal lowering must not be self-contained.");

        var history = new DogwoodTrustedEvent(
            "history-1",
            0,
            "Test::Action",
            "Approve",
            "request",
            new CedarEntityRef("Test::User", "alice"),
            new CedarEntityRef("Test::Item", "item-1"),
            "{\"input\":{\"stock\":\"AMZN\"}}",
            "{\"input\":{\"stock\":\"AMZN\"}}",
            "[]");
        var runtime = new DogwoodTrustedRuntime(
            100,
            string.Empty,
            ImmutableArray.Create(history),
            ImmutableArray<DogwoodTrustedProviderOutcome>.Empty);

        var request = new CedarAuthorizationInput(
            new CedarEntityRef("Test::User", "alice"),
            new CedarEntityRef("Test::Action", "Sell"),
            new CedarEntityRef("Test::Item", "item-1"),
            "{\"input\":{\"stock\":\"AMZN\"}}",
            "[]");
        var result = temporal.AuthorizeWithTrustedRuntime(request, runtime);
        Require(!result.OperationFailed, "The trusted temporal authorization operation failed.");
        Require(result.Stage == CedarAuthorizationStage.Evaluation, "The trusted temporal authorization did not reach evaluation.");
        Require(result.Decision == CedarDecision.Allow, "The trusted temporal request was not allowed.");
        Require(result.IsCleanAllow, "The trusted temporal Allow was not clean.");
        Require(result.Authorized, "The trusted temporal Allow did not authorize.");
        Require(result.Errors.Count == 0, "The trusted temporal authorization returned diagnostics.");
        Require(result.PolicyErrors.Count == 0, "The trusted temporal authorization returned policy errors.");
        Require(result.DeterminingPolicyIds.Count == 1, "The trusted temporal Allow must identify one policy.");
        Require(result.DeterminingPolicyIds[0] == temporal.PolicyIds[0], "The trusted temporal policy identity was not preserved.");
        Require(temporal.RuleIndexFor(result.DeterminingPolicyIds[0]) == 0, "The trusted temporal identity does not map to source rule zero.");

        var ordinary = temporal.Authorize(request);
        Require(ordinary.OperationFailed, "The ordinary request path did not fail closed for a temporal policy.");
        Require(ordinary.Decision is null, "The ordinary request path produced a temporal decision.");
        Require(!ordinary.Authorized, "The ordinary request path authorized a temporal policy.");
        Require(
            ordinary.Stage == CedarAuthorizationStage.TrustedBindingUnavailable,
            "The ordinary temporal request did not report an unavailable trusted binding.");
    }

    private static string BuildContextJson(long count)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("count", count);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string BuildEntitiesJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void Phase(string phase, string state) =>
        Console.WriteLine($"D3_PHASE={phase} STATE={state}");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
