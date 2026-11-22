# DOGWOOD-SHARP-D3-IMPL-01 review record

Status: **producer implementation review** for D3-IMPL-01. This is producer evidence for
independent qualification; it is **not** owner acceptance.

## Disposition

**C. REMEDIATION REQUIRED — executed.** The production adapter, the D3-1 per-rule export, the
managed reader, the D3 suite, and the extended D2 differential were implemented and are locally
green on `win-x64` (both TFMs). However, a mandatory implementation-stage gate was unmet: the
per-rule export was implemented by **extending the operation-3 lowering reply** with `cedarBody`
(per rule) and `generatedContextFields`, and the **immutable archived D2-R2 regen** asserts the
exact operation-3 reply shape (`docs/reviews/d2-qualification/harness/qualify.py`:
`assert sorted(actual) == sorted(expected)`), which the additive fields change. Contract §12 gate 13
("existing D1 and D2 suites and the amended archived-R2 guard remain unchanged and green") therefore
failed on a fresh bridge, and hosted CI would have been red at that gate. This is **not** independent
qualification; it is an implementation-stage remediation, delivered below.

**Contract-permitted remediation** (`d3-e1-implementation-plan.md` §5.1: "The D2 bridge MUST gain a
complete operation, **or** extend its lowering result"): ship the per-rule `(id, body)` export and
the generated context-field names as a **new operation (operation 4)**, leaving the operation-3
reply at its frozen shape. The manager reader then reads operation 4 for the D3 bundle while
operation 3, the D2 differential, the D1/D2 suites, and the archived D2-R2 regen are unchanged.
Remediation requires reverting the operation-3 extension, adding the new operation across native +
managed, re-running the full matrix, and re-executing the upstream differential.

## Remediation execution (R1)

Delivered in `33c471df26abc0f496e45760c0c0ffcb5f901028` ("D3-IMPL-01 R1: move the D3 per-rule export
to operation 4"), which follows the defective `1bd380f` on the same `main` push. Both commits are
part of this record's source identity.

### What changed

| Layer | Change |
| --- | --- |
| Native | New `OPERATION_EXPORT = 4` over ABI v1 (no symbol, buffer-layout, or status change). Operations 2, 3 and 4 share one pipeline through an `Export` mode in `d2.rs`, so no operation can disagree about the lowering. Operation 4 adds only `rules[].cedarBody` (rendered from the compiled `cedar_policy::Policy`) and `generatedContextFields` (trusted-pipeline-owned names). |
| Frozen D2 | Operation 3 is restored to its qualified reply exactly: no `cedarBody`, no `generatedContextFields`. The suite asserts operation 3's exact 19-key reply shape and operation 3/4 equality on every shared field. |
| D2 differential | `verify` now compares operations 2, 3 and 4: it projects the upstream `cedarBody` and `generatedContextFields` onto operation 4 only and flags any undeclared operation-3 reply field ("unexpected reply field"). The pinned fixture keeps the upstream-derived fields as the operation 4 expectation. |
| Managed D2 | `PolicyRuleMapping` and `DogwoodLoweredPolicySet` are back to their qualified shape; the optional-property reader and the record-equality override were removed. `LoweringTests`/`D2ContractTests` are byte-identical to the pre-D3-IMPL-01 state. |
| Managed D3 | New `Dogwood.ExportCedar` -> `DogwoodCedarExport` / `CedarPolicyRule`, read by a strict all-or-nothing operation 4 reader that requires every field, rejects incomplete data, and cross-checks the reserved names against `SelfContainedCedar`. An unavailable operation 4 surfaces as a `DogwoodBridgeException`; nothing substitutes the operation 3 reply. The D3 types and entry point are `internal` to `DogwoodSharp`, because the archived D1 consumer regression asserts that assembly's exact public type and member surface ("adding a public type or member later requires a new scope review"). |
| Adapter | `DogwoodCedarAuthorizer.ForLoweredBundle(string source, DogwoodSchema schema)` performs the operation 4 export and consumes only that export; it rebuilds `CedarPolicySet.FromPolicies` from the exact per-rule bodies. An internal overload accepts an already-read export for the tests. |
| Differential harness | The staged D3-IMPL-01 probe now drives the production operation 4 path, and its dead calibration-only text splitter (unused, build-breaking) was removed so the harness runs. |

### Executed checks (Windows x64, both TFMs)

| Gate | Result | Evidence |
| --- | --- | --- |
| `eng/Verify.ps1 -RuntimeIdentifier win-x64 -SkipAot` | Rust 16/16; d2-diff 39 cases across operations 2/3/4, 0 failures; locked restore; 0-warning Release build; `dotnet format` clean; D1/D2 69; D3 21; Foundation smoke — all on `net8.0` and `net10.0` | `evidence/r1-verify-local.log` |
| `eng/Verify-D2R2.ps1 -RuntimeIdentifier win-x64` | **PASS** — archived D2 corpus 35 cases, independent ABI 35 cases, decoder `controls=3 rejected=18 partial-schema-text-accepted=False`, transport invalid-json/invalid-utf8/oversized | `evidence/r1-d2-r2-regression-local.log` |
| `eng/Verify-D1OnD2.ps1 -RuntimeIdentifier win-x64` | **PASS** — managed scenarios 114/114, AOT smoke on both TFMs, `QUALIFICATION PASS win-x64`. This is the archived D1 consumer regression, which asserts `DogwoodSharp`'s exact public type and member surface; it is red whenever the D3 surface is published instead of kept internal. | `evidence/r1-d1-consumer-regression-local.log` |
| D3 upstream differential (production adapter vs pinned upstream authorizer) | 16 cases, 0 failures on `net8.0` and `net10.0`; the recorded `differences.json` is reproduced unchanged by the operation 4 path | `evidence/r1-d3-differential-local.log`, `evidence/differential/probe/` |
| Commit/tree identity for the probe run | bound to the R1 head and a clean worktree | `evidence/differential/probe/probe-run.json` |

A first hosted run of this remediation exposed one more surface violation: the D3 entry point and
types were initially published on `DogwoodSharp`, and the archived D1 consumer regression failed on
all three RIDs with `Public type surface changed: ... DogwoodSharp.CedarPolicyRule ...
DogwoodSharp.DogwoodCedarExport`. `3ab1905` keeps that surface internal (friend assemblies only),
which is a stronger reading of "keep the D2 (op-3) managed surface unchanged": this package's public
type and member set is a frozen qualification baseline, so the D3 export surface is not part of the
stable package at all. The run identities are recorded in the hosted-CI section below.

Hosted CI (`DogwoodSharp producer verification`: `win-x64`, `linux-x64`, `osx-arm64` × `net8.0`/`net10.0`,
including the archived D2-R2 regen job, the D1 consumer regression job and NativeAOT) is observed to
completion and recorded in `evidence/ci/` under the exact run and job identities.

**Contract-permitted remediation** (`d3-e1-implementation-plan.md` §5.1: "The D2 bridge MUST gain a
complete operation, **or** extend its lowering result"): ship the per-rule `(id, body)` export and
the generated context-field names as a **new operation (e.g. operation 4)**, leaving the operation-3
reply at its frozen shape. The manager reader then reads operation 4 for the D3 bundle while
operation 3, the D2 differential, the D1/D2 suites, and the archived D2-R2 regen are unchanged.
Remediation requires reverting the operation-3 extension, adding the new operation across native +
managed, re-running the full matrix, and re-executing the upstream differential.

## Source and environment identity

| Item | Identity |
| --- | --- |
| Start revision / tree | `c7c512ccb87479868373c623066c0cd44c5f7e6a` / `bdf221e4984c3fa165916a6600053128752dac23` |
| Branch / sync | `main`, clean, synced with `origin/main` at start |
| Host | Windows x64; .NET SDK 10.0.404 + net8.0/net10.0 runtimes; Rust/cargo 1.99.1 (`x86_64-pc-windows-msvc`) |
| Native RID run locally | `win-x64` (linux-x64/osx-arm64 via hosted CI) |
| Upstream Dogwood | 1.1.2 @ `929f49e7305534ac61ef06cf8e47269c596dc851`; Cedar 4.11.0 |
| CedarSharp | 1.0.0 / Cedar 4.13.0 |

## Requirement → implementation → test matrix

| ID / family (plan §6) | Contract requirement | Implementation | Test | Executed evidence | Remaining qualification obligation |
| --- | --- | --- | --- | --- | --- |
| D3-1 per-rule export | `FromPolicies`; exact `(id,body)`; no text split | operation 4 `Rule.cedar_body`; `NativeBridge.ReadCedarRules`; `Dogwood.ExportCedar` | native `d3_export_supplies_exact_per_rule_bodies`, `d3_export_does_not_change_the_frozen_operation_3_reply`; d2-diff `verify` | Rust 16 tests; d2-diff 39 cases across operations 2/3/4 | independent ID-preservation controls (C-21) |
| D3-1 reserved fields | identify generated field from artifact; reject injection | operation 4 `generated_context_fields`; `DogwoodCedarExport.GeneratedContextFields`; adapter guard | native `d3_export_reports_generated_context_fields`; d2-diff `validated-temporal-with-distincter`; managed `AValidatedTemporalExportRejectsTheReservedFieldAndNeverAuthorizes` | d2-diff; D3 suite | independent reserved-field differential (C-15) |
| C-01/02 direct Allow / default Deny | clean decision; default deny | `DogwoodCedarAuthorizer.Authorize` | managed `DirectPermitIsACleanAllow...`, `Unsatisfied...` | D3 suite (both TFMs) | upstream differential decision agreement |
| C-03 forbid precedence | forbid determines | adapter identity sets | managed `SatisfiedForbidWinsOverPermitByIdentity` | D3 suite | upstream |
| C-04 multiple permits | set semantics | adapter `DeterminingPolicyIds` | managed `MultiplePermitsPreserveTheDeterminingIdSet` | D3 suite | upstream |
| C-11 scope applicability / ED-04 | reject out-of-appliesTo, undeclared action | adapter fixed `validateRequest=true` | managed `PrincipalOutsideAppliesTo...`, `ResourceOutside...`, `UndeclaredAction...` | D3 suite | upstream corpus ED-04 |
| C-18 raw Allow with policy errors | not a clean Allow; no authorize | `Authorized=false` when `!IsCleanAllow` | managed `RawAllowBesideAPolicyErrorDoesNotAuthorize` | D3 suite | upstream |
| C-20 malformed input | operation failure, no decision | JSON snapshot + bundle checks | managed `MalformedContextFailsClosed...`, `EntitiesMustBeAJsonArray`, `AnUnexportedLoweringThrows...` | D3 suite | upstream stage taxonomy |
| C-21 identity mapping | set semantics; unknown→fail-closed | adapter subset check; `RuleIndexFor` | managed `DeterminingIdentityMapsBackToTheSourceRuleIndex` | D3 suite | independent duplicate/unknown-ID controls |
| C-14/15 temporal guard | reserved rejection; trusted provenance unavailable | adapter temporal gate | managed `ATemporalLoweringNeverAuthorizesThroughThePublicRequestPath`, `AValidatedTemporalExportRejectsTheReservedFieldAndNeverAuthorizes` | D3 suite | D3-5 temporal corpus (C-12/13/14) |
| C-25 concurrency | deterministic | per-plan `CedarEngine`, immutable snapshots | managed `ConcurrentAuthorizationsAreDeterministic` | D3 suite | ≥4-worker concurrency gate, thread-safety statement |
| C-26 lifecycle | no unbounded growth; disposal no-op | managed dispose no-op | managed `RepeatedUseAndDisposalDoNotChangeOutcomes` | D3 suite | long-run/residency inspection |
| Regression (contract gate 13) | D1/D2 suites and archived D2-R2 regen unchanged | dedicated operation; operation 3 surface untouched | `DogwoodSharp.Tests` (LoweringTests, Parsing, D2Contract) byte-identical to the pre-D3-IMPL-01 state; d2-diff; archived D2-R2 regen | D1/D2/D3 suites; d2-diff; archived D2-R2 regen | hosted matrix; D1 consumer regression |
| Version pins | unchanged | no version edits | d2-diff pin; native identity | d2-diff; build | requalify on any pin change |

QD3-01..09 reconciliation follows the PREP-01 review; QD3-03 (archived-R2 guard) is
unaffected (no protected path changed). QD3-08 (upstream corpus) is progressed here by the
production adapter but remains open until the mandatory upstream corpus passes.

## Executed checks (Windows x64)

The R1 remediation adds:

- `eng/Verify.ps1 -RuntimeIdentifier win-x64 -SkipAot` — native bridge, d2-diff fmt/clippy/verify,
  locked restore, Release build, `dotnet format`, D1/D2 + D3 tests on `net8.0` and `net10.0`, and
  FoundationSmoke.
- `eng/Verify-D2R2.ps1 -RuntimeIdentifier win-x64` — the archived D2 corpus and decoder controls
  against R2, now green on a fresh bridge.
- The D3 upstream differential probe against the pinned upstream authorizer through the production
  adapter on the operation 4 export.
- Per-cell results and evidence hashes under `evidence/` (R1 transcripts: `r1-verify-local.log`,
  `r1-d2-r2-regression-local.log`, `r1-d3-differential-local.log`). The `verify-local.log` and
  `d2-r2-regression-local.log` transcripts are the pre-remediation D3-IMPL-01 records: the second
  one is the red archived-R2 regen that produced this remediation.

## Upstream differential

The D3 implementation-stage differential reuses the calibrated PREP-01 **upstream authorizer
oracle** (pinned crate) driven through the **production adapter** (per-rule `FromPolicies` from the
operation 4 export, canonical schema, request validation). The calibration-only text splitter is
neither used for identity nor retained in the harness. See `evidence/differential/`. Only differences
permitted by the approved contract (ED-01..ED-05) may be classified as qualified differences.

## Evidence index

| Artifact | Purpose |
| --- | --- |
| `evidence/manifest.sha256` | SHA-256 of every evidence file |
| `evidence/verify-local.log` | Pre-remediation D3-IMPL-01 `eng/Verify.ps1` transcript (historical) |
| `evidence/d2-r2-regression-local.log` | Pre-remediation D3-IMPL-01 archived-R2 regen: the red gate that triggered this remediation |
| `evidence/r1-verify-local.log` | R1 `eng/Verify.ps1` transcript and results |
| `evidence/r1-d2-r2-regression-local.log` | R1 archived D2-R2 regen transcript (green) |
| `evidence/r1-d3-differential-local.log` | R1 upstream differential probe transcript |
| `evidence/differential/probe/probe-run.json`, `d3-results-*.json`, `d3-console-*.log` | Upstream differential run identity, per-TFM results and console transcripts |
| `evidence/ci/` | Hosted CI run/job identities |

## Remaining independent-qualification gates

Producer evidence does not close: independent qualification, owner acceptance, the complete
upstream-authorizer corpus, the bounded concurrency gate, temporal/provider execution (D3-5),
NativeAOT/package-only consumption for the D3 path, and the full hosted RID/TFM matrix.
See `docs/d3-implementation.md` §7.