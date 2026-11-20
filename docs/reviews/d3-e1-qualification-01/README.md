# DOGWOOD-SHARP-D3-E1-Q1

**B. SUPPORTED WITH QUALIFICATIONS**

Recommend owner acceptance of the D3-E1 feasibility evidence and of CedarSharp
1.0.0 (Cedar 4.13.0) as the DogwoodSharp Cedar evaluation backend, subject to
the qualifications below. The review found no blocking defect in CedarSharp
and no semantic difference from the pinned Cedar 4.11.0 reference on any
executed envelope. Every finding is an adapter-, contract- or process-level
qualification, not an engine defect. **This review does not grant owner
acceptance**; the owner retains the acceptance and finding-closure decisions.

| Finding | Severity | Recommendation |
| --- | --- | --- |
| QD3-01 | Medium | Resolve before D3-3/D3-4 ship. The D3 plan's schema-always and bare-scope-entity constraints are not jointly satisfiable; the adapter must declare one scope/entity mode and requalify it. |
| QD3-02 | Medium | Resolve before D3-4 ships. A schema-less authorization allows a principal outside the action's `appliesTo`; scope conformance must be enforced on every request. |
| QD3-03 | Medium | Producer action required before D3 work starts. Hosted CI is red at the qualified revision for a pre-existing guard conflict plus one macOS network failure. |
| QD3-04 | Low | Encode in D3-2. Determining-policy ID order is unstable between identical requests; use set semantics. |
| QD3-05 | Low | Encode in D3-1. A text-form policy set exposes no policy IDs at all while its diagnostics still report `policy0..N`. |
| QD3-06 | Low | Encode in D3-4. The two exported schema forms are not value-equal in CedarSharp although they authorize identically. |
| QD3-07 | Low | Encode in D3-6. A RID-specific NativeAOT publish mutates the committed lockfile. |
| QD3-08 | Medium | Resolve before any upstream-equivalence claim. No upstream Dogwood authorizer was executed in this qualification either. |
| QD3-09 | Info | Record in D3-7. The D2 lowering's strict validation bounds the adapter's input space. |

## Reviewer and baseline

Reviewer session `DOGWOOD-SHARP-D3-E1-Q1 / 2026-11-20 / independent
qualification`. The reviewer is an AI working without producer context. The
producer's harness, corpus and documents were used as material to audit, not
as the source of expectations: every expectation executed by the reviewer
harness was derived by hand from the authored fixtures and from source reading
of the pinned upstream Dogwood authorizer.

| Item | Identity |
| --- | --- |
| Commissioned `main` at preflight | `001bd3e930011ca8de7098087b0ac1bafdcad30f` |
| Commissioned tree | `df11aaa372af5742d32d195f4c70ffb4ea9d964d` |
| Worktree at preflight | Clean, synced with `origin/main` |
| D3-E1 commit boundary | `c61d096` Cedar 4.11 reference, `489b6f0` prototype, `7c8654b` AOT consumer, `001bd3e` reports |
| D3-E1 delta | New research/reference projects and four D3-E1 documents only; no production source, dependency or configuration change |
| Dogwood / Cedar (bridge) | `929f49e7305534ac61ef06cf8e47269c596dc851` (1.1.2) / 4.11.0, native SHA-256 `8abf40d2…914d63` |
| CedarSharp native | SDK 4.13.0, language 4.5, features `datetime`, `decimal`, `ipaddr`, SHA-256 `77de6976…03895` |
| Host | Windows build 10.0.26200 x64, .NET SDK 10.0.404, runtimes 8.0.31 and 10.0.15 (the NativeAOT publishes report 8.0.34 / 10.0.15 from the statically linked runtime), Rust 1.99.1, PowerShell 7.7.0, Git 2.55.0 |
| Producer CI at the qualified revision | [44683180759](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44683180759) — failure (QD3-03) |
| Reviewer probe | 49 cases, 0 failures, identical verdicts on `net8.0` and `net10.0` |
| Producer suite, re-executed by the reviewer | 34/34 passed on `net8.0` and on `net10.0` |

