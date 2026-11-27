# D1 managed API

```csharp
using DogwoodSharp;

DogwoodNativeVersion version = Dogwood.GetNativeVersion();
PolicyParseResult result = Dogwood.Parse("permit(principal, action, resource);");
```

Use .NET 8 or .NET 10. A package consumer receives the matching native image and
manifest from the stable package; a source-tree build may instead stage the
native artifact using the [manifest layout](architecture/native-artifacts.md).
The API is synchronous and supports independent concurrent calls. Both results
are immutable snapshots. The version reports ABI, bridge, Dogwood revision and
version, Cedar, Rust and native target.

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
`NativeStatus` carries the raw status for a failed native call. The D2-enabled
distribution uses the [process-lifetime loader](#native-library-lifetime) for
D1 and D2 calls.

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
A D1-only native asset reports unknown operation (status 3). The R1 managed
response checks reject replies from pre-R1 D2 assets.

`DogwoodSchema` contains the Cedar action schema and optional event schema,
macro library and distincter. Both operations use the same distincter for the
actual lowering they validate. Both check the generated IDs against the
original action schema even with a caller-supplied distincter.

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

For an exported bundle, the decoder reads the JSON schema as Cedar's map of
namespaces. Cedar 4.11 emits text for each named namespace, including empty
ones. The unnamed namespace emits text when it declares a common type, entity
type, or action. If the JSON structure requires text, the text must contain a
non-whitespace character. Otherwise the text must be the empty string. An
empty policy set can still export a nonempty schema. Contradictions fail with
`DogwoodBridgeFailure.MalformedResponse`. These are structural checks. The
managed API trusts the native bridge to serialize both forms from the same
validated fragment. Cedar round trips and differential conformance verify
semantic equivalence during qualification.

`RuleIndex` and `CedarPolicyId` trace each validated rule to its Dogwood
source position. Export additionally includes the macro-expanded `SourceText`.
Validation findings include a category and an upstream message. Error
locations, when supplied, are UTF-8 byte ranges. D2 projects warning locations
as null. Pinned Cedar warnings can expose diagnostic labels. Mapping them
requires checking source provenance and rebasing byte offsets to the authored
Dogwood source. Authorization is outside the D2 surface.

## Native library lifetime

In the supported JIT host, load DogwoodSharp in the default
`AssemblyLoadContext`. A plugin can call a copy shared by the host. Private
copies in custom contexts fail with `LoadOrIdentity` before acquiring a
native reference, including copies in collectible contexts. NativeAOT
executables use the same lazy initialization and residency policy.

The first operation verifies the asset and native identity. Successful
initialization retains one loader reference until the process exits. An
initialization failure releases any acquired reference after the identity
response is freed. The managed singleton caches either result. Restart the
process to replace an image or retry a failed deployment. Managed calls copy
and release each native response buffer immediately. Concurrent calls share
the resident function pointers and use independent buffers.

Plugin unload and managed hot reload leave the native image resident. A
future process-isolated host could support native-memory reclamation or
image replacement by restarting its worker. D2 supports in-process hosting
and process shutdown. See the [native caller contract](architecture/native-interop.md#d2-image-lifetime).

# D3 experimental package API

The stable `DogwoodSharp` `0.1.0-preview.1` public API is unchanged from the
closed D1/D2 surface. The D3 per-rule export types and entry point remain
internal to that assembly. The experimental
`DogwoodSharp.CedarSharp` `0.1.0-experimental.1` package is the public D3
surface for the preview release candidate:

```csharp
using DogwoodSharp;
using DogwoodSharp.Experimental.CedarSharp;

var schema = new DogwoodSchema(
    "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { count: Long } }; }");

using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(
    "permit (principal, action == App::Action::\"read\", resource) when { context.count > 3 };",
    schema);

var result = authorizer.Authorize(new CedarAuthorizationInput(
    new CedarEntityRef("App::User", "alice"),
    new CedarEntityRef("App::Action", "read"),
    new CedarEntityRef("App::Doc", "doc-1"),
    "{ \"count\": 5 }",
    "[]"));
```

Only an error-free `Allow` sets `Authorized` and `IsCleanAllow`. A lowering,
request, validation, identity, native, or transport failure never authorizes.
The generated Cedar policy identity in `DeterminingPolicyIds` must be a member
of `authorizer.PolicyIds`, and `RuleIndexFor` maps that identity back to the
source rule.

The experimental package declares exact dependency ranges for `DogwoodSharp`
`0.1.0-preview.1` and `CedarSharp` `1.0.0`; it does not repack CedarSharp.
It is not a stable API contract, and this document is not a publication or
release authorization. Publication requires separate explicit owner
authorization.

## Package-only consumer command

The disposable consumer under `qualification/D3.PackageConsumer/` is not in
`DogwoodSharp.slnx`. After the candidate and original dependency packages have
been produced, run one local cell entirely from those packages:

```powershell
./qualification/D3.PackageConsumer/run.ps1 `
  -PackageDirectory "$env:TEMP/dogwoodsharp-d3-candidates/candidates" `
  -CedarSharpPackageDirectory "$env:TEMP/dogwoodsharp-d3-candidates/dependencies" `
  -RuntimeIdentifier win-x64 `
  -TargetFramework net8.0 `
  -WorkDirectory "$env:TEMP/dogwoodsharp-d3-consumer"
```

The script uses .NET SDK `10.0.404`, an isolated NuGet cache, disposable
lockfiles, reflection-free JSON, JIT execution, and NativeAOT publication. It
has no project reference, direct assembly reference, `HintPath`, native
environment override, manual native staging, or source-tree package fallback.
The hosted command runs the same script for all six RID/TFM cells.
