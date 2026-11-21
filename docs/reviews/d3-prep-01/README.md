# DOGWOOD-SHARP-D3-PREP-01

## Formal disposition

**D3-PREP-01 is COMPLETE WITHIN THE AUTHORIZED SCOPE.**

D3-E1 is accepted with qualifications and closed. D3-PREP-01 records that
acceptance, remediates the archived-R2 CI guard, resolves the D3 authorization
semantics, calibrates a differential against the actual pinned upstream
Dogwood authorizer, binds the implementation and qualification contracts, and
disposes QD3-01 through QD3-09 as recorded below.

This review does not authorize a production adapter, public API, package,
release, upstream-equivalence claim, or any dependency/native change. Those
remain outside D3-PREP-01.

## Recommendation: A

Recommend the **schema-carrying, request-validating CedarSharp path** defined
by the [D3 integration contract](../../d3-integration-contract.md) as the
binding architecture for the next, separately authorized D3 implementation.

That path requires the immutable augmented schema on every request, validation
on every dispatch, exact forwarding of caller entities, schema-derived action
hierarchy, rejection of incomplete `appliesTo` objects and conflicting caller
action data before dispatch, and no schema-less fallback. It intentionally
differs from the calibrated upstream decision path where upstream can allow an
out-of-scope principal or resource; those differences are explicit and fail
closed on the D3 side.

This recommendation selects an architecture for a future commission. It does
not implement that architecture or qualify upstream equivalence.

## Binding source and environment identity

The local probe and guard regression were executed from the same clean,
synced `main` revision:

| Item | Exact identity |
| --- | --- |
| PREP preflight | `e73c1caedb2df27f975f25bcdfe3f122d951ca9a`, tree `f65498e89e2ec39e08b4103e6c6d7082b2e09f4b` |
| Guard remediation | `584fab31cacb0ad9dab3c5463f53a86956e947a2`, tree `41231d21c4d6a501499eabb7bf2017b580130765` |
| Semantic/evidence design | `c79b423c35ff56a98c02d795da3f3f6009109d17`, tree `8ef3e73ee3940ce3d5db08fefe1ffe731a4b38c7` |
| Harness identity hardening | `f1dd0b1ce65a14a542941f1bf771db9e3b541d3e`, tree `1eda7cd83ebccc05d06f6fe29997c9dd5716c361` |
| **Evidence binding revision** | **`a95c5dbfb6fdae4fd571466f49220d7283c9b6cd`, tree `69fc265227060674ba894673cde23b865c900494`** |
| Branch / synchronization | `main`; `origin/main` at the evidence binding revision; ahead/behind `0/0` at both harness starts |
| Upstream Dogwood | `dogwood-language` 1.1.2 at `929f49e7305534ac61ef06cf8e47269c596dc851`; checkout tree `e8fbf0b20de69d34b422aa82d8d46d626b9ed143` |
| Upstream Cargo lock | SHA-256 `3b448aeadc587c2c211ae7dee59476ef551c270e51a11298cf301c8315f090f0` |
| D2 bridge native | SHA-256 `8abf40d2526eab650003e7ee1c05d97aa82cbeb69890849e867333ea54914d63` |
| CedarSharp native | SHA-256 `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895` |
| Managed package lock | SHA-256 `2d57741f811a9a22417ab414d9c58d54e4ff766397f4e98a72570b3f8debb374` |
| Local host | Microsoft Windows NT 10.0.26200.0, x64; PowerShell 7.7.0; Git 2.55.0.windows.3 |
| Toolchains | .NET SDK 10.0.404; Cargo 1.99.1; rustc 1.99.1 (`x86_64-pc-windows-msvc`) |

The evidence-binding revision contains the final harness and contract sources
used by both local runs. The later review-delivery commit adds this review,
durable evidence, and one broken-link correction; the final delivery revision
and hosted run are recorded in `evidence/ci-status.json`.

## Method