The reviewer harness, fixtures, consumers and evidence are new files under
`docs/reviews/d3-e1-qualification-01/`. The producer's prototype was not
modified, not added to `DogwoodSharp.slnx`, and not relied on for any
expectation. No production source, package pin, workflow or configuration was
changed. One tracked file was touched only by a tool side effect and was
restored before delivery (QD3-07).

## Scope and method

Qualification covered the seven D3-E1 questions: policy identity, authorization
semantics, schema/entity interoperability, the trusted temporal pipeline,
lifecycle and NativeAOT, the limits of the differential oracle, and D3
readiness.

- **Reviewer harness** (`evidence/probe/`): 49 hand-derived cases over
  CedarSharp 1.0.0's public API and the D2 bridge, executed in one process on
  both supported TFMs. Expectations were written before execution, from the
  fixtures and from the upstream author's three-tier entity-store semantics
  (`dogwood-language/src/api.rs`, `build_entities`).
- **Independent non-sequential and reordered identity cases**: caller IDs
  `z-rule-9`, `a-rule-42`, `m-rule-0` in two supply orders, plus a text-form
  set carrying `@id("custom-a")`-style annotations.
- **Differential subset** (`evidence/probe/Program.cs`, F2): 12 identical
  envelopes — 11 D2-lowered bundles plus one direct raw-allow-with-error
  envelope — through CedarSharp 4.13.0 and the pinned Cedar 4.11.0 FFI
  reference, normalized on decision, determining set, policy-error ID set and
  failed-ness.
- **NativeAOT reproduction** (`evidence/probe-aot/`): `win-x64` publishes for
  both TFMs with a staged/unstaged/staged-out asset sequence, proving the
  fail-closed native-asset contract under AOT.
- **Hosted CI audit**: the qualified revision's producer run and its
  predecessors, with the failure cause isolated to a guard and a network fault.
- **Producer suite re-execution**: the producer's 34 tests per TFM, including
  their 30-case differential, as supporting material only.

The Cedar 4.11.0 reference binary was rebuilt with `--locked` (no lockfile
change) and is supporting evidence: it wraps `cedar_policy::ffi` directly, so
agreement with it is not evidence of equivalence with the upstream Dogwood
authorizer, and it does not cover the upstream entity-store, temporal or
provider pipeline.

## Binding requirements

The D3-E1 compatibility report's eight required qualifications are binding for
D3, together with the D3 plan's design constraints and required gates and the
D2 acceptance record's authorization scope:

| Ref | Binding requirement |
| --- | --- |
| Q1-01 | Export policy bodies per rule and preserve exported IDs with `CedarPolicySet.FromPolicies`; never reparse concatenated text for identity; never treat a source `@id` annotation as the stored ID. |
| Q1-02 | Map determining and erroring IDs back by identity; do not rely on policy-set enumeration order. |
| Q1-03 | Always transport the augmented schema with each request; the action hierarchy is schema-derived. |
| Q1-04 | Reproduce the upstream entity-store construction, including bare scope entities, or explicitly document the different fail-closed mode; do not forward caller entities as if they were the complete upstream store. |
| Q1-05 | Enforce a clean-Allow policy or an explicit fail-closed rule at the adapter boundary; a raw Allow carrying policy errors is not clean. |
| Q1-06 | Preserve the temporal generated-field trust boundary: reserved fields are computed only by the trusted pipeline and caller-supplied temporal values are rejected. |
| Q1-07 | Keep both native images resident until process exit; no unload or replacement promise. |
| Q1-08 | Re-qualify the version pair when either Cedar pin changes; the pair is qualified for the executed corpus only. |
| Q1-09 (plan) | The D3 plan's constraints (schema always; no action-entity transport; upstream bare scope entities; raw-Allow exposure; temporal reserved fields; process-lifetime residency) and its required gates (restore/build, unit and integration, differential, ID preservation, schema/entity round trip, temporal boundary, lifecycle, regression, evidence). |

