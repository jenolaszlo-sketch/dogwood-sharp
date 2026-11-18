# D1 managed API

```csharp
using DogwoodSharp;

DogwoodNativeVersion version = Dogwood.GetNativeVersion();
PolicyParseResult result = Dogwood.Parse("permit(principal, action, resource);");
```

Use .NET 8 or .NET 10 and stage the native artifact beside the application using
the [manifest layout](architecture/native-artifacts.md). The API is synchronous
and supports independent concurrent calls. Both results are immutable
snapshots. The version reports ABI, bridge, Dogwood revision and version, Cedar,
Rust and native target.

`Dogwood.Parse(string source)` accepts Unicode source up to
`Dogwood.MaximumSourceBytes` (1 MiB when encoded as UTF-8). A successful result
has `Success == true`, a nonnegative `PolicyCount` and empty diagnostics. A
failed result has `Success == false`, `PolicyCount == null` and an immutable
array of syntax or macro diagnostics in upstream order. Diagnostic offsets and
lengths count UTF-8 bytes, with the [ABI EOF convention](architecture/native-interop.md#operations).
Message text is upstream-dependent and may contain policy text. A successful
parse establishes that Dogwood accepted the source for parsing and macro
expansion. Schema validation and authorization are beyond D1.

Null source raises `ArgumentNullException`. Unpaired surrogates or oversized
source raise `ArgumentException`. `DogwoodBridgeException.Failure` identifies
asset or identity failure, nonzero native call status, and malformed responses.
`NativeStatus` carries the raw status for a failed native call. The process-wide
loader caches its initial load result, including any initial failure. Restart
the process after correcting an asset deployment.

The wrapper uses explicit JSON handling and unmanaged function pointers. The
verification script runs a NativeAOT consumer for each target framework.

# D2 managed API

```csharp
using DogwoodSharp;

var schema = new DogwoodSchema("namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { count: Long } }; }");

DogwoodValidationResult validated = Dogwood.Validate(
    "permit (principal, action == App::Action::\"read\", resource) when { context.count > 3 };", schema);
DogwoodLoweredPolicySet lowered = Dogwood.Lower(
    "permit (principal, action == App::Action::\"read\", resource) when { context.count > 3 };", schema);
```

Operations 2 and 3 of the [native ABI](architecture/native-interop.md#d2-operations),
added without changing operations 0 and 1. The native artifact must carry them:
an older asset answers with status 3, which surfaces as a `DogwoodBridgeException`
with `NativeStatus == 3`.

`DogwoodSchema` bundles the Cedar `.cedarschema` action schema with the optional
service-schema halves. `EventSchema` and `Macros` default to the pinned upstream
defaults. `Distincter` namespaces synthesized Cedar policy ids and hoisted
`context` field names, so lowering several sets separately and combining them
cannot collide; it is accepted by `Lower` only.

`DogwoodSchema.MaximumSchemaBytes` (1 MiB) bounds each schema text field, and a
distincter must be a Cedar identifier. Violations raise `ArgumentException`
before any native call.

## Results

`DogwoodValidationResult.IsValid` is true only when parsing, lowering and
validation all succeeded. `Parsed`, `Lowered` and `ValidationPassed` name each
stage, and `ParseDiagnostics`, `ValidationErrors`, `ValidationWarnings`,
`RuleCount`, `SelfContainedCedar`, `Rules` and `StageError` are populated for the
stages that ran. Warnings never fail validation.

`DogwoodLoweredPolicySet.IsLowered` is true only when parsing and lowering both
succeeded. `CedarPolicyText`, `CedarSchemaJson` and `CedarSchemaText` are empty
unless lowering succeeded, so a failed lowering never returns partial
authoritative output. `SelfContainedCedar` reports whether the exported
artifacts reproduce Dogwood semantics with no extra `context`; when false the
hoisted `context.<id>` fields must be computed by temporal/provider evaluation,
which this milestone does not implement.

`RuleIndex` and `CedarPolicyId` trace every lowered rule back to its Dogwood
source position, and `Lower` also returns each rule's macro-expanded
`SourceText`. `PolicyValidationFinding` carries a stable `PolicyValidationCategory`,
an upstream-owned `Message` and a UTF-8 `PolicySourceLocation`. `DogwoodStageError`
names the `DogwoodStage` that stopped and a `DogwoodFailureCategory` reason.

A validated or lowered result is a validated policy artifact. It is not an
authorization decision, and authorization is not implemented at this milestone.

## Verification

`Dogwood.Validate` and `Dogwood.Lower` are compared against the pinned
differential fixtures on every supported runtime identifier and both target
frameworks. See [d2-implementation.md](d2-implementation.md) for results and
[verification](verification.md) for executed evidence.
