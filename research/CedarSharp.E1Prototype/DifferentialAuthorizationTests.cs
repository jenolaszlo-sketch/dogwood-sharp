using System.Text.Json;
using CedarSharp;
using Xunit;
using Xunit.Abstractions;

namespace CedarSharp.E1Prototype;

/// <summary>
/// E1-4 differential authorization: every corpus case is evaluated by
/// CedarSharp (Cedar 4.13.0) and by the pinned Cedar 4.11.0 reference on the
/// same effective policy set, schema, entities, context, and request.
/// </summary>
/// <remarks>
/// Semantic content is compared, not wording: the success marker, the raw
/// decision, the determining policy ID sets, the per-policy error counts and
/// their policy IDs, and operation-level error presence. Diagnostic message
/// wording may differ between SDK versions and is reported, not normalized
/// away.
/// </remarks>
public sealed class DifferentialAuthorizationTests
{
    private readonly ITestOutputHelper output;

    public DifferentialAuthorizationTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void EveryCorpusCase_AgreesAcrossTheVersionPair()
    {
        var failures = new List<string>();
        foreach (var testCase in E1Corpus.Cases)
        {
            var (bundle, managed) = E1Requests.Run(testCase);
            var entities = testCase.MergeActionEntities
                ? E1Requests.MergeActionEntities(testCase.EntitiesJson, bundle)
                : testCase.EntitiesJson;

            var referenceInput = E1ReferenceInput.Authorization(
                bundle,
                new CedarEntityUid(testCase.Principal.Type, testCase.Principal.Id),
                new CedarEntityUid(testCase.Action.Type, testCase.Action.Id),
                new CedarEntityUid(testCase.Resource.Type, testCase.Resource.Id),
                testCase.ContextJson,
                entities,
                testCase.ValidateRequest,
                testCase.PassSchema);
            using var referenceDocument = Cedar11Reference.Call(Cedar11Reference.OperationAuthorize, referenceInput).Document;
            var reference = NormalizedAnswer.Normalize(referenceDocument.RootElement);
            var managedNormalized = NormalizedAnswer.Normalize(managed.Raw);

            var difference = Compare(managedNormalized, reference);
            if (difference is not null)
            {
                failures.Add($"{testCase.Name}: {difference}");
            }

            output.WriteLine(
                $"{testCase.Name}: managed={Describe(managedNormalized, bundle)} reference={Describe(reference, bundle)}");

            if (testCase.ExpectedDecision is not null)
            {
                var expected = testCase.ExpectedDecision.ToLowerInvariant();
                if (managedNormalized.Decision != expected)
                {
                    failures.Add($"{testCase.Name}: managed decision {managedNormalized.Decision} != expected {expected}");
                }

                if (testCase.ExpectedReasonRuleIndices is not null)
                {
                    var indices = RuleIndices(managedNormalized.Reason, bundle, testCase.Name, failures);
                    if (indices is not null && !indices.SequenceEqual(testCase.ExpectedReasonRuleIndices))
                    {
                        failures.Add(
                            $"{testCase.Name}: determining rule indices [{string.Join(",", indices)}] != expected [{string.Join(",", testCase.ExpectedReasonRuleIndices)}]");
                    }
                }

                if (testCase.ExpectedErrorRuleIndices is not null)
                {
                    var errorIds = managedNormalized.PolicyErrors.Select(error => error.PolicyId).ToArray();
                    var indices = RuleIndices(errorIds, bundle, testCase.Name, failures);
                    if (indices is not null && !indices.SequenceEqual(testCase.ExpectedErrorRuleIndices))
                    {
                        failures.Add(
                            $"{testCase.Name}: erroring rule indices [{string.Join(",", indices)}] != expected [{string.Join(",", testCase.ExpectedErrorRuleIndices)}]");
                    }
                }
            }

            CompareValidation(testCase, bundle, failures);
            CompareEntityParsing(testCase, bundle, entities, failures);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void RawAllowWithPolicyErrors_IsNotACleanAllow()
    {
        var testCase = E1Corpus.Cases.Single(candidate => candidate.Name == "allow-with-policy-errors");
        var (bundle, managed) = E1Requests.Run(testCase);
        output.WriteLine($"managed: success={managed.IsSuccess} decision={managed.Decision} " +
            $"clean={managed.IsCleanAllow} errors={managed.PolicyErrors.Count} operationErrors={managed.Errors.Count} raw={managed.Raw}");

        Assert.True(managed.IsSuccess);
        Assert.Equal(CedarSharp.CedarDecision.Allow, managed.Decision);
        Assert.Equal(["e1_0"], managed.DeterminingPolicies);

        // A raw Allow that carries a policy error is not a clean Allow.
        Assert.False(managed.IsCleanAllow);
        Assert.Single(managed.PolicyErrors);
        Assert.Equal("e1_1", managed.PolicyErrors[0].PolicyId);
        Assert.Equal(1, bundle.RuleIndexFor(managed.PolicyErrors[0].PolicyId));

        // The reference reaches the same mode on the same inputs.
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
        output.WriteLine($"reference: success={reference.IsSuccess} decision={reference.Decision} " +
            $"errors={reference.PolicyErrors.Count} operationErrors={reference.OperationErrors.Count}");

        Assert.True(reference.IsSuccess);
        Assert.Equal("allow", reference.Decision);
        Assert.Equal(["e1_0"], reference.Reason);
        Assert.Single(reference.PolicyErrors);
        Assert.Equal("e1_1", reference.PolicyErrors[0].PolicyId);
    }

    [Fact]
    public void ErroredForbidIsSkipped_AndThePermitDecides()
    {
        var testCase = E1Corpus.Cases.Single(candidate => candidate.Name == "forbid-error-skipped");
        var (bundle, managed) = E1Requests.Run(testCase);
        output.WriteLine($"managed: success={managed.IsSuccess} decision={managed.Decision} " +
            $"errors={managed.PolicyErrors.Count} raw={managed.Raw}");

        Assert.True(managed.IsSuccess);
        Assert.Equal(CedarSharp.CedarDecision.Allow, managed.Decision);
        Assert.Equal(["e1_1"], managed.DeterminingPolicies);
        Assert.False(managed.IsCleanAllow);
        Assert.Single(managed.PolicyErrors);
        Assert.Equal("e1_0", managed.PolicyErrors[0].PolicyId);
        Assert.Equal(0, bundle.RuleIndexFor(managed.PolicyErrors[0].PolicyId));

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
        output.WriteLine($"reference: success={reference.IsSuccess} decision={reference.Decision} " +
            $"errors={reference.PolicyErrors.Count}");

        Assert.True(reference.IsSuccess);
        Assert.Equal("allow", reference.Decision);
        Assert.Equal(["e1_1"], reference.Reason);
        Assert.Single(reference.PolicyErrors);
        Assert.Equal("e1_0", reference.PolicyErrors[0].PolicyId);
    }

    private static int[]? RuleIndices(
        IEnumerable<string> cedarPolicyIds,
        E1Bundle bundle,
        string caseName,
        List<string> failures)
    {
        var indices = new List<int>();
        foreach (var id in cedarPolicyIds)
        {
            var index = bundle.RuleIndexFor(id);
            if (index is null)
            {
                failures.Add($"{caseName}: Cedar policy ID '{id}' maps to no Dogwood rule.");
                return null;
            }

            indices.Add(index.Value);
        }

        indices.Sort();
        return indices.ToArray();
    }

    private static string? Compare(NormalizedAnswer managed, NormalizedAnswer reference)
    {
        if (managed.IsSuccess != reference.IsSuccess)
        {
            return $"success differs (managed {managed.IsSuccess}, reference {reference.IsSuccess})";
        }

        if (managed.Decision != reference.Decision)
        {
            return $"decision differs (managed {managed.Decision ?? "<none>"}, reference {reference.Decision ?? "<none>"})";
        }

        if (!managed.Reason.OrderBy(id => id, StringComparer.Ordinal)
            .SequenceEqual(reference.Reason.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            return $"determining IDs differ (managed [{string.Join(",", managed.Reason)}], reference [{string.Join(",", reference.Reason)}])";
        }

        var managedErrors = managed.PolicyErrors
            .GroupBy(error => error.PolicyId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}={group.Count()}");
        var referenceErrors = reference.PolicyErrors
            .GroupBy(error => error.PolicyId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}={group.Count()}");
        if (!managedErrors.SequenceEqual(referenceErrors, StringComparer.Ordinal))
        {
            return $"policy errors differ (managed [{string.Join(",", managedErrors)}], reference [{string.Join(",", referenceErrors)}])";
        }

        if ((managed.OperationErrors.Count > 0) != (reference.OperationErrors.Count > 0))
        {
            return $"operation error presence differs (managed {managed.OperationErrors.Count}, reference {reference.OperationErrors.Count})";
        }

        if (managed.Warnings.Count != reference.Warnings.Count)
        {
            return $"warning counts differ (managed {managed.Warnings.Count}, reference {reference.Warnings.Count})";
        }

        return null;
    }

    private static string Describe(NormalizedAnswer answer, E1Bundle bundle) =>
        $"{(answer.IsSuccess ? answer.Decision ?? "<none>" : "operation-failure")}" +
        $" reasons=[{string.Join(",", answer.Reason.OrderBy(id => id, StringComparer.Ordinal))}]" +
        $" errors=[{string.Join(",", answer.PolicyErrors.Select(error => $"{error.PolicyId}:{error.Message.Split('\n')[0]}"))}]" +
        $" operationErrors=[{string.Join(" | ", answer.OperationErrors.Select(message => message.Split('\n')[0]))}]";

    private static void CompareValidation(E1Case testCase, E1Bundle bundle, List<string> failures)
    {
        var managed = E1Requests.Engine.ValidatePolicies(bundle.PolicySet, bundle.SchemaJson);
        using var referenceDocument = Cedar11Reference.Call(
            Cedar11Reference.OperationValidate,
            E1ReferenceInput.Validate(bundle)).Document;
        var reference = referenceDocument.RootElement;

        var managedType = managed.Raw.GetProperty("type").GetString();
        var referenceType = reference.GetProperty("type").GetString();
        if (managedType != referenceType)
        {
            failures.Add($"{testCase.Name}: validation outcome type differs (managed {managedType}, reference {referenceType}).");
            return;
        }

        if (managedType == "success")
        {
            var managedErrors = managed.ValidationErrors.Select(error => error.PolicyId).OrderBy(id => id, StringComparer.Ordinal);
            var referenceErrors = reference.GetProperty("validationErrors").EnumerateArray()
                .Select(error => error.GetProperty("policyId").GetString() ?? string.Empty)
                .OrderBy(id => id, StringComparer.Ordinal);
            if (!managedErrors.SequenceEqual(referenceErrors, StringComparer.Ordinal))
            {
                failures.Add($"{testCase.Name}: validation error policy IDs differ.");
            }

            if (managed.ValidationWarnings.Count != reference.GetProperty("validationWarnings").GetArrayLength())
            {
                failures.Add($"{testCase.Name}: validation warning counts differ.");
            }
        }
        else if (managedType == "failure")
        {
            var managedCount = managed.Errors.Count;
            var referenceCount = reference.GetProperty("errors").GetArrayLength();
            if (managedCount != referenceCount)
            {
                failures.Add($"{testCase.Name}: validation failure counts differ ({managedCount} vs {referenceCount}).");
            }
        }
    }

    private static void CompareEntityParsing(
        E1Case testCase, E1Bundle bundle, string entitiesJson, List<string> failures)
    {
        var managed = E1Requests.Engine.CheckEntities(entitiesJson, bundle.SchemaJson);
        var input = $"{{\"entities\":{entitiesJson},\"schema\":{bundle.Lowered.CedarSchemaJson}}}";
        using var referenceDocument = Cedar11Reference.Call(Cedar11Reference.OperationCheckEntities, input).Document;
        var reference = referenceDocument.RootElement;
        var referenceType = reference.GetProperty("type").GetString();
        var managedType = managed.Raw.GetProperty("type").GetString();
        if (managedType != referenceType)
        {
            failures.Add(
                $"{testCase.Name}: entity parse outcome differs (managed {managedType} [{string.Join(" | ", managed.Errors.Select(error => error.Message.Split('\n')[0]))}], reference {referenceType}).");
        }
    }
}