## Results by qualification

| Qualification | Reviewer result | Evidence |
| --- | --- | --- |
| Q1-01 policy identity | Met, with two strengthened observations. `FromPolicies` preserves non-sequential caller IDs and the determining set names them; reordering supply changes only enumeration order. `FromText` numbers policies positionally (`policy0` for the first of three) and ignores the source `@id` annotations. Determining-ID **order varies between identical requests** (four distinct orders in five runs). A text-form set exposes **no** policy IDs at all. | A1–A8, C6 |
| Q1-02 authorization semantics | Met. Allow/deny, forbid precedence, conditions, entity and action hierarchy, `has`, missing referenced entities, extension types, scope and context conformance all behave as hand-derived. Two modes matter: a failing policy does not flip another policy's Allow (per-policy error semantics), and a schema-less request authorizes a principal outside the action's `appliesTo`. | B1–B12 |
| Q1-03 schema/entity interoperability | Met with one contract tension (QD3-01). With the schema attached, action-group membership resolves from an empty entity payload; schema-less, it resolves only when action entities are supplied explicitly, and otherwise denies silently. Caller entities are conformance-checked and an incomplete entity fails the call closed. Pre-validate plus a schema-less call with a bare scope principal reproduces the upstream-equivalent outcome. | B4–B7, C1–C7 |
| Q1-04 trusted temporal pipeline | Met. The lowering exposes exactly one generated Boolean field (`q1t_0__temporal_0`) declared by the augmented schema and absent from the authored schema; the lowering is not self-contained. A missing generated field fails closed on both the validated and unvalidated paths, and an authored collision is rejected at lowering. A caller-supplied value flips the decision, which is the trust-boundary hazard the plan must close. | C7, D1–D6 |
| Q1-05 lifecycle and NativeAOT | Met on the executed scope. The D2 bridge (Cedar 4.11.0) and the CedarSharp native (Cedar 4.13.0) coexist in one process with independent verified identities and a working lower→authorize flow. `win-x64` NativeAOT publishes on both TFMs fail closed without the staged bridge asset and succeed with it. | E1, AOT evidence |
| Q1-06 differential-oracle limits | Met as far as the oracle permits. Twelve identical envelopes agree with the pinned Cedar 4.11.0 reference on decision, determining set, policy-error IDs and failed-ness, including the fail-closed and raw-Allow cases. The oracle is direct Cedar FFI: it cannot speak for the upstream Dogwood authorizer, whose entity-store, temporal and provider pipeline were not executed. | F1, F2 |
| Q1-07 D3 readiness | Partially met. The backend question is answered; the plan's scope/entity mode is undecided (QD3-01), the upstream-authorizer differential is still unexecuted (QD3-08), and hosted CI is red (QD3-03). | D3 plan review, CI evidence |

## Findings

### QD3-01 — The plan's schema-always and bare-scope-entity constraints are not jointly satisfiable

**Severity:** Medium.

**Binding requirement:** Q1-04 (reproduce the upstream entity-store construction or document the different fail-closed mode) against the D3 plan's constraint that every request must carry the augmented schema and must not transport action entities.

**Evidence.** The upstream authorizer builds its store in three tiers: caller-supplied entities are schema-checked; the scope principal and resource are then added bare and unchecked for uids the caller did not supply; the schema's action entities are merged last (`dogwood-language/src/api.rs`, `build_entities`). CedarSharp's request carries one flat entity list plus an optional schema, and when the schema is attached every supplied entity is conformance-checked. The two consequences cannot both be satisfied by one call:

- A bare scope entity that omits a required attribute fails the whole schema-carrying call: "entity does not conform to the schema: expected entity `App::Session::\"s1\"` to have attribute `token`, but it does not" (C4). Upstream adds that same entity *after* validation, unchecked.
- Without the schema, the action hierarchy disappears and `action in App::Action::"ReadGroup"` denies silently unless action entities are supplied explicitly (C2 versus C1).

**Reproduction.** `docs/reviews/d3-e1-qualification-01/run.ps1`, cases `Q1-03-C1`, `Q1-03-C2`, `Q1-03-C3`, `Q1-03-C4` in `evidence/probe-results-net8.0.json`.

**D3 impact.** D3-3 (entity-store construction) and D3-4 (schema and request conformance) currently specify mutually exclusive transports. An adapter that always carries the schema cannot also inject upstream's bare scope entities, so for policies that read scope attributes it will report a different diagnostic than upstream (upstream: the attribute is missing; direct Cedar: the entity does not exist). Both fail closed on the outcome; the diagnostic mode differs.

**Remediation / deferred gate.** D3-3 and D3-4 must state one mode explicitly and requalify it. The reviewer verified that the upstream-shaped mode is executable with public APIs: `CheckEntities`/`CheckContext`/`CheckFullRequest` against the augmented schema, then a schema-less authorization carrying the bare scope principal and the synthesized action entities. That path produced the upstream-equivalent Allow with no errors in the executed case (C3), and it also keeps the scope-conformance guarantee that the schema-carrying path provides (QD3-02). The mode chosen must be recorded as a documented difference from upstream unless the upstream-authorizer differential (QD3-08) is executed.

### QD3-02 — A schema-less authorization allows a principal outside the action's `appliesTo`

**Severity:** Medium.

**Binding requirement:** Q1-03 and the D3 plan's schema-and-request-conformance package (D3-4), including its "conformance failures fail closed" gate.

**Evidence.** With the augmented schema attached and `validateRequest` enabled, an out-of-schema principal is rejected: the operation fails with "principal type `App::Ghost` is not declared in the schema" and no decision is produced. The same request, same policy and same context evaluated schema-less without request validation returns **Allow** — the policy constrains only the action, so nothing rejects the principal (B9).

**Reproduction.** `Q1-02-B9` in `evidence/probe-results-net8.0.json`; the two runs differ only in whether the schema is carried and validated.

**D3 impact.** The schema-less reconstruction route (QD3-01) and the plan's "schema always" rule pull in opposite directions here. If the adapter adopts a schema-less evaluation path, it must carry out the scope check itself — `CheckScope`/`CheckRequest`/`CheckFullRequest` — on every request, and must not treat "no conformance error" as optional.

**Remediation / deferred gate.** D3-4 must require a scope check on every request regardless of the transport mode, and the D3 qualification corpus must contain a negative case with a principal type outside `appliesTo` for both modes.

### QD3-03 — Hosted CI is red at the qualified revision

**Severity:** Medium.

**Binding requirement:** The qualification scope's hosted-CI element and the D3 plan's regression gate ("existing D1 and D2 suites unchanged and passing").

**Evidence.** Run [44683180759](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44683180759) on `001bd3e` fails on all three RIDs. The failures are not CedarSharp or D3-E1 semantic failures:

- `win-x64` and `linux-x64` fail in step "Verify the archived D2 corpus and decoder controls against R2" with "R2 producer verification requires a clean checkout with intact historical evidence". The guard in `eng/Verify-D2R2.ps1` requires `git diff --name-only b79fdff… HEAD -- docs/reviews …` to be empty; the D2 R2 requalification added `docs/reviews/d2-qualification-02/**` after that archived revision. The same guard is non-empty at the preceding green commit `9db149d`, so the conflict pre-dates the D3-E1 commits; commit `28073db` carried the `[skip ci]` marker, so `001bd3e` was the first producer-verification run to exercise it.
- `osx-arm64` fails earlier, in "Verify native integration on both frameworks", because the runner could not resolve `index.crates.io`/`static.crates.io` and `cargo` failed to download `bytes 1.12.2`; the artifact upload then failed with `ENOTFOUND`.