The managed path executes the D2 lowering and CedarSharp 1.0.0 (Cedar 4.13.0)
with the canonical augmented schema and request validation enabled. The
comparison path links the actual `dogwood-language` crate and calls its
public `Authorizer::is_authorized` path after constructing the upstream
`ServiceSchema`, `PolicySchema`, `LoweredPolicySet`, `Authorizer`, and
`Event`. It is not a mock and not a Cedar-to-Cedar reference.

The same authored fixture drives both paths. The runner checks the lowering
artifact export and rule IDs, exact decision, operation-failed state, error
presence, determining-ID set, policy-error-ID set, categorized errors,
clean-Allow state, and the exact versioned difference-register observation.
Stage labels and fail-closed-effect prose in the PREP register are contract
documentation; they are not independently machine-validated by this
calibration. The future mandatory runner must validate them.

## Baseline-to-fix observations

| Issue | Baseline observation | Remediation and observed result | Evidence |
| --- | --- | --- | --- |
| Archived-R2 CI guard | The guard protected all of `docs/reviews`, so adding a legitimate D3 review made the archived diff non-empty. Run `44769429084` at `3f22013` failed all three producer jobs at that guard. | Commit `584fab3` narrowed the guard to exactly `docs/reviews/d2-qualification`, `docs/d1-acceptance.md`, and the two D1/D2 qualification workflows. Run `44793572785` passed `win-x64`, `linux-x64`, and `osx-arm64`. The five-case regression proves unchanged and unrelated-report cases pass, while modification, deletion, and unauthorized addition in the archive fail. | `evidence/guard-regression/guard-baseline-ci.json`, `evidence/guard-regression/guard-regression.json` |
| Authorization architecture | D3-E1-Q1 found that schema-always and upstream-style bare scope injection could not be combined in one CedarSharp call, while a schema-less reconstruction could allow a principal outside `appliesTo`. | The binding contract selects schema carrying plus request validation, exact caller entities, no scope/action synthesis, no fallback, complete `appliesTo`, and a separate clean-Allow enforcement decision. ED-01 through ED-05 record the bounded differences. | [Integration contract](../../d3-integration-contract.md), [implementation contract](../../d3-e1-implementation-plan.md), `evidence/probe/differences.json` |
| Upstream differential | D3-E1-Q1 did not execute the upstream Dogwood authorizer; its reference was direct Cedar 4.11 FFI. Cedar-to-Cedar agreement could not establish upstream equivalence. | The PREP oracle pins and verifies the actual upstream Git revision before building, executes upstream's `Authorizer`, and compares 16 static fixtures on both TFMs. Seven cases normalize to matches and nine to explicitly encoded expected differences. | `evidence/upstream-oracle/`, `evidence/probe/probe-run.json`, `evidence/probe/prep-results-*.json` |
| Evidence fidelity | The first guard runner returned its command transcript together with the result object, and the first probe identity hard-coded clean-state values, captured status before its own output existed, and could not be rerun over its prior outputs. | Commits `59f9bd7`, `f1dd0b1`, and `a95c5db` collectively isolate the result object, record measured status and cleanup, remove stale own outputs, verify the upstream checkout before the build, and bind the actual source/tree/environment. The final local runs report no unexpected source changes. | `evidence/probe/probe-run.json`, `evidence/guard-regression/guard-regression.json` |

## QD3 finding disposition

