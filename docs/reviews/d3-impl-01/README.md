# DOGWOOD-SHARP-D3-IMPL-01 review record

Status: **producer implementation review** for D3-IMPL-01. This is producer evidence for
independent qualification; it is **not** owner acceptance.

## Disposition

**C. REMEDIATION REQUIRED.** The production adapter, the D3-1 per-rule export, the managed
reader, the D3 suite, and the extended D2 differential were implemented and are locally green on
`win-x64` (both TFMs). However, a mandatory implementation-stage gate is unmet: the per-rule export
was implemented by **extending the operation-3 lowering reply** with `cedarBody` (per rule) and
`generatedContextFields`, and the **immutable archived D2-R2 regen** asserts the exact operation-3
reply shape (`docs/reviews/d2-qualification/harness/qualify.py`:
`assert sorted(actual) == sorted(expected)`), which the additive fields change. Contract §12 gate 13
("existing D1 and D2 suites and the amended archived-R2 guard remain unchanged and green") therefore
fails on a fresh bridge, and hosted CI would be red at that gate. This is **not** independent
qualification; it is an implementation-stage remediation.

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
| D3-1 per-rule export | `FromPolicies`; exact `(id,body)`; no text split | `d2.rs` `Rule.cedar_body`; `NativeBridge.ReadRules`; `Dogwood.Lower` | native `d2_export_supplies_exact_per_rule_bodies`; d2-diff `verify` | Rust 15 tests; d2-diff 39 cases | independent ID-preservation controls (C-21) |
| D3-1 reserved fields | identify generated field from artifact; reject injection | `d2.rs` `generated_context_fields`; `DogwoodLoweredPolicySet.GeneratedContextFields`; adapter guard | native generated-field test; d2-diff `validated-temporal-with-distincter` | d2-diff | independent reserved-field differential (C-15) |
| C-01/02 direct Allow / default Deny | clean decision; default deny | `DogwoodCedarAuthorizer.Authorize` | managed `DirectPermitIsACleanAllow...`, `Unsatisfied...` | D3 suite (both TFMs) | upstream differential decision agreement |
| C-03 forbid precedence | forbid determines | adapter identity sets | managed `SatisfiedForbidWinsOverPermitByIdentity` | D3 suite | upstream |
| C-04 multiple permits | set semantics | adapter `DeterminingPolicyIds` | managed `MultiplePermitsPreserveTheDeterminingIdSet` | D3 suite | upstream |
| C-11 scope applicability / ED-04 | reject out-of-appliesTo, undeclared action | adapter fixed `validateRequest=true` | managed `PrincipalOutsideAppliesTo...`, `ResourceOutside...`, `UndeclaredAction...` | D3 suite | upstream corpus ED-04 |
| C-18 raw Allow with policy errors | not a clean Allow; no authorize | `Authorized=false` when `!IsCleanAllow` | managed `RawAllowBesideAPolicyErrorDoesNotAuthorize` | D3 suite | upstream |
| C-20 malformed input | operation failure, no decision | JSON snapshot + bundle checks | managed `MalformedContextFailsClosed...`, `EntitiesMustBeAJsonArray`, `AnUnexportedLoweringThrows...` | D3 suite | upstream stage taxonomy |
| C-21 identity mapping | set semantics; unknown→fail-closed | adapter subset check; `RuleIndexFor` | managed `DeterminingIdentityMapsBackToTheSourceRuleIndex` | D3 suite | independent duplicate/unknown-ID controls |
| C-14/15 temporal guard | reserved rejection; trusted provenance unavailable | adapter temporal gate | managed `ATemporalLoweringNeverAuthorizesThroughThePublicRequestPath` | D3 suite | D3-5 temporal corpus (C-12/13/14) |
| C-25 concurrency | deterministic | per-plan `CedarEngine`, immutable snapshots | managed `ConcurrentAuthorizationsAreDeterministic` | D3 suite | ≥4-worker concurrency gate, thread-safety statement |
| C-26 lifecycle | no unbounded growth; disposal no-op | managed dispose no-op | managed `RepeatedUseAndDisposalDoNotChangeOutcomes` | D3 suite | long-run/residency inspection |
| Regression (contract gate 13) | D1/D2 suites unchanged | additive fields + tests | `DogwoodSharp.Tests` (LoweringTests, Parsing, D2Contract); d2-diff | D1/D2/D3 suites; d2-diff | hosted matrix; D1 consumer regression |
| Version pins | unchanged | no version edits | d2-diff pin; native identity | d2-diff; build | requalify on any pin change |

QD3-01..09 reconciliation follows the PREP-01 review; QD3-03 (archived-R2 guard) is
unaffected (no protected path changed). QD3-08 (upstream corpus) is progressed here by the
production adapter but remains open until the mandatory upstream corpus passes.

## Executed local checks (Windows x64)

- `eng/Verify.ps1 -RuntimeIdentifier win-x64 -SkipAot` — build native, cargo fmt/clippy/test,
  d2-diff fmt/clippy/verify, locked restore, Release build, `dotnet format`, D1/D2 + D3 tests
  (net8.0, net10.0), FoundationSmoke.
- Recorded per-cell results and evidence hashes under `evidence/`.

## Upstream differential

The D3 implementation-stage differential reuses the calibrated PREP-01 **upstream authorizer
oracle** (pinned crate) driven through the **production adapter** (per-rule `FromPolicies`,
canonical schema, request validation) instead of the calibration-only splitter. See
`evidence/differential/`. Only differences permitted by the approved contract (ED-01..ED-05)
may be classified as qualified differences.

## Evidence index

| Artifact | Purpose |
| --- | --- |
| `evidence/manifest.sha256` | SHA-256 of every evidence file |
| `evidence/verify-local.log` | Local `eng/Verify.ps1` transcript and results |
| `evidence/rust-tests.log`, `evidence/d2-diff.log` | Native test and differential transcripts |
| `evidence/differential/` | Upstream authorizer differential harness + results |
| `evidence/ci/` | Hosted CI run/job identities |

## Remaining independent-qualification gates

Producer evidence does not close: independent qualification, owner acceptance, the complete
upstream-authorizer corpus, the bounded concurrency gate, temporal/provider execution (D3-5),
NativeAOT/package-only consumption for the D3 path, and the full hosted RID/TFM matrix.
See `docs/d3-implementation.md` §7.