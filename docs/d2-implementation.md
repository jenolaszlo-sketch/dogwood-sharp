# DogwoodSharp D2 producer implementation

## DOGWOOD-SHARP-D2-R1

R1 reconciles operations 2 and 3 at ABI v1. Both validate the exact policy and
schema produced by the same lowering, with or without a caller distincter.
Operation 3 gates export on collision checks and successful validation. The
original D1 ABI and identity contract remain in effect. See the
[native wire contract](architecture/native-interop.md#d2-operations) and
[managed result contract](managed-api.md#d2-managed-api).

| Finding | R1 producer disposition |
| --- | --- |
| DL-01 | Remediated. Check actual upstream field IDs and target actions against the original Cedar context declarations. The known replacement remains pinned as upstream behavior and is rejected by both wrapper operations. |
| DL-02 | Documentation corrected. Decoded per-field limits and the full encoded JSON request limit apply independently. |
| DL-03 | Open limitation. Cedar warnings continue to carry no source span. |
| DL-04 | Open limitation. Policy-store integration and higher-level conflict detection remain outside D2. |
| DL-05 | Reconciled. The D2 producer runs a version-aware copy of the historical D1 controls, including positive and negative operation-2 cases. |

Neither DL-01 nor DL-05 has been independently qualified. Details are in the
[upstream mapping](architecture/d2-upstream-mapping.md), the
[D1-on-D2 regression record](d2-r1-verification.md#d1-consumer-regression) and
the [R1 producer verification record](d2-r1-verification.md).

## DOGWOOD-SHARP-D2-IMPL-01 historical producer report

The following sections record the initial D2 producer baseline and its test
results before R1. Use the R1 contracts linked above for current behavior.

D1 was accepted with qualifications and closed by the owner's acceptance record
([d1-acceptance.md](d1-acceptance.md)). This report covers the D2 commission
only: policy lowering, schemas and validation, plus upstream differential
qualification fixtures. D2 produces validated policy artifacts. It does not
produce authorization decisions.

## Baseline

| Item | Value |
| --- | --- |
| Starting commit / tree | `08b8449ebe9f12a014e7bf7321578e4e582ca3a2` / `5afc6e57a705a6c138f4deb163b1daf477c258e0` |
| D1 implementation | `85298534e55ad0a5403277c54872842960a516e0` |
| D1 qualification | [reviews/d1-qualification](reviews/d1-qualification/README.md) |
| Upstream pin | Dogwood `929f49e7305534ac61ef06cf8e47269c596dc851` (1.1.2), Cedar 4.11.0 |
| Rust | 1.99.1 |
| Bridge `Cargo.lock` SHA-256 | `2ca6001ce075006aebd71c9b372cec23d374043bcfa0b971fc36a8eac88669e7` (unchanged: no new dependency) |

The pinned implementation is the semantic authority. No lowering or validation
logic was reimplemented: the bridge and the differential both call the upstream
crate, and the wrapper only projects its results. The upstream surface used, and
the mapping to this repository's native and managed operations, is recorded in
[d2-upstream-mapping.md](architecture/d2-upstream-mapping.md).

## Native additions (ABI v1, backward compatible)

Operations 0 and 1 keep their exact behavior, symbols, statuses and wire
contract. Two operations were added over the same symbols:

| ID | Operation | Upstream calls |
| --- | --- | --- |
| 2 | `validate` | `ParsedPolicySet::parse`, `ParsedPolicySet::lower`, `Validator::validate` |
| 3 | `lower` | `ParsedPolicySet::parse`, `ParsedPolicySet::lower` / `lower_with_distincter` |

A D1-only library answers a D2 caller with status 3 (unknown operation), and a
D2 library answers D1 callers exactly as before, so the extension is backward
compatible without an ABI bump. The identity response is deliberately unchanged
so a D1 consumer cannot see the added operations.

Both D2 operations share one request shape. `eventSchema`, `macros` and
`distincter` are optional; validation rejects a request carrying a distincter
rather than dropping it. Both replies keep each stage's outcome separately, and
a fatal stage error is reported instead of partial artifacts. Full wire
contract: [native-interop.md](architecture/native-interop.md#d2-operations).

Limits. The D1 request ceiling and source ceiling apply unchanged. Each D2
schema text field is bounded at 1 MiB, a distincter at 256 bytes, and the D2
encoded response at 8 MiB (the D1 ceiling stays 2 MiB for operations 0 and 1).
The full JSON request must fit the unchanged encoded envelope limit. A
maximum-size decoded source can fit alongside a schema when the encoding
and other fields keep the envelope within its independent limit.

## Managed additions

| API | Native operation |
| --- | --- |
| `Dogwood.Validate(source, schema)` → `DogwoodValidationResult` | 2 |
| `Dogwood.Lower(source, schema)` → `DogwoodLoweredPolicySet` | 3 |
| `DogwoodSchema( PolicySchema, EventSchema?, Macros?, Distincter? )` | request fields |
| `DogwoodValidationResult`, `DogwoodLoweredPolicySet`, `DogwoodStageError`, `PolicyValidationFinding`, `PolicyRuleMapping` | replies |

`Dogwood.Parse`, `Dogwood.GetNativeVersion`, `Dogwood.MaximumSourceBytes` and
every D1 type are unchanged. The D2 result types are immutable snapshots. A
distincter that is not a Cedar identifier, oversized text, or an unpaired
surrogate raises `ArgumentException` before any native call; the bridge enforces
the same rules again.

Outcome separation is explicit. `DogwoodValidationResult.IsValid` is true only
when parsing, lowering and validation all succeeded. `DogwoodLoweredPolicySet.IsLowered`
is true only when parsing and lowering both succeeded, and its artifacts are
empty otherwise. Authorization is not implemented and is not representable.

## Feature coverage

| Feature | Status | Evidence |
| --- | --- | --- |
| Cedar policy parse, lower and validate | Implemented | `valid-cedar-*` fixtures |
| Action schema from Cedar `.cedarschema` text | Implemented | shared `defaultPolicySchema` |
| Service schema construction (event schema, macro library) | Implemented | `library-macro-*`, `event-schema-override` |
| Policy validation with structured findings | Implemented | `unknown-*`, `type-mismatch`, `missing-generated-context-field` |
| Dogwood-to-Cedar lowering | Implemented | `temporal-policy`, `inline-temporal-macro` |
| Augmented Cedar schema export (JSON and text forms) | Implemented | `temporal-policy-conflicting-declaration` |
| Lowered policy id and rule/source mapping | Implemented | `multiple-policies`, `annotated-policy-ids` |
| Distincter-based lowering namespaces | Implemented (distincter) | `temporal-policy-conflicting-distincters*` |
| Native AOT and JIT operation coverage | Implemented | smoke sample, managed suite |
| Upstream differential qualification fixtures | Implemented (37 cases) | `tests/DogwoodSharp.D2Conformance/lowering-v1.json` |
| Authorization, temporal evaluation, provider execution | Not implemented | out of scope; `provider-clause-unsupported` proves providers stay off |
| CedarSharp interop, export beyond this repository | Not implemented | later milestone |

## Tests and outcomes

Local Windows run (`eng/Verify.ps1`, exit 0) and producer gates on all three
RIDs:

| Check | Result |
| --- | --- |
| `cargo fmt --check` (bridge and differential crate) | Pass |
| `cargo clippy --locked --release --all-targets -- -D warnings` | Pass |
| `cargo test --locked --release` (bridge) | 11 passed (5 D1, 6 D2) |
| `cargo build --release --locked` (differential crate) | Pass |
| Locked solution restore, Release build, `dotnet format --verify-no-changes` | Pass, 0 warnings |
| Managed tests, both TFMs | 15 passed per TFM (13 D1, 2 D2), 0 skipped |
| JIT smoke, both TFMs (D1 and D2 checks) | Pass |
| NativeAOT smoke, both TFMs (D1 and D2 checks) | Pass |
| D2 differential against the pinned crate and the C ABI | 37 cases, 0 failures |

The differential runner re-derives every case from the pinned crate, checks the
pinned fixture still matches upstream, and then compares the bridge's exported C
ABI replies for operations 2 and 3. Cedar policy text is compared exactly;
augmented schema JSON, findings and rule mappings structurally; diagnostic
message text is upstream-owned and outside the assertions.

## Known limitations and deviations

| ID | Severity | Limitation |
| --- | --- | --- |
| DL-01 | Important, documented | Upstream augmentation replaces a user-declared context attribute whose name collides with a synthesized hoisted name (`<namespace>_<k>__temporal_<j>`), so a conflicting user field does not survive. The pinned behaviour is recorded in `temporal-policy-conflicting-declaration`; the wrapper does not re-lower to avoid it. Detecting the collision and failing needs an owner decision on which semantics wins. |
| DL-02 | Documented | The full encoded JSON envelope and decoded UTF-8 source have separate limits. JSON escaping and other fields affect the total encoded size. |
| DL-03 | Documented | Cedar does not publish a validation warning's source span, so warning `location` is always null. Errors carry spans. |
| DL-04 | Documented | The wrapper adds no higher-level conflict detection, schema comparison or policy-store integration; those need a separate commission with owner agreement. |
| DL-05 | Important, documented | The preserved independent D1 qualification harness asserts that operation ID 2 is *unimplemented*. Three of its controls - `reject_operation_two`, `reject_operation_precedes_input` and `wire-operation-2` - expect status 3 (unknown operation) for operation 2, and now observe 5, 1 and 5 because the operation exists and rejects those bodies as envelopes. The historical native run recorded 81 of 83 C ABI checks per RID, and the complete D1 differential (73 sources, 130 wire requests, 0 message mismatches, 0 wire-length mismatches, response ceiling exercised). The harness is preserved unchanged as commissioned; the D2 independent qualification should re-point those controls at a genuinely unimplemented operation such as 9. |

## D1 compatibility evidence

Operations 0 and 1 are byte-for-byte behaviour-preserved. The evidence, all against
the D2 revision:

| Evidence | Result |
| --- | --- |
| Native Rust tests for the D1 ABI, ownership, envelope limits, source limits and injected unwind | 5 passed, unchanged |
| D1 independent differential (preserved harness) against the D2 library | 73 sources / 130 wire requests; 0 message mismatches; 0 wire-length mismatches; only `wire-operation-2` differs |
| D1 independent C ABI consumer, 83 checks, three RIDs | 81 pass per RID; only the two operation-2 negative controls differ |
| D1 managed suite and smoke consumers, three RIDs x both TFMs | 13 D1 tests and both smokes pass |
| Re-dispatch of the preserved D1 qualification workflow | Reports the three DL-05 controls as the only failure, on all three RIDs |

## Reproducing

```powershell
./eng/Verify.ps1 -RuntimeIdentifier win-x64
cargo run --manifest-path native/dogwoodsharp-d2-diff/Cargo.toml -- `
  verify tests/DogwoodSharp.D2Conformance/lowering-v1.json `
  --library artifacts/native/runtimes/win-x64/native/dogwoodsharp_bridge.dll
```

This is producer implementation and verification. Independent qualification and
owner acceptance are separate decisions and are not claimed here.