| Finding | Disposition | Binding result and remaining gate |
| --- | --- | --- |
| QD3-01 | **Resolved as architecture; implementation qualification pending.** | The selected schema-carrying mode is authoritative. It leaves an omitted scope entity absent and does not synthesize entities or relationships. ED-01 and ED-03 record the resulting fail-closed diagnostic differences. D3-3/D3-4 must implement and qualify the selected mode. |
| QD3-02 | **Resolved as architecture; implementation qualification pending.** | Request validation is fixed on, every dispatchable action must preserve a complete `appliesTo`, and schema-less fallback is forbidden. ED-04 records that upstream can allow out-of-scope scope while D3 rejects before a decision. D3-4 must qualify this on every supported path. |
| QD3-03 | **Closed for D3-PREP-01.** | The scoped guard fix, baseline/remediation CI record, five-case regression, and green hosted producer-verification matrix are durable evidence. The archived archive remains protected; unrelated reports are allowed. |
| QD3-04 | **Contracted; qualification pending.** | Determining and erroring identities are sets, never positions. The PREP harness compares sets. The future D3-2 gate must include non-sequential, reordered, duplicate, and unknown-ID controls. |
| QD3-05 | **Contracted; implementation pending.** | The production contract requires one exact body per generated ID and `CedarPolicySet.FromPolicies`; concatenated text must not be an identity input. PREP's positional split is calibration-only and does not qualify D3-1. |
| QD3-06 | **Contracted; qualification pending.** | The augmented schema JSON is the sole canonical transport/cache/equality value. The text form is diagnostic-only. D3-2 must prove both forms parse while canonical behavior uses JSON. |
| QD3-07 | **Contracted; qualification pending.** | RID-specific publish work must occur in a disposable copy or be followed by a clean-tree/lockfile check. PREP did not rerun NativeAOT; D3-6 remains mandatory. |
| QD3-08 | **Narrowed by PREP; mandatory gate remains open.** | The actual pinned upstream authorizer now executes 16 static cases. This is not the complete upstream or mandatory D3 corpus, and no upstream-equivalence claim is permitted. Temporal, provider, entity, error, and complete-corpus qualification remain mandatory. |
| QD3-09 | **Contracted; corpus qualification pending.** | The mandatory corpus must use D2-lowered fixtures and must treat a non-exported lowering as a lowering/operation failure, never an empty policy set. D3-7 remains pending. |

## Bounded difference register

| ID | Cases | Upstream observation | Selected D3 observation | Security effect |
| --- | --- | --- | --- | --- |
| ED-01 | P1-08, P1-11 | An omitted scope entity is added bare. | The entity remains absent. | Attribute evaluation fails closed; a raw Allow with an error is not authorization. |
| ED-02 | P1-07, P1-09 | Invalid caller-entity conformance becomes a fail-closed Deny with diagnostics. | Validation becomes an operation failure with no decision. | Neither side authorizes the invalid request. |
| ED-03 | P1-08, P1-11 | An absent scope entity is reported as missing a required attribute. | The entity is reported as absent. | The failing policy does not authorize; only the diagnostic category differs. |
| ED-04 | P1-10a–P1-10d | The calibrated upstream decision path can allow an out-of-`appliesTo` principal or resource. | Validation rejects out-of-scope principal/resource and undeclared action before a decision. | This owner-approved stricter D3 contract prevents the upstream out-of-scope Allow. |
| ED-05 | P1-12 | Missing required context can surface as a policy-evaluation error. | Context parsing can produce an operation failure. | Both sides fail closed; the failure stage differs. |

P1-13 is a normalized match: both paths reject malformed policy input before
an authorization decision.

## Executed checks

| Check | Scope | Result | Durable record |
| --- | --- | --- | --- |
| Archived-R2 guard regression | One unchanged case, one unrelated report, one protected modification, one protected deletion, one protected unauthorized addition | 5/5 passed | `evidence/guard-regression/guard-regression.json` |
| Managed/upstream differential probe | 16 authored cases on `net8.0` | 16/16 passed; 7 normalized matches; 9 expected-difference cases | `evidence/probe/prep-results-net8.0.json`, `evidence/probe/prep-console-net8.0.log` |
| Managed/upstream differential probe | The same 16 cases on `net10.0` | 16/16 passed; 7 normalized matches; 9 expected-difference cases | `evidence/probe/prep-results-net10.0.json`, `evidence/probe/prep-console-net10.0.log` |
| Upstream pin and oracle identity | Lock source, resolved Cargo tree, exact checkout HEAD/tree, oracle hash | Passed | `evidence/probe/probe-run.json` |
| Rust format | `cargo fmt --check` | Exit 0 | `evidence/quality-checks.json` |
| Rust lint | Locked offline release clippy, all targets, warnings denied | Exit 0 | `evidence/quality-checks.json` |
| Hosted producer verification | `win-x64`, `linux-x64`, `osx-arm64` | Run `44889338862` succeeded in all three jobs | `evidence/ci-status.json` |
| Evidence integrity | Every file listed by `evidence/SHA256SUMS` | 19/19 hashes verified | `evidence/SHA256SUMS` |