Before those failures the jobs passed the substantive checks: the D2 differential reported "DIFFERENTIAL cases=39 failures=0" on both `win-x64` and `linux-x64`, and the D1 independent differential reported "message mismatches: 0; wire length mismatches: 0".

**Reproduction.** `gh run view 44683180759 --log-failed`; the guard reproduction is `git diff --name-only b79fdffa07ac45cb49c5ee2ef100299f2d2adcf5 001bd3e930011ca8de7098087b0ac1bafdcad30f -- docs/reviews docs/d1-acceptance.md .github/workflows/d1-qualification.yml .github/workflows/d2-qualification.yml`. The full record is `evidence/ci-status.json`.

**D3 impact.** D3-6's lifecycle gate and D3-8's acceptance gate assume a green baseline. The D3-E1 slice changed no file under `docs/reviews` and no D1/D2 behavior, so this is a pre-existing process conflict that the delivery surfaced rather than caused — but a red baseline cannot be inherited by D3.

**Remediation / deferred gate.** Before D3 implementation starts, the producer must resolve the guard conflict (re-pin the archived revision or scope the guard to the files it actually owns), re-run the producer verification workflow to green on the three RIDs, and record the macOS runner network failure as an infrastructure retry. This qualification does not modify the workflow or the archived harness.

### QD3-04 — Determining-policy ID order is unstable between identical requests

**Severity:** Low.

**Binding requirement:** Q1-02 (map determining and erroring IDs back by identity; do not rely on policy-set enumeration order).

**Evidence.** Five identical authorizations of a three-permit set produced determining orders `["p2","p0","p1"]`, `["p1","p0","p2"]`, `["p2","p1","p0"]`, `["p2","p0","p1"]`, `["p0","p2","p1"]` in one process — four distinct orders over five runs. The set is stable; the order is not (A4, `orderStableAcrossRuns: false`). The upstream authorizer likewise forwards Cedar's determining IDs without imposing an order (`api.rs`, `decide`).

**Reproduction.** `Q1-01-A4` in `evidence/probe-results-net8.0.json`.

**D3 impact.** Any D3 surface that exposes determining policies as an ordered list, or that compares them positionally against the source rule order, will produce unstable output and unstable tests.

**Remediation / deferred gate.** D3-2 must specify set semantics (or an explicit deterministic sort) for determining and erroring IDs, and the D3 qualification corpus must assert set equality rather than sequence equality.

### QD3-05 — A text-form policy set exposes no policy IDs

**Severity:** Low.

**Binding requirement:** Q1-01 (never reparse concatenated text for identity).

**Evidence.** A three-policy set built with `CedarPolicySet.FromText` reports an empty `PolicyIds` collection, while its determining set still names the positional `policy0` for the first policy — the `@id("custom-a")` annotation in the same source is ignored for identity (A3). The paired `FromPolicies` route keeps the exported IDs `q1_0`, `q1_1`, `q1_2` and names `q1_0` in the determining set (C6). By contrast, `FromPolicies` preserves caller IDs exactly, including the non-sequential set `z-rule-9`, `a-rule-42`, `m-rule-0` (A1) and its reordering (A2).

**Reproduction.** `Q1-01-A1`, `Q1-01-A2`, `Q1-01-A3`, `Q1-03-C6` in `evidence/probe-results-net8.0.json`.

**D3 impact.** Q1-01 is confirmed and strengthened: the text route loses not only the exported IDs but the ability to enumerate IDs at all. D3-1 must keep the per-rule native export and the `FromPolicies` pairing; a text-form set is not an acceptable fallback.

**Remediation / deferred gate.** D3-1 should treat a text-form policy set as a defect if it is ever used for identity, and the D3-2 error surface must always report the exported rule ID.

