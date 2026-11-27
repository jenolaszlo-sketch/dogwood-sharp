# DOGWOOD-SHARP-D3 implementation (D3-IMPL-01)

Status: **producer implementation** of the schema-carrying CedarSharp authorization
adapter defined by the accepted [D3 integration contract](d3-integration-contract.md)
and [D3-E1 qualifications](d3-e1-acceptance.md). This document records what was
implemented, where, how the binding requirements map to implementation and tests, the
dependency identities, executed local verification, and the gates that remain for
independent qualification. It is producer evidence, not acceptance.

## 1. Scope and dispositions

D3-IMPL-01 implements the production CedarSharp integration authorized after
D3-PREP-01. Independent qualification, owner acceptance, package-only qualification,
publication, tags, and releases remain **separate milestones** and are out of scope here.

The implementation follows the binding
[implementation contract](d3-e1-implementation-plan.md):
the schema-carrying, request-validating mode; exact per-rule policy identity; a
separate clean-Allow enforcement decision; fail-closed behavior; an experimental
public surface; and no schema-less fallback.

## 2. Production implementation locations

| Work package | Production location | Notes |
| --- | --- | --- |
| D3-1 per-rule export | `native/dogwoodsharp-bridge/src/d2.rs` (`Export::D3`, `Rule.cedar_body`, `Artifacts.generated_context_fields`), `src/lib.rs` (`OPERATION_EXPORT = 4`) | ABI v1 operation 4: the same pipeline as operations 2 and 3, plus the exact `(cedar_policy_id, cedar_body)` rendered from the compiled `cedar_policy::Policy` and the trusted generated context-field names. Operation 3 keeps its frozen reply shape (D3-IMPL-01 R1 remediation). |
| D3-1 managed reader | `src/DogwoodSharp/Dogwood.cs` (`Dogwood.ExportCedar`, `DogwoodCedarExport`, `CedarPolicyRule`), `src/DogwoodSharp/NativeBridge.cs` (`OperationExport`, `ReadExportedResult`, `ReadCedarRules`, `ReadGeneratedContextFields`) | Strict wire-contract reader for operation 4. The D2 (operation 3) surface is unchanged: `PolicyRuleMapping` and `DogwoodLoweredPolicySet` carry no per-rule body. The D3 types and entry point are `internal`: the archived D1/D2 consumer qualification asserts this package's exact public type and member surface, so the D3 surface is consumed only through the experimental adapter. |
| D3-2/3/4/8 adapter | `src/DogwoodSharp.CedarSharp/` (namespace `DogwoodSharp.Experimental.CedarSharp`) | `DogwoodCedarAuthorizer`, `CedarAuthorizationInput`, `CedarAuthorizationResult`, diagnostics, `CedarBundleException`. `CedarPolicySet.FromPolicies` from operation 4 per-rule pairs; canonical schema JSON on every request; `validateRequest=true` fixed; clean-Allow-only authorization. No fallback to the operation 3 reply. |
| D2 differential expansion | `native/dogwoodsharp-d2-diff/src/main.rs`, `native/dogwoodsharp-d2-diff/src/oracle.rs`, `tests/DogwoodSharp.D2Conformance/lowering-v1.json` | The pinned fixture and the crate-derived `Expect` carry `cedarBody` per rule and `generatedContextFields`; `verify` now compares operations 2, 3 and 4, projecting the D3 fields onto operation 4 only and asserting operation 3's exact frozen shape (D2 bridge operation-change requalification, contract §15). |
| Native regression tests | `native/dogwoodsharp-bridge/src/tests.rs` (`d3_export_supplies_exact_per_rule_bodies`, `d3_export_reports_generated_context_fields`, `d3_export_does_not_change_the_frozen_operation_3_reply`) | Exact per-id body pairing derived from the crate; reserved-field/self-containment reporting; operation 3's exact key set and operation 3/4 equivalence on every shared field. |
| Managed D3 tests | `tests/DogwoodSharp.CedarSharp.Tests/CedarSharpAdapterTests.cs` | Decision, forbid precedence, clean-Allow, raw-Allow-with-error, scope rejection, malformed input, identity mapping, temporal fail-closed (unexported and validated), concurrency, lifecycle, operation 3/4 surface boundary, per-rule identity rebuild. |
| Verification wiring | `eng/Verify.ps1`, `DogwoodSharp.slnx` | Stages the verified bridge and runs the D3 tests on both TFMs inside the existing per-RID verification (JIT matrix). |

## 3. Public API (experimental)

`DogwoodSharp.Experimental.CedarSharp`:

- `DogwoodCedarAuthorizer.ForLoweredBundle(string source, DogwoodSchema schema)` — performs the
  operation 4 export and owns one immutable bundle; throws `CedarBundleException` on incomplete,
  mispaired, duplicated, or schema-inconsistent bundles; never builds an empty set that continues.
  - `PolicyIds`, `CanonicalSchemaJson`, `SelfContained`, `RuleIndexFor(id)`.
  - `Authorize(CedarAuthorizationInput)` -> `CedarAuthorizationResult`.
  - `Dispose()` — managed references only; never unloads a native image.
