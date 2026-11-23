// DOGWOOD-SHARP-D3-IMPL-01-R2 managed differential probe (evidence tooling).
//
// The managed decision path is the production DogwoodCedarAuthorizer. Static
// cases use ForLoweredBundle(...).Authorize(...); temporal/provider cases use
// ForLoweredBundle(...).AuthorizeWithTrustedRuntime(...). The upstream Rust
// oracle remains the authorization oracle: this program compares production
// observations against that oracle and never substitutes a Cedar-to-Cedar run.
//
// Usage:
//   PrepProbe differential <fixtures.json> <oracle.exe> <differences.json>
//       <results.json> [lifecycle-results.json]
//   PrepProbe lifecycle-probe <scenario>
//
// Test tooling only; never a production execution path.

using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CedarSharp;
using DogwoodSharp;
using DogwoodSharp.Experimental.CedarSharp;

namespace R2Probe;

internal static class Program
{
    private const string SchemaVersion = "2";
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
        if (args.Length < 6)
        {
            throw new InvalidOperationException(
                "differential mode requires fixtures, oracle, difference register, output, and optional lifecycle-result paths");
        }

        var fixturesPath = Path.GetFullPath(args[1]);
        var oraclePath = Path.GetFullPath(args[2]);
        var differencesPath = Path.GetFullPath(args[3]);
        var outputPath = Path.GetFullPath(args[4]);
        var lifecyclePath = args.Length > 5 ? Path.GetFullPath(args[5]) : null;

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

