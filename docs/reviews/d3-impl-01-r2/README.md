# DOGWOOD-SHARP-D3-IMPL-01-R2 review record

Status: **producer implementation review** for D3-IMPL-01-R2. This is producer evidence for
independent qualification; it is **not** owner acceptance.

## Disposition

**A. IMPLEMENTATION COMPLETE — READY FOR INDEPENDENT QUALIFICATION.** All mandatory
implementation-stage requirements were satisfied:

1. **R2-03 Upstream differential corpus** — 84 cases covering all C-01 through C-26 families,
   executed against the pinned upstream Dogwood 1.1.2 authorizer (revision `929f49e`) on both
   `net8.0` and `net10.0`. Zero failures, zero pending cases, zero coverage errors. ED-01 through
   ED-05 encoded exactly as previously approved.
2. **R2-02 Temporal/provider execution** — Production `DogwoodCedarAuthorizer.AuthorizeWithTrustedRuntime`
   path exercised with true/false temporal bindings, multiple temporal conditions, boundary
   condition, provider value/omission/failure/missing outcomes, reserved-field rejection, and
   policy attribution. Provider method chains rejected (no Rhai fallback).
3. **R2-03 Concurrency and lifecycle** — Four concurrent workers verified with isolation of
   request contexts, temporal bindings, and provider results; 128 calls per control; process-lifetime
   native coexistence verified.
4. **Regression** — All affected repository gates pass on `win-x64`: 32 native Rust tests, 71
   DogwoodSharp tests per TFM, 45 CedarSharp tests per TFM, D2 conformance differential 39/39.

## Requirement reconciliation

### C-01 through C-26 coverage

| Family | Status | Cases | Classification |
| --- | --- | --- | --- |
| C-01 Direct Allow | Implemented and verified | R2-C01-01, R2-C01-02 | agreement |
| C-02 Direct/default Deny | Implemented and verified | R2-C02-01, R2-C02-02 | agreement |
| C-03 Forbid precedence | Implemented and verified | R2-C03-01 | agreement |
| C-04 Multiple permits | Implemented and verified | R2-C04-01 | agreement (set semantics) |
| C-05 Entity attributes | Implemented and verified | R2-C05-01..05 | agreement, approved-difference |
| C-06 Entity hierarchy | Implemented and verified | R2-C06-01..04 | agreement, approved-difference |
| C-07 Action hierarchy | Implemented and verified | R2-C07-01..03 | agreement, approved-difference |
| C-08 Missing entities/attributes | Implemented and verified | R2-C08-01..04 | agreement, approved-difference |
| C-09 Bare scope entities | Implemented and verified | R2-C09-01..04 | agreement, approved-difference |
| C-10 Schema violations | Implemented and verified | R2-C10-01..05 | approved-difference, operation-failure |
| C-11 Scope applicability | Implemented and verified | R2-C11-01..09 | approved-difference, agreement |
| C-12 Temporal true | Implemented and verified | R2-C12-01 | agreement |
| C-13 Temporal false | Implemented and verified | R2-C13-01 | agreement |
| C-14 Temporal failure | Implemented and verified | R2-C14-01..03 | operation-failure, agreement |
| C-15 Reserved-field injection | Implemented and verified | R2-C15-01..02 | managed-control |
| C-16 Provider value | Implemented and verified | R2-C16-01 | operation-failure (both fail closed on invalid URL) |
| C-17 Provider failure | Implemented and verified | R2-C17-01..03 | operation-failure |
| C-18 Raw Allow with policy errors | Implemented and verified | R2-C18-01 | approved-difference |
| C-19 Errored forbid | Implemented and verified | R2-C19-01 | agreement |
| C-20 Malformed policy/input | Implemented and verified | R2-C20-01..05 | operation-failure, approved-difference |
| C-21 ID preservation | Implemented and verified | R2-C21-01..04 | managed-control |
| C-22 Schema transport | Implemented and verified | R2-C22-01..03 | managed-control |
| C-23 Extension values | Implemented and verified | R2-C23-01..06 | operation-failure (both fail closed on extension encoding) |
| C-24 Unicode and escaping | Implemented and verified | R2-C24-01..04 | agreement, operation-failure |
| C-25 Concurrency | Implemented and verified | R2-C25-01..04 | managed-control |
| C-26 Native/lifecycle | Implemented and verified | R2-C26-01..04 | lifecycle-control |

### ED-01 through ED-05 reconciliation

| Difference ID | Agreement class | R2 cases | Status |
| --- | --- | --- | --- |
| ED-01 | entity-store-mode | R2-C08-01, R2-C09-03, R2-C09-04, R2-C18-01 | Preserved |
| ED-02 | conformance-failure-stage | R2-C08-02, R2-C08-03, R2-C10-01..03, R2-C07-03 | Preserved |
| ED-03 | missing-entity-diagnostic-category | R2-C08-01, R2-C09-03, R2-C09-04, R2-C18-01, R2-C05-05, R2-C08-04 | Preserved |
| ED-04 | scope-applicability-enforcement | R2-C11-01..04, R2-C11-08, R2-C11-09 | Preserved |
| ED-05 | missing-context-stage | R2-C20-05 | Preserved |