- `CedarAuthorizationInput` — `CedarEntityRef` principal/action/resource + `ContextJson`,
  `EntitiesJson`. No CedarSharp type is exposed; the adapter maps its own identifiers.
- `CedarAuthorizationResult` — `OperationFailed`, `Stage`, `Decision` (raw),
  `Authorized` (clean-Allow-only enforcement), `IsCleanAllow`,
  `DeterminingPolicyIds` (set), `PolicyErrors` (keyed by generated id),
  `Errors` (categorized).
- Categories and stages are stable; diagnostic wording is informative only, per contract §6.

`DogwoodSharp` gains the operation 4 entry point that produces the bundle, **internal** to that
assembly: `Dogwood.ExportCedar(source, schema)` -> `DogwoodCedarExport` / `CedarPolicyRule`, read by
`NativeBridge.ReadExportedResult`. The archived D1/D2 consumer qualification asserts the exact
public type and member surface of `DogwoodSharp` (`GetExportedTypes()` and the `Dogwood` static
member list), so the D3 surface is not part of the stable public package; the experimental adapter
and its tests are its friend assemblies. `Dogwood.Lower` and the rest of the D2 surface are
unchanged: `PolicyRuleMapping` has no per-rule body and `DogwoodLoweredPolicySet` has no
generated-context-field list. An unavailable operation 4 surfaces as a `DogwoodBridgeException`;
nothing substitutes the operation 3 reply.

## 4. Binding-requirement compliance

| Contract requirement | Implementation | Test |
| --- | --- | --- |
| Per-rule `FromPolicies`; no text-split identity | operation 4 export + `NativeBridge` reader + `ForLoweredBundle` | native `d3_export_supplies_exact_per_rule_bodies`, `d3_export_does_not_change_the_frozen_operation_3_reply`; d2-diff `verify` |
| Canonical schema every request; fixed `validateRequest=true` | adapter | managed malformed/scope tests; d2-diff schema compare |
| Scope/context/entity conformance rejected | adapter + CedarSharp request validation | managed principal/resource/action scope tests |
| Exact caller entities; no synthesis; no bare scope | adapter forwards unchanged | managed raw-Allow-with-error; d2-diff entity cases |
| Clean-Allow-only authorization | `Authorized = IsCleanAllow` | managed clean-Allow + raw-Allow-with-error tests |
| Raw vs enforcement decisions separated | `Decision` vs `Authorized` | managed raw-Allow-with-error |
| Unknown diagnostic id fails closed | identity subset check | native/d2-diff pin; managed identity test |
| Distinct error categories/stages | `CedarDiagnosticCategory`, `CedarAuthorizationStage` | managed scope/malformed/temporal tests |
| Temporal reserved-field rejection + trusted provenance | operation 4 `generatedContextFields` + adapter guard | native generated-field test; d2-diff `validated-temporal-with-distincter`; managed validated-temporal test |
| Both native images resident; disposal no-op | adapter `Dispose` | managed lifecycle test; E1 coexistence evidence (bounded) |
| Experimental surface, out of stable package | `DogwoodSharp.CedarSharp` project, `IsPackable=false` | (packaging/extracted at qualification) |
| D2/D1 behavior preserved | dedicated operation; operation 3 surface untouched | full D2 differential + LoweringTests + D1 consumer regression + archived D2-R2 regen |
| Version pins unchanged | Dogwood 1.1.2 @`929f49e…`; Cedar 4.11.0; CedarSharp 1.0.0 / Cedar 4.13.0 | d2-diff pin asserts; native identity |

## 5. Dependency identities

Pins are unchanged from the accepted contract:

- Upstream Dogwood `dogwood-language` 1.1.2 @ `929f49e7305534ac61ef06cf8e47269c596dc851`
- Bridge Cedar `4.11.0`
- CedarSharp `1.0.0` / Cedar `4.13.0` (native assets `win-x64`, `linux-x64`, `osx-arm64`)
- .NET `net8.0`, `net10.0`; Rust `1.99.1`

CedarSharp is added only to the new experimental `DogwoodSharp.CedarSharp` project (central
package management). The stable `DogwoodSharp` package's dependency graph is unchanged.

## 6. Local verification (Windows x64)

Executed locally on `win-x64` (both TFMs) via `eng/Verify.ps1 -RuntimeIdentifier win-x64 -SkipAot`
and the native crates, after the D3-IMPL-01 R1 remediation (operation 4 replaces the operation-3
extension):

- Rust bridge `cargo fmt --check`, `cargo clippy -D warnings`, `cargo test` — **16 passed, 0 failed**
  (13 retained D1/D2 tests + 3 D3 export tests: per-rule bodies, generated context fields, and the
  frozen operation 3 reply).
- Rust `d2-diff` `fmt`/`clippy` + `verify` against the rebuilt bridge — **39 cases, 0 failures**,
  comparing operations 2, 3 and 4.
