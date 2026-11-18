# D2 qualification findings

## QD2-01 · Major · Partial successful export accepted by the managed decoder

**Requirement.** The managed API defines `ArtifactsExported` and `IsLowered`
as availability of the complete validated policy and schema export. WP-Q1-5
requires a partial artifact set to be rejected at the response boundary.

**Reproduction.** Use the independent `cedar-policy` case, which returns
nonempty Cedar policy text, a nonempty schema in JSON, and nonempty Cedar
schema text. Preserve the successful native reply and change only its
`cedarSchemaText` value to the empty string. Run the qualification consumer's
`decoder` mode with the generated `cases.json`. The positive controls
include a genuinely empty policy and schema export.

**Expected.** Managed decoding rejects the incomplete bundle with
`DogwoodBridgeFailure.MalformedResponse`.

**Observed.** On Windows under .NET 8 and .NET 10, decoding returned an object
with `ArtifactsExported == true`, `IsLowered == true` and an empty
`CedarSchemaText`. The same decoder rejected a missing property and malformed
schema JSON. The public native operations returned complete artifacts for the
unmodified fixture.

**Location.** [NativeBridge.ReadLoweredResult](../../../src/DogwoodSharp/NativeBridge.cs#L264)
reads schema text as a string, checks that schema JSON is an object, and
checks policy text against the rule count. It omits the presence check for
schema text when the reply's JSON describes a nonempty schema.

**Impact.** A malformed reply from the trusted bridge can present missing
schema text as a complete bundle. A downstream consumer that selects the text
form can receive an empty schema while the paired JSON describes a nonempty
schema. This evidence uses a bounded response fixture. The native production
export path produced no such reply in the exercised corpus.

**Recommended action.** Define the structural completeness rule for both
schema representations, including truly empty schemas. Reject the inconsistent
reply and add it as a managed regression alongside a valid empty export.
Requalify the affected managed boundary before D2 acceptance.

## QD2-02 · Informational · DL-03 overstates the upstream warning-span limitation

**Requirement.** Describe the warning-location boundary and its upstream
provenance accurately.

**Reproduction.** The independent `cedar-warning-only` case uses the Ledger
schema and the policy

```cedar
permit (principal, action == Ledger::Action::"post", resource) when { false };
```

Inspect the pinned Dogwood Cedar warning through `miette::Diagnostic::labels`
and compare it with the wrapper's warning location on both operations.

**Expected.** The documented wire contract reports null warning locations.
The explanation should distinguish the wire projection from the upstream
API's diagnostic labels.

**Observed.** Validation passed with one Cedar warning. The pinned upstream
warning's public diagnostic label reported offset 0 and length 78 in the
local probe. Both wrapper operations reported a null warning location, in
agreement with the wire contract.

**Location.** [finding_warning](../../../native/dogwoodsharp-bridge/src/d2.rs)
projects warning locations as null. Cedar 4.11.0 forwards diagnostic labels
through its public `ValidationWarning` enum and its wrapped warning variants.

**Impact.** Warning text and category are preserved under the current D2
contract. Consumers lack a warning byte range through the wrapper, while
the upstream location capability is broader than the producer report states.

**Recommended action.** Retain the current D2 wire limitation and describe
it as a wrapper projection. Evaluate mapping the upstream diagnostic labels
through a separately authorized contract change if a consumer needs them.
