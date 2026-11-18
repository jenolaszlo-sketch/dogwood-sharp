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

Operations 2 and 3 of the [native ABI](architecture/native-interop.md#d2-operations)
use the R1 wire contract. Use matching native and managed components for D2.
A D1-only native asset reports unknown operation (status 3). A pre-R1 D2
reply does not satisfy the R1 managed response checks.

`DogwoodSchema` contains the Cedar action schema and optional event schema,
macro library and distincter. Both operations use the same distincter for the
actual lowering they validate. A distincter namespaces the generated IDs; it
does not waive collision checks against the original action schema.

`Dogwood.MaximumSourceBytes` and `DogwoodSchema.MaximumSchemaBytes` independently
limit decoded UTF-8 text to 1 MiB. Distincters are limited to 256 bytes and use
Cedar identifier syntax. The encoded JSON envelope has its own ceiling of
6,291,520 bytes, including escaping, all fields, and JSON framing.

## Results

`DogwoodValidationResult.IsValid` indicates that the collision check and
validation succeeded. Its immutable snapshot reports `ServiceSchemaBuilt`,
`Parsed`, `ActionSchemaBuilt`, `Lowered`, `CollisionCheckPassed` and
`ValidationPassed`, along with parse diagnostics, validation findings, the
fatal stage error when available, and the colliding field and action when found.
Validation warnings are preserved on successful calls.

`DogwoodLoweredPolicySet.Validation` exposes the same outcome for the exact
lowered set used by that call. `Lowered` describes the intermediate lowering
stage. `ArtifactsExported` and `IsLowered` both indicate availability of the
complete validated export. When export is unavailable, all three Cedar
artifact strings and the rule mapping are empty. `SelfContainedCedar` reports
whether the lowered policy requires generated context values. Temporal policy
exports still require values computed by a temporal engine.

`RuleIndex` and `CedarPolicyId` trace each validated rule to its Dogwood
source position. Export additionally includes the macro-expanded `SourceText`.
Validation findings include a category, an upstream message and a UTF-8
location when available. Authorization is outside the D2 surface.