- `dotnet restore --locked-mode`, `dotnet build -c Release`, `dotnet format --verify-no-changes` —
  **0 warnings, 0 errors** across the solution including the new projects.
- `dotnet test` D1/D2 (`DogwoodSharp.Tests`) — **69 passed** on `net8.0` and `net10.0`. The archived
  D2 corpus assertions on the exact operation 3 reply shape are unchanged and green.
- `dotnet test` D3 (`DogwoodSharp.CedarSharp.Tests`) — **21 passed** on `net8.0` and `net10.0`.
- Foundation smoke — passed on both TFMs.

The remediation removes the operation-3 extension entirely: operation 3 keeps its qualified reply
shape (`d2-diff` flags any undeclared reply field, and the D2 suite asserts the exact key set), and
the per-rule bodies plus the trusted generated context-field names are reported only by the new
operation 4. The managed D2 surface is likewise unchanged — `PolicyRuleMapping` has no per-rule body
and `DogwoodLoweredPolicySet` has no generated-context-field list — while `Dogwood.ExportCedar`
reads operation 4 with a strict, all-or-nothing reader.

Also executed locally on `win-x64` after the remediation:

- `eng/Verify-D2R2.ps1 -RuntimeIdentifier win-x64` — **PASS**. The archived D2 corpus (35 cases), the
  independent ABI corpus and the decoder controls (`controls=3 rejected=18
  partial-schema-text-accepted=False`) are green on a bridge that now answers operation 4, because
  operation 3's reply shape is restored exactly.
- The D3 upstream differential probe (pinned upstream authorizer oracle through the production
  adapter on the operation 4 export) — **16 cases, 0 failures** on `net8.0` and `net10.0`. The
  recorded differences are reproduced unchanged by the operation 4 path.

Linux/macOS cells and NativeAOT for the D3 path were not executed locally; Windows-only evidence is
local only. The hosted RID/TFM matrix (including the archived D2-R2 regen job, the archived D1 consumer
regression and NativeAOT for the D2/D1 paths) ran to completion and passed: workflow run `44993430717`
(head `f7a1c8154e12d137ec9246639763456663eb237c`), jobs `137119270361` (win-x64), `137119270108`
(linux-x64, also Valgrind and the resident-workload measurement) and `137119270291` (osx-arm64), all
`success`. See `docs/reviews/d3-impl-01/README.md`.

## 7. Known limitations and remaining qualification gates

Producer implementation does not qualify the following; each remains a gate:

- **Upstream differential** — the calibrated D3 corpus run against the pinned upstream
  authorizer via the production adapter (see `docs/reviews/d3-impl-01/`).
- **Concurrency qualification** — the adapter is exercised concurrently in tests, but the
  bounded four-worker concurrency gate and the public thread-safety statement are a
  qualification obligation (integration contract §9).
- **Temporal/provider execution** — D3-5 (trusted pipeline) is a separate commission. This
  stage implements reserved-field rejection and fail-closed for non-self-contained lowerings
  and does **not** implement the history-replay temporal engine or provider resolver.
- **NativeAOT + package-only consumption** for the D3 path.
- **Full hosted matrix** — `linux-x64`, `osx-arm64` cells run in hosted CI.
- **Independent qualification and owner acceptance** — separate milestones.

## R2 implementation update (D3-IMPL-01-R2)

The R2 implementation-stage work is complete. The trusted temporal/provider execution path
runs through the production DogwoodCedarAuthorizer.AuthorizeWithTrustedRuntime API, the
native operation 4 runtime binding uses the pinned upstream Authorizer with
InMemoryTemporalEngine and a deterministic ProviderResolver, and provider method chains
are rejected rather than executed through Rhai.

The R2 upstream differential corpus covers all C-01 through C-26 mandatory families with 84
cases across agreement, approved-difference, and operation-failure classifications. It runs
against the pinned upstream Dogwood 1.1.2 authorizer (revision 929f49e) on both net8.0 and
net10.0 with zero failures, zero pending cases, and zero coverage errors. The previously
approved ED-01 through ED-05 differences are preserved without addition.

Concurrency is verified at four concurrent workers with 128 calls per control, covering
request-context isolation, temporal-binding isolation, provider-result isolation, and failure
isolation. The process-lifetime native image coexist
ence boundary is preserved: no unload,
no hot-replacement, and process isolation remains the recovery boundary.

See the review record for the complete requirement and gate reconciliation.

## Closure status (27 November 2026)

The producer-stage limitations above are historical. The independent Q3-R1
requalification and the owner acceptance subsequently closed D3 with the
documented residual qualifications. The authoritative successor records are
`docs/reviews/d3-qualification-01-q3-r1/README.md` and
`docs/reviews/d3-close-release-prep-01/README.md`. This producer report is not
rewritten; the closure record binds the exact reviewed identities. The preview
release candidate is prepared under `DOGWOOD-SHARP-D3-CLOSE-RELEASE-PREP-01`;
publication is not authorized by this report.
