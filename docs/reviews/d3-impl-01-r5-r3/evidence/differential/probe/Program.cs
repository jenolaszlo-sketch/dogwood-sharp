// DOGWOOD-SHARP-D3-IMPL-01-R5-R1 managed differential probe (evidence tooling).
//
// This is the successor of the frozen R4 probe
// (docs/reviews/d3-impl-01/evidence/differential/r4/probe). The frozen R4
// probe is preserved byte-for-byte and is NOT modified by this commission;
// this copy adds the R5-R1 obligations and is the only harness the R5-R1
// evidence runs.
//
// The managed decision path is the production DogwoodCedarAuthorizer. Static
// cases use ForLoweredBundle(...).Authorize(...); temporal/provider cases use
// ForLoweredBundle(...).AuthorizeWithTrustedRuntime(...). The upstream Rust
// oracle remains the authorization oracle: this program compares production
// observations against that oracle and never substitutes a Cedar-to-Cedar run.
// The oracle is supplied as an argument, so the same harness compares the
// original pinned Dogwood reference and the corrected reference (the patched
// pinned tree staged by corrected-oracle/prepare-dogwood.ps1).
//
// R5-R1 hardening over the R4 probe (commission DOGWOOD-SHARP-D3-IMPL-01-R5-R1):
//   * family coverage may declare, per family, the decision a required control
//     role must actually produce (`requiredControlDecisions`). The R4
//     validator only checked that a control name appeared and that the family
//     produced some decision, so a required positive observation that turned
//     into a clean Deny still satisfied the family (qualification finding
//     QD3Q-07). The R5-R1 validator requires the declared decision on BOTH
//     sides for at least one case of that control, and the self-test carries
//     the negative control that proves the false pass is now rejected;
//   * a dedicated managed control observes a two-rule export whose rules are
//     supplied in reverse order, so the C-21 reorder obligation is established
//     by an executed observation instead of a single-rule no-op (QD3Q-12);
//   * a dedicated managed control observes a schema with two complete actions
//     that carry independent principal, resource and context constraints, so
//     the C-11 multi-action `appliesTo` canonicalization obligation is
//     established by an executed observation (QD3Q-12).
//
// R3 hardening over the R2 probe (qualification findings QD3Q-02 and QD3Q-07):
//   * dual-failure cases compare stage, decision, operation-failed state,
//     determining sets and policy-error IDs instead of being blanket-normalized
//     to agreement; error categories are compared whenever the upstream side
//     reports them;
//   * every differential case declares the exact upstream stage
//     (`intendedStage`) its observation must reach, and a case fails when that
//     stage is never reached;
//   * an upstream envelope fault (malformed shared fixture input) is a harness
//     fault that always fails the case instead of producing a conformance
//     claim;
//   * an unexpected difference always fails a case even when the fixture's
//     expectation names it, so a corpus cannot bless its own divergence;
//   * family coverage is validated semantically (required controls plus a
//     decision-coverage role per family), not by case name alone;
//   * the native/lifecycle controls are executed here, in isolated processes
//     for destructive scenarios, and the observations are computed from
//     execution evidence rather than supplied prose.
//
// R4 hardening over the R3 probe (commission DOGWOOD-SHARP-D3-IMPL-01-R4,
// C-23 compatibility ceiling closure):
//   * an approved-difference row may declare `authorizedDifferenceFields`: the
//     exact set of comparison fields whose values the owner has authorized to
//     differ for that row's cases. Every observed difference must be inside
//     that set, every field outside it must match exactly, and the authorized
//     fields must all actually differ. The scope is therefore machine-enforced
//     and cannot be widened by editing prose;
//   * a row that carries an `authorizedDifferenceFields` scope may be the only
//     approved difference claimed by its cases, so one ceiling cannot be used
//     to absorb an unrelated divergence;
//   * the register rows carry the exact per-case upstream and D3 observations,
//     including the authorization result and every failure classification, so
//     a registered difference still requires an exact match on both sides;
//   * the C-23 semantic regression controls execute the production adapter
//     against typed ipaddr and datetime context values: valid authorization,
//     boundary values, invalid and malformed values, trusted-context binding
//     integrity, fail-closed behavior on unexpected errors, and the absence of
//     any untrusted substitution of the caller's typed value.
//
// Usage:
//   PrepProbe differential <fixtures.json> <oracle.exe> <differences.json>
//       <results.json> <lifecycle-results.json> <lifecycle-work-root>
//   PrepProbe lifecycle-probe <scenario>
//   PrepProbe self-test <oracle.exe> <output.json>
//
// Test tooling only; never a production execution path.

using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CedarSharp;
using DogwoodSharp;
using DogwoodSharp.Experimental.CedarSharp;

namespace R5R1Probe;