The hosted workflow is the producer-verification matrix. It verifies that the
guard remediation did not disturb the existing D1/D2 gates on all three RIDs;
it does **not** execute this PREP upstream differential on Linux or macOS.

## Cleanup and source integrity

- The probe removed `probe/bin`, `probe/obj`, and `upstream-oracle/target`;
  `cleanup.removed` is true and `residualPaths` is empty.
- The guard regression removed its disposable clone; `cleanup.removed` is
  true.
- The Rust quality run used and removed a disposable clone and its build
  target; no residual path remained.
- Temporary staging directories used while preserving the clean source state
  between the two local harnesses were removed.
- Both harnesses record an empty source status at start and no unexpected
  source path at completion. Their expected result files remain intentionally
  as durable evidence.

## Explicit exclusions and unresolved qualification gates

PREP-01 does not establish or qualify:

- a production D3 adapter or public API;
- a D2 bridge per-rule production export;
- a CedarSharp change, dependency update, package, publication, or release;
- complete mandatory-family coverage (`C-01` through `C-26`);
- production policy-body/ID pairing or source-rule-index mapping;
- temporal history, reserved-field injection, or provider success/failure;
- concurrency, throughput, memory, or native unload behavior;
- NativeAOT/trimming or package-only consumption;
- cross-platform semantic behavior of the D3 path;
- the upstream project's complete corpus or an upstream-equivalence claim;
- independent D3 qualification or a new owner acceptance.

Those items remain blocking gates in the [binding implementation
contract](../../d3-e1-implementation-plan.md) and [qualification
plan](../../d3-qualification-plan.md). Missing or skipped required cases are
pending, never passed.

## Evidence index

| Artifact | Purpose |
| --- | --- |
| `evidence/ci-status.json` | Baseline-to-fix and hosted producer-verification run/job identities |
| `evidence/SHA256SUMS` | SHA-256 manifest for every other file in this evidence package |
| `evidence/quality-checks.json` | Rust format/lint identities, results, and cleanup |
| `evidence/guard-regression/run.ps1` | Disposable-clone archived-R2 guard regression |
| `evidence/guard-regression/guard-baseline-ci.json` | Guard failure/remediation CI record |
| `evidence/guard-regression/guard-regression.json` | Five guard cases, source identity, commands, and cleanup |
| `evidence/probe/run.ps1` | Pinned upstream/managed differential runner and identity binding |
| `evidence/probe/Program.cs` | Two-engine execution and exact expectation/difference validation |
| `evidence/probe/fixtures.json` | Authored 16-case fixture and hand-derived expectations |
| `evidence/probe/differences.json` | Versioned ED-01 through ED-05 difference register |
| `evidence/probe/probe-run.json` | Source, upstream, native, toolchain, command, summary, and cleanup identity |
| `evidence/probe/prep-results-*.json` | Per-case measured results for both TFMs |
| `evidence/probe/prep-console-*.log` | Per-TFM console transcripts |
| `evidence/upstream-oracle/` | Actual upstream `dogwood-language` authorizer oracle and locked graph |

The governing D3 documents are the [owner acceptance](../../d3-e1-acceptance.md),
[integration contract](../../d3-integration-contract.md), [binding
implementation contract](../../d3-e1-implementation-plan.md), and
[qualification plan](../../d3-qualification-plan.md). The source findings
remain preserved in the [D3-E1-Q1 review](../d3-e1-qualification-01/README.md).
