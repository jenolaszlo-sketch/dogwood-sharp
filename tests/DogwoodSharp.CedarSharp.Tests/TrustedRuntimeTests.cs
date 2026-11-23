using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DogwoodSharp;
using DogwoodSharp.Experimental.CedarSharp;
using Xunit;

namespace DogwoodSharp.CedarSharp.Tests;

/// <summary>
/// Production-boundary tests for the trusted temporal/provider path. Every temporal and provider
/// value is computed by the pinned upstream authorizer inside the native operation 4 runtime and is
/// consumed through <see cref="DogwoodCedarAuthorizer"/>; the tests never inject a generated value
/// into ordinary caller context.
/// </summary>
public sealed class TrustedRuntimeTests
{
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
          action Audit appliesTo {
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

    private const string MultipleTemporalSource =
        """
        permit (principal, action == Test::Action::"Sell", resource)
        when temporal { formerly within 1h Test::Action::"Approve"::request { input.stock: context.input.stock } };
        permit (principal, action == Test::Action::"Sell", resource)
        when temporal { formerly within 1h Test::Action::"Audit"::request { input.stock: context.input.stock } };
        """;

    private const string ProviderSchema =
        """
        namespace Test {
          entity User;
          entity Item;
          action Read appliesTo {
            principal: [User],
            resource: [Item],
            context: { input: { key: String } }
          };
        }
        """;

    private const string ProviderSource =
        """
        permit (principal, action == Test::Action::"Read", resource)
        when { Http::Fetch("https://trusted.invalid/fetch", context.input.key).body == "ok" };
        """;

    private const string ProviderDeclarations =
        """
        {
          "availableProviders": {
            "Http::Fetch": {
              "argumentTypes": [
                { "paramType": "string" },
                { "paramType": "string" }
              ],
              "outputType": {
                "paramType": "record",
                "fields": { "body": { "paramType": "string" } },
                "required": ["body"]
              },
              "implementation": {
                "kind": "rhai",
                "script": "fn evaluate(_url, _key) { #{ body: \"ok\" } }"
              }
            }
          }
        }
        """;

    private static CedarEntityRef User(string id) => new("Test::User", id);
    private static CedarEntityRef Item(string id) => new("Test::Item", id);
    private static CedarEntityRef Action(string id) => new("Test::Action", id);

    private static CedarAuthorizationInput Request(
        string action = "Sell",
        string contextJson = "{\"input\":{\"stock\":\"AMZN\"}}",
        string entitiesJson = "[]") =>
        new(User("alice"), Action(action), Item("i1"), contextJson, entitiesJson);

    private static DogwoodTrustedEvent HistoryEvent(
        string identity,
        long timestamp,
        string action,
        string stock = "AMZN") =>
        new(
            identity,
            timestamp,
            "Test::Action",
            action,
            "request",
            User("alice"),
            Item("i1"),
            $"{{\"input\":{{\"stock\":\"{stock}\"}}}}",
            $"{{\"input\":{{\"stock\":\"{stock}\"}}}}",
            "[]");

    private static DogwoodTrustedRuntime TemporalRuntime(
        long currentTimestamp = 100,
        IEnumerable<DogwoodTrustedEvent>? history = null) =>
        new(
            currentTimestamp,
            string.Empty,
            history ?? [HistoryEvent("history-1", 0, "Approve")],
            []);

    private static DogwoodTrustedRuntime ProviderRuntime(string body) =>
        new(
            100,
            string.Empty,
            [],
            [DogwoodTrustedProviderOutcome.Succeeded("Http::Fetch", $"{{\"body\":\"{body}\"}}")]);

    [Fact]
    public void TrustedTemporalTrueBindingIsUsedAndAttributed()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2t"));

        var result = plan.AuthorizeWithTrustedRuntime(Request(), TemporalRuntime());

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarAuthorizationStage.Evaluation, result.Stage);
        Assert.Equal(CedarDecision.Allow, result.Decision);
        Assert.True(result.IsCleanAllow);
        Assert.True(result.Authorized);
        Assert.Equal(plan.PolicyIds, result.DeterminingPolicyIds);
        Assert.Empty(result.PolicyErrors);
    }

    [Fact]
    public void TrustedTemporalFalseBindingProducesACleanDefaultDeny()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2t"));

        var runtime = TemporalRuntime(history:
        [
            HistoryEvent("history-1", 0, "Approve", "MSFT")
        ]);
        var result = plan.AuthorizeWithTrustedRuntime(Request(), runtime);

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Deny, result.Decision);
        Assert.False(result.Authorized);
        Assert.Empty(result.DeterminingPolicyIds);
        Assert.Empty(result.PolicyErrors);
    }

    [Fact]
    public void EmptyCurrentLoggedRecordMirrorsOnlyTheCurrentInputGroup()
    {
        const string CurrentInputSource =
            """
            permit (principal, action == Test::Action::"Sell", resource)
            when temporal { formerly within 1h Test::Action::"Sell"::request { input.stock: context.input.stock } };
            """;
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            CurrentInputSource, new DogwoodSchema(TemporalSchema, Distincter: "r2current"));

        var result = plan.AuthorizeWithTrustedRuntime(Request(), TemporalRuntime(history: []));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Allow, result.Decision);
        Assert.True(result.IsCleanAllow);
        Assert.True(result.Authorized);
        Assert.Equal(plan.PolicyIds, result.DeterminingPolicyIds);
    }

    [Fact]
    public void EmptyTrustedHistoryProducesACleanTemporalDeny()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2t"));

        var result = plan.AuthorizeWithTrustedRuntime(Request(), TemporalRuntime(history: []));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Deny, result.Decision);
        Assert.False(result.Authorized);
        Assert.Empty(result.DeterminingPolicyIds);
        Assert.Empty(result.PolicyErrors);
    }

    [Fact]
    public void NonDecisionTrustedHistoryIsObservedWithoutBecomingADecision()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2t"));

        var response = new DogwoodTrustedEvent(
            "history-response", 0, "Test::Action", "Approve", "response",
            User("alice"), Item("i1"),
            "{\"input\":{\"stock\":\"AMZN\"}}",
            "{\"input\":{\"stock\":\"AMZN\"}}",
            "[]");
        var result = plan.AuthorizeWithTrustedRuntime(
            Request(), TemporalRuntime(history: [response]));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Deny, result.Decision);
        Assert.False(result.Authorized);
        Assert.Empty(result.DeterminingPolicyIds);
        Assert.Empty(result.PolicyErrors);
    }

    [Fact]
    public void MultipleTrustedTemporalFieldsAreAllBoundAndAttributed()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            MultipleTemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2multi"));

        var runtime = TemporalRuntime(history:
        [
            HistoryEvent("history-1", 0, "Approve"),
            HistoryEvent("history-2", 10, "Audit")
        ]);
        var result = plan.AuthorizeWithTrustedRuntime(Request(), runtime);

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Allow, result.Decision);
        Assert.True(result.Authorized);
        Assert.Equal(plan.PolicyIds.OrderBy(id => id, StringComparer.Ordinal),
            result.DeterminingPolicyIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(3600, 0, true)]
    [InlineData(3601, 0, false)]
    public void TemporalWindowBoundaryUsesTheTrustedMonotonicTimeline(
        long currentTimestamp, long historyTimestamp, bool expectedAllow)
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2t"));

        var runtime = TemporalRuntime(currentTimestamp,
        [
            HistoryEvent("history-1", historyTimestamp, "Approve")
        ]);
        var result = plan.AuthorizeWithTrustedRuntime(Request(), runtime);

        Assert.False(result.OperationFailed);
        Assert.Equal(expectedAllow ? CedarDecision.Allow : CedarDecision.Deny, result.Decision);
    }

    [Fact]
    public void HistoryAtTheCurrentTimestampFailsClosedAsAnInvalidTimeline()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2t"));

        var runtime = TemporalRuntime(100,
        [
            HistoryEvent("history-1", 100, "Approve")
        ]);
        var result = plan.AuthorizeWithTrustedRuntime(Request(), runtime);

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
        Assert.Equal(CedarAuthorizationStage.TrustedBindingUnavailable, result.Stage);
    }

    [Fact]
    public void MalformedTrustedHistoryFailsClosedWithoutADecision()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2t"));

        var malformed = new DogwoodTrustedEvent(
            "history-1", 0, "Test::Action", "Approve", "request",
            User("alice"), Item("i1"), "{}", "not-json", "[]");
        var result = plan.AuthorizeWithTrustedRuntime(Request(), TemporalRuntime(history: [malformed]));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
        Assert.Equal(CedarAuthorizationStage.TrustedBindingUnavailable, result.Stage);
        Assert.Contains(CedarDiagnosticCategory.TrustedBindingUnavailable, result.Errors.Select(error => error.Category));
    }

    [Fact]
    public void DuplicateTrustedHistoryIdentityFailsClosed()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2t"));

        var runtime = TemporalRuntime(history:
        [
            HistoryEvent("duplicate", 0, "Approve"),
            HistoryEvent("duplicate", 1, "Approve")
        ]);
        var result = plan.AuthorizeWithTrustedRuntime(Request(), runtime);

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
    }

    [Fact]
    public void NonMonotonicTrustedHistoryFailsClosed()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2t"));

        var runtime = TemporalRuntime(history:
        [
            HistoryEvent("history-1", 10, "Approve"),
            HistoryEvent("history-2", 9, "Approve")
        ]);
        var result = plan.AuthorizeWithTrustedRuntime(Request(), runtime);

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
    }

    [Fact]
    public void CallerCannotSubstituteTheReservedTemporalField()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2t"));
        var export = Dogwood.ExportCedar(
            TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: "r2t"));
        var reserved = Assert.Single(export.GeneratedContextFields);

        var context = $"{{\"input\":{{\"stock\":\"AMZN\"}},\"{reserved}\":true}}";
        var result = plan.AuthorizeWithTrustedRuntime(
            Request(contextJson: context), TemporalRuntime());

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
        Assert.Equal(CedarAuthorizationStage.SchemaOrRequestValidation, result.Stage);
        Assert.Contains(CedarDiagnosticCategory.ReservedContextField, result.Errors.Select(error => error.Category));
    }

    [Fact]
    public void GeneratedTemporalFieldCollisionPreventsBundleConstruction()
    {
        const string CollisionSchema =
            """
            namespace Test {
              entity User;
              entity Item;
              action Approve appliesTo {
                principal: [User], resource: [Item],
                context: { input: { stock: String } }
              };
              action Sell appliesTo {
                principal: [User], resource: [Item],
                context: { input: { stock: String }, r2t_0__temporal_0: Bool }
              };
            }
            """;

        Assert.Throws<CedarBundleException>(() => DogwoodCedarAuthorizer.ForLoweredBundle(
            TemporalSource, new DogwoodSchema(CollisionSchema, Distincter: "r2t")));
    }

    [Fact]
    public void TrustedProviderValueIsUsedAndAttributed()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            ProviderSource,
            new DogwoodSchema(ProviderSchema, Distincter: "r2p"),
            ProviderDeclarations);

        var result = plan.AuthorizeWithTrustedRuntime(
            Request("Read", "{\"input\":{\"key\":\"document\"}}"), ProviderRuntime("ok"));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Allow, result.Decision);
        Assert.True(result.IsCleanAllow);
        Assert.True(result.Authorized);
        Assert.Equal(plan.PolicyIds, result.DeterminingPolicyIds);
        Assert.Empty(result.PolicyErrors);
    }

    [Fact]
    public void TrustedProviderFalseValueProducesACleanDefaultDeny()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            ProviderSource,
            new DogwoodSchema(ProviderSchema, Distincter: "r2p"),
            ProviderDeclarations);

        var result = plan.AuthorizeWithTrustedRuntime(
            Request("Read", "{\"input\":{\"key\":\"document\"}}"), ProviderRuntime("blocked"));

        Assert.False(result.OperationFailed);
        Assert.Equal(CedarDecision.Deny, result.Decision);
        Assert.False(result.Authorized);
        Assert.Empty(result.DeterminingPolicyIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OmittedOrFailedProviderFailsClosedWithoutADecision(bool failure)
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            ProviderSource,
            new DogwoodSchema(ProviderSchema, Distincter: "r2p"),
            ProviderDeclarations);

        var outcome = failure
            ? DogwoodTrustedProviderOutcome.Failed("Http::Fetch", "trusted provider failed")
            : DogwoodTrustedProviderOutcome.Omitted("Http::Fetch", "trusted provider omitted");
        var runtime = new DogwoodTrustedRuntime(100, string.Empty, [], [outcome]);
        var result = plan.AuthorizeWithTrustedRuntime(
            Request("Read", "{\"input\":{\"key\":\"document\"}}"), runtime);

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
        Assert.Equal(CedarAuthorizationStage.TrustedBindingUnavailable, result.Stage);
    }

    [Fact]
    public void MissingProviderOutcomeFailsClosedWithoutAFallback()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            ProviderSource,
            new DogwoodSchema(ProviderSchema, Distincter: "r2p"),
            ProviderDeclarations);

        var runtime = new DogwoodTrustedRuntime(100, string.Empty, [], []);
        var result = plan.AuthorizeWithTrustedRuntime(
            Request("Read", "{\"input\":{\"key\":\"document\"}}"), runtime);

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
    }

    [Fact]
    public void MalformedTrustedProviderValueFailsClosed()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            ProviderSource,
            new DogwoodSchema(ProviderSchema, Distincter: "r2p"),
            ProviderDeclarations);

        var runtime = new DogwoodTrustedRuntime(
            100,
            string.Empty,
            [],
            [DogwoodTrustedProviderOutcome.Succeeded("Http::Fetch", "not-json")]);
        var result = plan.AuthorizeWithTrustedRuntime(
            Request("Read", "{\"input\":{\"key\":\"document\"}}"), runtime);

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
        Assert.Equal(CedarAuthorizationStage.TrustedBindingUnavailable, result.Stage);
    }

    [Fact]
    public void CallerCannotSubstituteTheReservedProviderRecord()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            ProviderSource,
            new DogwoodSchema(ProviderSchema, Distincter: "r2p"),
            ProviderDeclarations);

        var context = "{\"input\":{\"key\":\"document\"},\"providers\":{\"r2p_0_p_0\":{\"body\":\"ok\"}}}";
        var result = plan.AuthorizeWithTrustedRuntime(
            Request("Read", context), ProviderRuntime("ok"));

        Assert.True(result.OperationFailed);
        Assert.Null(result.Decision);
        Assert.False(result.Authorized);
        Assert.Equal(CedarAuthorizationStage.SchemaOrRequestValidation, result.Stage);
        Assert.Contains(CedarDiagnosticCategory.ReservedContextField, result.Errors.Select(error => error.Category));
    }

    [Fact]
    public void MalformedProviderDeclarationsPreventBundleConstruction()
    {
        Assert.Throws<CedarBundleException>(() => DogwoodCedarAuthorizer.ForLoweredBundle(
            ProviderSource,
            new DogwoodSchema(ProviderSchema, Distincter: "r2p"),
            "{not-json"));
    }

    [Fact]
    public async Task FourConcurrentWorkersKeepProviderContextsResultsAndFailuresIsolated()
    {
        using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
            ProviderSource,
            new DogwoodSchema(ProviderSchema, Distincter: "r2p"),
            ProviderDeclarations);

        const int workerCount = 4;
        const int iterations = 8;
        using var start = new Barrier(workerCount);
        var failures = new ConcurrentQueue<string>();
        var tasks = Enumerable.Range(0, workerCount).Select(worker => Task.Run(() =>
        {
            start.SignalAndWait();
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                var runtime = worker switch
                {
                    0 => ProviderRuntime("ok"),
                    1 => ProviderRuntime("blocked"),
                    2 => new DogwoodTrustedRuntime(
                        100,
                        string.Empty,
                        [],
                        [DogwoodTrustedProviderOutcome.Failed("Http::Fetch", "trusted provider failed")]),
                    _ => new DogwoodTrustedRuntime(
                        100,
                        string.Empty,
                        [],
                        [DogwoodTrustedProviderOutcome.Omitted("Http::Fetch", "trusted provider omitted")]),
                };
                var result = plan.AuthorizeWithTrustedRuntime(
                    Request("Read", $"{{\"input\":{{\"key\":\"document-{worker}\"}}}}"), runtime);

                if (worker < 2)
                {
                    var expected = worker == 0 ? CedarDecision.Allow : CedarDecision.Deny;
                    if (result.OperationFailed || result.Decision != expected ||
                        result.Authorized != (worker == 0) ||
                        (worker == 0 && !result.DeterminingPolicyIds.SequenceEqual(plan.PolicyIds)))
                    {
                        failures.Enqueue($"worker={worker} iteration={iteration}");
                    }
                }
                else if (!result.OperationFailed || result.Decision is not null || result.Authorized ||
                    result.Stage != CedarAuthorizationStage.TrustedBindingUnavailable)
                {
                    failures.Enqueue($"worker={worker} iteration={iteration}");
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task IndependentAuthorizersCanBeUsedConcurrently()
    {
        const int workerCount = 4;
        using var start = new Barrier(workerCount);
        var failures = new ConcurrentQueue<string>();
        var tasks = Enumerable.Range(0, workerCount).Select(worker => Task.Run(() =>
        {
            start.SignalAndWait();
            using var plan = DogwoodCedarAuthorizer.ForLoweredBundle(
                TemporalSource, new DogwoodSchema(TemporalSchema, Distincter: $"r2c{worker}"));
            var runtime = TemporalRuntime(history:
            [
                HistoryEvent($"history-{worker}", worker, "Approve")
            ]);
            var result = plan.AuthorizeWithTrustedRuntime(Request(), runtime);
            if (result.OperationFailed || !result.Authorized)
            {
                failures.Enqueue($"worker={worker}");
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        Assert.Empty(failures);
    }
}