        var lifecycleResults = ReadLifecycleResults(lifecyclePath);
        var oracleResults = RunOracleBatch(oraclePath, caseElements, schemas, providerDeclarations);
        var differenceRows = ReadDifferenceRows(differenceRoot);
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
                coverage.Add(control, classification);
            }
        }

        var coverageErrors = ValidateFamilyCoverage(fixtureRoot, familyCoverage);
        var output = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["commission"] = "DOGWOOD-SHARP-D3-IMPL-01-R2",
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
            $"R2 differential: {reports.Count} cases, " +
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
                    managed = ExecuteManagedCase(caseElement, schemas, providerDeclarations);
                    differences = CompareObservations(upstream, managed);
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
            verdict = "pending";
            if (pendingReason is not null)
            {
                differenceValidation["pendingReason"] = pendingReason;
            }
        }
        else if (kind == "differential")
        {
            var observedDifferences = differences ?? new JsonArray();
            // When both sides fail closed (no decision, operation failed), the
            // authoritative comparison is fail-closed agreement, not diagnostic
            // category wording. Skip field comparison so both operation-failure
            // paths normalize to the same classification.
            if (upstream!["operationFailed"]!.GetValue<bool>()
                && managed!["operationFailed"]!.GetValue<bool>()
                && IsNullOrNullNode(upstream!["decision"])
                && IsNullOrNullNode(managed!["decision"]))
            {
                observedDifferences = new JsonArray();
            }
            if (expectedDifferenceIds.Count != 0)
            {
                var errors = ValidateExpectedDifferences(
                    id,
                    expectedDifferenceIds,
                    differenceRows,
                    upstream!,
                    managed!);
                differenceValidation["errors"] = new JsonArray(errors.Select(error => (JsonNode?)error).ToArray());
                classification = errors.Count == 0 && observedDifferences.Count != 0
                    ? "approved-difference"
                    : "unexpected-difference";
            }
            else if (observedDifferences.Count != 0)
            {
                classification = "unexpected-difference";
            }
            else if (upstream!["operationFailed"]!.GetValue<bool>() && managed!["operationFailed"]!.GetValue<bool>())
            {
                classification = "operation-failure";
            }
            else
            {
                classification = "agreement";
            }

            verdict = classification == expectedClassification ? "pass" : "fail";
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
            verdict = classification == expectedClassification ? "pass" : "fail";
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
        var hasErrors = errors.Count != 0;
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
            "identity-duplicate" => IdentityDuplicateControl(caseElement, schemas),
            "identity-unknown" => IdentityUnknownControl(caseElement, schemas),
            "action-entity-conflict" => ActionEntityConflictControl(caseElement, schemas),
            "concurrency" => ConcurrencyControl(caseElement, schemas),
            "lifecycle-repeat" => LifecycleRepeatControl(caseElement, schemas),
            "provider-field-ownership" => ProviderFieldOwnershipControl(caseElement, schemas, providerDeclarations),
            "reserved-temporal-field" => ReservedTemporalFieldControl(caseElement, schemas),
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

    private static JsonObject IdentityDuplicateControl(
        JsonObject caseElement,
        IReadOnlyDictionary<string, string> schemas)
    {
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
        var schemaName = RequiredString(caseElement, "schema");
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

        var upstreamBindings = upstream["trustedBindings"]?.AsObject() ?? new JsonObject();
        var managedBindings = managed["trustedBindings"]?.AsObject() ?? new JsonObject();
        foreach (var binding in new[] { "temporal", "providers" })
        {
            var left = CanonicalValue(upstreamBindings[binding]);
            var right = CanonicalValue(managedBindings[binding]);
            if (!JsonNode.DeepEquals(left, right))
            {
                differences.Add(new JsonObject
                {
                    ["field"] = $"trustedBindings.{binding}",
                    ["upstream"] = left?.DeepClone(),
                    ["managed"] = right?.DeepClone(),
                });
            }
        }
        return differences;
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
        JsonObject managed)
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
            var requiredControls = familyValue.AsArray()
                .Select(node => node!.GetValue<string>())
                .ToHashSet(StringComparer.Ordinal);
            if (!actual.TryGetValue(family.Key, out var coverage))
            {
                errors.Add($"family {family.Key} has no cases");
                continue;
            }
            foreach (var control in requiredControls)
            {
                if (!coverage.Controls.ContainsKey(control))
                {
                    errors.Add($"family {family.Key} has no `{control}` control");
                }
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
        string? input = null)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
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

    private static int RunLifecycleProbe(string[] args)
    {
        if (args.Length != 2)
        {
            throw new InvalidOperationException("lifecycle-probe mode requires one scenario");
        }
        var scenario = args[1];
        var result = new JsonObject
        {
            ["scenario"] = scenario,
            ["passed"] = false,
            ["operationFailed"] = false,
            ["failureBoundary"] = null,
            ["error"] = null,
        };
        try
        {
            var version = Dogwood.GetNativeVersion();
            result["nativeIdentity"] = new JsonObject
            {
                ["abiVersion"] = version.AbiVersion,
                ["bridgeVersion"] = version.BridgeVersion,
                ["dogwoodRevision"] = version.DogwoodRevision,
                ["dogwoodVersion"] = version.DogwoodVersion,
                ["cedarVersion"] = version.CedarVersion,
                ["rustVersion"] = version.RustVersion,
                ["target"] = version.Target,
            };
            result["passed"] = true;
        }
        catch (Exception exception)
        {
            result["operationFailed"] = true;
            result["failureBoundary"] = "native-load-or-identity";
            result["error"] = $"{exception.GetType().Name}: {exception.Message}";
            if (exception is DogwoodBridgeException bridge)
            {
                result["bridgeFailure"] = bridge.Failure.ToString();
            }
        }
        Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static IReadOnlyDictionary<string, JsonObject> ReadLifecycleResults(string? path)
    {
        var results = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (path is null || !File.Exists(path))
        {
            return results;
        }
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("lifecycle result file is not an object");
        foreach (var node in root["cases"]?.AsArray() ?? new JsonArray())
        {
            var item = node?.AsObject() ?? throw new InvalidOperationException("lifecycle case is not an object");
            results.Add(RequiredString(item, "id"), item);
        }
        return results;
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

        public void Add(string control, string classification)
        {
            Controls[control] = classification;
        }
    }
}
