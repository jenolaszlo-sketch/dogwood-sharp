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
| D3-1 per-rule export | `native/dogwoodsharp-bridge/src/d2.rs` (`Rule.cedar_body`, `Artifacts.generated_context_fields`) | Exact `(cedar_policy_id, cedar_body)` rendered from the compiled `cedar_policy::Policy`, never split from `cedar_policy_text`; all-or-nothing under the existing bounded response. |
| D3-1 managed reader | `src/DogwoodSharp/Dogwood.cs` (`PolicyRuleMapping.CedarPolicyBody`, `DogwoodLoweredPolicySet.GeneratedContextFields`), `src/DogwoodSharp/NativeBridge.cs` (`ReadRules`, `ReadGeneratedContextFields`) | Strict wire-contract reader; enforces `GeneratedContextFields` non-empty exactly when `SelfContainedCedar` is false. |
| D3-2/3/4/8 adapter | `src/DogwoodSharp.CedarSharp/` (namespace `DogwoodSharp.Experimental.CedarSharp`) | `DogwoodCedarAuthorizer`, `CedarAuthorizationInput`, `CedarAuthorizationResult`, diagnostics, `CedarBundleException`. `CedarPolicySet.FromPolicies` from per-rule pairs; canonical schema JSON on every request; `validateRequest=true` fixed; clean-Allow-only authorization. |
| D2 differential expansion | `native/dogwoodsharp-d2-diff/src/{oracle.rs,main.rs}`, `tests/DogwoodSharp.D2Conformance/lowering-v1.json` | The pinned fixture and the crate-derived `Expect` now carry `cedarBody` per rule and `generatedContextFields`; `verify` compares them (D2 bridge operation-change requalification, contract §15). |
| Native regression tests | `native/dogwoodsharp-bridge/src/tests.rs` (`d2_export_supplies_exact_per_rule_bodies`, `d2_export_reports_generated_context_fields`) | Exact per-id body pairing derived from the crate; reserved-field/self-containment reporting. |
| Managed D3 tests | `tests/DogwoodSharp.CedarSharp.Tests/CedarSharpAdapterTests.cs` | Decision, forbid precedence, clean-Allow, raw-Allow-with-error, scope rejection, malformed input, identity mapping, temporal fail-closed, concurrency, lifecycle. |
| Verification wiring | `eng/Verify.ps1`, `DogwoodSharp.slnx` | Stages the verified bridge and runs the D3 tests on both TFMs inside the existing per-RID verification (JIT matrix). |

## 3. Public API (experimental)

`DogwoodSharp.Experimental.CedarSharp`:

- `DogwoodCedarAuthorizer.ForLoweredBundle(DogwoodLoweredPolicySet)` — owns one immutable
  bundle; throws `CedarBundleException` on incomplete, mispaired, duplicated, or
  schema-inconsistent bundles; never builds an empty set that continues.
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

## 4. Binding-requirement compliance

| Contract requirement | Implementation | Test |
| --- | --- | --- |
| Per-rule `FromPolicies`; no text-split identity | `d2.rs` export + `NativeBridge` reader + `ForLoweredBundle` | native `d2_export_supplies_exact_per_rule_bodies`; d2-diff `verify` |
| Canonical schema every request; fixed `validateRequest=true` | adapter | managed malformed/scope tests; d2-diff schema compare |
| Scope/context/entity conformance rejected | adapter + CedarSharp request validation | managed principal/resource/action scope tests |
| Exact caller entities; no synthesis; no bare scope | adapter forwards unchanged | managed raw-Allow-with-error; d2-diff entity cases |
| Clean-Allow-only authorization | `Authorized = IsCleanAllow` | managed clean-Allow + raw-Allow-with-error tests |
| Raw vs enforcement decisions separated | `Decision` vs `Authorized` | managed raw-Allow-with-error |
| Unknown diagnostic id fails closed | identity subset check | native/d2-diff pin; managed identity test |
| Distinct error categories/stages | `CedarDiagnosticCategory`, `CedarAuthorizationStage` | managed scope/malformed/temporal tests |
| Temporal reserved-field rejection + trusted provenance | `generated_context_fields` export + adapter guard | native generated-field test; d2-diff `validated-temporal-with-distincter`; managed temporal fail-closed |
| Both native images resident; disposal no-op | adapter `Dispose` | managed lifecycle test; E1 coexistence evidence (bounded) |
| Experimental surface, out of stable package | `DogwoodSharp.CedarSharp` project, `IsPackable=false` | (packaging/extracted at qualification) |
| D2/D1 behavior preserved | additive native fields + additive managed props | full D2 differential + LoweringTests + D1 consumer regression |
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
and the native crates:

- Rust bridge `cargo fmt --check`, `cargo clippy -D warnings`, `cargo test` — **15 passed, 0 failed**.
- Rust `d2-diff` `fmt`/`clippy` + `verify` against the rebuilt bridge — **39 cases, 0 failures**.
- `dotnet restore --locked-mode`, `dotnet build -c Release`, `dotnet format --verify-no-changes` — **0 warnings, 0 errors** across the solution including the new projects.
- `dotnet test` D1/D2 (`DogwoodSharp.Tests`) — **69 passed** on `net8.0` and `net10.0`.
- `dotnet test` D3 (`DogwoodSharp.CedarSharp.Tests`) — **17 passed** on `net8.0` and `net10.0`.
- Foundation smoke — passed on both TFMs.

The extension keeps the **managed** reader backward compatible (the new fields are optional, and
`PolicyRuleMapping` equality stays over the `(index, id, source)` triple). It does **not** keep the
bridge's raw operation-3 reply shape unchanged: the archived D2-R2 regen asserts the exact operation-3
key set and fails on a fresh bridge (see [the D3-IMPL-01 review disposition](reviews/d3-impl-01/README.md)).
The contract-permitted remediation is to ship the export as a dedicated operation rather than extend
operation 3. **Hosted CI was not run** because the win-x64 D2-R2 regen is red pending this remediation.

Linux/macOS cells and NativeAOT for the D3 path were not executed; Windows-only evidence is local
only.

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