### QD3-06 — The two exported schema forms are not value-equal

**Severity:** Low.

**Binding requirement:** The D3 plan's "schema/entity round trip" gate ("both schema forms") and its schema transport constraint.

**Evidence.** `CedarSchema.FromJson(lowered.CedarSchemaJson)` and `CedarSchema.FromText(lowered.CedarSchemaText)` are not `Equals` (CedarSharp compares schemas by upstream JSON), yet both authorize identically: same decision, same determining set `q1_0`, same empty policy-error set (C5, `valueEqual: false`, `behaviorallyEqual: true`).

**Reproduction.** `Q1-03-C5` in `evidence/probe-results-net8.0.json`.

**D3 impact.** A D3 adapter that caches, memoizes or compares schemas with `CedarSchema.Equals` will treat one augmented schema as two distinct values, depending on which export form it received. The same schema can also reach the engine in two shapes, so a cache keyed on the wrong form silently misses.

**Remediation / deferred gate.** D3-4 must canonicalize one export form (the JSON export is the natural candidate, as it is the D2 artifact) for transport, caching and equality, and the corpus must assert that the other form is never used as a cache key.

### QD3-07 — A RID-specific NativeAOT publish mutates the committed lockfile

**Severity:** Low.

**Binding requirement:** The D3 plan's restore/build gate ("locked restore where the RID graph permits") and D3-6 packaging.

**Evidence.** Publishing the reviewer's AOT consumer with `-r win-x64` caused `src/DogwoodSharp/packages.lock.json` to be rewritten in the working tree (a whitespace/line-ending-only difference; no dependency version changed). The reviewer restored the file before delivery, so the committed lockfile is untouched and no dependency pin moved.

**Reproduction.** `docs/reviews/d3-e1-qualification-01/aot.ps1` followed by `git status --porcelain` in the canonical checkout.

**D3 impact.** A D3 packaging pipeline that publishes for a RID in the canonical checkout will dirty the tree, break clean-checkout verification and can mask real dependency drift.

**Remediation / deferred gate.** D3-6 must publish from a disposable copy or restore the lockfile afterwards, and the D3 evidence gate must include a clean-tree check after the AOT publish.

### QD3-08 — Upstream-authorizer equivalence is still uncalibrated

**Severity:** Medium.

**Binding requirement:** Q1-04, the producer's own open item "Differential against the upstream Dogwood authorizer — not executed", and the D3 plan's differential gate, which requires "the pinned Cedar FFI and, for entity/temporal claims, the upstream Dogwood authorizer".

**Evidence.** This qualification also did not execute the upstream Dogwood authorizer: the reference binary wraps `cedar_policy::ffi` directly. What the reviewer adds is a narrowing of the known gap, not its closure. For the `has` family on a scope principal the caller did not supply, direct Cedar 4.13.0 evaluates `has` as false and allows, exactly as upstream's injected bare entity does: absent principal → Allow with no errors; bare principal → Allow; principal carrying the attribute → Deny; schema-carrying absent principal → Allow (B8). The Cedar 4.11.0 reference agrees on the same envelope (F2 `has-absent-principal`). The producer's corpus separately records that reading a *required attribute* of an absent scope entity errors with "entity … does not exist" in direct Cedar, where upstream's bare entity reports the attribute as missing — a diagnostic-mode difference with the same fail-closed outcome.

**Reproduction.** `Q1-02-B8`, `Q1-06-F2-has-absent-principal`, `Q1-06-F2-has-bare-principal`, `Q1-06-F2-has-attributed-principal` in `evidence/probe-results-net8.0.json`.

**D3 impact.** The backend choice does not depend on closing this gap, but any D3 claim of upstream-authorizer equivalence does. D3-7's corpus must be executed against the upstream authorizer before D3-8's acceptance gate, and the upstream entity-store, temporal and provider pipeline are exactly where the two engines differ.