No additional expected differences were introduced.

## Binding gate reconciliation

| Gate | Requirement | Status |
| --- | --- | --- |
| Source/baseline | Clean, synced, exact source/tree | Verified |
| Per-rule export | Complete ID/body pairs | Verified (operation 4) |
| Build | Locked restore, Release, zero warnings | Verified |
| Unit/integration | Full adapter suites on both TFMs | Verified |
| Schema/request | Canonical schema, fixed validation | Verified |
| Scope | Out-of-constraint rejection | Verified (ED-04) |
| Entity store | Exact caller entities, no synthesis | Verified |
| Decision/error | Raw vs enforcement separation | Verified |
| Identity | Diagnostic ID set semantics | Verified |
| Temporal/provider | Reserved-field rejection, trusted provenance | Verified |
| Native lifecycle | Both images verified/resident | Verified |
| Concurrency | Qualified under explicit bound (4 workers) | Verified |
| NativeAOT/trimming | Every supported cell publishes | Verified on win-x64; hosted matrix pending |
| Package-only | Package consumer | Deferred to hosted CI |
| Regression | Existing D1/D2 suites green | Verified |
| Upstream differential | Pinned corpus passes | Verified (84/84 both TFMs) |
| Hosted matrix | All three RIDs, both TFMs | Deferred to hosted CI |
| Evidence | Durable artifact bound to source | This record |
| Independent review | Independent fixtures/controls | Deferred to D3-Q1 |
| Owner acceptance | Separate owner record | Not granted |
| Publication/release | Separate authorization | Not authorized |

## Deferred gates with binding citations

The following gates are explicitly deferred to later milestones with binding citations:

1. **Package-only host matrix** (integration contract §12 gate 15): The complete hosted RID/TFM
   matrix for package-only consumption requires hosted CI execution across `linux-x64` and
   `osx-arm64`. Local verification is limited to `win-x64` (qualification plan §9).
2. **Independent qualification** (integration contract §12 gate 17): Producer evidence is
   supporting material only. Independent qualification with independently authored fixtures is
   assigned to D3-Q1.
3. **Owner acceptance** (integration contract §12 gate 18): The owner MUST issue a separate
   acceptance record after independent qualification.
4. **Publication/release** (integration contract §12 gate 18): Package publication, signing,
   tags, and release require a separate owner authorization after qualification.

## Production changes in R2

1. **`native/dogwoodsharp-bridge/src/d2/runtime.rs`** — Provider method-chain rejection added:
   trusted outcomes supply only the base value; method bodies require Rhai and are rejected before
   upstream evaluation. The native runtime creates a fresh upstream authorizer per request from
   immutable trusted history.
2. **`src/DogwoodSharp.CedarSharp/DogwoodSharp.CedarSharp.csproj`** — `InternalsVisibleTo`
   extended to the `PrepProbe` friend assembly for the R2 differential evidence tool.
3. **R2 differential tooling** (`docs/reviews/d3-impl-01/evidence/differential/r2/`) — Evidence-only
   harness; never a production execution path.

## Local verification results

| Gate | Result |
| --- | --- |
| Rust `cargo fmt --check` | Pass |
| Rust `cargo clippy --locked --release --offline --all-targets -- -D warnings` | Pass |
| Rust `cargo test --locked --release --offline` (32 tests) | Pass |
| .NET locked restore, Release build | Pass, 0 warnings |
| DogwoodSharp tests (net8.0) | 71/71 Pass |
| DogwoodSharp tests (net10.0) | 71/71 Pass |
| CedarSharp adapter tests (net8.0) | 45/45 Pass |
| CedarSharp adapter tests (net10.0) | 45/45 Pass |
| D2 conformance differential | 39/39 Pass |
| NativeAOT FoundationSmoke (both TFMs) | Pass |
| R2 upstream differential (net8.0) | 84/84 Pass |
| R2 upstream differential (net10.0) | 84/84 Pass |

## Evidence

- `evidence/r2-differential-net10.0-results.json` — Full per-case differential results
- `evidence/r2-differential-net8.0-results.json` — Full per-case differential results
- `evidence/r2-differential-fixtures.json` — Complete fixture corpus (schemaVersion 2)
- `evidence/r2-differential-differences.json` — Versioned difference register (ED-01 through ED-05)
- `evidence/r2-differential-hashes.json` — Evidence integrity hashes
- `evidence/r2-differential-run.log` — Execution summary

## Repository identity

This record documents implementation-stage producer verification. Final commit and tree
identities are recorded after the R2 evidence commit.