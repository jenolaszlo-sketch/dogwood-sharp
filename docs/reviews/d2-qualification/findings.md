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

**Observed.** In all six JIT platform and framework cells, decoding returned an object
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

**Observed.** On all three RIDs validation passed with one Cedar warning. Its
pinned upstream public diagnostic label reported offset 0 and length 78.
Both wrapper operations reported a null warning location, in agreement with
the wire contract. Mapping any upstream label into authored source would
also require checking source provenance and byte-offset rebasing.

**Location.** [finding_warning](../../../native/dogwoodsharp-bridge/src/d2.rs)
projects warning locations as null. Cedar 4.11.0 forwards diagnostic labels
through its public `ValidationWarning` enum and its wrapped warning variants.

**Impact.** Warning text and category are preserved under the current D2
contract. Consumers lack a warning byte range through the wrapper, while
the upstream location capability is broader than the producer report states.

**Recommended action.** Retain the current D2 wire limitation and describe
it as a wrapper projection. Evaluate mapping the upstream diagnostic labels
through a separately authorized contract change if a consumer needs them.

## QD2-03 · Major · Linux memory loss after releasing buffers and unloading the bridge

**Requirement.** WP-Q1-7 requires independent native ownership and Linux
memory checking. The ABI keeps the library loaded until all live buffers
are released. The consumer completes that lifetime before closing the image.

**Reproduction.** On Linux x64, build `harness/ownership.c` as shown in
`run.ps1`. Run the `mixed-unload` control against the verified bridge under
Valgrind. The control opens the image, completes 32 iterations of D1 parse,
D2 export, rejected D2 validation and a malformed operation-2 envelope,
releases every buffer, and closes the image using `dlclose`.

**Expected.** Each buffer resets upon release. Linux reports zero definite
or indirect loss after this native ownership and unload sequence.

**Observed in the first run.** All 128 C call assertions and buffer resets
passed. Memcheck then reported 3,460 definitely lost bytes, 39,456 indirectly
lost bytes and 122 error contexts. The separate Rust D1 and D2 lifetime
tests and the archived C consumer regression had zero memory errors.
The first run stopped before the independent Linux managed consumers.

**Follow-up on Linux x64 with Valgrind 3.22.0.**

| Control | C calls | Errors | Definitely lost | Indirectly lost | Possibly lost |
| --- | ---: | ---: | ---: | ---: | ---: |
| Open and close the image | 0 | 0 | 0 | 0 | 0 |
| D1 parse and unload | 32 | 0 | 0 | 0 | 0 |
| D1 and D2 calls, image resident at exit | 128 | 0 | 0 | 0 | 13,136 bytes |
| Same D1 and D2 calls, then unload | 128 | 121 | 3,460 bytes | 39,456 bytes | 0 |

All C call and release assertions passed. The exact byte losses reproduced
with symbols retained. The follow-up completed the Linux managed cases in
both frameworks and then reported the failed ownership gate.

**Implementation location.** The sequence crosses the native call and
release exports in [lib.rs](../../../native/dogwoodsharp-bridge/src/lib.rs).
Symbolized stacks include Cedar's lazily initialized schema and entity-name
parsers and its extension tables. They are reached through Dogwood event
schema derivation, lowering and validation. See the
[initial log](evidence/initial-valgrind-ownership.log),
[symbolized follow-up log](evidence/valgrind-ownership-mixed-unload.log), and
[resident control](evidence/valgrind-ownership-resident.log).

**Impact.** The mixed D2 sequence leaves dependency allocations behind after
native library unload, even when the consumer releases every response first.
The resident control reports no definite or indirect loss under the same
workload. The measured loss is for a single unload after D2 use. Repeated
load/unload amplification and memory behavior on other operating systems
remain unmeasured.

**Recommended action.** Decide the supported native library lifetime before
D2 acceptance. Requalify unload after remediation if unload remains
supported. If the owner adopts a process-lifetime boundary, document and
verify that contract and its consequences for downstream hosting.