internal static class Program
{
    private const string SchemaVersion = "3";
    private const string Commission = "DOGWOOD-SHARP-D3-IMPL-01-R5-R1";
    private static readonly string[] ComparisonFields =
    [
        "stage",
        "operationFailed",
        "decision",
        "determining",
        "hasErrors",
        "errorCategories",
        "policyErrorIds",
        "cleanAllow",
    ];

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                throw new InvalidOperationException("a probe mode is required");
            }

            return args[0] switch
            {
                "differential" => RunDifferential(args),
                "lifecycle-probe" => RunLifecycleProbe(args),
                "self-test" => RunSelfTest(args),
                _ => throw new InvalidOperationException($"unknown probe mode `{args[0]}`"),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            Console.Error.WriteLine(exception.StackTrace);
            return 2;
        }
    }

    private static int RunDifferential(string[] args)
    {
        if (args.Length < 7)
        {
            throw new InvalidOperationException(
                "differential mode requires fixtures, oracle, difference register, output, lifecycle-results output, and lifecycle-work-root paths");
        }

        var fixturesPath = Path.GetFullPath(args[1]);
        var oraclePath = Path.GetFullPath(args[2]);
        var differencesPath = Path.GetFullPath(args[3]);
        var outputPath = Path.GetFullPath(args[4]);
        var lifecycleOutputPath = Path.GetFullPath(args[5]);
        var lifecycleWorkRoot = Path.GetFullPath(args[6]);

        var fixtureText = File.ReadAllText(fixturesPath);
        var differenceText = File.ReadAllText(differencesPath);
        var fixtureRoot = JsonNode.Parse(fixtureText)?.AsObject()
            ?? throw new InvalidOperationException("fixture manifest is not an object");
        var differenceRoot = JsonNode.Parse(differenceText)?.AsObject()
            ?? throw new InvalidOperationException("difference register is not an object");

        var catalogErrors = ValidateCatalog(fixtureRoot, differenceRoot);
        if (catalogErrors.Count != 0)
        {
            foreach (var error in catalogErrors)
            {
                Console.Error.WriteLine($"manifest error: {error}");
            }
            return 2;
        }

        var schemas = ReadStringMap(fixtureRoot["schemas"]?.AsObject(), "schemas");
        var providerDeclarations = ReadStringMap(
            fixtureRoot["providerDeclarations"]?.AsObject(), "providerDeclarations");
        var cases = fixtureRoot["cases"]?.AsArray()
            ?? throw new InvalidOperationException("fixture manifest has no cases array");
        var caseElements = cases.Select(node => node?.AsObject()
            ?? throw new InvalidOperationException("fixture case is not an object")).ToArray();

        // The lifecycle controls execute here, from execution evidence: the
        // destructive native scenarios run in isolated processes against staged
        // copies of this probe's host directory, and the residency scenario runs
        // in this process. There is no supplied-observations path.
        var lifecycleResults = ExecuteLifecycleScenarios(
            caseElements, schemas, providerDeclarations, lifecycleWorkRoot, lifecycleOutputPath);
        var oracleResults = RunOracleBatch(oraclePath, caseElements, schemas, providerDeclarations);
        var differenceRows = ReadDifferenceRows(differenceRoot);
        var normalizationRows = ReadNormalizationRows(differenceRoot);
        var reports = new List<JsonObject>();
        var familyCoverage = new Dictionary<string, FamilyCoverage>(StringComparer.Ordinal);

        foreach (var caseElement in caseElements)
        {
            var report = ExecuteCase(
                caseElement,
                schemas,
                providerDeclarations,
                oracleResults,
                differenceRows,
                normalizationRows,
                lifecycleResults);
            reports.Add(report);

            var families = ReadStringArray(caseElement["families"]?.AsArray(), "families");
            var control = caseElement["control"]?.GetValue<string>()
                ?? throw new InvalidOperationException("fixture case has no control");
            var classification = report["classification"]?.GetValue<string>() ?? "unexecuted/pending";
            foreach (var family in families)
            {
                if (!familyCoverage.TryGetValue(family, out var coverage))
                {
                    coverage = new FamilyCoverage();
                    familyCoverage.Add(family, coverage);
                }
                coverage.Add(
                    control,
                    classification,
                    report["managed"] as JsonObject,
                    report["upstream"] as JsonObject);
            }
        }

        var coverageErrors = ValidateFamilyCoverage(fixtureRoot, familyCoverage);
        var output = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["commission"] = Commission,
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("o"),
            ["runtime"] = new JsonObject
            {
                ["framework"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ["rid"] = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                ["architecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                ["oracle"] = oraclePath,
                ["fixtures"] = fixturesPath,
                ["fixturesSha256"] = Sha256(fixtureText),
                ["differenceRegister"] = differencesPath,
                ["differenceRegisterSha256"] = Sha256(differenceText),
                ["nativeBridge"] = DescribeNativeBridge(),
            },
            ["summary"] = BuildSummary(reports, familyCoverage),
            ["familyCoverage"] = BuildFamilyCoverage(familyCoverage),
            ["coverageErrors"] = new JsonArray(coverageErrors.Select(error => (JsonNode?)error).ToArray()),
            ["cases"] = new JsonArray(reports.Select(report => (JsonNode?)report).ToArray()),
        };

        var json = output.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json + Environment.NewLine);
        Console.WriteLine(
            $"R5-R1 differential: {reports.Count} cases, " +
            $"{output["summary"]!["failed"]!.GetValue<int>()} failed, " +
            $"{output["summary"]!["pending"]!.GetValue<int>()} pending -> {outputPath}");

        var failed = output["summary"]!["failed"]!.GetValue<int>();
        var pending = output["summary"]!["pending"]!.GetValue<int>();
        return failed == 0 && pending == 0 && coverageErrors.Count == 0 ? 0 : 1;
    }

    private static JsonObject ExecuteCase(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas,
        IReadOnlyDictionary<string, string> providerDeclarations,
        IReadOnlyDictionary<string, JsonObject> oracleResults,
        IReadOnlyDictionary<string, JsonObject> differenceRows,
        IReadOnlyDictionary<string, JsonObject> normalizationRows,
        IReadOnlyDictionary<string, JsonObject> lifecycleResults)
    {
        var id = RequiredString(caseElement, "id");
        var kind = RequiredString(caseElement, "kind");
        var expectedClassification = caseElement["expectedClassification"]?.GetValue<string>() ?? "agreement";
        var expectedDifferenceIds = ReadStringArray(
            caseElement["expectedDifferenceIds"]?.AsArray(), "expectedDifferenceIds");

        JsonObject? upstream = null;
        JsonObject? managed = null;
        JsonArray? differences = null;
        string? pendingReason = null;

        try
        {
            if (kind == "differential")
            {
                if (!oracleResults.TryGetValue(id, out var oracleOutcome))
                {
                    pendingReason = "the upstream oracle returned no result for this case";
                }
                else
                {
                    upstream = NormalizeUpstream(oracleOutcome);
                    // A malformed shared fixture input reaches the oracle as an
                    // envelope fault. That is a harness defect, never a
                    // conformance observation, so the case cannot pass.
                    var upstreamError = upstream["error"]?.GetValue<string>();
                    if (upstream["stage"]?.GetValue<string>() == "request-validation"
                        && upstreamError is not null
                        && upstreamError.StartsWith("envelope", StringComparison.Ordinal))
                    {
                        pendingReason = $"harness envelope fault: {upstreamError}";
                    }
                    else
                    {
                        managed = ExecuteManagedCase(caseElement, schemas, providerDeclarations);
                        differences = CompareObservations(upstream, managed);
                    }
                }
            }
            else if (kind == "managed-control")
            {
                managed = ExecuteManagedControl(caseElement, schemas, providerDeclarations);
            }
            else if (kind == "lifecycle-control")
            {
                if (!lifecycleResults.TryGetValue(id, out var lifecycle))
                {
                    pendingReason = "the isolated native lifecycle control was not executed";
                }
                else
                {
                    managed = lifecycle;
                }
            }
            else
            {
                pendingReason = $"unknown fixture kind `{kind}`";
            }
        }
        catch (Exception exception)
        {
            pendingReason = $"probe execution error: {exception.GetType().Name}: {exception.Message}";
        }

        string classification;
        string verdict;
        var differenceValidation = new JsonObject();

        if (pendingReason is not null || upstream is null && kind == "differential" || managed is null)
        {
            classification = "unexecuted/pending";
            verdict = "fail";
            if (pendingReason is not null)
            {
                differenceValidation["pendingReason"] = pendingReason;
            }
        }
        else if (kind == "differential")
        {
            var observedDifferences = differences ?? new JsonArray();
            // When both sides fail closed (no decision, operation failed), the
            // comparison is still strict on the fields the plan makes normative:
            // stage, operation-failed state, decision, determining set and
            // policy-error IDs must match. Error categories are compared
            // whenever the upstream side reports them; harness-level upstream
            // failures carry no categories, and the managed categories are
            // recorded as evidence in that case. (R3 hardening of QD3Q-07.)
            if (upstream!["operationFailed"]!.GetValue<bool>()
                && managed!["operationFailed"]!.GetValue<bool>()
                && IsNullOrNullNode(upstream!["decision"])
                && IsNullOrNullNode(managed!["decision"]))
            {
                observedDifferences = CompareDualFailures(upstream, managed);
            }

            // The fixture declares the exact upstream stage the case must
            // reach. A case whose intended stage is never reached fails
            // qualification regardless of how the two failures compare.
            var intendedStage = caseElement["intendedStage"]?.GetValue<string>();
            var intendedStageMismatch = false;
            if (intendedStage is not null)
            {
                var observedStage = upstream["stage"]?.GetValue<string>();
                differenceValidation["intendedStage"] = intendedStage;
                differenceValidation["intendedStageObserved"] = observedStage;
                if (!string.Equals(observedStage, intendedStage, StringComparison.Ordinal))
                {
                    intendedStageMismatch = true;
                    observedDifferences.Add(new JsonObject
                    {
                        ["field"] = "intendedStage",
                        ["upstream"] = observedStage,
                        ["managed"] = managed!["stage"]?.DeepClone(),
                    });
                }
            }

            var normalizationId = caseElement["failClosedNormalizationId"]?.GetValue<string>();
            if (normalizationId is not null)
            {
                var errors = ValidateFailClosedNormalization(
                    id,
                    normalizationId,
                    normalizationRows,
                    upstream!,
                    managed!);
                differenceValidation["failClosedNormalization"] = normalizationId;
                differenceValidation["normalizationErrors"] =
                    new JsonArray(errors.Select(error => (JsonNode?)error).ToArray());
                classification = errors.Count == 0
                    ? "fail-closed-equivalence"
                    : "unexpected-difference";
            }
            else if (expectedDifferenceIds.Count != 0)
            {
                var errors = ValidateExpectedDifferences(
                    id,
                    expectedDifferenceIds,
                    differenceRows,
                    upstream!,
                    managed!,
                    observedDifferences);
                differenceValidation["errors"] = new JsonArray(errors.Select(error => (JsonNode?)error).ToArray());
                classification = errors.Count == 0 && observedDifferences.Count != 0
                    ? "approved-difference"
                    : "unexpected-difference";
            }
            else if (observedDifferences.Count != 0)
            {
                classification = "unexpected-difference";
            }
            else if (IsVacuousAgreement(upstream!, managed!))
            {
                // Neither side decided and neither failed: there is no
                // authorization observation to agree on.
                classification = "unexpected-difference";
                differenceValidation["vacuousAgreement"] = true;
            }
            else if (upstream!["operationFailed"]!.GetValue<bool>() && managed!["operationFailed"]!.GetValue<bool>())
            {
                classification = "operation-failure";
            }
            else
            {
                classification = "agreement";
            }

            // A case that never reached its declared stage is a harness fault,
            // not an approvalable divergence: an approved-difference row may
            // never mask it. (R4 hardening: a registered difference must not be
            // able to convert a stage fault into a passing case.)
            if (intendedStageMismatch)
            {
                classification = "unexpected-difference";
                differenceValidation["intendedStageMismatch"] = true;
            }

            // An unexpected difference or a harness fault always fails the
            // case: a fixture cannot bless its own divergence or a malformed
            // shared input by naming the classification.
            verdict = classification == expectedClassification
                && classification != "unexpected-difference"
                ? "pass"
                : "fail";
            // Record the authoritative comparison (dual-failure or normal) so
            // the published result shows exactly which fields were compared.
            differences = observedDifferences;
        }
        else
        {
            var controlPassed = managed!["passed"]?.GetValue<bool>() ?? false;
            var expectedFailure = caseElement["expectedOperationFailure"]?.GetValue<bool>() ?? false;
            if (controlPassed && expectedFailure)
            {
                classification = "operation-failure";
            }
            else if (controlPassed)
            {
                classification = "agreement";
            }
            else
            {
                classification = "unexpected-difference";
            }
            verdict = classification == expectedClassification && classification != "unexpected-difference"
                ? "pass"
                : "fail";
        }

        var report = new JsonObject
        {
            ["id"] = id,
            ["kind"] = kind,
            ["control"] = RequiredString(caseElement, "control"),
            ["families"] = new JsonArray(ReadStringArray(caseElement["families"]?.AsArray(), "families")
                .Select(value => (JsonNode?)value).ToArray()),
            ["question"] = caseElement["question"]?.GetValue<string>() ?? string.Empty,
            ["expectedClassification"] = expectedClassification,
            ["expectedDifferenceIds"] = new JsonArray(expectedDifferenceIds.Select(value => (JsonNode?)value).ToArray()),
            ["classification"] = classification,
            ["verdict"] = verdict,
            ["upstream"] = upstream?.DeepClone(),
            ["managed"] = managed?.DeepClone(),
            ["observedDifferenceFields"] = differences,
            ["differenceValidation"] = differenceValidation,
        };
        return report;
    }

    private static JsonObject NormalizeUpstream(JsonObject outcome)
    {
        var errors = outcome["errors"]?.AsArray() ?? new JsonArray();
        var decision = outcome["decision"]?.GetValue<string>();
        var failed = outcome["failed"]?.GetValue<bool>() ?? false;
        // An operation failure is an error observation even when the upstream
        // channel carries the failure in `error` rather than in `errors`.
        var hasErrors = errors.Count != 0 || failed;
        return new JsonObject
        {
            ["side"] = "upstream",
            ["stage"] = outcome["stage"]?.GetValue<string>() ?? "evaluation",
            ["operationFailed"] = outcome["failed"]?.GetValue<bool>() ?? false,
            ["decision"] = decision,
            ["determining"] = outcome["determiningRules"]?.DeepClone() ?? new JsonArray(),
            ["hasErrors"] = hasErrors,
            ["errorCategories"] = outcome["errorCategories"]?.DeepClone() ?? new JsonArray(),
            ["policyErrorIds"] = outcome["policyErrorIds"]?.DeepClone() ?? new JsonArray(),
            ["cleanAllow"] = decision == "allow" && !hasErrors,
            ["errors"] = errors.DeepClone(),
            ["trustedBindings"] = outcome["trustedBindings"]?.DeepClone() ?? new JsonObject(),
            ["history"] = outcome["history"]?.DeepClone() ?? new JsonArray(),
            ["error"] = outcome["error"]?.DeepClone(),
        };
    }

    private static JsonObject ExecuteManagedCase(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas,
        IReadOnlyDictionary<string, string> providerDeclarations)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var providerText = ResolveProviderDeclarations(caseElement, providerDeclarations);
        DogwoodCedarAuthorizer? authorizer = null;
        try
        {
            authorizer = providerText is null
                ? DogwoodCedarAuthorizer.ForLoweredBundle(source, schema)
                : DogwoodCedarAuthorizer.ForLoweredBundle(source, schema, providerText);
        }
        catch (Exception exception)
        {
            return ManagedFailure(exception, "lowering");
        }

        try
        {
            var request = BuildAuthorizationInput(caseElement);
            var useTrustedRuntime = caseElement["trustedRuntime"]?.GetValue<bool>() ?? false;
            DogwoodSharp.Experimental.CedarSharp.CedarAuthorizationResult result;
            JsonObject? bindingObservation = null;
            if (useTrustedRuntime)
            {
                var runtime = BuildTrustedRuntime(caseElement);
                result = authorizer.AuthorizeWithTrustedRuntime(request, runtime);
                bindingObservation = ObserveTrustedBindings(
                    source,
                    schema,
                    providerText,
                    caseElement,
                    runtime);
            }
            else
            {
                result = authorizer.Authorize(request);
            }

            var determining = new JsonArray();
            foreach (var id in result.DeterminingPolicyIds.OrderBy(value => value, StringComparer.Ordinal))
            {
                determining.Add(new JsonObject
                {
                    ["ruleIndex"] = authorizer.RuleIndexFor(id),
                    ["cedarPolicyId"] = id,
                });
            }

            var policyErrorIds = result.PolicyErrors
                .Select(error => error.PolicyId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(value => (JsonNode?)value)
                .ToArray();
            var categories = result.Errors.Select(error => error.Category)
                .Concat(result.PolicyErrors.Select(error => error.Category))
                .Select(NormalizeCategory)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(value => (JsonNode?)value)
                .ToArray();
            var errors = new JsonArray();
            foreach (var error in result.Errors)
            {
                errors.Add(new JsonObject
                {
                    ["category"] = NormalizeCategory(error.Category),
                    ["message"] = error.Message,
                });
            }
            foreach (var error in result.PolicyErrors)
            {
                errors.Add(new JsonObject
                {
                    ["category"] = NormalizeCategory(error.Category),
                    ["message"] = error.Message,
                    ["policyId"] = error.PolicyId,
                });
            }

            return new JsonObject
            {
                ["side"] = "managed",
                ["stage"] = result.OperationFailed ? MapManagedStage(result.Stage) : "evaluation",
                ["operationFailed"] = result.OperationFailed,
                ["decision"] = result.Decision?.ToString().ToLowerInvariant(),
                ["determining"] = determining,
                ["hasErrors"] = errors.Count != 0,
                ["errorCategories"] = new JsonArray(categories),
                ["policyErrorIds"] = new JsonArray(policyErrorIds),
                ["cleanAllow"] = result.IsCleanAllow,
                ["errors"] = errors,
                ["trustedBindings"] = bindingObservation?["bindings"]?.DeepClone() ?? new JsonObject
                {
                    ["temporal"] = new JsonArray(),
                    ["providers"] = new JsonArray()
                },
                ["bindingObservationError"] = bindingObservation?["error"]?.DeepClone(),
                ["error"] = null,
            };
        }
        catch (Exception exception)
        {
            return ManagedFailure(exception, "native-transport");
        }
        finally
        {
            authorizer.Dispose();
        }
    }

    private static JsonObject ManagedFailure(Exception exception, string stage)
    {
        return new JsonObject
        {
            ["side"] = "managed",
            ["stage"] = stage,
            ["operationFailed"] = true,
            ["decision"] = null,
            ["determining"] = new JsonArray(),
            ["hasErrors"] = true,
            ["errorCategories"] = new JsonArray("operation-error"),
            ["policyErrorIds"] = new JsonArray(),
            ["cleanAllow"] = false,
            ["errors"] = new JsonArray(new JsonObject
            {
                ["category"] = "operation-error",
                ["message"] = $"{exception.GetType().Name}: {exception.Message}",
            }),
            ["trustedBindings"] = new JsonObject
            {
                ["temporal"] = new JsonArray(),
                ["providers"] = new JsonArray()
            },
            ["error"] = $"{exception.GetType().Name}: {exception.Message}",
        };
    }

    private static JsonObject ExecuteManagedControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas,
        IReadOnlyDictionary<string, string> providerDeclarations)
    {
        var control = RequiredString(caseElement, "control");
        return control switch
        {
            "schema-transport" => SchemaTransportControl(caseElement, schemas),
            "canonical-schema" => CanonicalSchemaControl(caseElement, schemas),
            "identity-non-sequential" => IdentityNonSequentialControl(caseElement, schemas),
            "identity-reordered" => IdentityReorderedControl(caseElement, schemas),
            "identity-reordered-two-rule" => IdentityReorderedTwoRuleControl(caseElement, schemas),
            "multi-action-appliesto" => MultiActionAppliesToControl(caseElement, schemas),
            "identity-duplicate" => IdentityDuplicateControl(caseElement, schemas),
            "identity-unknown" => IdentityUnknownControl(caseElement, schemas),
            "action-entity-conflict" => ActionEntityConflictControl(caseElement, schemas),
            "concurrency" => ConcurrencyControl(caseElement, schemas),
            "lifecycle-repeat" => LifecycleRepeatControl(caseElement, schemas),
            "provider-field-ownership" => ProviderFieldOwnershipControl(caseElement, schemas, providerDeclarations),
            "reserved-temporal-field" => ReservedTemporalFieldControl(caseElement, schemas),
            "typed-context" => TypedContextControl(caseElement, schemas),
            "typed-context-binding" => TypedContextBindingControl(caseElement, schemas),
            _ => throw new InvalidOperationException($"unknown managed control `{control}`"),
        };
    }

    private static JsonObject SchemaTransportControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var export = Dogwood.ExportCedar(source, schema);
        var jsonSchema = CedarSchema.FromJson(export.CedarSchemaJson);
        var textSchema = CedarSchema.FromText(export.CedarSchemaText);
        using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(source, schema);
        var checks = new JsonObject
        {
            ["jsonParsed"] = true,
            ["textParsed"] = true,
            ["canonicalJsonIsExportJson"] = string.Equals(
                authorizer.CanonicalSchemaJson,
                export.CedarSchemaJson,
                StringComparison.Ordinal),
            ["canonicalJsonSha256"] = Sha256(export.CedarSchemaJson),
            ["diagnosticTextSha256"] = Sha256(export.CedarSchemaText),
            ["canonicalIsJson"] = !authorizer.CanonicalSchemaJson.TrimStart().StartsWith("namespace ", StringComparison.Ordinal),
        };
        var passed = checks["canonicalJsonIsExportJson"]!.GetValue<bool>()
            && checks["canonicalIsJson"]!.GetValue<bool>();
        return ControlResult(passed, checks);
    }

    private static JsonObject CanonicalSchemaControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var export = Dogwood.ExportCedar(source, schema);
        using var document = JsonDocument.Parse(export.CedarSchemaJson);
        var expected = caseElement["expectedAppliesTo"]?.AsObject()
            ?? throw new InvalidOperationException("canonical-schema control has no expectedAppliesTo");
        var actual = new JsonObject();
        var allMatched = true;

        foreach (var namespaceEntry in document.RootElement.EnumerateObject())
        {
            if (!namespaceEntry.Value.TryGetProperty("actions", out var actions))
            {
                continue;
            }
            foreach (var action in actions.EnumerateObject())
            {
                var actionId = action.Name;
                if (!expected.TryGetPropertyValue(actionId, out var expectedAction))
                {
                    continue;
                }
                var observed = new JsonObject();
                var appliesTo = action.Value.TryGetProperty("appliesTo", out var value) ? value : default;
                foreach (var property in new[] { "principalTypes", "resourceTypes" })
                {
                    var values = appliesTo.ValueKind == JsonValueKind.Object
                        && appliesTo.TryGetProperty(property, out var array)
                            ? array.EnumerateArray().Select(item => item.GetString()).ToArray()
                            : Array.Empty<string?>();
                    observed[property] = new JsonArray(values.Select(item => (JsonNode?)item).ToArray());
                }
                var context = new JsonObject();
                if (appliesTo.ValueKind == JsonValueKind.Object
                    && appliesTo.TryGetProperty("context", out var contextValue)
                    && contextValue.TryGetProperty("attributes", out var attributes))
                {
                    foreach (var attribute in attributes.EnumerateObject())
                    {
                        context[attribute.Name] = attribute.Value.TryGetProperty("name", out var typeName)
                            ? typeName.GetString()
                            : attribute.Value.GetRawText();
                    }
                }
                observed["context"] = context;
                actual[actionId] = observed;
                allMatched &= JsonNode.DeepEquals(observed, expectedAction);
            }
        }

        var checks = new JsonObject
        {
            ["actual"] = actual,
            ["allExpectedActionsObserved"] = actual.Count == expected.Count,
            ["allAppliesToConstraintsPreserved"] = allMatched,
            ["canonicalSchemaJson"] = export.CedarSchemaJson,
        };
        return ControlResult(
            checks["allExpectedActionsObserved"]!.GetValue<bool>() && allMatched,
            checks);
    }

    private static JsonObject IdentityNonSequentialControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var export = Dogwood.ExportCedar(source, schema);
        var customId = caseElement["customPolicyId"]?.GetValue<string>() ?? "custom-42";
        var rules = export.Rules.Select((rule, index) => new CedarPolicyRule(
            index,
            customId,
            rule.SourceText,
            rule.CedarPolicyBody)).ToImmutableArray();
        var candidate = new DogwoodCedarExport(
            export.Validation,
            export.ArtifactsExported,
            export.CedarPolicyText,
            export.CedarSchemaJson,
            export.CedarSchemaText,
            rules,
            export.GeneratedContextFields);
        using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(candidate);
        var checks = new JsonObject
        {
            ["policyIds"] = new JsonArray(authorizer.PolicyIds.Select(value => (JsonNode?)value).ToArray()),
            ["ruleIndex"] = authorizer.RuleIndexFor(customId),
            ["sourceAnnotationStored"] = authorizer.PolicyIds.Contains("source-label"),
        };
        var passed = authorizer.PolicyIds.SequenceEqual([customId])
            && authorizer.RuleIndexFor(customId) == 0
            && !checks["sourceAnnotationStored"]!.GetValue<bool>();
        return ControlResult(passed, checks);
    }

    private static JsonObject IdentityReorderedControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var export = Dogwood.ExportCedar(source, schema);
        var rules = export.Rules.Reverse().ToImmutableArray();
        var candidate = new DogwoodCedarExport(
            export.Validation,
            export.ArtifactsExported,
            export.CedarPolicyText,
            export.CedarSchemaJson,
            export.CedarSchemaText,
            rules,
            export.GeneratedContextFields);
        using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(candidate);
        var checks = new JsonObject
        {
            ["suppliedOrder"] = new JsonArray(rules.Select(rule => (JsonNode?)rule.CedarPolicyId).ToArray()),
            ["mappedIndices"] = new JsonArray(rules.Select(rule => (JsonNode?)authorizer.RuleIndexFor(rule.CedarPolicyId)).ToArray()),
        };
        var passed = rules.Select(rule => authorizer.RuleIndexFor(rule.CedarPolicyId))
            .SequenceEqual(rules.Select(rule => rule.RuleIndex));
        return ControlResult(passed, checks);
    }

    /// <summary>
    /// The R5-R1 C-21 reorder control. The committed R4 control reversed a
    /// single exported rule, so reversing it was a no-op and its success could
    /// not establish that the exported index follows the supplied rule order
    /// (qualification finding QD3Q-12). This control reverses a two-rule
    /// export: the reversal is observable, the mapped indices must follow the
    /// supplied order, and the first supplied rule must be the later source
    /// rule.
    /// </summary>
    private static JsonObject IdentityReorderedTwoRuleControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var export = Dogwood.ExportCedar(source, schema);
        var sourceOrder = export.Rules.Select(rule => rule.CedarPolicyId).ToArray();
        if (export.Rules.Length < 2)
        {
            return ControlResult(false, new JsonObject
            {
                ["sourceOrder"] = new JsonArray(sourceOrder.Select(value => (JsonNode?)value).ToArray()),
                ["ruleCount"] = export.Rules.Length,
                ["failure"] = "the source must export at least two rules for the reorder control to be observable",
            });
        }
        var rules = export.Rules.Reverse().ToImmutableArray();
        var candidate = new DogwoodCedarExport(
            export.Validation,
            export.ArtifactsExported,
            export.CedarPolicyText,
            export.CedarSchemaJson,
            export.CedarSchemaText,
            rules,
            export.GeneratedContextFields);
        using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(candidate);
        var suppliedOrder = rules.Select(rule => rule.CedarPolicyId).ToArray();
        var mappedIndices = rules.Select(rule => authorizer.RuleIndexFor(rule.CedarPolicyId)).ToArray();
        var checks = new JsonObject
        {
            ["sourceOrder"] = new JsonArray(sourceOrder.Select(value => (JsonNode?)value).ToArray()),
            ["suppliedOrder"] = new JsonArray(suppliedOrder.Select(value => (JsonNode?)value).ToArray()),
            ["mappedIndices"] = new JsonArray(mappedIndices.Select(value => (JsonNode?)value).ToArray()),
            ["ruleCount"] = rules.Length,
            ["reversalIsObservable"] = !suppliedOrder.SequenceEqual(sourceOrder),
            ["mappedIndicesFollowSuppliedOrder"] = mappedIndices
                .SequenceEqual(rules.Select(rule => rule.RuleIndex)),
        };
        var passed = checks["reversalIsObservable"]!.GetValue<bool>()
            && checks["mappedIndicesFollowSuppliedOrder"]!.GetValue<bool>()
            && mappedIndices.Length >= 2
            && mappedIndices[0] > mappedIndices[^1];
        return ControlResult(passed, checks);
    }

    /// <summary>
    /// The R5-R1 C-11 multi-action `appliesTo` canonicalization control. The
    /// committed R4 corpus exercised a single complete action, so it could not
    /// establish that each action keeps its own principal, resource and
    /// context constraints (qualification finding QD3Q-12). This control
    /// carries a schema with two complete actions and checks the canonical
    /// schema the adapter actually exposes, action by action, against the
    /// constraints the fixture declares.
    /// </summary>
    private static JsonObject MultiActionAppliesToControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var export = Dogwood.ExportCedar(source, schema);
        using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(source, schema);
        var canonical = JsonNode.Parse(authorizer.CanonicalSchemaJson)?.AsObject()
            ?? throw new InvalidOperationException("the canonical schema is not a JSON object");
        var expected = caseElement["expectedActions"]?.AsObject()
            ?? throw new InvalidOperationException("the multi-action control declares no expectedActions");
        var actions = canonical["App"]?["actions"]?.AsObject()
            ?? throw new InvalidOperationException("the canonical schema declares no App actions");
        var observed = new JsonObject();
        var mismatches = new JsonArray();
        foreach (var entry in expected)
        {
            var name = entry.Key;
            var expectation = entry.Value?.AsObject()
                ?? throw new InvalidOperationException($"expectedActions[{name}] is not an object");
            var appliesTo = actions[name]?["appliesTo"]?.AsObject();
            if (appliesTo is null)
            {
                mismatches.Add($"action `{name}` has no appliesTo block");
                continue;
            }
            var principals = ReadStringSet(appliesTo["principalTypes"]?.AsArray());
            var resources = ReadStringSet(appliesTo["resourceTypes"]?.AsArray());
            var contextAttributes = ReadStringSet(
                appliesTo["context"]?["attributes"]?.AsObject()
                    is JsonObject contextAttributeObject
                    ? contextAttributeObject.Select(property => (JsonNode?)property.Key).ToArray()
                    : null);            observed[name] = new JsonObject
            {
                ["principalTypes"] = new JsonArray(principals.OrderBy(value => value, StringComparer.Ordinal)
                    .Select(value => (JsonNode?)value).ToArray()),
                ["resourceTypes"] = new JsonArray(resources.OrderBy(value => value, StringComparer.Ordinal)
                    .Select(value => (JsonNode?)value).ToArray()),
                ["contextAttributes"] = new JsonArray(contextAttributes.OrderBy(value => value, StringComparer.Ordinal)
                    .Select(value => (JsonNode?)value).ToArray()),
            };
            CompareStringSet(
                $"action `{name}` principalTypes",
                ReadStringSet(expectation["principalTypes"]?.AsArray()),
                principals,
                mismatches);
            CompareStringSet(
                $"action `{name}` resourceTypes",
                ReadStringSet(expectation["resourceTypes"]?.AsArray()),
                resources,
                mismatches);
            CompareStringSet(
                $"action `{name}` contextAttributes",
                ReadStringSet(expectation["contextAttributes"]?.AsArray()),
                contextAttributes,
                mismatches);
        }
        var checks = new JsonObject
        {
            ["declaredActions"] = new JsonArray(expected.Select(property => (JsonNode?)property.Key).ToArray()),
            ["canonicalActions"] = new JsonArray(actions.Select(property => (JsonNode?)property.Key).ToArray()),
            ["observed"] = observed,
            ["mismatches"] = mismatches,
            ["canonicalIsExportJson"] = string.Equals(
                authorizer.CanonicalSchemaJson,
                export.CedarSchemaJson,
                StringComparison.Ordinal),
            ["actionsAreIndependent"] = expected.Count >= 2
                && observed.Select(property => property.Value?.ToJsonString())
                    .Distinct(StringComparer.Ordinal)
                    .Count() == expected.Count,
        };
        var passed = mismatches.Count == 0
            && checks["canonicalIsExportJson"]!.GetValue<bool>()
            && checks["actionsAreIndependent"]!.GetValue<bool>();
        return ControlResult(passed, checks);
    }

    private static HashSet<string> ReadStringSet(IEnumerable<JsonNode?>? values)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in values ?? Array.Empty<JsonNode?>())
        {
            if (node is not null)
            {
                result.Add(node.GetValue<string>());
            }
        }
        return result;
    }

    private static void CompareStringSet(
        string label,
        HashSet<string> expected,
        HashSet<string> observed,
        JsonArray mismatches)
    {
        // The canonical Cedar JSON schema qualifies an entity type inside an
        // action's namespace with the namespace's local name only. The
        // comparison is therefore namespace-insensitive on the local name and
        // exact on the namespace of the expectation: `App::Reader` and
        // `Reader` are the same type in the canonical schema, while a type
        // from another namespace is a different type and is reported.
        foreach (var value in expected.Except(observed, StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!observed.Any(candidate => SameTypeName(candidate, value)))
            {
                mismatches.Add($"{label} is missing `{value}`");
            }
        }
        foreach (var value in observed.Except(expected, StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!expected.Any(candidate => SameTypeName(candidate, value)))
            {
                mismatches.Add($"{label} carries the unexpected `{value}`");
            }
        }
    }

    private static bool SameTypeName(string left, string right)
    {
        return string.Equals(LocalTypeName(left), LocalTypeName(right), StringComparison.Ordinal);
    }

    private static string LocalTypeName(string value)
    {
        var separator = value.LastIndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? value : value[(separator + 2)..];
    }

    private static JsonObject IdentityDuplicateControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var export = Dogwood.ExportCedar(source, schema);
        var first = export.Rules[0];
        var duplicate = new CedarPolicyRule(first.RuleIndex, first.CedarPolicyId, first.SourceText, first.CedarPolicyBody);
        var rules = export.Rules.Append(duplicate).ToImmutableArray();
        var candidate = new DogwoodCedarExport(
            export.Validation,
            export.ArtifactsExported,
            export.CedarPolicyText,
            export.CedarSchemaJson,
            export.CedarSchemaText,
            rules,
            export.GeneratedContextFields);
        try
        {
            using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(candidate);
            return ControlResult(false, new JsonObject
            {
                ["exception"] = null,
                ["failure"] = null,
            });
        }
        catch (CedarBundleException exception)
        {
            var passed = exception.Failure == CedarBundleFailure.DuplicatePolicyId;
            return ControlResult(passed, new JsonObject
            {
                ["exception"] = exception.GetType().Name,
                ["failure"] = exception.Failure.ToString(),
                ["message"] = exception.Message,
            });
        }
    }

    private static JsonObject IdentityUnknownControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(source, schema);
        var field = typeof(DogwoodCedarAuthorizer).GetField(
            "policyIds",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("cannot locate the immutable policy-id set");
        field.SetValue(authorizer, ImmutableHashSet<string>.Empty);
        var request = BuildAuthorizationInput(caseElement);
        var result = authorizer.Authorize(request);
        var checks = new JsonObject
        {
            ["operationFailed"] = result.OperationFailed,
            ["stage"] = result.Stage.ToString(),
            ["decision"] = result.Decision?.ToString(),
            ["errorCategories"] = new JsonArray(result.Errors.Select(error => (JsonNode?)NormalizeCategory(error.Category)).ToArray()),
        };
        var passed = result.OperationFailed
            && result.Stage == CedarAuthorizationStage.UnknownPolicyIdentity
            && result.Decision is null
            && !result.Authorized;
        return ControlResult(passed, checks);
    }

    private static JsonObject ActionEntityConflictControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(source, schema);
        var request = BuildAuthorizationInput(caseElement);
        var result = authorizer.Authorize(request);
        var checks = new JsonObject
        {
            ["operationFailed"] = result.OperationFailed,
            ["stage"] = result.Stage.ToString(),
            ["decision"] = result.Decision?.ToString(),
            ["authorized"] = result.Authorized,
            ["errorCategories"] = new JsonArray(result.Errors.Select(error => (JsonNode?)NormalizeCategory(error.Category)).ToArray()),
        };
        var passed = result.OperationFailed && !result.Authorized && result.Decision is null;
        return ControlResult(passed, checks);
    }

    private static JsonObject ConcurrencyControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(source, schema);
        var request = BuildAuthorizationInput(caseElement);
        var workers = caseElement["workers"]?.GetValue<int>() ?? 4;
        var iterations = caseElement["iterations"]?.GetValue<int>() ?? 32;
        var expected = NormalizeResult(authorizer.Authorize(request));

        var tasks = Enumerable.Range(0, workers).Select(worker => Task.Run(() =>
        {
            var observations = new List<string>();
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                observations.Add(NormalizeResult(authorizer.Authorize(request)));
            }
            return observations;
        })).ToArray();
        Task.WaitAll(tasks);
        var all = tasks.SelectMany(task => task.Result).ToArray();
        var distinct = all.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var checks = new JsonObject
        {
            ["workers"] = workers,
            ["iterations"] = iterations,
            ["callCount"] = all.Length,
            ["expected"] = expected,
            ["distinctResults"] = new JsonArray(distinct.Select(value => (JsonNode?)value).ToArray()),
            ["allDeterministic"] = distinct.Length == 1 && distinct[0] == expected,
        };
        return ControlResult(checks["allDeterministic"]!.GetValue<bool>(), checks);
    }

    private static JsonObject LifecycleRepeatControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(source, schema);
        var request = BuildAuthorizationInput(caseElement);
        var before = NormalizeResult(authorizer.Authorize(request));
        authorizer.Dispose();
        var after = NormalizeResult(authorizer.Authorize(request));
        var checks = new JsonObject
        {
            ["before"] = before,
            ["after"] = after,
            ["sameAfterDispose"] = before == after,
        };
        return ControlResult(before == after, checks);
    }

    private static JsonObject ReservedTemporalFieldControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var export = Dogwood.ExportCedar(source, schema);
        if (!export.ArtifactsExported || export.SelfContainedCedar)
        {
            return ControlResult(false, new JsonObject
            {
                ["reason"] = "the lowering must export a trusted temporal field"
            });
        }

        var reserved = Assert_Single(export.GeneratedContextFields);
        using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(export);
        var request = BuildAuthorizationInput(caseElement);
        // Inject the reserved field into the caller context using proper JSON.
        using var contextDoc = JsonDocument.Parse(request.ContextJson);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in contextDoc.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }
            writer.WriteBoolean(reserved, true);
            writer.WriteEndObject();
        }
        var injectedJson = System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
        var injected = request with { ContextJson = injectedJson };
        var result = authorizer.Authorize(injected);

        var passed = result.OperationFailed
            && result.Decision is null
            && !result.Authorized
            && result.Errors.Any(error => error.Category == CedarDiagnosticCategory.ReservedContextField);
        return ControlResult(passed, new JsonObject
        {
            ["reservedField"] = reserved,
            ["operationFailed"] = result.OperationFailed,
            ["decision"] = result.Decision?.ToString(),
            ["authorized"] = result.Authorized,
            ["errorCategories"] = new JsonArray(result.Errors.Select(error => (JsonNode?)NormalizeCategory(error.Category)).ToArray()),
        });
    }

    private static string Assert_Single(System.Collections.Immutable.ImmutableArray<string> values)
    {
        if (values.Length != 1)
        {
            throw new InvalidOperationException("expected exactly one generated context field");
        }
        return values[0];
    }

    /// <summary>
    /// The C-23 semantic regression control. Each expectation names the exact
    /// caller context and the exact outcome the production adapter must
    /// produce: the authorization result, the clean-allow state, the
    /// operation-failed state, the determining policy set and every diagnostic
    /// category. The control therefore pins observed behavior rather than
    /// asserted prose, and a control that observes anything else fails.
    /// </summary>
    private static JsonObject TypedContextControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var expectations = caseElement["expectations"]?.AsArray()
            ?? throw new InvalidOperationException("typed-context control has no expectations");
        var observations = new JsonArray();
        var passed = true;

        foreach (var node in expectations)
        {
            var expectation = node?.AsObject()
                ?? throw new InvalidOperationException("typed-context expectation is not an object");
            var label = RequiredString(expectation, "label");
            var source = expectation["source"]?.GetValue<string>()
                ?? RequiredString(caseElement, "source");
            var schemaName = expectation["schema"]?.GetValue<string>()
                ?? RequiredString(caseElement, "schema");
            var observation = new JsonObject { ["label"] = label };
            try
            {
                var schema = BuildDogwoodSchema(schemaName, caseElement, schemas);
                using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(source, schema);
                var request = BuildTypedContextRequest(caseElement, expectation);
                var result = authorizer.Authorize(request);
                var decision = result.Decision?.ToString().ToLowerInvariant();
                var determining = result.DeterminingPolicyIds
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                var categories = result.Errors.Select(error => NormalizeCategory(error.Category))
                    .Concat(result.PolicyErrors.Select(error => NormalizeCategory(error.Category)))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                observation["decision"] = decision;
                observation["operationFailed"] = result.OperationFailed;
                observation["cleanAllow"] = result.IsCleanAllow;
                observation["authorized"] = result.Authorized;
                observation["stage"] = result.OperationFailed ? MapManagedStage(result.Stage) : "evaluation";
                observation["determining"] = new JsonArray(
                    determining.Select(value => (JsonNode?)value).ToArray());
                observation["errorCategories"] = new JsonArray(
                    categories.Select(value => (JsonNode?)value).ToArray());

                var failures = new List<string>();
                var expectedDecision = expectation["expectedDecision"]?.GetValue<string>();
                if (expectedDecision is null)
                {
                    if (decision is not null)
                    {
                        failures.Add($"expected no decision but observed `{decision}`");
                    }
                }
                else if (!string.Equals(decision, expectedDecision, StringComparison.Ordinal))
                {
                    failures.Add($"expected decision `{expectedDecision}` but observed `{decision ?? "none"}`");
                }

                var expectedOperationFailed = expectation["expectedOperationFailed"]?.GetValue<bool>() ?? false;
                if (result.OperationFailed != expectedOperationFailed)
                {
                    failures.Add($"expected operationFailed={expectedOperationFailed} but observed {result.OperationFailed}");
                }

                var expectedCleanAllow = expectation["expectedCleanAllow"]?.GetValue<bool>() ?? false;
                if (result.IsCleanAllow != expectedCleanAllow)
                {
                    failures.Add($"expected cleanAllow={expectedCleanAllow} but observed {result.IsCleanAllow}");
                }

                var expectedAuthorized = expectation["expectedAuthorized"]?.GetValue<bool>() ?? false;
                if (result.Authorized != expectedAuthorized)
                {
                    failures.Add($"expected authorized={expectedAuthorized} but observed {result.Authorized}");
                }

                var expectedDeterminingCount = expectation["expectedDeterminingCount"]?.GetValue<int>() ?? 0;
                if (determining.Length != expectedDeterminingCount)
                {
                    failures.Add($"expected {expectedDeterminingCount} determining policies but observed {determining.Length}");
                }

                var expectedCategories = ReadStringArray(
                    expectation["expectedErrorCategories"]?.AsArray(),
                    "expectedErrorCategories");
                if (!categories.SequenceEqual(expectedCategories, StringComparer.Ordinal))
                {
                    failures.Add(
                        $"expected error categories [{string.Join(", ", expectedCategories)}] "
                        + $"but observed [{string.Join(", ", categories)}]");
                }

                observation["failures"] = new JsonArray(failures.Select(failure => (JsonNode?)failure).ToArray());
                observation["passed"] = failures.Count == 0;
            }
            catch (Exception exception)
            {
                observation["passed"] = false;
                observation["exception"] = $"{exception.GetType().Name}: {exception.Message}";
            }

            passed &= observation["passed"]!.GetValue<bool>();
            observations.Add(observation);
        }

        return ControlResult(passed, new JsonObject
        {
            ["expectationCount"] = expectations.Count,
            ["observations"] = observations,
        });
    }

    /// <summary>
    /// The C-23 trusted-context binding integrity control. It asserts that the
    /// typed context value is never synthesized by the adapter, that a caller
    /// cannot omit it and have a default substituted, and that a caller cannot
    /// smuggle an additional field into the typed context: the extra field is
    /// rejected before dispatch instead of being merged or ignored.
    /// </summary>
    private static JsonObject TypedContextBindingControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var export = Dogwood.ExportCedar(source, schema);
        var expectedGeneratedFields = caseElement["expectedGeneratedContextFieldCount"]?.GetValue<int>() ?? 0;
        var generatedFields = export.GeneratedContextFields;
        var checks = new JsonObject
        {
            ["artifactsExported"] = export.ArtifactsExported,
            ["selfContainedCedar"] = export.SelfContainedCedar,
            ["generatedContextFields"] = new JsonArray(
                generatedFields.Select(value => (JsonNode?)value).ToArray()),
            ["expectedGeneratedContextFieldCount"] = expectedGeneratedFields,
            ["noSynthesizedContextField"] = generatedFields.Length == expectedGeneratedFields,
            ["authorizerIsSelfContained"] = export.SelfContainedCedar,
        };

        var expectations = caseElement["expectations"]?.AsArray()
            ?? throw new InvalidOperationException("typed-context-binding control has no expectations");
        var observations = new JsonArray();
        var passed = checks["noSynthesizedContextField"]!.GetValue<bool>()
            && checks["authorizerIsSelfContained"]!.GetValue<bool>();

        foreach (var node in expectations)
        {
            var expectation = node?.AsObject()
                ?? throw new InvalidOperationException("typed-context-binding expectation is not an object");
            var label = RequiredString(expectation, "label");
            var observation = new JsonObject { ["label"] = label };
            try
            {
                using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(source, schema);
                var request = BuildTypedContextRequest(caseElement, expectation);
                var result = authorizer.Authorize(request);
                var decision = result.Decision?.ToString().ToLowerInvariant();
                var categories = result.Errors.Select(error => NormalizeCategory(error.Category))
                    .Concat(result.PolicyErrors.Select(error => NormalizeCategory(error.Category)))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                observation["decision"] = decision;
                observation["operationFailed"] = result.OperationFailed;
                observation["cleanAllow"] = result.IsCleanAllow;
                observation["authorized"] = result.Authorized;
                observation["stage"] = result.OperationFailed ? MapManagedStage(result.Stage) : "evaluation";
                observation["errorCategories"] = new JsonArray(
                    categories.Select(value => (JsonNode?)value).ToArray());

                var failures = new List<string>();
                var expectedDecision = expectation["expectedDecision"]?.GetValue<string>();
                if (expectedDecision is null)
                {
                    if (decision is not null)
                    {
                        failures.Add($"expected no decision but observed `{decision}`");
                    }
                }
                else if (!string.Equals(decision, expectedDecision, StringComparison.Ordinal))
                {
                    failures.Add($"expected decision `{expectedDecision}` but observed `{decision ?? "none"}`");
                }

                var expectedOperationFailed = expectation["expectedOperationFailed"]?.GetValue<bool>() ?? false;
                if (result.OperationFailed != expectedOperationFailed)
                {
                    failures.Add($"expected operationFailed={expectedOperationFailed} but observed {result.OperationFailed}");
                }

                var expectedAuthorized = expectation["expectedAuthorized"]?.GetValue<bool>() ?? false;
                if (result.Authorized != expectedAuthorized)
                {
                    failures.Add($"expected authorized={expectedAuthorized} but observed {result.Authorized}");
                }

                var expectedCategories = ReadStringArray(
                    expectation["expectedErrorCategories"]?.AsArray(),
                    "expectedErrorCategories");
                if (!categories.SequenceEqual(expectedCategories, StringComparer.Ordinal))
                {
                    failures.Add(
                        $"expected error categories [{string.Join(", ", expectedCategories)}] "
                        + $"but observed [{string.Join(", ", categories)}]");
                }

                observation["failures"] = new JsonArray(failures.Select(failure => (JsonNode?)failure).ToArray());
                observation["passed"] = failures.Count == 0;
            }
            catch (Exception exception)
            {
                observation["passed"] = false;
                observation["exception"] = $"{exception.GetType().Name}: {exception.Message}";
            }

            passed &= observation["passed"]!.GetValue<bool>();
            observations.Add(observation);
        }

        checks["observations"] = observations;
        return ControlResult(passed, checks);
    }

    private static CedarAuthorizationInput BuildTypedContextRequest(
        JsonObject caseElement,
        JsonObject expectation)
    {
        var eventElement = caseElement["event"]?.AsObject()
            ?? throw new InvalidOperationException("case has no event object");
        var principal = ReadEntityRef(eventElement["principal"]?.AsObject(), "principal");
        var resource = ReadEntityRef(eventElement["resource"]?.AsObject(), "resource");
        var actionNamespace = ReadStringArray(eventElement["actionNamespace"]?.AsArray(), "actionNamespace");
        var actionId = RequiredString(eventElement, "actionId");
        var context = expectation["context"]?.AsObject()
            ?? throw new InvalidOperationException("typed-context expectation has no context");
        var entitiesJson = RawOrSerialized(
            eventElement,
            "entities",
            eventElement["entitiesJson"]?.GetValue<string>());
        return new CedarAuthorizationInput(
            new CedarEntityRef(principal.type, principal.id),
            new CedarEntityRef(string.Join("::", actionNamespace), actionId),
            new CedarEntityRef(resource.type, resource.id),
            context.ToJsonString(),
            entitiesJson);
    }

    private static JsonObject ProviderFieldOwnershipControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas,
        IReadOnlyDictionary<string, string> providerDeclarations)
    {
        var source = RequiredString(caseElement, "source");
        var schema = BuildDogwoodSchema(caseElement, schemas);
        var providerText = ResolveProviderDeclarations(caseElement, providerDeclarations)
            ?? throw new InvalidOperationException("provider-field-ownership control has no provider declarations");
        using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(source, schema, providerText);
        var request = BuildAuthorizationInput(caseElement);
        var result = authorizer.Authorize(request);
        var checks = new JsonObject
        {
            ["operationFailed"] = result.OperationFailed,
            ["stage"] = result.Stage.ToString(),
            ["decision"] = result.Decision?.ToString(),
            ["authorized"] = result.Authorized,
            ["errorCategories"] = new JsonArray(result.Errors.Select(error => (JsonNode?)NormalizeCategory(error.Category)).ToArray()),
        };
        var passed = result.OperationFailed
            && result.Stage == CedarAuthorizationStage.SchemaOrRequestValidation
            && result.Errors.Any(error => error.Category == CedarDiagnosticCategory.ReservedContextField);
        return ControlResult(passed, checks);
    }

    private static JsonObject ControlResult(bool passed, JsonObject checks)
    {
        return new JsonObject
        {
            ["side"] = "managed",
            ["control"] = true,
            ["passed"] = passed,
            ["operationFailed"] = false,
            ["checks"] = checks,
        };
    }

    private static string NormalizeResult(DogwoodSharp.Experimental.CedarSharp.CedarAuthorizationResult result)
    {
        return string.Join("|",
            result.OperationFailed,
            result.Stage,
            result.Decision?.ToString(),
            result.Authorized,
            result.IsCleanAllow,
            string.Join(",", result.DeterminingPolicyIds.OrderBy(value => value, StringComparer.Ordinal)),
            string.Join(",", result.PolicyErrors.Select(error => error.PolicyId).OrderBy(value => value, StringComparer.Ordinal)),
            string.Join(",", result.Errors.Select(error => error.Category).OrderBy(value => value)));
    }

    private static DogwoodSchema BuildDogwoodSchema(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        return BuildDogwoodSchema(RequiredString(caseElement, "schema"), caseElement, schemas);
    }

    private static DogwoodSchema BuildDogwoodSchema(
        string schemaName,
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
        if (!schemas.TryGetValue(schemaName, out var policySchema))
        {
            throw new InvalidOperationException($"unknown schema `{schemaName}`");
        }
        return new DogwoodSchema(
            policySchema,
            caseElement["eventSchema"]?.GetValue<string>(),
            caseElement["macros"]?.GetValue<string>(),
            caseElement["distincter"]?.GetValue<string>());
    }

    private static string? ResolveProviderDeclarations(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> providerDeclarations)
    {
        var name = caseElement["providerDeclarations"]?.GetValue<string>();
        if (name is null)
        {
            return null;
        }
        if (!providerDeclarations.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"unknown provider declaration set `{name}`");
        }
        return value;
    }

    private static CedarAuthorizationInput BuildAuthorizationInput(JsonObject caseElement)
    {
        var eventElement = caseElement["event"]?.AsObject()
            ?? throw new InvalidOperationException("case has no event object");
        var principal = ReadEntityRef(eventElement["principal"]?.AsObject(), "principal");
        var resource = ReadEntityRef(eventElement["resource"]?.AsObject(), "resource");
        var actionNamespace = ReadStringArray(eventElement["actionNamespace"]?.AsArray(), "actionNamespace");
        var actionId = RequiredString(eventElement, "actionId");
        var contextJson = RawOrSerialized(
            eventElement,
            "context",
            eventElement["contextJson"]?.GetValue<string>());
        var entitiesJson = RawOrSerialized(
            eventElement,
            "entities",
            eventElement["entitiesJson"]?.GetValue<string>());
        return new CedarAuthorizationInput(
            new CedarEntityRef(principal.type, principal.id),
            new CedarEntityRef(string.Join("::", actionNamespace), actionId),
            new CedarEntityRef(resource.type, resource.id),
            contextJson,
            entitiesJson);
    }

    private static DogwoodTrustedRuntime BuildTrustedRuntime(JsonObject caseElement)
    {
        var eventElement = caseElement["event"]?.AsObject()
            ?? throw new InvalidOperationException("case has no event object");
        var timestamp = eventElement["timestamp"]?.GetValue<long>() ?? 0;
        var currentLogged = eventElement["logged"] is null && eventElement["loggedJson"] is null
            ? string.Empty
            : RawOrSerialized(
                eventElement,
                "logged",
                eventElement["loggedJson"]?.GetValue<string>());
        var history = ImmutableArray.CreateBuilder<DogwoodTrustedEvent>();
        foreach (var node in eventElement.Parent?["history"]?.AsArray() ?? caseElement["history"]?.AsArray()
            ?? new JsonArray())
        {
            var item = node?.AsObject() ?? throw new InvalidOperationException("history item is not an object");
            var namespaceValues = ReadStringArray(item["actionNamespace"]?.AsArray(), "history.actionNamespace");
            var principalRef = item["principal"] is null ? (CedarEntityRef?)null : new CedarEntityRef(
                ReadEntityRef(item["principal"]!.AsObject(), "history.principal").type,
                ReadEntityRef(item["principal"]!.AsObject(), "history.principal").id);
            var resourceRef = item["resource"] is null ? (CedarEntityRef?)null : new CedarEntityRef(
                ReadEntityRef(item["resource"]!.AsObject(), "history.resource").type,
                ReadEntityRef(item["resource"]!.AsObject(), "history.resource").id);
            history.Add(new DogwoodTrustedEvent(
                RequiredString(item, "identity"),
                item["timestamp"]?.GetValue<long>() ?? 0,
                string.Join("::", namespaceValues),
                RequiredString(item, "actionId"),
                item["kind"]?.GetValue<string>() ?? "request",
                principalRef,
                resourceRef,
                RawOrSerialized(item, "context", item["contextJson"]?.GetValue<string>()),
                RawOrSerialized(item, "logged", item["loggedJson"]?.GetValue<string>()),
                RawOrSerialized(item, "entities", item["entitiesJson"]?.GetValue<string>())));
        }

        var providers = ImmutableArray.CreateBuilder<DogwoodTrustedProviderOutcome>();
        foreach (var node in caseElement["providerOutcomes"]?.AsArray() ?? new JsonArray())
        {
            var item = node?.AsObject() ?? throw new InvalidOperationException("provider outcome is not an object");
            var name = RequiredString(item, "name");
            var kind = RequiredString(item, "outcome");
            providers.Add(kind switch
            {
                "value" => DogwoodTrustedProviderOutcome.Succeeded(
                    name,
                    item["value"]?.ToJsonString() ?? throw new InvalidOperationException("provider value is missing")),
                "omitted" => DogwoodTrustedProviderOutcome.Omitted(name),
                "failure" => DogwoodTrustedProviderOutcome.Failed(
                    name,
                    RequiredString(item, "message")),
                _ => throw new InvalidOperationException($"unknown provider outcome `{kind}`"),
            });
        }

        return new DogwoodTrustedRuntime(timestamp, currentLogged, history.ToImmutable(), providers.ToImmutable());
    }

    private static JsonObject ObserveTrustedBindings(
        string source,
        DogwoodSchema schema,
        string? providerText,
        JsonObject caseElement,
        DogwoodTrustedRuntime runtime)
    {
        try
        {
            var runtimeJson = BuildRuntimeBindingJson(caseElement, runtime);
            var export = Dogwood.ExportCedarWithRuntime(source, schema, providerText, runtimeJson);
            var binding = export.RuntimeBinding;
            if (binding is null || !binding.Bound)
            {
                return new JsonObject
                {
                    ["bindings"] = new JsonObject(),
                    ["error"] = binding?.Error ?? "trusted binding was not observed",
                };
            }

            using var document = JsonDocument.Parse(binding.ContextJson);
            var temporal = new JsonArray();
            var providers = new JsonArray();
            foreach (var field in export.GeneratedContextFields)
            {
                if (field == "providers")
                {
                    if (document.RootElement.TryGetProperty("providers", out var providerValues)
                        && providerValues.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var provider in providerValues.EnumerateObject())
                        {
                            providers.Add(new JsonObject
                            {
                                ["fieldId"] = provider.Name,
                                ["outcome"] = "value",
                                ["value"] = JsonNode.Parse(provider.Value.GetRawText()),
                            });
                        }
                    }
                    continue;
                }
                if (document.RootElement.TryGetProperty(field, out var value)
                    && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    temporal.Add(new JsonObject
                    {
                        ["id"] = field,
                        ["value"] = value.GetBoolean(),
                    });
                }
            }
            return new JsonObject
            {
                ["bindings"] = new JsonObject
                {
                    ["temporal"] = temporal,
                    ["providers"] = providers,
                },
                ["error"] = null,
            };
        }
        catch (Exception exception)
        {
            return new JsonObject
            {
                ["bindings"] = new JsonObject(),
                ["error"] = $"{exception.GetType().Name}: {exception.Message}",
            };
        }
    }

    private static string BuildRuntimeBindingJson(JsonObject caseElement, DogwoodTrustedRuntime runtime)
    {
        var eventElement = caseElement["event"]?.AsObject()
            ?? throw new InvalidOperationException("case has no event object");
        var request = BuildAuthorizationInput(caseElement);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("timestamp", runtime.Timestamp);
            writer.WriteStartArray("history");
            foreach (var item in runtime.History)
            {
                writer.WriteStartObject();
                writer.WriteString("identity", item.Identity);
                writer.WriteNumber("timestamp", item.Timestamp);
                writer.WriteString("actionType", item.ActionType);
                writer.WriteString("actionId", item.ActionId);
                writer.WriteString("kind", item.Kind);
                WriteEntityRef(writer, "principal", item.Principal);
                WriteEntityRef(writer, "resource", item.Resource);
                writer.WriteString("contextJson", item.ContextJson);
                writer.WriteString("loggedJson", item.LoggedJson);
                writer.WriteString("entitiesJson", item.EntitiesJson);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("providers");
            foreach (var provider in runtime.Providers)
            {
                writer.WriteStartObject();
                writer.WriteString("name", provider.Name);
                writer.WriteString("outcome", provider.Outcome switch
                {
                    DogwoodTrustedProviderOutcomeKind.Value => "value",
                    DogwoodTrustedProviderOutcomeKind.Omitted => "omitted",
                    DogwoodTrustedProviderOutcomeKind.Failure => "failure",
                    _ => throw new InvalidOperationException("unknown provider outcome"),
                });
                if (provider.ValueJson is null)
                {
                    writer.WriteNull("valueJson");
                }
                else
                {
                    writer.WriteString("valueJson", provider.ValueJson);
                }
                if (provider.Message is null)
                {
                    writer.WriteNull("message");
                }
                else
                {
                    writer.WriteString("message", provider.Message);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartObject("current");
            writer.WriteString("actionType", request.Action.Type);
            writer.WriteString("actionId", request.Action.Id);
            writer.WriteString("kind", "request");
            WriteEntityRef(writer, "principal", request.Principal);
            WriteEntityRef(writer, "resource", request.Resource);
            writer.WriteString("contextJson", request.ContextJson);
            writer.WriteString("entitiesJson", request.EntitiesJson);
            writer.WriteString("loggedJson", runtime.CurrentLoggedJson);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteEntityRef(Utf8JsonWriter writer, string name, CedarEntityRef? entity)
    {
        if (entity is null)
        {
            writer.WriteNull(name);
            return;
        }
        writer.WriteStartObject(name);
        writer.WriteString("type", entity.Value.Type);
        writer.WriteString("id", entity.Value.Id);
        writer.WriteEndObject();
    }

    private static JsonArray CompareObservations(JsonObject upstream, JsonObject managed)
    {
        var differences = new JsonArray();
        foreach (var field in ComparisonFields)
        {
            JsonNode? left;
            JsonNode? right;
            if (field == "determining")
            {
                // Set semantics: order is not assumed for determining IDs.
                left = SortJsonArray(upstream[field]);
                right = SortJsonArray(managed[field]);
            }
            else
            {
                left = CanonicalValue(upstream[field]);
                right = CanonicalValue(managed[field]);
            }
            if (!JsonNode.DeepEquals(left, right))
            {
                differences.Add(new JsonObject
                {
                    ["field"] = field,
                    ["upstream"] = left?.DeepClone(),
                    ["managed"] = right?.DeepClone(),
                });
            }
        }

        // Trusted-binding records are retained as evidence on each side but are
        // not part of the plan §5 comparison contract. Their shape is
        // implementation-specific (upstream records resolved provider arguments
        // and names; D3 records the exported field id), and no normative field
        // is carried only there: the decision, stage, determining set and error
        // channels above are the compared surface.
        return differences;
    }

    /// <summary>
    /// Strict comparison for the case where both sides produced no decision and
    /// reported an operation failure. The plan makes stage, operation-failed
    /// state, decision, determining set, and error presence normative, so none
    /// of them is normalized away. Error categories are compared whenever the
    /// upstream side reports any: the upstream harness uses an empty category
    /// channel for its own pre-pipeline faults, and in that situation the
    /// managed categories are retained as evidence instead of being treated as
    /// a difference. (R3 hardening of qualification finding QD3Q-07.)
    /// </summary>
    private static JsonArray CompareDualFailures(JsonObject upstream, JsonObject managed)
    {
        var differences = new JsonArray();
        foreach (var field in new[] { "stage", "operationFailed", "decision", "hasErrors", "cleanAllow", "policyErrorIds" })
        {
            var left = CanonicalValue(upstream[field]);
            var right = CanonicalValue(managed[field]);
            if (!JsonNode.DeepEquals(left, right))
            {
                differences.Add(new JsonObject
                {
                    ["field"] = field,
                    ["upstream"] = left?.DeepClone(),
                    ["managed"] = right?.DeepClone(),
                });
            }
        }

        var leftDetermining = SortJsonArray(upstream["determining"]);
        var rightDetermining = SortJsonArray(managed["determining"]);
        if (!JsonNode.DeepEquals(leftDetermining, rightDetermining))
        {
            differences.Add(new JsonObject
            {
                ["field"] = "determining",
                ["upstream"] = leftDetermining?.DeepClone(),
                ["managed"] = rightDetermining?.DeepClone(),
            });
        }

        var upstreamCategories = upstream["errorCategories"]?.AsArray() ?? new JsonArray();
        if (upstreamCategories.Count != 0)
        {
            var left = CanonicalValue(upstreamCategories);
            var right = CanonicalValue(managed["errorCategories"]);
            if (!JsonNode.DeepEquals(left, right))
            {
                differences.Add(new JsonObject
                {
                    ["field"] = "errorCategories",
                    ["upstream"] = left?.DeepClone(),
                    ["managed"] = right?.DeepClone(),
                });
            }
        }
        return differences;
    }

    /// <summary>
    /// A case is a vacuous agreement when neither side produced a decision and
    /// neither side reported an operation failure. Such a case carries no
    /// authorization observation and must never satisfy coverage.
    /// </summary>
    private static bool IsVacuousAgreement(JsonObject upstream, JsonObject managed)
    {
        return IsNullOrNullNode(upstream["decision"])
            && IsNullOrNullNode(managed["decision"])
            && !(upstream["operationFailed"]?.GetValue<bool>() ?? false)
            && !(managed["operationFailed"]?.GetValue<bool>() ?? false);
    }

    private static bool IsNullOrNullNode(JsonNode? node)
    {
        return node is null || node.GetValueKind() == JsonValueKind.Null;
    }

    private static JsonNode? SortJsonArray(JsonNode? value)
    {
        if (value is not JsonArray array)
        {
            return CanonicalValue(value);
        }
        var items = array.Select(item => CanonicalValue(item)?.DeepClone())
            .Where(item => item is not null)
            .OrderBy(item => item!.ToJsonString(), StringComparer.Ordinal)
            .ToArray();
        return new JsonArray(items);
    }

    private static JsonNode? CanonicalValue(JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }
        if (value is JsonArray array)
        {
            return new JsonArray(array.Select(item => CanonicalValue(item)?.DeepClone()).ToArray());
        }
        if (value is JsonObject obj)
        {
            var output = new JsonObject();
            foreach (var property in obj.OrderBy(property => property.Key, StringComparer.Ordinal))
            {
                output[property.Key] = CanonicalValue(property.Value)?.DeepClone();
            }
            return output;
        }
        return value.DeepClone();
    }

    private static List<string> ValidateExpectedDifferences(
        string caseId,
        IReadOnlyList<string> differenceIds,
        IReadOnlyDictionary<string, JsonObject> differenceRows,
        JsonObject upstream,
        JsonObject managed,
        JsonArray observedDifferences)
    {
        var errors = new List<string>();
        foreach (var differenceId in differenceIds)
        {
            if (!differenceRows.TryGetValue(differenceId, out var row))
            {
                errors.Add($"{caseId} names unknown difference id `{differenceId}`");
                continue;
            }
            if (!row["cases"]!.AsArray().Select(node => node!.GetValue<string>()).Contains(caseId, StringComparer.Ordinal))
            {
                errors.Add($"{differenceId} does not list case `{caseId}`");
            }
            if (!row["caseObservations"]!.AsObject().TryGetPropertyValue(caseId, out var observation))
            {
                errors.Add($"{differenceId} has no exact observation for `{caseId}`");
                continue;
            }
            if (observation is null)
            {
                errors.Add($"{differenceId} observation for `{caseId}` is null");
                continue;
            }
            var expectedUpstream = observation["upstream"]?.AsObject();
            var expectedManaged = observation["d3"]?.AsObject()
                ?? observation["managed"]?.AsObject();
            if (expectedUpstream is null || expectedManaged is null)
            {
                errors.Add($"{differenceId} observation for `{caseId}` is incomplete");
                continue;
            }
            if (!MatchesObservation(expectedUpstream, upstream))
            {
                errors.Add($"{differenceId} upstream observation differs for `{caseId}`");
            }
            if (!MatchesObservation(expectedManaged, managed))
            {
                errors.Add($"{differenceId} managed observation differs for `{caseId}`");
            }
            errors.AddRange(ValidateAuthorizedDifferenceScope(differenceId, caseId, row, observedDifferences));
        }
        return errors;
    }

    /// <summary>
    /// Enforces the owner-approved scope of an approved-difference row. When a
    /// row declares `authorizedDifferenceFields`, that array is the exhaustive
    /// set of comparison fields whose values the owner has authorized to differ
    /// for the row's cases. Every observed difference must be inside the set,
    /// every field outside the set must match exactly, and every authorized
    /// field must actually differ. The scope is therefore machine-enforced:
    /// editing the prose of a register row cannot widen it, and a case that
    /// drifts outside the approved fields fails instead of being absorbed.
    /// </summary>
    private static List<string> ValidateAuthorizedDifferenceScope(
        string differenceId,
        string caseId,
        JsonObject row,
        JsonArray observedDifferences)
    {
        var errors = new List<string>();
        var scopeNode = row["authorizedDifferenceFields"];
        if (scopeNode is null)
        {
            return errors;
        }
        if (scopeNode is not JsonArray scopeArray)
        {
            errors.Add($"{differenceId} has a malformed authorizedDifferenceFields scope");
            return errors;
        }
        var authorized = scopeArray
            .Select(node => node!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
        var observed = observedDifferences
            .Select(node => node?["field"]?.GetValue<string>())
            .Where(field => field is not null && ComparisonFields.Contains(field, StringComparer.Ordinal))
            .Select(field => field!)
            .ToHashSet(StringComparer.Ordinal);
        var outside = observed
            .Except(authorized, StringComparer.Ordinal)
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToArray();
        if (outside.Length != 0)
        {
            errors.Add(
                $"{differenceId} observes differences outside its authorized scope for `{caseId}`: "
                + string.Join(", ", outside));
        }
        var absent = authorized
            .Except(observed, StringComparer.Ordinal)
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToArray();
        if (absent.Length != 0)
        {
            errors.Add(
                $"{differenceId} does not observe its authorized difference fields for `{caseId}`: "
                + string.Join(", ", absent));
        }
        return errors;
    }

    private static bool MatchesObservation(JsonObject expected, JsonObject actual)
    {
        foreach (var property in expected)
        {
            if (!actual.TryGetPropertyValue(property.Key, out var actualValue))
            {
                return false;
            }
            if (!JsonNode.DeepEquals(CanonicalValue(property.Value), CanonicalValue(actualValue)))
            {
                return false;
            }
        }
        return true;
    }

    private static IReadOnlyDictionary<string, JsonObject> ReadNormalizationRows(JsonObject root)
    {
        var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in root["failClosedNormalizations"]?.AsArray() ?? new JsonArray())
        {
            var row = node?.AsObject()
                ?? throw new InvalidOperationException("fail-closed normalization row is not an object");
            var id = RequiredString(row, "id");
            rows.Add(id, row);
        }
        return rows;
    }

    /// <summary>
    /// Validates a registered fail-closed normalization: the case must be
    /// listed with exact observations, both sides must fail closed, and
    /// neither side may allow. This is the only sanctioned way to reconcile a
    /// non-authorizing difference whose decision shape the pinned upstream
    /// documents as undefined behavior; it is not an approved difference.
    /// </summary>
    private static List<string> ValidateFailClosedNormalization(
        string caseId,
        string normalizationId,
        IReadOnlyDictionary<string, JsonObject> rows,
        JsonObject upstream,
        JsonObject managed)
    {
        var errors = new List<string>();
        if (!rows.TryGetValue(normalizationId, out var row))
        {
            errors.Add($"{caseId} names unknown fail-closed normalization `{normalizationId}`");
            return errors;
        }
        var rowCases = row["cases"]?.AsArray()?.Select(node => node!.GetValue<string>()).ToArray()
            ?? Array.Empty<string>();
        if (!rowCases.Contains(caseId, StringComparer.Ordinal))
        {
            errors.Add($"{normalizationId} does not list case `{caseId}`");
        }
        var observations = row["caseObservations"]?.AsObject();
        if (observations is null || !observations.TryGetPropertyValue(caseId, out var observation)
            || observation is null)
        {
            errors.Add($"{normalizationId} has no exact observation for `{caseId}`");
        }
        else
        {
            var expectedUpstream = observation["upstream"]?.AsObject();
            var expectedManaged = observation["d3"]?.AsObject()
                ?? observation["managed"]?.AsObject();
            if (expectedUpstream is null || expectedManaged is null)
            {
                errors.Add($"{normalizationId} observation for `{caseId}` is incomplete");
            }
            else
            {
                if (!MatchesObservation(expectedUpstream, upstream))
                {
                    errors.Add($"{normalizationId} upstream observation differs for `{caseId}`");
                }
                if (!MatchesObservation(expectedManaged, managed))
                {
                    errors.Add($"{normalizationId} managed observation differs for `{caseId}`");
                }
            }
        }

        if (!IsFailClosedObservation(upstream))
        {
            errors.Add($"{normalizationId} upstream observation for `{caseId}` is not fail-closed");
        }
        if (!IsFailClosedObservation(managed))
        {
            errors.Add($"{normalizationId} managed observation for `{caseId}` is not fail-closed");
        }
        if (upstream["decision"]?.GetValue<string>() == "allow"
            || managed["decision"]?.GetValue<string>() == "allow")
        {
            errors.Add($"{normalizationId} case `{caseId}` produced an Allow");
        }
        return errors;
    }

    private static bool IsFailClosedObservation(JsonObject observation)
    {
        var decision = observation["decision"]?.GetValue<string>();
        var failed = observation["operationFailed"]?.GetValue<bool>() ?? false;
        var hasErrors = observation["hasErrors"]?.GetValue<bool>() ?? false;
        var cleanAllow = observation["cleanAllow"]?.GetValue<bool>() ?? false;
        return decision != "allow"
            && !cleanAllow
            && (decision == "deny" || failed || hasErrors);
    }

    private static IReadOnlyDictionary<string, JsonObject> ReadDifferenceRows(JsonObject root)
    {
        var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in root["differences"]?.AsArray() ?? new JsonArray())
        {
            var row = node?.AsObject() ?? throw new InvalidOperationException("difference row is not an object");
            var id = RequiredString(row, "id");
            rows.Add(id, row);
        }
        return rows;
    }

    private static List<string> ValidateCatalog(JsonObject fixtures, JsonObject differences)
    {
        var errors = new List<string>();
        if (fixtures["schemaVersion"]?.GetValue<string>() != SchemaVersion)
        {
            errors.Add($"fixture schemaVersion must be {SchemaVersion}");
        }
        if (differences["schemaVersion"]?.GetValue<string>() != SchemaVersion)
        {
            errors.Add($"difference schemaVersion must be {SchemaVersion}");
        }

        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        var references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var node in fixtures["cases"]?.AsArray() ?? new JsonArray())
        {
            var item = node?.AsObject() ?? throw new InvalidOperationException("fixture case is not an object");
            var id = RequiredString(item, "id");
            if (!caseIds.Add(id))
            {
                errors.Add($"duplicate fixture id `{id}`");
            }
            var ids = ReadStringArray(item["expectedDifferenceIds"]?.AsArray(), "expectedDifferenceIds");
            var unique = new HashSet<string>(ids, StringComparer.Ordinal);
            if (unique.Count != ids.Count)
            {
                errors.Add($"{id} repeats a difference id");
            }
            references[id] = unique;
        }

        var rows = ReadDifferenceRows(differences);
        foreach (var (id, row) in rows)
        {
            var required = new[]
            {
                "agreementClass",
                "cases",
                "upstreamObservation",
                "d3Observation",
                "failClosedEffect",
                "caseObservations",
            };
            foreach (var field in required)
            {
                if (!row.ContainsKey(field))
                {
                    errors.Add($"{id} is missing `{field}`");
                }
            }
            var rowCases = row["cases"]?.AsArray()?.Select(node => node!.GetValue<string>()).ToArray()
                ?? Array.Empty<string>();
            var uniqueCases = new HashSet<string>(rowCases, StringComparer.Ordinal);
            if (uniqueCases.Count != rowCases.Length || rowCases.Length == 0)
            {
                errors.Add($"{id} has an empty or repeated case list");
            }
            var observations = row["caseObservations"]?.AsObject() ?? new JsonObject();
            if (!observations.Select(property => property.Key).ToHashSet(StringComparer.Ordinal).SetEquals(uniqueCases))
            {
                errors.Add($"{id} case observations do not match its case list");
            }
            // An owner-approved difference scope names the exact comparison
            // fields whose values may differ. The names must be real comparison
            // fields, and the scope is one ceiling per row: a scoped row may be
            // the only approved difference its cases claim, so a single
            // authorization ceiling can never absorb an unrelated divergence.
            if (row.TryGetPropertyValue("authorizedDifferenceFields", out var scopeNode)
                && scopeNode is not null)
            {
                if (scopeNode is not JsonArray scopeArray)
                {
                    errors.Add($"{id} has a malformed authorizedDifferenceFields scope");
                }
                else
                {
                    var scope = scopeArray.Select(node => node!.GetValue<string>()).ToArray();
                    if (scope.Length == 0)
                    {
                        errors.Add($"{id} has an empty authorizedDifferenceFields scope");
                    }
                    if (scope.Distinct(StringComparer.Ordinal).Count() != scope.Length)
                    {
                        errors.Add($"{id} repeats an authorizedDifferenceFields entry");
                    }
                    foreach (var field in scope)
                    {
                        if (!ComparisonFields.Contains(field, StringComparer.Ordinal))
                        {
                            errors.Add($"{id} authorizes unknown comparison field `{field}`");
                        }
                    }
                }
            }
            foreach (var caseId in uniqueCases)
            {
                if (!references.TryGetValue(caseId, out var referenced))
                {
                    errors.Add($"{id} names unknown case `{caseId}`");
                }
                else if (!referenced.Contains(id))
                {
                    errors.Add($"{id} is not referenced by case `{caseId}`");
                }
                else if (row.TryGetPropertyValue("authorizedDifferenceFields", out var scopeValue)
                    && scopeValue is not null
                    && referenced.Count != 1)
                {
                    errors.Add(
                        $"{id} carries an authorized difference scope, so case `{caseId}` must claim no other approved difference");
                }
            }
        }

        foreach (var (caseId, referenced) in references)
        {
            foreach (var differenceId in referenced)
            {
                if (!rows.ContainsKey(differenceId))
                {
                    errors.Add($"{caseId} names unknown difference id `{differenceId}`");
                }
            }
        }

        // Fail-closed normalizations are a distinct, narrower mechanism: they
        // may only be attached to a case that does not also claim an approved
        // difference, and every listed case must carry the exact reference.
        var normalizations = ReadNormalizationRows(differences);
        var normalizationReferences = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var node in fixtures["cases"]?.AsArray() ?? new JsonArray())
        {
            var item = node?.AsObject() ?? throw new InvalidOperationException("fixture case is not an object");
            var id = RequiredString(item, "id");
            var normalizationId = item["failClosedNormalizationId"]?.GetValue<string>();
            if (normalizationId is null)
            {
                continue;
            }
            if (references[id].Count != 0)
            {
                errors.Add($"{id} claims both an approved difference and a fail-closed normalization");
            }
            if (!normalizations.ContainsKey(normalizationId))
            {
                errors.Add($"{id} names unknown fail-closed normalization `{normalizationId}`");
            }
            normalizationReferences[id] = new HashSet<string>(new[] { normalizationId }, StringComparer.Ordinal);
        }
        foreach (var (id, row) in normalizations)
        {
            var required = new[]
            {
                "title",
                "cases",
                "upstreamObservation",
                "d3Observation",
                "basis",
                "failClosedEffect",
                "caseObservations",
            };
            foreach (var field in required)
            {
                if (!row.ContainsKey(field))
                {
                    errors.Add($"{id} is missing `{field}`");
                }
            }
            var rowCases = row["cases"]?.AsArray()?.Select(node => node!.GetValue<string>()).ToArray()
                ?? Array.Empty<string>();
            var uniqueCases = new HashSet<string>(rowCases, StringComparer.Ordinal);
            if (uniqueCases.Count != rowCases.Length || rowCases.Length == 0)
            {
                errors.Add($"{id} has an empty or repeated case list");
            }
            var observations = row["caseObservations"]?.AsObject() ?? new JsonObject();
            if (!observations.Select(property => property.Key).ToHashSet(StringComparer.Ordinal).SetEquals(uniqueCases))
            {
                errors.Add($"{id} case observations do not match its case list");
            }
            foreach (var caseId in uniqueCases)
            {
                if (!normalizationReferences.TryGetValue(caseId, out var referenced)
                    || !referenced.Contains(id))
                {
                    errors.Add($"{id} is not referenced by case `{caseId}`");
                }
            }
        }
        return errors;
    }

    private static List<string> ValidateFamilyCoverage(
        JsonObject fixtures,
        IReadOnlyDictionary<string, FamilyCoverage> actual)
    {
        var errors = new List<string>();
        var familyRequirements = fixtures["familyRequirements"];
        if (familyRequirements is null)
        {
            throw new InvalidOperationException("fixture manifest has no familyRequirements");
        }
        var requirements = familyRequirements.AsObject();
        foreach (var family in requirements)
        {
            var familyValue = family.Value;
            if (familyValue is null)
            {
                errors.Add($"family {family.Key} has null value");
                continue;
            }
            var requirement = familyValue.AsObject();
            var requiredControls = (requirement["requiredControls"]?.AsArray() ?? new JsonArray())
                .Select(node => node!.GetValue<string>())
                .ToHashSet(StringComparer.Ordinal);
            if (requiredControls.Count == 0)
            {
                errors.Add($"family {family.Key} declares no required controls");
            }
            if (!actual.TryGetValue(family.Key, out var coverage))
            {
                errors.Add($"family {family.Key} has no cases");
                continue;
            }
            foreach (var control in requiredControls)
            {
                if (!coverage.Controls.TryGetValue(control, out var classification))
                {
                    errors.Add($"family {family.Key} has no `{control}` control");
                }
                else if (classification is "unexecuted/pending" or "unexpected-difference")
                {
                    errors.Add($"family {family.Key} control `{control}` is {classification}");
                }
            }

            // A required control may carry the decision its role must actually
            // produce. The R4 validator only checked that the control name
            // appeared and that the family produced *some* decision, so a
            // required positive observation that had turned into a clean Deny
            // still satisfied the family (qualification finding QD3Q-07). The
            // role is now machine-enforced: at least one case of that control
            // must have produced the declared decision on BOTH sides.
            if (requirement["requiredControlDecisions"] is JsonObject requiredControlDecisions)
            {
                foreach (var role in requiredControlDecisions)
                {
                    var control = role.Key;
                    var expected = role.Value?.GetValue<string>()
                        ?? throw new InvalidOperationException(
                            $"family {family.Key} has a malformed requiredControlDecisions entry");
                    if (!coverage.Controls.TryGetValue(control, out var classification))
                    {
                        errors.Add(
                            $"family {family.Key} requires control `{control}` to produce `{expected}` but the family has no `{control}` control");
                        continue;
                    }
                    if (classification is "unexecuted/pending" or "unexpected-difference")
                    {
                        errors.Add($"family {family.Key} control `{control}` is {classification}");
                        continue;
                    }
                    var observed = coverage.ControlDecisionPairs.TryGetValue(control, out var pairs)
                        ? pairs.Count(pair => pair.Upstream == expected && pair.Managed == expected)
                        : 0;
                    if (observed == 0)
                    {
                        errors.Add(
                            $"family {family.Key} control `{control}` never produced the required `{expected}` decision on both sides");
                    }
                }
            }

            var decisionCoverage = requirement["decisionCoverage"]?.GetValue<string>() ?? "required";
            switch (decisionCoverage)
            {
                case "required":
                    // A family whose only cases fail before a decision cannot
                    // satisfy a positive/negative coverage role. This is the
                    // exact false-green mechanism QD3Q-07 identified: an
                    // operation-failure classification used to satisfy a
                    // positive family merely by naming its control.
                    if (coverage.Decisions.Count == 0)
                    {
                        errors.Add(
                            $"family {family.Key} requires decision coverage but no case produced a decision");
                    }
                    if (!coverage.HasDecisionBearingClassification)
                    {
                        errors.Add(
                            $"family {family.Key} requires decision coverage but has no agreement or approved-difference case");
                    }
                    break;
                case "managed-control-only":
                    if (!coverage.HasManagedControl)
                    {
                        errors.Add(
                            $"family {family.Key} requires a managed control observation but none executed");
                    }
                    break;
                case "exempt":
                    break;
                default:
                    errors.Add($"family {family.Key} has unknown decisionCoverage `{decisionCoverage}`");
                    break;
            }
        }

        foreach (var family in actual.Keys)
        {
            if (!requirements.ContainsKey(family))
            {
                errors.Add($"family {family} is not declared in familyRequirements");
            }
        }
        return errors;
    }

    private static JsonObject BuildSummary(
        IReadOnlyList<JsonObject> reports,
        IReadOnlyDictionary<string, FamilyCoverage> familyCoverage)
    {
        var classifications = new JsonObject();
        foreach (var group in reports.GroupBy(
            report => report["classification"]!.GetValue<string>(),
            StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            classifications[group.Key] = group.Count();
        }
        var requiredCases = reports.Count;
        var passed = reports.Count(report => report["verdict"]!.GetValue<string>() == "pass");
        var failed = reports.Count(report => report["verdict"]!.GetValue<string>() == "fail");
        var pending = reports.Count(report => report["verdict"]!.GetValue<string>() == "pending");
        return new JsonObject
        {
            ["total"] = requiredCases,
            ["passed"] = passed,
            ["failed"] = failed,
            ["pending"] = pending,
            ["familyCount"] = familyCoverage.Count,
            ["classifications"] = classifications,
        };
    }

    private static JsonObject BuildFamilyCoverage(IReadOnlyDictionary<string, FamilyCoverage> coverage)
    {
        var output = new JsonObject();
        foreach (var family in coverage.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var controls = new JsonObject();
            foreach (var control in family.Value.Controls.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                controls[control.Key] = control.Value;
            }
            output[family.Key] = new JsonObject
            {
                ["controls"] = controls,
            };
        }
        return output;
    }

    private static IReadOnlyDictionary<string, JsonObject> RunOracleBatch(
        string oraclePath,
        IReadOnlyList<JsonObject> cases,
        IReadOnlyDictionary<string, string> schemas,
        IReadOnlyDictionary<string, string> providerDeclarations)
    {
        var envelopes = new JsonArray();
        foreach (var item in cases.Where(item => item["kind"]?.GetValue<string>() == "differential"))
        {
            var schemaName = RequiredString(item, "schema");
            if (!schemas.TryGetValue(schemaName, out var schema))
            {
                throw new InvalidOperationException($"unknown schema `{schemaName}`");
            }
            var envelope = new JsonObject
            {
                ["id"] = RequiredString(item, "id"),
                ["source"] = RequiredString(item, "source"),
                ["policySchema"] = schema,
                ["event"] = item["event"]?.DeepClone() ?? new JsonObject(),
            };
            CopyOptional(item, envelope, "eventSchema");
            CopyOptional(item, envelope, "macros");
            CopyOptional(item, envelope, "distincter");
            var providerName = item["providerDeclarations"]?.GetValue<string>();
            if (providerName is not null)
            {
                if (!providerDeclarations.TryGetValue(providerName, out var providerText))
                {
                    throw new InvalidOperationException($"unknown provider declaration set `{providerName}`");
                }
                envelope["providerDeclarations"] = providerText;
            }
            if (item["providerOutcomes"] is not null)
            {
                envelope["providerOutcomes"] = item["providerOutcomes"]!.DeepClone();
            }
            if (item["history"] is not null)
            {
                envelope["history"] = item["history"]!.DeepClone();
            }
            envelopes.Add(envelope);
        }

        if (envelopes.Count == 0)
        {
            return new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        }

        var input = new JsonObject { ["cases"] = envelopes };
        var process = RunProcess(oraclePath, Array.Empty<string>(), input.ToJsonString());
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"upstream oracle failed: {process.Error}");
        }
        var root = JsonNode.Parse(process.Output)?.AsObject()
            ?? throw new InvalidOperationException("upstream oracle output is not an object");
        var results = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in root["results"]?.AsArray() ?? new JsonArray())
        {
            var item = node?.AsObject() ?? throw new InvalidOperationException("oracle result is not an object");
            var id = RequiredString(item, "id");
            var outcome = item["outcome"]?.AsObject()
                ?? throw new InvalidOperationException("oracle outcome is not an object");
            results.Add(id, outcome);
        }
        return results;
    }

    private static (int ExitCode, string Output, string Error) RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string? input = null,
        string? workingDirectory = null)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (workingDirectory is not null)
        {
            start.WorkingDirectory = workingDirectory;
        }
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"could not start `{fileName}`");
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    /// <summary>
    /// Harness regression controls. Each check recreates one false-green
    /// mechanism that the Q1 review found in the R2 probe and asserts that the
    /// hardened runner now fails it. The checks run against the same
    /// <see cref="ExecuteCase"/> and <see cref="ValidateFamilyCoverage"/> code
    /// paths as a real differential run; no separate reimplementation.
    /// </summary>
    private static int RunSelfTest(string[] args)
    {
        if (args.Length != 3)
        {
            throw new InvalidOperationException("self-test mode requires an oracle path and an output path");
        }
        var outputPath = Path.GetFullPath(args[2]);
        var checks = new JsonArray();
        var schemas = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["self"] = "namespace Test { entity User; entity Item; action Read appliesTo "
                + "{ principal: [User], resource: [Item], context: { input: { key: String } } }; }",
            ["temporal"] = "namespace Test { entity User; entity Item; "
                + "action Approve appliesTo { principal: [User], resource: [Item], context: { input: { stock: String } } }; "
                + "action Sell appliesTo { principal: [User], resource: [Item], context: { input: { stock: String } } }; }",
        };
        var providerDeclarations = new Dictionary<string, string>(StringComparer.Ordinal);
        var emptyLifecycle = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        void Check(string name, Func<bool> assertion, string detail)
        {
            bool passed;
            string? error = null;
            try
            {
                passed = assertion();
            }
            catch (Exception exception)
            {
                passed = false;
                error = $"{exception.GetType().Name}: {exception.Message}";
            }
            checks.Add(new JsonObject
            {
                ["name"] = name,
                ["passed"] = passed,
                ["detail"] = detail,
                ["error"] = error,
            });
        }

        JsonObject Case(string id, string schema, string source, string expected, params string[] differenceIds)
        {
            return new JsonObject
            {
                ["id"] = id,
                ["kind"] = "differential",
                ["control"] = id,
                ["families"] = new JsonArray("C-01"),
                ["question"] = "self-test",
                ["schema"] = schema,
                ["source"] = source,
                ["expectedClassification"] = expected,
                ["expectedDifferenceIds"] = new JsonArray(differenceIds.Select(value => (JsonNode?)value).ToArray()),
                ["intendedStage"] = "evaluation",
                ["event"] = new JsonObject
                {
                    ["timestamp"] = 100,
                    ["actionNamespace"] = new JsonArray("Test", "Action"),
                    ["actionId"] = "Read",
                    ["principal"] = new JsonObject { ["type"] = "Test::User", ["id"] = "alice" },
                    ["resource"] = new JsonObject { ["type"] = "Test::Item", ["id"] = "item-1" },
                    ["entities"] = new JsonArray(),
                    ["context"] = new JsonObject { ["input"] = new JsonObject { ["key"] = "x" } },
                },
            };
        }

        JsonObject Oracle(string stage, bool failed, string? decision, string? error, params string[] categories)
        {
            return new JsonObject
            {
                ["stage"] = stage,
                ["failed"] = failed,
                ["decision"] = decision,
                ["determiningRules"] = new JsonArray(),
                ["errors"] = error is null ? new JsonArray() : new JsonArray(new JsonObject { ["message"] = error }),
                ["errorCategories"] = new JsonArray(categories.Select(value => (JsonNode?)value).ToArray()),
                ["policyErrorIds"] = new JsonArray(),
                ["error"] = error,
            };
        }

        JsonObject Run(JsonObject testCase, JsonObject oracle, IReadOnlyDictionary<string, JsonObject>? rows = null)
        {
            var id = RequiredString(testCase, "id");
            return ExecuteCase(
                testCase,
                schemas,
                providerDeclarations,
                new Dictionary<string, JsonObject>(StringComparer.Ordinal) { [id] = oracle },
                rows ?? new Dictionary<string, JsonObject>(StringComparer.Ordinal),
                new Dictionary<string, JsonObject>(StringComparer.Ordinal),
                emptyLifecycle);
        }

        var allowSource = "permit(principal, action == Test::Action::\"Read\", resource);";
        var conditionalSource =
            "permit(principal, action == Test::Action::\"Read\", resource) when { context.input.key == \"match\" };";

        Check(
            "malformed-shared-input-cannot-pass",
            () =>
            {
                var report = Run(
                    Case("self-envelope", "self", allowSource, "agreement"),
                    Oracle("request-validation", true, null, "envelope parse: unknown field `valueJson`"));
                return report["verdict"]!.GetValue<string>() == "fail"
                    && report["classification"]!.GetValue<string>() == "unexecuted/pending"
                    && report["differenceValidation"]!["pendingReason"]!.GetValue<string>()
                        .Contains("harness envelope fault", StringComparison.Ordinal);
            },
            "an upstream envelope fault is a harness defect, never an agreement");

        Check(
            "unregistered-difference-fails",
            () =>
            {
                var report = Run(
                    Case("self-unregistered", "self", conditionalSource, "agreement"),
                    Oracle("evaluation", false, "allow", null));
                return report["verdict"]!.GetValue<string>() == "fail"
                    && report["classification"]!.GetValue<string>() == "unexpected-difference";
            },
            "an Allow-versus-Deny divergence with no register row fails");

        Check(
            "registered-difference-with-wrong-observation-fails",
            () =>
            {
                var testCase = Case("self-registered", "self", conditionalSource, "approved-difference", "ED-SELFTEST");
                var row = new JsonObject
                {
                    ["id"] = "ED-SELFTEST",
                    ["agreementClass"] = "expected-difference",
                    ["cases"] = new JsonArray("self-registered"),
                    ["upstreamObservation"] = "self-test",
                    ["d3Observation"] = "self-test",
                    ["failClosedEffect"] = "self-test",
                    ["caseObservations"] = new JsonObject
                    {
                        ["self-registered"] = new JsonObject
                        {
                            ["upstream"] = new JsonObject { ["decision"] = "deny" },
                            ["d3"] = new JsonObject { ["decision"] = "allow" },
                        },
                    },
                };
                var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal) { ["ED-SELFTEST"] = row };
                var report = Run(testCase, Oracle("evaluation", false, "allow", null), rows);
                var errors = report["differenceValidation"]!["errors"]!.AsArray();
                return report["verdict"]!.GetValue<string>() == "fail"
                    && report["classification"]!.GetValue<string>() == "unexpected-difference"
                    && errors.Count != 0;
            },
            "a register row that does not match the observed sides fails");

        JsonObject ScopedRow(string caseId, string[] authorizedFields, string upstreamDecision)
        {
            return new JsonObject
            {
                ["id"] = "ED-SCOPE",
                ["agreementClass"] = "expected-difference",
                ["cases"] = new JsonArray(caseId),
                ["upstreamObservation"] = "self-test",
                ["d3Observation"] = "self-test",
                ["failClosedEffect"] = "self-test",
                ["authorizedDifferenceFields"] = new JsonArray(
                    authorizedFields.Select(value => (JsonNode?)value).ToArray()),
                ["caseObservations"] = new JsonObject
                {
                    [caseId] = new JsonObject
                    {
                        ["upstream"] = new JsonObject { ["decision"] = upstreamDecision },
                        ["d3"] = new JsonObject { ["decision"] = "allow" },
                    },
                },
            };
        }

        Check(
            "authorized-difference-scope-accepted",
            () =>
            {
                var testCase = Case("self-scoped", "self", allowSource, "approved-difference", "ED-SCOPE");
                var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
                {
                    ["ED-SCOPE"] = ScopedRow("self-scoped", ["decision", "determining", "cleanAllow"], "deny"),
                };
                var report = Run(testCase, Oracle("evaluation", false, "deny", null), rows);
                return report["verdict"]!.GetValue<string>() == "pass"
                    && report["classification"]!.GetValue<string>() == "approved-difference";
            },
            "an approved difference whose observed fields are exactly its authorized scope passes");

        Check(
            "authorized-difference-scope-rejects-an-out-of-scope-field",
            () =>
            {
                var testCase = Case("self-scoped-wide", "self", allowSource, "approved-difference", "ED-SCOPE");
                var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
                {
                    ["ED-SCOPE"] = ScopedRow("self-scoped-wide", ["decision"], "deny"),
                };
                var report = Run(testCase, Oracle("evaluation", false, "deny", null), rows);
                var errors = report["differenceValidation"]!["errors"]!.AsArray()
                    .Select(node => node!.GetValue<string>())
                    .ToArray();
                return report["verdict"]!.GetValue<string>() == "fail"
                    && report["classification"]!.GetValue<string>() == "unexpected-difference"
                    && errors.Any(error => error.Contains("outside its authorized scope", StringComparison.Ordinal));
            },
            "a difference outside the authorized scope is never absorbed by a register row");

        Check(
            "authorized-difference-scope-requires-the-authorized-field-to-differ",
            () =>
            {
                var testCase = Case("self-scoped-idle", "self", allowSource, "approved-difference", "ED-SCOPE");
                var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
                {
                    ["ED-SCOPE"] = ScopedRow(
                        "self-scoped-idle",
                        ["decision", "determining", "cleanAllow", "stage"],
                        "deny"),
                };
                var report = Run(testCase, Oracle("evaluation", false, "deny", null), rows);
                var errors = report["differenceValidation"]!["errors"]!.AsArray()
                    .Select(node => node!.GetValue<string>())
                    .ToArray();
                return report["verdict"]!.GetValue<string>() == "fail"
                    && errors.Any(error => error.Contains("does not observe its authorized difference fields", StringComparison.Ordinal));
            },
            "an authorized scope field that does not actually differ fails the case");

        Check(
            "authorized-difference-scope-rejects-a-difference-that-vanished",
            () =>
            {
                var testCase = Case("self-scoped-vanished", "self", allowSource, "approved-difference", "ED-SCOPE");
                var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
                {
                    ["ED-SCOPE"] = ScopedRow("self-scoped-vanished", ["decision", "determining", "cleanAllow"], "deny"),
                };
                var report = Run(testCase, Oracle("evaluation", false, "allow", null), rows);
                return report["verdict"]!.GetValue<string>() == "fail"
                    && report["classification"]!.GetValue<string>() == "unexpected-difference";
            },
            "a case that stopped differing is no longer an approved difference");

        Check(
            "approved-difference-cannot-mask-an-intended-stage-mismatch",
            () =>
            {
                var testCase = Case("self-scoped-stage", "self", allowSource, "approved-difference", "ED-SCOPE");
                var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
                {
                    ["ED-SCOPE"] = ScopedRow("self-scoped-stage", ["decision", "determining", "cleanAllow"], "deny"),
                };
                var report = Run(testCase, Oracle("lowering", false, "deny", null), rows);
                return report["verdict"]!.GetValue<string>() == "fail"
                    && report["classification"]!.GetValue<string>() == "unexpected-difference"
                    && report["differenceValidation"]!["intendedStageMismatch"]!.GetValue<bool>();
            },
            "an approved difference never converts a stage fault into a passing case");

        Check(
            "scoped-difference-row-must-stand-alone",
            () =>
            {
                var fixtures = new JsonObject
                {
                    ["schemaVersion"] = SchemaVersion,
                    ["cases"] = new JsonArray(new JsonObject
                    {
                        ["id"] = "self-scoped-catalog",
                        ["expectedDifferenceIds"] = new JsonArray("ED-SCOPE", "ED-OTHER"),
                    }),
                };
                var differences = new JsonObject
                {
                    ["schemaVersion"] = SchemaVersion,
                    ["differences"] = new JsonArray(
                        ScopedRow("self-scoped-catalog", ["decision"], "allow"),
                        new JsonObject
                        {
                            ["id"] = "ED-OTHER",
                            ["agreementClass"] = "expected-difference",
                            ["cases"] = new JsonArray("self-scoped-catalog"),
                            ["upstreamObservation"] = "self-test",
                            ["d3Observation"] = "self-test",
                            ["failClosedEffect"] = "self-test",
                            ["caseObservations"] = new JsonObject
                            {
                                ["self-scoped-catalog"] = new JsonObject
                                {
                                    ["upstream"] = new JsonObject { ["decision"] = "allow" },
                                    ["d3"] = new JsonObject { ["decision"] = "deny" },
                                },
                            },
                        }),
                };
                var errors = ValidateCatalog(fixtures, differences);
                return errors.Any(error => error.Contains("must claim no other approved difference", StringComparison.Ordinal));
            },
            "one owner-approved ceiling may not absorb an unrelated divergence");

        Check(
            "authorized-difference-scope-rejects-an-unknown-field",
            () =>
            {
                var fixtures = new JsonObject
                {
                    ["schemaVersion"] = SchemaVersion,
                    ["cases"] = new JsonArray(new JsonObject
                    {
                        ["id"] = "self-scoped-unknown",
                        ["expectedDifferenceIds"] = new JsonArray("ED-SCOPE"),
                    }),
                };
                var differences = new JsonObject
                {
                    ["schemaVersion"] = SchemaVersion,
                    ["differences"] = new JsonArray(
                        ScopedRow("self-scoped-unknown", ["decision", "notAField"], "allow")),
                };
                var errors = ValidateCatalog(fixtures, differences);
                return errors.Any(error => error.Contains("authorizes unknown comparison field", StringComparison.Ordinal));
            },
            "an authorized scope may only name real comparison fields");

        Check(
            "intended-stage-mismatch-fails",
            () =>
            {
                var report = Run(
                    Case("self-stage", "self", allowSource, "operation-failure"),
                    Oracle("lowering", true, null, "lower: boom"));
                return report["verdict"]!.GetValue<string>() == "fail"
                    && report["differenceValidation"]!["intendedStageObserved"]!.GetValue<string>() == "lowering";
            },
            "a case that never reaches its declared stage fails");

        var duplicateHistoryCase = new JsonObject
        {
            ["id"] = "self-dual-stage",
            ["kind"] = "differential",
            ["control"] = "self-dual-stage",
            ["families"] = new JsonArray("C-14"),
            ["question"] = "self-test",
            ["schema"] = "temporal",
            ["source"] = "permit(principal, action == Test::Action::\"Sell\", resource) "
                + "when temporal { formerly within 1h Test::Action::\"Approve\"::request { input.stock: context.input.stock } };",
            ["expectedClassification"] = "operation-failure",
            ["expectedDifferenceIds"] = new JsonArray(),
            ["intendedStage"] = "trusted-binding",
            ["trustedRuntime"] = true,
            ["event"] = new JsonObject
            {
                ["timestamp"] = 100,
                ["actionNamespace"] = new JsonArray("Test", "Action"),
                ["actionId"] = "Sell",
                ["principal"] = new JsonObject { ["type"] = "Test::User", ["id"] = "alice" },
                ["resource"] = new JsonObject { ["type"] = "Test::Item", ["id"] = "item-1" },
                ["entities"] = new JsonArray(),
                ["context"] = new JsonObject { ["input"] = new JsonObject { ["stock"] = "AMZN" } },
            },
            ["history"] = new JsonArray(
                HistoryEvent("dup", 10),
                HistoryEvent("dup", 20)),
        };

        Check(
            "dual-failure-stage-difference-detected",
            () =>
            {
                var report = Run(
                    duplicateHistoryCase,
                    Oracle("request-validation", true, null, "request: boom"));
                var fields = report["observedDifferenceFields"]!.AsArray()
                    .Select(node => node!["field"]!.GetValue<string>())
                    .ToHashSet(StringComparer.Ordinal);
                return report["verdict"]!.GetValue<string>() == "fail"
                    && fields.Contains("stage");
            },
            "a stage difference on dual failure is not normalized away");

        Check(
            "dual-failure-category-difference-detected",
            () =>
            {
                var report = Run(
                    duplicateHistoryCase,
                    Oracle("trusted-binding", true, null, "resolver failed", "provider-evaluation-error"));
                var fields = report["observedDifferenceFields"]!.AsArray()
                    .Select(node => node!["field"]!.GetValue<string>())
                    .ToHashSet(StringComparer.Ordinal);
                return report["verdict"]!.GetValue<string>() == "fail"
                    && fields.Contains("errorCategories");
            },
            "an error-category difference on dual failure is not normalized away");

        Check(
            "vacuous-agreement-rejected",
            () =>
            {
                var upstream = NormalizeUpstream(Oracle("evaluation", false, null, null));
                var managed = new JsonObject { ["decision"] = null, ["operationFailed"] = false };
                return IsVacuousAgreement(upstream, managed);
            },
            "two null decisions with no failure are not an agreement");

        Check(
            "operation-failure-only-family-coverage-rejected",
            () =>
            {
                var coverage = new Dictionary<string, FamilyCoverage>(StringComparer.Ordinal);
                var family = new FamilyCoverage();
                family.Add("direct-allow", "operation-failure", new JsonObject { ["passed"] = true }, null);
                coverage["C-01"] = family;
                var fixtures = new JsonObject
                {
                    ["familyRequirements"] = new JsonObject
                    {
                        ["C-01"] = new JsonObject
                        {
                            ["requiredControls"] = new JsonArray("direct-allow"),
                            ["decisionCoverage"] = "required",
                        },
                    },
                };
                return ValidateFamilyCoverage(fixtures, coverage).Count != 0;
            },
            "an operation-failure case cannot satisfy a decision-bearing family");

        Check(
            "decision-bearing-family-coverage-accepted",
            () =>
            {
                var coverage = new Dictionary<string, FamilyCoverage>(StringComparer.Ordinal);
                var family = new FamilyCoverage();
                family.Add("direct-allow", "agreement", new JsonObject { ["decision"] = "allow" }, null);
                coverage["C-01"] = family;
                var fixtures = new JsonObject
                {
                    ["familyRequirements"] = new JsonObject
                    {
                        ["C-01"] = new JsonObject
                        {
                            ["requiredControls"] = new JsonArray("direct-allow"),
                            ["decisionCoverage"] = "required",
                        },
                    },
                };
                return ValidateFamilyCoverage(fixtures, coverage).Count == 0;
            },
            "a decision-bearing agreement satisfies decision coverage");

        // QD3Q-07 negative control: a required positive role that produced a
        // clean Deny on both sides must not satisfy the family. The R4
        // validator accepted exactly that false pass.
        Check(
            "family-role-false-pass-rejected",
            () =>
            {
                var coverage = new Dictionary<string, FamilyCoverage>(StringComparer.Ordinal);
                var family = new FamilyCoverage();
                family.Add(
                    "direct-allow",
                    "agreement",
                    new JsonObject { ["decision"] = "deny", ["operationFailed"] = false },
                    new JsonObject { ["decision"] = "deny", ["operationFailed"] = false });
                coverage["C-01"] = family;
                var fixtures = new JsonObject
                {
                    ["familyRequirements"] = new JsonObject
                    {
                        ["C-01"] = new JsonObject
                        {
                            ["requiredControls"] = new JsonArray("direct-allow"),
                            ["requiredControlDecisions"] = new JsonObject
                            {
                                ["direct-allow"] = "allow",
                            },
                            ["decisionCoverage"] = "required",
                        },
                    },
                };
                var errors = ValidateFamilyCoverage(fixtures, coverage);
                return errors.Any(error => error.Contains("never produced the required `allow` decision", StringComparison.Ordinal));
            },
            "a required Allow role that produced a clean Deny on both sides is rejected");

        Check(
            "family-role-satisfied-accepted",
            () =>
            {
                var coverage = new Dictionary<string, FamilyCoverage>(StringComparer.Ordinal);
                var family = new FamilyCoverage();
                family.Add(
                    "direct-allow",
                    "agreement",
                    new JsonObject { ["decision"] = "allow", ["operationFailed"] = false },
                    new JsonObject { ["decision"] = "allow", ["operationFailed"] = false });
                coverage["C-01"] = family;
                var fixtures = new JsonObject
                {
                    ["familyRequirements"] = new JsonObject
                    {
                        ["C-01"] = new JsonObject
                        {
                            ["requiredControls"] = new JsonArray("direct-allow"),
                            ["requiredControlDecisions"] = new JsonObject
                            {
                                ["direct-allow"] = "allow",
                            },
                            ["decisionCoverage"] = "required",
                        },
                    },
                };
                return ValidateFamilyCoverage(fixtures, coverage).Count == 0;
            },
            "a required Allow role that produced an Allow on both sides is accepted");

        Check(
            "family-role-single-sided-allow-rejected",
            () =>
            {
                var coverage = new Dictionary<string, FamilyCoverage>(StringComparer.Ordinal);
                var family = new FamilyCoverage();
                family.Add(
                    "direct-allow",
                    "agreement",
                    new JsonObject { ["decision"] = "allow", ["operationFailed"] = false },
                    new JsonObject { ["decision"] = "deny", ["operationFailed"] = false });
                coverage["C-01"] = family;
                var fixtures = new JsonObject
                {
                    ["familyRequirements"] = new JsonObject
                    {
                        ["C-01"] = new JsonObject
                        {
                            ["requiredControls"] = new JsonArray("direct-allow"),
                            ["requiredControlDecisions"] = new JsonObject
                            {
                                ["direct-allow"] = "allow",
                            },
                            ["decisionCoverage"] = "required",
                        },
                    },
                };
                return ValidateFamilyCoverage(fixtures, coverage).Count != 0;
            },
            "a required Allow role produced by one side only does not satisfy the family");

        var failed = checks.Count(node => node!["passed"]!.GetValue<bool>() == false);
        var document = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["commission"] = Commission,
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("o"),
            ["summary"] = new JsonObject
            {
                ["total"] = checks.Count,
                ["passed"] = checks.Count - failed,
                ["failed"] = failed,
            },
            ["checks"] = checks,
        };
        File.WriteAllText(
            outputPath,
            document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine($"R5-R1 harness self-test: {checks.Count - failed}/{checks.Count} passed -> {outputPath}");
        return failed == 0 ? 0 : 1;
    }

    private static JsonObject HistoryEvent(string identity, long timestamp)
    {
        return new JsonObject
        {
            ["identity"] = identity,
            ["timestamp"] = timestamp,
            ["actionNamespace"] = new JsonArray("Test", "Action"),
            ["actionId"] = "Approve",
            ["kind"] = "request",
            ["principal"] = new JsonObject { ["type"] = "Test::User", ["id"] = "alice" },
            ["resource"] = new JsonObject { ["type"] = "Test::Item", ["id"] = "item-1" },
            ["context"] = new JsonObject { ["input"] = new JsonObject { ["stock"] = "AMZN" } },
            ["logged"] = new JsonObject { ["input"] = new JsonObject { ["stock"] = "AMZN" } },
            ["entities"] = new JsonArray(),
        };
    }

    private static int RunLifecycleProbe(string[] args)
    {
        if (args.Length < 2)
        {
            throw new InvalidOperationException("lifecycle-probe mode requires one scenario");
        }
        var scenario = args[1];
        var casePath = args.Length > 2 ? Path.GetFullPath(args[2]) : null;
        var outputPath = args.Length > 3 ? Path.GetFullPath(args[3]) : null;

        JsonObject result;
        if (scenario == "resident")
        {
            var input = ReadLifecycleInput(casePath);
            result = RunResidentScenario(
                input["case"]!.AsObject(),
                ReadStringMap(input["schemas"]?.AsObject(), "schemas"),
                ReadStringMap(input["providerDeclarations"]?.AsObject(), "providerDeclarations"));
        }
        else
        {
            result = RunDestructiveScenario(scenario, casePath);
        }

        var json = result.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (outputPath is not null)
        {
            File.WriteAllText(outputPath, json + Environment.NewLine);
        }
        Console.WriteLine(json);
        return result["passed"]?.GetValue<bool>() == true ? 0 : 1;
    }

    private static JsonObject ReadLifecycleInput(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            throw new InvalidOperationException("lifecycle-probe requires an input document");
        }
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("lifecycle input is not an object");
    }

    /// <summary>
    /// Runs one destructive native scenario inside this (already isolated)
    /// process. The staged asset layout is inspected directly, the native
    /// identity gate is exercised for real, and no authorization is attempted
    /// once the gate fails. Every observation is computed here, not supplied.
    /// </summary>
    private static JsonObject RunDestructiveScenario(string scenario, string? casePath)
    {
        var result = new JsonObject
        {
            ["scenario"] = scenario,
            ["passed"] = false,
            ["operationFailed"] = false,
            ["failureBoundary"] = null,
            ["decision"] = null,
            ["stage"] = null,
            ["error"] = null,
            ["checks"] = new JsonObject(),
            ["baseDirectory"] = AppContext.BaseDirectory,
        };
        var checks = result["checks"]!.AsObject();
        var (rid, target, file) = NativeBridge.Platform();
        var adjacentDll = Path.Combine(AppContext.BaseDirectory, file);
        var adjacentManifest = Path.Combine(AppContext.BaseDirectory, "dogwoodsharp-native.json");
        var nestedDirectory = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native");
        var nestedDll = Path.Combine(nestedDirectory, file);
        var nestedManifest = Path.Combine(nestedDirectory, "dogwoodsharp-native.json");
        var wrongRidDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            rid == "win-x64" ? "win-arm64" : "win-x64",
            "native");

        try
        {
            switch (scenario)
            {
                case "missing-asset":
                    checks["adjacentDllAbsent"] = !File.Exists(adjacentDll);
                    checks["nestedDllAbsent"] = !File.Exists(nestedDll);
                    checks["adjacentManifestAbsent"] = !File.Exists(adjacentManifest);
                    checks["nestedManifestAbsent"] = !File.Exists(nestedManifest);
                    break;
                case "mismatched-asset":
                {
                    var manifest = JsonNode.Parse(File.ReadAllText(
                        File.Exists(nestedManifest) ? nestedManifest : adjacentManifest))!.AsObject();
                    var declared = manifest["sha256"]!.GetValue<string>();
                    var image = File.Exists(nestedDll) ? nestedDll : adjacentDll;
                    var actual = Sha256File(image);
                    checks["declaredSha256"] = declared;
                    checks["actualSha256"] = actual;
                    checks["manifestHashMatchesImage"] = string.Equals(declared, actual, StringComparison.OrdinalIgnoreCase);
                    break;
                }
                case "wrong-rid":
                    checks["actualRidDllAbsent"] = !File.Exists(adjacentDll) && !File.Exists(nestedDll);
                    checks["wrongRidDllPresent"] = File.Exists(Path.Combine(wrongRidDirectory, file));
                    checks["wrongRidManifestPresent"] =
                        File.Exists(Path.Combine(wrongRidDirectory, "dogwoodsharp-native.json"));
                    break;
                case "incompatible-abi":
                {
                    var manifestPath = File.Exists(nestedManifest) ? nestedManifest : adjacentManifest;
                    var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
                    checks["declaredAbiVersion"] = manifest["abiVersion"]?.DeepClone();
                    checks["declaredAbiVersionIsTwo"] = manifest["abiVersion"]?.GetValue<uint>() == 2;
                    break;
                }
                default:
                    throw new InvalidOperationException($"unknown lifecycle scenario `{scenario}`");
            }

            var version = Dogwood.GetNativeVersion();
            result["nativeIdentity"] = new JsonObject
            {
                ["abiVersion"] = version.AbiVersion,
                ["bridgeVersion"] = version.BridgeVersion,
                ["dogwoodRevision"] = version.DogwoodRevision,
                ["target"] = version.Target,
            };
            result["failureBoundary"] = "none";
            checks["nativeIdentityGateRejected"] = false;
            checks["noAuthorizationAttemptedAfterGate"] = false;
        }
        catch (Exception exception)
        {
            result["operationFailed"] = true;
            result["failureBoundary"] = "native-load-or-identity";
            result["error"] = $"{exception.GetType().Name}: {exception.Message}";
            checks["nativeIdentityGateRejected"] = true;
            // The gate failed before dispatch: no decision may exist.
            checks["noAuthorizationAttemptedAfterGate"] = IsNullOrNullNode(result["decision"]);
            if (exception is DogwoodBridgeException bridge)
            {
                result["bridgeFailure"] = bridge.Failure.ToString();
            }
        }

        var expected = scenario switch
        {
            "missing-asset" => checks["adjacentDllAbsent"]!.GetValue<bool>()
                && checks["nestedDllAbsent"]!.GetValue<bool>()
                && checks["adjacentManifestAbsent"]!.GetValue<bool>()
                && checks["nestedManifestAbsent"]!.GetValue<bool>(),
            "mismatched-asset" => !checks["manifestHashMatchesImage"]!.GetValue<bool>(),
            "wrong-rid" => checks["actualRidDllAbsent"]!.GetValue<bool>()
                && checks["wrongRidDllPresent"]!.GetValue<bool>()
                && checks["wrongRidManifestPresent"]!.GetValue<bool>(),
            "incompatible-abi" => checks["declaredAbiVersionIsTwo"]!.GetValue<bool>(),
            _ => false,
        };
        result["passed"] = expected
            && (result["operationFailed"]?.GetValue<bool>() ?? false)
            && IsNullOrNullNode(result["decision"])
            && (checks["nativeIdentityGateRejected"]?.GetValue<bool>() ?? false);
        return result;
    }

    /// <summary>
    /// Executes the resident lifecycle control in the current process: the
    /// verified image is loaded once, its module identity is observed, and
    /// repeated authorizations must keep the same identity and result. No
    /// unloading or hot replacement is performed or permitted.
    /// </summary>
    private static JsonObject RunResidentScenario(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas,
        IReadOnlyDictionary<string, string> providerDeclarations)
    {
        var result = new JsonObject
        {
            ["scenario"] = "resident",
            ["passed"] = false,
            ["operationFailed"] = false,
            ["decision"] = null,
            ["iterations"] = 0,
            ["checks"] = new JsonObject(),
        };
        var checks = result["checks"]!.AsObject();
        var (rid, _, file) = NativeBridge.Platform();
        var resolved = NativeBridge.ResolveNativePath(AppContext.BaseDirectory, rid, file);
        result["resolvedPath"] = resolved;
        result["resolvedPathSha256"] = Sha256File(resolved);
        var first = Dogwood.GetNativeVersion();
        result["nativeIdentity"] = new JsonObject
        {
            ["abiVersion"] = first.AbiVersion,
            ["bridgeVersion"] = first.BridgeVersion,
            ["dogwoodRevision"] = first.DogwoodRevision,
            ["target"] = first.Target,
        };
        var module = Process.GetCurrentProcess().Modules
            .Cast<ProcessModule>()
            .FirstOrDefault(candidate => string.Equals(
                Path.GetFullPath(candidate.FileName), Path.GetFullPath(resolved), StringComparison.OrdinalIgnoreCase));
        // .NET's Process.Modules does not always enumerate a dlopen'd dylib on
        // macOS. Resolve the live image through dladdr on the exported ABI
        // symbol instead; this still proves which image the process is running.
        var modulePath = module?.FileName
            ?? (OperatingSystem.IsMacOS() ? MacOsLoadedModulePath(resolved) : null);
        checks["loadedModuleMatchesVerifiedAsset"] = modulePath is not null
            && string.Equals(Path.GetFullPath(modulePath), Path.GetFullPath(resolved),
                StringComparison.OrdinalIgnoreCase);
        result["modulePath"] = modulePath;

        var decisions = new HashSet<string>(StringComparer.Ordinal);
        var identityStable = true;
        const int iterations = 25;
        for (var index = 0; index < iterations; index++)
        {
            var version = Dogwood.GetNativeVersion();
            identityStable &= version.AbiVersion == first.AbiVersion
                && version.BridgeVersion == first.BridgeVersion
                && version.DogwoodRevision == first.DogwoodRevision
                && version.Target == first.Target;
            var managed = ExecuteManagedCase(caseElement, schemas, providerDeclarations);
            if (managed["operationFailed"]?.GetValue<bool>() == true)
            {
                result["operationFailed"] = true;
                result["error"] = managed["error"]?.DeepClone();
                break;
            }
            var decision = managed["decision"]?.GetValue<string>();
            if (decision is not null)
            {
                decisions.Add(decision);
            }
        }
        result["iterations"] = iterations;
        result["decisions"] = new JsonArray(decisions.Select(value => (JsonNode?)value).ToArray());
        result["decision"] = decisions.Count == 1 ? decisions.First() : null;
        checks["identityStable"] = identityStable;
        checks["singleDecisionAcrossIterations"] = decisions.Count == 1;
        checks["noOperationFailure"] = result["operationFailed"]?.GetValue<bool>() == false;
        result["passed"] = identityStable
            && decisions.Count == 1
            && result["operationFailed"]?.GetValue<bool>() == false
            && (checks["loadedModuleMatchesVerifiedAsset"]?.GetValue<bool>() ?? false);
        return result;
    }

    private static IReadOnlyDictionary<string, JsonObject> ExecuteLifecycleScenarios(
        IReadOnlyList<JsonObject> cases,
        IReadOnlyDictionary<string, string> schemas,
        IReadOnlyDictionary<string, string> providerDeclarations,
        string workRoot,
        string outputPath)
    {
        Directory.CreateDirectory(workRoot);
        var results = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var documentCases = new JsonArray();
        foreach (var caseElement in cases.Where(item => item["kind"]?.GetValue<string>() == "lifecycle-control"))
        {
            var id = RequiredString(caseElement, "id");
            var scenario = RequiredString(caseElement, "scenario");
            var result = scenario == "resident"
                ? RunResidentScenario(caseElement, schemas, providerDeclarations)
                : ExecuteIsolatedScenario(caseElement, scenario, schemas, providerDeclarations, workRoot);
            result["id"] = id;
            results[id] = result;
            documentCases.Add(result.DeepClone());
        }

        var document = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["commission"] = Commission,
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("o"),
            ["isolation"] = "each destructive scenario runs in a child process against a private staged copy of the probe host directory",
            ["workRoot"] = workRoot,
            ["cases"] = documentCases,
        };
        File.WriteAllText(
            outputPath,
            document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return results;
    }

    /// <summary>
    /// Stages a private copy of this probe's host directory, applies exactly one
    /// native-asset fault, then executes the lifecycle probe in a child process
    /// rooted at that copy. The parent independently verifies the child's
    /// fail-closed observation before accepting the control.
    /// </summary>
    private static JsonObject ExecuteIsolatedScenario(
        JsonObject caseElement,
        string scenario,
        IReadOnlyDictionary<string, string> schemas,
        IReadOnlyDictionary<string, string> providerDeclarations,
        string workRoot)
    {
        var stage = Path.Combine(workRoot, $"{scenario}-{Guid.NewGuid():N}");
        var result = new JsonObject
        {
            ["scenario"] = scenario,
            ["passed"] = false,
            ["stageDirectory"] = stage,
        };
        try
        {
            CopyDirectory(AppContext.BaseDirectory, stage);
            var (rid, _, file) = NativeBridge.Platform();
            var adjacentDll = Path.Combine(stage, file);
            var adjacentManifest = Path.Combine(stage, "dogwoodsharp-native.json");
            var nestedDirectory = Path.Combine(stage, "runtimes", rid, "native");
            var nestedDll = Path.Combine(nestedDirectory, file);
            var nestedManifest = Path.Combine(nestedDirectory, "dogwoodsharp-native.json");
            var wrongRidDirectory = Path.Combine(
                stage,
                "runtimes",
                rid == "win-x64" ? "win-arm64" : "win-x64",
                "native");

            switch (scenario)
            {
                case "missing-asset":
                    DeleteIfExists(adjacentDll);
                    DeleteIfExists(adjacentManifest);
                    DeleteIfExists(nestedDll);
                    DeleteIfExists(nestedManifest);
                    break;
                case "mismatched-asset":
                    foreach (var image in new[] { adjacentDll, nestedDll })
                    {
                        if (File.Exists(image))
                        {
                            File.AppendAllText(image, "r4-mismatched-asset");
                        }
                    }
                    break;
                case "wrong-rid":
                    Directory.CreateDirectory(wrongRidDirectory);
                    foreach (var asset in new[] { adjacentDll, adjacentManifest, nestedDll, nestedManifest })
                    {
                        if (File.Exists(asset))
                        {
                            File.Copy(asset, Path.Combine(wrongRidDirectory, Path.GetFileName(asset)), overwrite: true);
                        }
                    }
                    DeleteIfExists(adjacentDll);
                    DeleteIfExists(adjacentManifest);
                    DeleteIfExists(nestedDll);
                    DeleteIfExists(nestedManifest);
                    break;
                case "incompatible-abi":
                    foreach (var manifest in new[] { adjacentManifest, nestedManifest })
                    {
                        if (!File.Exists(manifest))
                        {
                            continue;
                        }
                        var json = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
                        json["abiVersion"] = 2;
                        File.WriteAllText(
                            manifest,
                            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"unknown lifecycle scenario `{scenario}`");
            }

            var inputPath = Path.Combine(stage, "lifecycle-case.json");
            var input = new JsonObject
            {
                ["case"] = caseElement.DeepClone(),
                ["schemas"] = new JsonObject(schemas.Select(pair =>
                    KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value))),
                ["providerDeclarations"] = new JsonObject(providerDeclarations.Select(pair =>
                    KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value))),
            };
            File.WriteAllText(inputPath, input.ToJsonString());
            var childResultPath = Path.Combine(stage, "lifecycle-result.json");
            var executable = Path.Combine(stage, "PrepProbe.exe");
            var (exitCode, output, error) = File.Exists(executable)
                ? RunProcess(
                    executable,
                    new[] { "lifecycle-probe", scenario, inputPath, childResultPath },
                    input: null,
                    workingDirectory: stage)
                : RunProcess(
                    "dotnet",
                    new[] { Path.Combine(stage, "PrepProbe.dll"), "lifecycle-probe", scenario, inputPath, childResultPath },
                    input: null,
                    workingDirectory: stage);
            result["childExitCode"] = exitCode;
            result["childStderr"] = error;
            result["childStdout"] = output;
            if (!File.Exists(childResultPath))
            {
                result["error"] = "the isolated lifecycle child produced no observation file";
                return result;
            }
            var child = JsonNode.Parse(File.ReadAllText(childResultPath))!.AsObject();
            foreach (var property in child)
            {
                result[property.Key] = property.Value?.DeepClone();
            }
            result["stageDirectory"] = stage;
            var parentChecks = new JsonObject
            {
                ["childExitCodeZero"] = exitCode == 0,
                ["childReportedPass"] = child["passed"]?.GetValue<bool>() ?? false,
                ["operationFailed"] = child["operationFailed"]?.GetValue<bool>() ?? false,
                ["decisionNull"] = IsNullOrNullNode(child["decision"]),
                ["failureBoundaryIsNativeGate"] =
                    child["failureBoundary"]?.GetValue<string>() == "native-load-or-identity",
            };
            result["parentChecks"] = parentChecks;
            result["passed"] = parentChecks["childExitCodeZero"]!.GetValue<bool>()
                && parentChecks["childReportedPass"]!.GetValue<bool>()
                && parentChecks["operationFailed"]!.GetValue<bool>()
                && parentChecks["decisionNull"]!.GetValue<bool>()
                && parentChecks["failureBoundaryIsNativeGate"]!.GetValue<bool>();
            return result;
        }
        finally
        {
            var removed = false;
            try
            {
                if (Directory.Exists(stage))
                {
                    Directory.Delete(stage, recursive: true);
                    removed = true;
                }
            }
            catch (IOException)
            {
                removed = false;
            }
            result["stageRemoved"] = removed;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DlInfo
    {
        public IntPtr FileName;
        public IntPtr BaseAddress;
        public IntPtr SymbolName;
        public IntPtr SymbolAddress;
    }

    [DllImport("libSystem.B.dylib", EntryPoint = "dladdr", SetLastError = false)]
    private static extern int dladdr(IntPtr address, out DlInfo info);

    /// <summary>
    /// Resolves the image path of the live native library from the address of
    /// one of its exported symbols. Used only on macOS, where Process.Modules
    /// does not reliably list a dlopen'd dylib.
    /// </summary>
    private static string? MacOsLoadedModulePath(string resolved)
    {
        try
        {
            var handle = NativeLibrary.Load(resolved);
            try
            {
                var symbol = NativeLibrary.GetExport(handle, "dogwoodsharp_abi_version");
                return dladdr(symbol, out var info) != 0 && info.FileName != IntPtr.Zero
                    ? Marshal.PtrToStringUTF8(info.FileName)
                    : null;
            }
            finally
            {
                // Release only this probe reference; the production singleton
                // retains the library for the process lifetime.
                NativeLibrary.Free(handle);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            BadImageFormatException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static JsonObject DescribeNativeBridge()
    {
        try
        {
            var version = Dogwood.GetNativeVersion();
            return new JsonObject
            {
                ["loaded"] = true,
                ["abiVersion"] = version.AbiVersion,
                ["bridgeVersion"] = version.BridgeVersion,
                ["dogwoodRevision"] = version.DogwoodRevision,
                ["dogwoodVersion"] = version.DogwoodVersion,
                ["cedarVersion"] = version.CedarVersion,
                ["rustVersion"] = version.RustVersion,
                ["target"] = version.Target,
            };
        }
        catch (Exception exception)
        {
            return new JsonObject
            {
                ["loaded"] = false,
                ["error"] = $"{exception.GetType().Name}: {exception.Message}",
            };
        }
    }

    private static string MapManagedStage(CedarAuthorizationStage stage)
    {
        return stage switch
        {
            CedarAuthorizationStage.RequestConstruction => "request-validation",
            CedarAuthorizationStage.SchemaOrRequestValidation => "request-validation",
            CedarAuthorizationStage.Evaluation => "evaluation",
            CedarAuthorizationStage.NativeTransport => "native-transport",
            CedarAuthorizationStage.UnknownPolicyIdentity => "evaluation",
            CedarAuthorizationStage.TrustedBindingUnavailable => "trusted-binding",
            _ => "evaluation",
        };
    }

    private static string NormalizeCategory(CedarDiagnosticCategory category)
    {
        return category switch
        {
            CedarDiagnosticCategory.EntityConformance => "entity-conformance",
            CedarDiagnosticCategory.MissingAttribute => "missing-attribute",
            CedarDiagnosticCategory.ScopeMismatch => "scope-mismatch",
            CedarDiagnosticCategory.UndeclaredAction => "undeclared-action",
            CedarDiagnosticCategory.MissingEntity => "missing-entity",
            CedarDiagnosticCategory.PolicyEvaluationError => "policy-evaluation-error",
            CedarDiagnosticCategory.OperationError => "operation-error",
            CedarDiagnosticCategory.UnknownPolicyId => "unknown-policy-id",
            CedarDiagnosticCategory.ReservedContextField => "reserved-context-field",
            CedarDiagnosticCategory.TrustedBindingUnavailable => "trusted-binding-unavailable",
            _ => "operation-error",
        };
    }

    private static void CopyOptional(JsonObject source, JsonObject destination, string name)
    {
        if (source.TryGetPropertyValue(name, out var value) && value is not null)
        {
            destination[name] = value.DeepClone();
        }
    }

    private static (string type, string id) ReadEntityRef(JsonObject? value, string name)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"{name} is missing");
        }
        return (RequiredString(value, "type"), RequiredString(value, "id"));
    }

    private static string RawOrSerialized(
        JsonObject source,
        string structuredName,
        string? raw,
        bool emptyObject = false)
    {
        if (raw is not null)
        {
            return raw;
        }
        if (source.TryGetPropertyValue(structuredName, out var value))
        {
            return value!.ToJsonString();
        }
        return emptyObject ? "{}" : "{}";
    }

    private static string RequiredString(JsonObject value, string name)
    {
        if (!value.TryGetPropertyValue(name, out var property))
        {
            throw new InvalidOperationException($"missing string property `{name}`");
        }
        if (property is null || property.GetValueKind() != JsonValueKind.String)
        {
            throw new InvalidOperationException($"missing string property `{name}`");
        }
        return property.GetValue<string>();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonArray? values, string name)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }
        return values.Select(node => node?.GetValue<string>()
            ?? throw new InvalidOperationException($"{name} contains a non-string value")).ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadStringMap(JsonObject? values, string name)
    {
        if (values is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        return values.ToDictionary(
            property => property.Key,
            property => property.Value?.GetValue<string>()
                ?? throw new InvalidOperationException($"{name}.{property.Key} is not a string"),
            StringComparer.Ordinal);
    }

    private static string Sha256(string text)
    {
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }

    private sealed class FamilyCoverage
    {
        public Dictionary<string, string> Controls { get; } = new(StringComparer.Ordinal);

        /// <summary>Decisions actually observed in this family (either side).</summary>
        public HashSet<string> Decisions { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The per-control `(upstream, managed)` decision pairs observed by the
        /// cases of this family. A pair is recorded only when both sides
        /// produced a decision, so a pair is an observation both engines agree
        /// on the *shape* of, and the family validator can require the exact
        /// decision a control role is supposed to produce (QD3Q-07).
        /// </summary>
        public Dictionary<string, List<(string Upstream, string Managed)>> ControlDecisionPairs { get; }
            = new(StringComparer.Ordinal);

        public int CaseCount { get; private set; }

        public bool HasDecisionBearingClassification { get; private set; }

        public bool HasManagedControl { get; private set; }

        public void Add(
            string control,
            string classification,
            JsonObject? managed,
            JsonObject? upstream)
        {
            Controls[control] = classification;
            CaseCount++;
            HasDecisionBearingClassification |= classification is "agreement" or "approved-difference"
                or "fail-closed-equivalence";
            HasManagedControl |= managed?["passed"] is JsonNode;
            var upstreamDecision = upstream?["decision"]?.GetValue<string>();
            var managedDecision = managed?["decision"]?.GetValue<string>();
            if (upstreamDecision is not null && managedDecision is not null)
            {
                if (!ControlDecisionPairs.TryGetValue(control, out var pairs))
                {
                    pairs = new List<(string, string)>();
                    ControlDecisionPairs.Add(control, pairs);
                }
                pairs.Add((upstreamDecision, managedDecision));
            }
            foreach (var decision in new[] { managedDecision, upstreamDecision })
            {
                if (decision is not null)
                {
                    Decisions.Add(decision);
                }
            }
        }
    }
}