**Remediation / deferred gate.** D3-7 must add an upstream-authorizer differential over the D3 corpus (entity construction, scope attributes, temporal bindings, provider failures) or D3 must state, in its public posture, that equivalence is not claimed. This finding is deferred, not waived.

### QD3-09 — The D2 lowering's strict validation bounds the adapter's input space

**Severity:** Info.

**Binding requirement:** The D2 lowering contract that D3 consumes ("a complete, validated D2 lowering result") and the D3-7 corpus design.

**Evidence.** A policy that reads an optional attribute without a `has` guard cannot be exported by the D2 bridge: the lowering reports `artifactsExported: false`, no stage error and no exception, because the rejection is a Cedar validation finding rather than a pipeline failure (B12). CedarSharp evaluates the same policy when it is supplied directly, and then fails closed with the policy error "entity `App::User::\"bob\"` does not exist" (B12 `directEvaluation`, B6). The guarded form (`resource.owner has name && resource.owner.name == "n"`) lowers cleanly — one exported rule, `q1_0` — and then fails closed on both engines (B12 `guarded`, F2 `owner-missing-entity`).

**Reproduction.** `Q1-02-B12`, `Q1-02-B6`, `Q1-06-F2-owner-missing-entity` in `evidence/probe-results-net8.0.json`.

**D3 impact.** The adapter's input space is the D2-validated subset, not all of Cedar. A D3 corpus built from arbitrary Cedar policies will silently fail to lower, and a missing artifact must be reported as a lowering failure rather than an empty policy set.

**Remediation / deferred gate.** D3-7 must construct its corpus from D2-lowered fixtures and must assert that a non-exported lowering is surfaced as an error, not as an empty evaluation.

## D3 readiness and authorization

**D3-E1 readiness for owner acceptance: ready, with the qualifications above.**
The E1 feasibility question is answered for the executed scope: CedarSharp 1.0.0
evaluates D2-lowered output without a CedarSharp change, agrees with the
pinned Cedar 4.11.0 reference on every executed envelope, fails closed on the
conformance, temporal and asset boundaries, and behaves identically under
NativeAOT on both TFMs. The reviewer recommends accepting the E1 evidence
under Q1-01…Q1-08 and the nine findings. **This review does not grant that
acceptance**; the owner decides.

**Full D3 implementation: not authorized by this qualification.**
The D3 plan's own entry conditions are not all met. Owner acceptance of the
E1 evidence is outstanding; the plan requires the implementation commission
to name the supported RIDs and TFMs while E1 executed only Windows `win-x64`;
the hosted baseline is red (QD3-03); the scope/entity mode is undecided
(QD3-01) with its scope-conformance consequence (QD3-02); and the
upstream-authorizer differential required by the plan's differential gate has
not been executed (QD3-08). D3 may proceed to implementation planning and
corpus design, but no full-D3 execution or packaging is qualified by this
report.

**More feasibility work: not required for the backend decision; required for
the equivalence claim.**
No further feasibility work is needed to decide the backend: the executed
evidence supports CedarSharp with qualifications, and no CedarSharp change or
alternative backend is indicated. The remaining work is implementation and
qualification work under D3-1…D3-8 — including the upstream-authorizer
differential (QD3-08), which is feasibility-grade work against the *upstream*
pipeline and must precede any upstream-equivalence claim, and the resolution
of the scope/entity mode (QD3-01), which is a design decision with a bounded
re-qualification cost.

## Exclusions and limitations

- Windows `win-x64` only; `linux-x64` and `osx-arm64` were not executed by
  the reviewer. The macOS producer job failed on a runner network fault
  (QD3-03).
- The upstream Dogwood authorizer was not executed (QD3-08). The Cedar 4.11.0
  FFI reference is supporting evidence only.
- No concurrency, throughput, memory or unload behavior was measured. The
  reviewer did not attempt to unload either native image, and makes no
  lifetime claim beyond the process-lifetime residency observed in the
  coexistence and AOT runs.
- No provider, open-ended or large-scale policy corpus was executed; the
  temporal work covered one lowered temporal policy and its collision and
  missing-field controls.
- The reviewer did not re-run the D2 six-cell matrix, provision Linux or
  macOS environments, or modify any production artifact.
- The AOT consumer exercises only JSON inputs; this is not a general trimming
  guarantee for arbitrary CedarSharp object overloads.
- The reviewer's expectation set is a reviewer artifact: it is a hand-derived
  set over the fixtures in `evidence/probe/Program.cs`, not an upstream
  conformance suite.

## Evidence index

| Artifact | Content |
| --- | --- |
| `evidence/probe/Program.cs`, `evidence/probe/Probe.csproj` | Reviewer harness: 49 hand-derived cases, both TFMs |
| `evidence/probe-aot/Program.cs`, `evidence/probe-aot/AotProbe.csproj` | Reflection-free NativeAOT consumer |
| `run.ps1`, `aot.ps1` | Reproduction scripts for the JIT and AOT evidence |
| `evidence/probe-results-net8.0.json`, `evidence/probe-results-net10.0.json` | Full per-case results, expectations, observations and verdicts |
| `evidence/probe-console-net8.0.log`, `evidence/probe-console-net10.0.log` | Harness console transcripts |
| `evidence/asset-identity.json` | Baseline revision, tree, worktree state and native asset hashes |
| `evidence/aot-results-*.json`, `evidence/aot-summary-*.json`, `evidence/aot-console-*.log`, `evidence/aot-publish-*.log` | NativeAOT runs: staged, unstaged, unstaged-again, per TFM |
| `evidence/ci-status.json` | Hosted CI run history, failure isolation and guard analysis |
| `evidence/producer-prototype-tests-net8.0.log`, `evidence/producer-prototype-tests-net10.0.log` | Producer suite re-executed by the reviewer (34/34 per TFM) |
| `evidence/SHA256SUMS` | SHA-256 over the committed (LF-normalized) bytes of the harness sources, the scripts and the evidence artifacts, excluding itself; verify from a fresh clone with `sha256sum -c evidence/SHA256SUMS` from this directory |

Reproduction on Windows `win-x64`:

```powershell
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:USERPROFILE\.cargo\bin;$env:Path"
& "$env:LOCALAPPDATA\PowerShell\pwsh.exe" -NoProfile -ExecutionPolicy Bypass -File docs\reviews\d3-e1-qualification-01\run.ps1
& "$env:LOCALAPPDATA\PowerShell\pwsh.exe" -NoProfile -ExecutionPolicy Bypass -File docs\reviews\d3-e1-qualification-01\aot.ps1 -TargetFramework net8.0
& "$env:LOCALAPPDATA\PowerShell\pwsh.exe" -NoProfile -ExecutionPolicy Bypass -File docs\reviews\d3-e1-qualification-01\aot.ps1 -TargetFramework net10.0
```

## Cleanup state

The reviewer worked in the canonical checkout on `main` and created no branch,
worktree or linked checkout. Build outputs stay inside ignored directories
(`**/bin/`, `**/obj/`, `**/target/`). The NativeAOT publish output was written
to the disposable temporary directory and is not delivered. The one tracked
file touched by a tool side effect, `src/DogwoodSharp/packages.lock.json`, was
restored to the committed content (QD3-07); the dependency pins are unchanged.
The Cedar 4.11.0 reference was rebuilt with `--locked`, leaving
`native/dogwoodsharp-e1-ref/Cargo.lock` unchanged. `git status` shows only the
new `docs/reviews/d3-e1-qualification-01/` tree.

The delivery contains qualification documents and evidence only: no production
source change, no CedarSharp change, no dependency or pin change, no package,
tag, release or publish.
