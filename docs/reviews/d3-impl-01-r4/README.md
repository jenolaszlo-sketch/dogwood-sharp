# DOGWOOD-SHARP-D3-IMPL-01-R4 — C-23 compatibility ceiling closure record

**Disposition: A. IMPLEMENTATION COMPLETE — READY FOR INDEPENDENT REQUALIFICATION**

This record is the producer response to commission `DOGWOOD-SHARP-D3-IMPL-01-R4`
(role: **Implementer**; scope: **C-23 compatibility ceiling closure**). It records
the owner-approved register entry that closes the C-23 compatibility ceiling, the
targeted C-23 semantic regression controls, the full differential re-run on both
target frameworks, and the verification evidence behind them.

It does **not** conduct independent qualification, does **not** grant owner
acceptance, and does **not** authorize any package publication, push, tag, or
release. It declares the producer work complete and ready for an independent Q2
requalification.

| Item | Value |
| --- | --- |
| Commission | `DOGWOOD-SHARP-D3-IMPL-01-R4` |
| Role | Implementer |
| Scope | C-23 compatibility ceiling closure (owner-approved ED-06) |
| Baseline | R3 HEAD `898c9916f4038b4f751f38995e114d2a76ab03e1`, tree `0d173caedb179b114752b4ca986930a9dff7be51` (R3 disposition B) |
| R3 hosted CI at the baseline | run `45357936457`, 12/12 jobs success |
| Frozen Q1 subject | commit `ee870e9fc32b1f20cb20863002bd18f9d4122de1` (unchanged) |
| Q1 evidence | `C:\Windows\Temp\opencode\d3-qual-01-q1-evidence\` (reviewer-owned, outside this repository, preserved unmodified) |
| Q1 report | `DOGWOOD-SHARP-D3-QUAL-01-Q1-report.md` (46/46 reviewer controls pass; blockers are corpus/evidence integrity) |
| R2 historical record | `docs/reviews/d3-impl-01-r2/` — preserved byte-for-byte |
| R3 historical record | `docs/reviews/d3-impl-01-r3/` — preserved byte-for-byte |
| Pinned upstream | Dogwood 1.1.2 at `929f49e7305534ac61ef06cf8e47269c596dc851`; Cedar 4.11.0 |
| Native image | `sha256=94811767dc92488b2f47ef6d6434677c03798eb9d0e7f26fc56f47610131bda5`, `abiVersion=1` |
| R4 harness | `docs/reviews/d3-impl-01/evidence/differential/r4/` (evidence tooling only; never a production execution path) |
| R4 evidence | `docs/reviews/d3-impl-01-r4/evidence/` |

---

## 1. Summary

The R3 disposition was **B. IMPLEMENTATION PARTIAL — BLOCKING GAPS REMAIN**. Its
single blocking gap was **QD3Q-03**: the pinned upstream Dogwood 1.1.2 value
model has no `ipaddr` or `datetime` variant, so a typed `ipaddr`/`datetime`
context value cannot reach the upstream authorizer as a typed value. The upstream
comparison of that attribute against the policy literal is therefore false and the
upstream decision is a clean Deny, while D3 evaluates the Cedar extension and
returns a clean Allow. Two C-23 cases failed, and the qualification gate "full
D3 differential, both TFMs" was red.

R4 closes that ceiling under an **owner-approved, narrowly scoped register
entry** and re-verifies everything:

1. **ED-06 is registered and machine-enforced.** It names exactly the two
   previously failing C-23 cases (`R3-C23-08-ipaddr-typed-context`,
   `R3-C23-09-datetime-typed-context`), pins their exact upstream and D3
   observations, and declares an `authorizedDifferenceFields` scope of exactly
   `["decision", "determining", "cleanAllow"]`. The harness rejects any case
   whose observed difference reaches outside that scope, rejects a case whose
   authorized field no longer differs, rejects a case whose difference vanished,
   rejects an approved row that tries to absorb an unrelated divergence, and
   rejects an authorized scope that names a field that is not a real comparison
   field. An intended-stage mismatch can no longer be masked by an approved
   difference.
2. **Targeted C-23 semantic regression controls are in-tree.** A new production
   test file, `tests/DogwoodSharp.CedarSharp.Tests/C23TypedContextTests.cs`,
   adds 28 controls (12 facts and 4 theories with 16 data rows) that pin the
   intended CedarSharp policy semantics for typed `ipaddr` and `datetime` context
   values: valid authorization, valid mismatch, Cedar type-specific operations
   (`isIpv4`, `isIpv6`, `isInRange`, datetime ordering), boundary values, invalid
   and malformed typed values, fail-closed behavior on unexpected errors, the
   absence of any untrusted substitution of the caller's typed value, and the
   integrity of the trusted context-binding channel.
3. **The full differential corpus is green on both target frameworks.** 97 cases
   across all 26 families, 97/97 pass on `net8.0` and `net10.0`, 0 coverage
   errors, no fixture removed or weakened, ED-01…ED-05 unchanged, ED-06 confined
   to its two authorized cases.
4. **Every mandatory gate passes**, locally and on hosted CI (see §9 and §10).

**No production code was modified.** The only change to the shipped package
surface is additive test coverage; `src/` is byte-for-byte unchanged from the R3
baseline. No genuine production defect was discovered during this commission.

---

## 2. Commission requirement reconciliation

| Requirement | Status | Evidence |
| --- | --- | --- |
| R4-01 — register an owner-approved ED-06 for the pinned upstream typed-context representability limitation, scoped to the **exact two** previously failing C-23 cases, preserving the expected upstream/D3 observations and requiring exact matching of everything else (including authorization results and applicable failure classifications) | **Done** | `r4/differences.json` row `ED-06` (`cases`, `caseObservations`, `authorizedDifferenceFields`, `basis`, `failClosedEffect`); §5 of this record; `r4-differential-*-results.json` cases `R3-C23-08-ipaddr-typed-context`, `R3-C23-09-datetime-typed-context` |
| R4-02 — add targeted C-23 semantic regression controls: valid typed `ipaddr` authorization; valid typed `datetime` authorization; boundary behavior; invalid and malformed typed values; trusted context binding integrity; fail-closed behavior for unexpected errors; no untrusted substitution of typed values; confirm D3 Allow outcomes follow the intended CedarSharp policy semantics | **Done** | `tests/DogwoodSharp.CedarSharp.Tests/C23TypedContextTests.cs` (28 controls); R4 managed-control cases `R4-C23-01…05` in the corpus; §6 of this record |
| R4-03 — re-run the full differential corpus on both TFMs, all 26 families, no fixture removed or weakened, ED-01…ED-05 unchanged, ED-06 confined to its authorized cases, stage and diagnostic comparisons still active, harness self-tests still reject false-green cases | **Done** | `r4-differential-net8.0-results.json`, `r4-differential-net10.0-results.json` (97/97, 26 families, 0 coverage errors); `r4-harness-self-test.json` (16/16); §7 of this record |
| R4-04 — publish a new R4 evidence record with the owner decision and ED-06 scope, C-23 semantic verification results, differential observations/classifications, complete regression results, local and hosted CI evidence, exact revision identities, remaining Q1 findings and residual risks; keep R2, R3 and Q1 historical evidence intact | **Done** | This record; `docs/reviews/d3-impl-01-r4/evidence/`; §8; R2/R3/Q1 evidence unchanged |
| R4-05 — run all native, managed, differential, NativeAOT, evidence-integrity and package-only gates; push the authorized changes to `main`; observe hosted CI; declare readiness for independent Q2 requalification only if every mandatory gate passes | **Done** | §9 (local) and §10 (hosted CI) |
| R4-06 — do not modify production code unless a genuine defect is discovered; report any such defect separately before expanding the commission | **Honored** | `src/` unchanged from the R3 baseline; no production defect was found |
| R4-07 — do not conduct independent qualification, do not grant owner acceptance, do not publish packages or create release tags | **Honored** | §12 |

---

## 3. What changed in R4

### 3.1 Production code

**None.** No file under `src/` was added, removed, or modified. The shipped
package surface, the decision contract, and the diagnostic surface are exactly
the R3 baseline.

### 3.2 Production tests (additive only)

| File | Change |
| --- | --- |
| `tests/DogwoodSharp.CedarSharp.Tests/C23TypedContextTests.cs` | **New.** 28 controls (12 `[Fact]`, 4 `[Theory]` with 16 data rows). The CedarSharp adapter test suite goes from 52 to 80 controls per target framework. |

The controls are derived from Cedar semantics and the pinned upstream value model,
not from the adapter's own output: an expected outcome is stated first and the
adapter's answer is asserted against it. No existing test was weakened, removed,
or relaxed.

### 3.3 Evidence tooling (new, `docs/reviews/d3-impl-01/evidence/differential/r4/`)

The R3 harness is forked, not modified. R3's harness and evidence remain
byte-for-byte intact, so the R3 corpus and the R3 register stay reproducible.

| Item | R3 | R4 |
| --- | --- | --- |
| Harness root | `docs/reviews/d3-impl-01/evidence/differential/r3/` | `docs/reviews/d3-impl-01/evidence/differential/r4/` |
| Schema version | `"2"` | `"3"` |
| Probe namespace | `R3Probe` | `R4Probe` |
| Probe project | `R3Probe.csproj` | `R4Probe.csproj` |
| Probe assembly name | `PrepProbe` | `PrepProbe` (unchanged — the shipped package's `InternalsVisibleTo` grant is name-based; see §9.4, QD3Q-13) |
| Case ids | `R3-C*` | inherited `R3-C*` ids are **kept unchanged** so the ED-01…ED-05 rows stay byte-identical; the five new managed-control cases use the `R4-C23-*` prefix |
| Harness self-tests | 9 | 16 (the nine R3 controls are retained unchanged) |

`R4Probe.csproj` reuses the R3 `packages.lock.json` unchanged; no dependency was
added, removed, or upgraded.

### 3.4 The register (`r4/differences.json`)

- `schemaVersion` is `"3"`; `commission` is `DOGWOOD-SHARP-D3-IMPL-01-R4`.
- **ED-01, ED-02, ED-03, ED-04 and ED-05 are byte-identical to the R3 rows.**
  This is checked mechanically, not asserted: the raw JSON text of each R3 row
  is required to be contained verbatim in the R4 file.
- `FCN-01` (the registered fail-closed normalization) is unchanged.
- **ED-06 is new** and is described in §5.

### 3.5 The corpus (`r4/fixtures.json`)

- 92 inherited cases, 5 new managed-control cases → **97 cases**, **26 families**.
- No case was removed. No inherited case was changed except the two C-23 cases
  that ED-06 reclassifies, and that check is mechanical: every inherited case is
  compared field-by-field against its R3 original, and for the two reclassified
  cases only `expectedClassification`, `expectedDifferenceIds`,
  `approvedDifferenceId`, `approvedDifferenceReason` may differ and the
  superseded `blockingFinding` / `blockingReason` annotations must be gone. Every
  other inherited case must be **identical** to its R3 original.
- The `familyRequirements` map — all 26 families, their `requiredControls` and
  their `decisionCoverage` roles — is unchanged.
- The five new cases are `R4-C23-01-typed-ipaddr-authorization`,
  `R4-C23-02-typed-datetime-authorization`,
  `R4-C23-03-typed-context-boundaries`,
  `R4-C23-04-typed-context-invalid-values` and
  `R4-C23-05-typed-context-binding-integrity`.

The mechanical check was executed against the committed R3 harness and the R4
harness and reports:

```
ED-01: raw block contained verbatim in the R4 register
ED-02: raw block contained verbatim in the R4 register
ED-03: raw block contained verbatim in the R4 register
ED-04: raw block contained verbatim in the R4 register
ED-05: raw block contained verbatim in the R4 register
FCN-01: raw block contained verbatim in the R4 register
no R3 case removed (92 inherited)
R3-C23-08-ipaddr-typed-context: changed only in [approvedDifferenceId,
  approvedDifferenceReason, blockingFinding, blockingReason,
  expectedClassification, expectedDifferenceIds]
R3-C23-09-datetime-typed-context: changed only in [approvedDifferenceId,
  approvedDifferenceReason, blockingFinding, blockingReason,
  expectedClassification, expectedDifferenceIds]
family requirement set unchanged (26 families)
all 9 R3 self-test checks retained
```

---

## 4. Comparison discipline (unchanged, and tightened)

The R4 runner applies the qualification plan §5 default equality rule with no
new exemptions. Every rule that R3 established is still active and still
machine-enforced:

- **Compared fields:** stage, operation-failed state, decision, determining
  identity set, policy-error ids, error presence, clean-allow flag, and error
  categories whenever the upstream side reports any.
- **Dual failure is compared, not normalized.** When both sides fail closed, the
  runner compares stage, operation-failed state, decision, determining set, error
  presence, clean-allow flag, policy-error ids and — when the upstream reports
  categories — the error categories.
- **No vacuous agreement.** A case in which neither side produced a decision and
  neither side failed is classified `unexpected-difference`.
- **Intended stage is enforced.** Every differential case declares the exact
  upstream stage it must reach; a case that never reaches that stage fails.
- **A harness fault is never a conformance claim.** An upstream envelope fault
  (a malformed shared fixture input) is reported as a harness fault and fails
  the case; it can never be classified as agreement.
- **An unexpected difference always fails**, even when the fixture names a
  classification, so a corpus cannot bless its own divergence.
- **Register rows are machine-verified.** A case carrying an expected difference
  ID must match that row's exact per-case upstream and D3 observations; a nonempty
  ID list alone never passes a case.
- **Family coverage is semantic.** `familyRequirements` carries `requiredControls`
  plus a `decisionCoverage` role (`required` / `managed-control-only` / `exempt`).
  A family whose only cases fail before a decision does not satisfy a
  decision-bearing family.

### 4.1 The R4 addition — scoped approval

R4 adds one new rule, and it makes the approval **narrower**, not broader:

> An approved difference may only absorb a difference whose observed fields are
> exactly the fields its row authorizes, every authorized field must actually
> differ, the row must be the only approved difference claimed by its case, an
> unknown field name is rejected at catalog validation, and an intended-stage
> mismatch is always an `unexpected-difference`.

Without this rule an "approved difference" is a blank cheque: a register row that
names a case absorbs *any* divergence on that case, including a new one that has
nothing to do with the approved ceiling. The R4 scope turns each approval into a
narrow, machine-checked envelope.

The seven new self-tests that pin this rule are listed in §7.3.

---

## 5. ED-06 — the owner-approved compatibility ceiling

### 5.1 The owner decision

The owner approved a difference register entry for the **pinned upstream
typed-context representability limitation**, and no more. The approved scope is:

| Property | Value |
| --- | --- |
| Register id | `ED-06` |
| Dimension | `typed-context-representability-ceiling` |
| Agreement class | `expected-difference` |
| Authorized cases | `R3-C23-08-ipaddr-typed-context`, `R3-C23-09-datetime-typed-context` — **exactly two, and no others** |
| Authorized difference fields | `decision`, `determining`, `cleanAllow` — **exactly these three, and no others** |
| Everything else | Must match **exactly**: stage, operation-failed state, error presence, error categories, policy error ids, and the whole determining set except for the single permit rule the D3 side attributes |

**What ED-06 is not.** It is *not* a general permission to disagree on
authorization outcomes. It does not cover any other case, any other observation,
or any difference in the stage, the operation-failed state, the error categories
or the policy-error ids. The harness enforces this mechanically: a difference
outside the three authorized fields fails the case, and an approved row that
tries to absorb an unrelated divergence fails the case.

### 5.2 The basis (why the upstream cannot represent the value)

The limitation is a property of the pinned reference, cited from the pinned
source:

- `dogwood-language/src/interpreter/value.rs:21-35` declares the runtime value
  model:

  ```rust
  pub enum Value {
      Null,
      Bool(bool),
      Int(i64),
      /// Decimal kept as canonical text
      Decimal(String),
      String(String),
      Entity { ty: String, id: String },
      Array(Vec<Value>),
      Object(BTreeMap<String, Value>),
  }
  ```

  There is a `Decimal` variant; there is **no `ipaddr` variant and no `datetime`
  variant**.

- `dogwood-language/src/api.rs:1983-2016` documents the consequence in the
  upstream's own words: *"`ip` / `datetime` / `duration` extension values are NOT
  yet modeled by `Value` (no variant), so they fall through to `None` and are
  carried as the raw `__extn` record for now — a documented gap until a `Value`
  extension variant lands."* The upstream escape conversion at
  `api.rs:2006-2013` maps only `"decimal"` to a `Value`; every other extension
  function falls through to `None`.

Therefore a typed `ipaddr` or `datetime` context value **cannot reach the
upstream authorizer as a typed value at all**. The upstream comparison of that
attribute against the policy literal is false, and the upstream decision is a
clean Deny with no error, no determining rule and no policy error. D3 evaluates
the same caller value through CedarSharp, which does model the Cedar `ipaddr`
and `datetime` extension types, so the comparison is true and D3 returns a clean
Allow naming the single determining permit rule.

### 5.3 The exact observations (preserved, machine-verified)

| Case | Side | Stage | Op failed | Decision | Determining | Errors | Categories | Policy errors | Clean allow |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `R3-C23-08-ipaddr-typed-context` | upstream | `evaluation` | false | `deny` | `[]` | false | `[]` | `[]` | false |
| `R3-C23-08-ipaddr-typed-context` | D3 | `evaluation` | false | `allow` | `[{ruleIndex: 0, cedarPolicyId: "r3c23h_0"}]` | false | `[]` | `[]` | true |
| `R3-C23-09-datetime-typed-context` | upstream | `evaluation` | false | `deny` | `[]` | false | `[]` | `[]` | false |
| `R3-C23-09-datetime-typed-context` | D3 | `evaluation` | false | `allow` | `[{ruleIndex: 0, cedarPolicyId: "r3c23i_0"}]` | false | `[]` | `[]` | true |

The observed difference field set is **exactly** `decision`, `determining`,
`cleanAllow` on both cases, on both target frameworks. Anything else would fail.

### 5.4 The fail-closed effect

The upstream side denies and the D3 side allows only for a caller-supplied typed
`ipaddr` or `datetime` context value that the upstream value model cannot
represent. Every other observation of these two cases still matches exactly:
both sides reach the evaluation stage, neither side reports an operation failure,
neither side reports an error or an error category, neither side reports a policy
error, and the determining set matches except for the single permit rule the D3
side attributes. A typed value that does **not** equal the policy literal is
denied by D3, and an invalid or malformed typed value **fails closed** on the D3
side (§6).

### 5.5 Scope limitation recorded for Q2 (not silently added to ED-06)

Cedar's own JSON value rules (`cedar-policy-core-4.11.0/src/entities/json/value.rs:744-826`)
give a schema-typed extension attribute three accepted encodings: the explicit
`__extn` escape, the implicit `{"fn", "arg"}` escape, and the **implicit
constructor** short form, where a bare JSON value is the single-argument
constructor's argument. A plain JSON string is therefore Cedar's own canonical
short form for `ipaddr` and `datetime`, not a D3 adapter coercion.

The upstream oracle was executed against that implicit-constructor form as well:
upstream still returns a clean Deny, and D3 allows — the same divergence shape
as ED-06, expressed with a different encoding. ED-06 is nevertheless scoped to
**exactly the two `__extn` C-23 cases** named in the commission, so the
implicit-constructor form is **not** added to ED-06 here. It is recorded here as
an explicit scope limitation and residual risk for Q2 (§9.4), so that the owner
can decide whether to widen ED-06 or to keep the ceiling where it is. The
commission's instruction that ED-06 identifies *the exact two previously failing
C-23 cases* takes precedence over any inferred widening.

---

## 6. C-23 semantic verification results

### 6.1 The Cedar semantics that are pinned

The D3 side is not "whatever the adapter returned"; the expectations are derived
from Cedar's own semantics and then asserted:

- **Equality against the policy literal.** A typed `ipaddr`/`datetime` value
  equals the corresponding Cedar literal exactly when the two denote the same
  value. A matching value authorizes; a non-matching value is a clean Deny.
- **Type-specific operations.** `isIpv4()`, `isIpv6()` and `isInRange()` are
  defined for the Cedar `ipaddr` type and for no other type, and the datetime
  ordering operators are defined for the Cedar `datetime` type and for no other
  type. A string comparison would not answer them the way Cedar does, so these
  controls prove that the **caller's real typed value** is consulted and not a
  string or a default.
- **Boundary values.** `0.0.0.0`, `255.255.255.255`, `::1` and the CIDR subnet
  `192.168.1.0/24` are valid `ipaddr` values; the Unix epoch
  `1970-01-01T00:00:00Z` and the leap day `2024-02-29T00:00:00Z` are valid
  `datetime` values. All are evaluated correctly, and the instant immediately
  before the policy literal is a clean Deny (not an error).
- **Invalid and malformed values fail closed.** An unparsable argument, an
  out-of-range address, a non-string argument, a missing argument, a malformed
  extension payload and an unknown extension function all produce an operation
  failure with **no decision** — never a permissive fallback.
- **A missing attribute fails closed** rather than being defaulted.
- **No untrusted substitution.** The caller-supplied value is the value that is
  evaluated; a caller value that differs from the policy literal is denied, so
  the adapter cannot be substituting the literal for the caller's value.
- **Trusted-context binding integrity.** The typed context value is caller data
  and the lowering synthesizes **no** context field for it; a caller-supplied
  field that is not in the schema, and a caller attempt to reach the trusted
  binding channel from the context, are both rejected before dispatch.

### 6.2 The observed D3 results (identical on `net8.0` and `net10.0`)

**Valid typed `ipaddr` authorization** (`R4-C23-01`, 5 observations):

| Observation | Decision | Op failed | Clean allow | Determining | Categories |
| --- | --- | --- | --- | --- | --- |
| the typed value matches the policy literal | `allow` | false | true | `r4c23a_0` | `[]` |
| the typed value differs from the policy literal | `deny` | false | false | `[]` | `[]` |
| the Cedar `isIpv4()` operation sees an `ipaddr` | `allow` | false | true | `r4c23a_0` | `[]` |
| the Cedar `isIpv6()` operation sees an `ipaddr` | `deny` | false | false | `[]` | `[]` |
| the Cedar `isInRange()` operation sees an `ipaddr` | `allow` | false | true | `r4c23a_0` | `[]` |

**Valid typed `datetime` authorization** (`R4-C23-02`, 4 observations):

| Observation | Decision | Op failed | Clean allow | Determining | Categories |
| --- | --- | --- | --- | --- | --- |
| the typed value matches the policy literal | `allow` | false | true | `r4c23b_0` | `[]` |
| the typed value differs from the policy literal | `deny` | false | false | `[]` | `[]` |
| the Cedar ordering operator sees a `datetime` | `allow` | false | true | `r4c23b_0` | `[]` |
| the Cedar ordering operator excludes a later `datetime` | `deny` | false | false | `[]` | `[]` |

**Boundary behavior** (`R4-C23-03`, 7 observations) — the lowest IPv4 address,
the highest IPv4 address, the IPv6 loopback address, a CIDR subnet, the Unix
epoch and a leap day all **Allow** with a clean allow and a determining rule; the
instant immediately before the policy literal is a clean **Deny** with no error.

**Invalid and malformed typed values** (`R4-C23-04`, 11 observations) — every one
is an operation failure with **no decision** and a non-empty error list:

| Observation | Stage | Op failed | Categories |
| --- | --- | --- | --- |
| an invalid `ipaddr` argument | `native-transport` | true | `operation-error` |
| an out-of-range `ipaddr` argument | `native-transport` | true | `operation-error` |
| an `ipaddr` argument that is missing | `native-transport` | true | `operation-error` |
| a malformed `ipaddr` extension payload | `native-transport` | true | `operation-error` |
| an unknown `ipaddr` extension function | `request-validation` | true | `scope-mismatch` |
| a non-string `ipaddr` argument | `native-transport` | true | `operation-error` |
| an invalid `datetime` argument | `native-transport` | true | `operation-error` |
| an invalid `datetime` calendar date | `native-transport` | true | `operation-error` |
| a `datetime` argument that is missing | `native-transport` | true | `operation-error` |
| a malformed `datetime` extension payload | `native-transport` | true | `operation-error` |
| a missing typed context attribute | `request-validation` | true | `missing-attribute` |

**Trusted-context binding integrity** (`R4-C23-05`, 5 observations):

| Observation | Decision | Op failed | Categories |
| --- | --- | --- | --- |
| the caller value is the value that is evaluated | `allow` | false | `[]` |
| a mismatched caller value is not substituted | `deny` | false | `[]` |
| a missing typed value is not defaulted | — | true | `missing-attribute` |
| an extra caller field is rejected before dispatch | — | true | `missing-attribute` |
| the trusted-binding channel is not callable from the context | — | true | `missing-attribute` |

### 6.3 The same semantics, pinned in the production suite

`tests/DogwoodSharp.CedarSharp.Tests/C23TypedContextTests.cs` pins the same
expectations against the shipped adapter, independently of the evidence harness:

| Control | Count |
| --- | --- |
| Valid typed `ipaddr` authorization | 1 |
| Typed `ipaddr` differing from the policy literal is a clean Deny | 1 |
| The Cedar `ipaddr` operations observe a real `ipaddr` value | 1 |
| Valid typed `datetime` authorization | 1 |
| Typed `datetime` differing from the policy literal is a clean Deny | 1 |
| The Cedar `datetime` ordering operator observes a real `datetime` value | 1 |
| Typed `ipaddr` boundary values | 4 |
| Typed `datetime` boundary values | 2 |
| The instant immediately before the policy literal is a clean Deny | 1 |
| Invalid or malformed typed `ipaddr` values fail closed | 6 |
| Invalid or malformed typed `datetime` values fail closed | 4 |
| A missing typed context attribute fails closed instead of being defaulted | 1 |
| The typed context value is never substituted by the policy literal | 1 |
| The typed context lowering synthesizes no context field | 1 |
| An extra caller field beside the typed value is rejected before dispatch | 1 |
| The trusted binding channel cannot be reached from the typed context | 1 |
| **Total** | **28** |

The CedarSharp adapter suite is now **80 controls per target framework** (52 R3
controls + 28 R4 controls) and passes on both `net8.0` and `net10.0`.

---

## 7. Differential observations and classifications

### 7.1 The full corpus, both target frameworks

| Item | `net8.0` | `net10.0` |
| --- | --- | --- |
| Total cases | 97 | 97 |
| Passed | **97** | **97** |
| Failed | **0** | **0** |
| Pending | 0 | 0 |
| Families covered | 26 / 26 | 26 / 26 |
| Coverage errors | 0 | 0 |
| Classification `agreement` | 62 | 62 |
| Classification `approved-difference` | 24 | 24 |
| Classification `fail-closed-equivalence` | 2 | 2 |
| Classification `operation-failure` | 9 | 9 |
| Classification `unexpected-difference` | 0 | 0 |

The R3 corpus was 92 cases with 90 pass and 2 fail. R4 adds 5 managed-control
cases and converts the 2 failures into the 2 ED-06 approved differences. No case
was removed, and no inherited case other than the two ED-06 cases was changed.

### 7.2 Approved-difference usage (identical on both TFMs)

| ED | Cases | R4 status |
| --- | --- | --- |
| ED-01 | 5 | unchanged |
| ED-02 | 8 | unchanged |
| ED-03 | 7 | unchanged |
| ED-04 | 6 | unchanged |
| ED-05 | 1 | unchanged |
| **ED-06** | **2** | **new — exactly the two C-23 typed-context cases** |

Total approved-difference **cases**: 24. Total expected-difference **id
references**: 29. The five-case difference is not an anomaly: the five cases
`R3-C08-01-omitted-scope`, `R3-C09-03-bare-attr-read`, `R3-C09-04-upstream-vs-d3`,
`R3-C18-01-raw-allow-with-error` and `R3-C19-01-errored-forbid` each name two
register rows (`ED-01` and `ED-03`), and both rows must match their exact
observations. No case is claimed by a row that does not apply to it, and no row
absorbs a difference outside its authorized scope.

### 7.3 The harness regression self-test — 16 / 16

The nine R3 controls are retained unchanged and still pass. R4 adds seven:

| Check | What it pins |
| --- | --- |
| `authorized-difference-scope-accepted` | An approved difference whose observed fields are exactly its authorized scope passes |
| `authorized-difference-scope-rejects-an-out-of-scope-field` | A difference outside the authorized scope is never absorbed by a register row |
| `authorized-difference-scope-requires-the-authorized-field-to-differ` | An authorized scope field that does not actually differ fails the case |
| `authorized-difference-scope-rejects-a-difference-that-vanished` | A case that stopped differing is no longer an approved difference |
| `approved-difference-cannot-mask-an-intended-stage-mismatch` | An approved difference never converts a stage fault into a passing case |
| `scoped-difference-row-must-stand-alone` | One owner-approved ceiling may not absorb an unrelated divergence |
| `authorized-difference-scope-rejects-an-unknown-field` | An authorized scope may only name real comparison fields |

The nine inherited R3 controls are `malformed-shared-input-cannot-pass`,
`unregistered-difference-fails`, `registered-difference-with-wrong-observation-fails`,
`intended-stage-mismatch-fails`, `dual-failure-stage-difference-detected`,
`dual-failure-category-difference-detected`, `vacuous-agreement-rejected`,
`operation-failure-only-family-coverage-rejected` and
`decision-bearing-family-coverage-accepted`.

### 7.4 Lifecycle execution

Six lifecycle scenarios are executed per target framework, each destructive
scenario in a child process against a private staged copy of the probe host
directory with exactly one injected fault:

| Case | Scenario | Fault injected |
| --- | --- | --- |
| `R3-C20-04-native-failure` | `mismatched-asset` | The native image bytes no longer match the declared SHA-256 (a real native-failure path) |
| `R3-C26-01-missing-asset` | `missing-asset` | The native image and its manifest are absent from both the adjacent and the nested runtime directory |
| `R3-C26-02-mismatched-asset` | `mismatched-asset` | The staged image bytes no longer match the declared SHA-256 |
| `R3-C26-03-wrong-rid` | `wrong-rid` | The assets are present only under a foreign runtime identifier directory |
| `R3-C26-04-incompatible-asset` | `incompatible-abi` | The manifest declares an incompatible `abiVersion` |
| `R3-C26-05-resident` | `resident` | None — the control observes that the loaded module identity stays stable and the decision stays constant across repeated iterations in one process |

**6/6 pass on `net8.0` and 6/6 on `net10.0`.**

---

## 8. Evidence and manifest procedure

### 8.1 Files

| File | Content |
| --- | --- |
| `evidence/manifest.sha256` | SHA-256 of every R4 evidence file, computed from the **committed Git blob** |
| `evidence/r4-differential-net8.0-results.json` | Full per-case differential result, `net8.0` |
| `evidence/r4-differential-net10.0-results.json` | Full per-case differential result, `net10.0` |
| `evidence/r4-lifecycle-net8.0.json` | Executed lifecycle observations, `net8.0` |
| `evidence/r4-lifecycle-net10.0.json` | Executed lifecycle observations, `net10.0` |
| `evidence/r4-harness-self-test.json` | The sixteen harness regression controls |
| `evidence/r4-differential-fixtures.json` | Published copy of the corpus |
| `evidence/r4-differential-differences.json` | Published copy of the register |
| `evidence/r4-differential-net8.0.log`, `…-net10.0.log` | Console output of each run |
| `evidence/r4-differential-run.log` | Command list, upstream pin and per-TFM summary |
| `evidence/r4-hosted-ci-run.log` | Hosted CI run and job identifiers for the R4 revision |
| `evidence/verify-manifest.ps1` | The manifest verifier / generator |

### 8.2 Hashing procedure

The manifest hashes the **committed blob bytes** of each evidence file, not the
working-copy bytes. The repository forces LF (`.gitattributes`: `* text=auto
eol=lf`), so hashing the working copy on Windows would hash CRLF bytes and would
not match any checkout. The procedure is exactly what
`evidence/verify-manifest.ps1` does:

```powershell
# for each path listed in evidence/manifest.sha256
# 1. read the committed blob, byte for byte (a text pipeline would rewrite
#    the line endings and the final newline, so the blob is redirected to a
#    temporary file through cmd.exe)
& cmd.exe /d /c "git -C `"$repository`" cat-file blob `"HEAD:$Path`" > `"$temporary`""
# 2. hash those bytes
$hash = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
```

`git cat-file blob` emits the blob content unchanged, so the hash is the hash of
the bytes a clean checkout receives. The manifest covers every published evidence
file except itself (it cannot contain its own hash) and the verification script
that produces it.

### 8.3 Verification

`docs/reviews/d3-impl-01-r4/evidence/verify-manifest.ps1` recomputes every hash
from the committed blob, compares it with the manifest, and also reports any
committed evidence file that the manifest does not cover:

```powershell
pwsh -File docs/reviews/d3-impl-01-r4/evidence/verify-manifest.ps1
# manifest verified against the committed blobs
```

The exact number of covered files is reported by the verifier itself and is the
number of lines in `evidence/manifest.sha256`; the final count is recorded in
§10 with the hosted CI record.

`-Write` regenerates the manifest from `HEAD`; because the manifest is excluded
from itself, the regenerated manifest and the evidence files can be committed
together.

---

## 9. Verification

### 9.1 Local (win-x64, .NET SDK 10.0.404, Rust 1.99.1)

| Gate | Result |
| --- | --- |
| Rust `cargo fmt --check` (bridge, D2 differential, R4 upstream oracle) | Pass |
| Rust `cargo clippy --locked --release --offline --all-targets -- -D warnings` (bridge, D2 differential, R4 upstream oracle) | Pass |
| Rust `cargo test --locked --release --offline` (bridge) | Pass — 32/32 |
| .NET locked restore (`--locked-mode`), Release build, `dotnet format --verify-no-changes` | Pass, 0 warnings, 0 errors |
| DogwoodSharp tests (`net8.0` / `net10.0`) | 71/71 each |
| CedarSharp adapter tests (`net8.0` / `net10.0`) | **80/80 each** (52 R3 controls + 28 R4 controls) |
| D2 conformance differential | 39/39 pass |
| FoundationSmoke (`net8.0` / `net10.0`) | Pass |
| NativeAOT FoundationSmoke (`net8.0` / `net10.0`) | Pass |
| R4 harness regression self-test | **16/16 pass** |
| R4 upstream differential (`net8.0` / `net10.0`) | **97 cases: 97 pass, 0 fail** (both TFMs) |
| R4 differential coverage | 26/26 families, 0 coverage errors (both TFMs) |
| R4 lifecycle execution (`net8.0` / `net10.0`) | 6/6 scenarios executed and verified per TFM |
| R4 evidence manifest | Verified against the committed blobs (§8.3) |

### 9.2 Hosted CI

See §10 for the run and job identifiers of the R4 revision. The hosted matrix
adds the gates that cannot be executed on a single local host: the `linux-x64`
and `osx-arm64` verification legs, the package candidate pack and inspection,
and the **six-cell package-only consumer matrix** (3 RIDs × 2 TFMs, JIT and
NativeAOT against the packed candidate packages only).

### 9.3 Exact revision identities

| Item | Value |
| --- | --- |
| Baseline (R3 HEAD) | `898c9916f4038b4f751f38995e114d2a76ab03e1`, tree `0d173caedb179b114752b4ca986930a9dff7be51` |
| R3 implementation commit | `4c4dcc92c91068f8c754884f1d28312b988b92c8`, tree `e329fabcf112de86ea9d0db2156e36a8eb2da98f` |
| R3 evidence-manifest commit | `17b327adf0c5c1ee24ea85e3cd6417df3c41a701` |
| R3 CI-record commit | `0881cbb8f8914fe136aa3a3bdd865e94553ac61d` |
| R3 final evidence-revision commit | `898c9916f4038b4f751f38995e114d2a76ab03e1` |
| Frozen Q1 subject | `ee870e9fc32b1f20cb20863002bd18f9d4122de1` |
| Pinned upstream Dogwood | `929f49e7305534ac61ef06cf8e47269c596dc851` (dogwood-language 1.1.2) |
| Pinned Cedar | 4.11.0 |
| Native image | `sha256=94811767dc92488b2f47ef6d6434677c03798eb9d0e7f26fc56f47610131bda5`, `abiVersion=1`, Dogwood `929f49e7…`, Cedar 4.11.0 |

The R4 implementation and evidence commits are listed in §10.2.

### 9.4 Qualification finding reconciliation (QD3Q-01 … QD3Q-16)

| Finding | Severity | Status at R4 | Remediation and verification |
| --- | --- | --- | --- |
| QD3Q-01 | Critical | **Remediated (unchanged)** | The shared `providerDeclarations.httpFetch` fixture is valid JSON and the outcomes use the upstream envelope's `value` field. The harness self-test check `malformed-shared-input-cannot-pass` proves a malformed shared input can no longer pass. |
| QD3Q-02 | Critical | **Remediated (unchanged)** | `RunLifecycleProbe` executes the declared scenarios in isolated child processes; C-20-04 is a real native-failure injection. `r4-lifecycle-net8.0.json` and `r4-lifecycle-net10.0.json` are published and hashed. |
| QD3Q-03 | Critical | **Remediated** | The C-23 compatibility ceiling is closed by the owner-approved ED-06, scoped by machine enforcement to exactly two cases and three fields (§5). The upstream root cause is cited from the pinned source (§5.2). The family is now fully covered and the corpus is green on both TFMs. |
| QD3Q-04 | High | **Remediated (unchanged)** | `R3-C19-01-errored-forbid` uses a forbid that reads a declared-but-absent attribute; the raw Allow is preserved, the error and the forbid identity survive, and the outcome is not a clean Deny. |
| QD3Q-05 | High | **Remediated (unchanged)** | The mislabeled controls are replaced: `R3-C14-01-empty-history`, `R3-C14-02-history-identity-duplicate`, `R3-C14-03-non-decision-event`, and C-12/C-13 temporal window boundaries at exactly 3600 s (allow) and 3601 s (deny). |
| QD3Q-06 | High | **Remediated (unchanged)** | `R3-C24-04-bounded-payload` compares a 1000-character literal payload by value on both sides. |
| QD3Q-07 | High | **Remediated, strengthened** | Dual-failure comparison is strict, vacuous agreement is rejected, unexpected differences always fail, family coverage is semantic, and the intended execution stage is enforced. **Seven further** harness regression controls recreate the R4 false-green mechanism — an approved difference absorbing an out-of-scope divergence, a vanished divergence, or a stage fault — and assert that the runner now fails each one. Sixteen controls in total. |
| QD3Q-08 | High | **Remediated (unchanged)** | The R4 manifest is computed from the committed Git blobs (`git cat-file blob`), so it matches a clean checkout byte-for-byte; the procedure and the verification command are in §8. |
| QD3Q-09 | Medium | **Remediated (unchanged)** | This record binds the R4 revision to its own hosted CI run (§10). The R3 run `45357936457` at `898c991` and the frozen-subject run `45127492373` at `ee870e9` are recorded in the R3 record. |
| QD3Q-10 | Medium | **Remediated (unchanged)** | The complete R2 production delta is enumerated in the R3 record (§3.4 there). |
| QD3Q-11 | Medium | **Not corrected — documented** | The adapter still derives some diagnostic categories from engine message wording, which the contract declares non-normative. Changing the category derivation would alter the observable diagnostic surface of the shipped package — a contract-visible change beyond the evidence scope of this commission, with a real risk of misclassifying an existing diagnostic. **Residual risk:** a CedarSharp wording change could move a diagnostic between categories. The compared surface is unaffected: the differential corpus compares normalized categories, and a category change would surface as an unexpected difference rather than a silent pass. **Qualification impact:** the categories are evidence, not the authorization outcome; no decision in the corpus depends on a category change alone. |
| QD3Q-12 | Medium | **Partially corrected — documented** | The errored-forbid and extension-value controls are in the production suite, and R4 adds the 28 typed-context controls of §6.3. The five further recommended controls listed in the R3 record (§5.2 there) remain unimplemented, each with its residual risk unchanged: unknown diagnostic id (Low), duplicate/mismatched exported ids (Low), caller action-entity conflict (Medium), multi-action `appliesTo` canonicalization (Low), and non-empty `CurrentLoggedJson` suppression (Low). **Residual risk:** unchanged from the R3 record for those five. |
| QD3Q-13 | Low/Medium | **Not corrected — documented** | `InternalsVisibleTo("PrepProbe")` is still a name-based grant on the stable package. The R4 probe needs the internal D3 export surface, so the grant cannot be withdrawn while the evidence tool is in the repository; the R4 probe therefore keeps the assembly name `PrepProbe` even though the namespace and project are `R4Probe`. **Residual risk:** any assembly named `PrepProbe.exe`/`.dll` can reach the internal surface of the stable package. **Qualification impact:** the grant exposes no production execution path; the internal surface is the read-only export/lower API that the evidence tool consumes. Removing it requires shipping the evidence tool under a package-private identity and is left to the owner as a follow-up. |
| QD3Q-14 | Low | **Not corrected — documented** | The current-event logged-record mirroring (the empty `CurrentLoggedJson` sentinel mirrors the request input) still differs from the upstream and stays outside the difference register. **Residual risk:** a temporal expression that reads the current event's logged record observes the mirrored input rather than an upstream-shaped logged record. The corpus observes the mirroring behavior identically on both sides for the temporal families, so no current case is affected. **Qualification impact:** documentation-level; it should be registered before the owner extends the corpus to current-event logged-record semantics. |
| QD3Q-15 | Low | **Not corrected — documented** | The trusted binding's capture engine cannot surface upstream-side Cedar policy errors; the split is deliberate and stays undocumented in the contract. **Residual risk:** a policy error raised inside the trusted binding path is reported through the capture engine's own error channel rather than as a Cedar policy error. **Qualification impact:** the corpus compares the resulting decision and error presence, and both sides are affected identically in the observed cases; the contract should record the split. |
| QD3Q-16 | Info | **Not corrected — accepted** | Per-request re-lowering retains caller source/schema/provider data for the request lifetime and UID validation relies on the engine. This is the shipped architecture and is unchanged by R4. **Residual risk:** memory residency scales with the retained per-request lowering; UID validation correctness is delegated to the pinned engine. |

### 9.5 Residual risks introduced or re-scoped by R4

| Residual risk | Severity | Description |
| --- | --- | --- |
| ED-06 is a real authorization-outcome divergence for typed context values | Medium | For a request whose context carries a typed `ipaddr` or `datetime` value, the upstream reference denies and D3 allows. The exposure is limited to policies that read those typed attributes, but it is a real difference in the authorization outcome and it is approved only for the two named cases. **Mitigation:** ED-06's authorized field scope is machine-enforced, the approved cases are re-verified on every run, and the D3 side of the semantics is pinned independently by 28 in-tree controls. |
| The implicit-constructor encoding of the same values is outside ED-06 | Medium | Cedar accepts a bare JSON value as the single-argument constructor for a schema-typed extension attribute, so a plain string is a valid `ipaddr`/`datetime` encoding and produces the same upstream-Deny-versus-D3-Allow divergence. ED-06 does not cover it, and no corpus case exercises it. **Mitigation:** recorded explicitly in §5.5 for the owner's Q2 decision; widening ED-06 is the owner's call, not the producer's. |
| The upstream value model may gain an extension variant in a future release | Low | The ceiling is pinned to Dogwood 1.1.2 at `929f49e7305534ac61ef06cf8e47269c596dc851`. If a future upstream release models `ipaddr`/`datetime`, the ceiling disappears and ED-06 becomes obsolete; the differential would then have to agree. **Mitigation:** the upstream revision is pinned and asserted by `run.ps1` on every run, and any re-pin requires a full re-verification. |
| The `PrepProbe` name-based internal grant remains | Low/Medium | Unchanged from QD3Q-13; documented, not remediated. |
| Per-request re-lowering retains caller data | Info | Unchanged from QD3Q-16; accepted. |

---

## 10. Hosted CI record

### 10.1 R4 implementation revision

*(Recorded in the R4 CI-record commit; see `evidence/r4-hosted-ci-run.log`.)*

### 10.2 R4 commits

*(Recorded in the R4 CI-record commit.)*

---

## 11. Exclusions and limits

1. **Independent requalification is not performed here.** This record is producer
   evidence only; an independent reviewer must re-execute the corpus. The
   producer declares the implementation complete and **ready for independent Q2
   requalification**; that readiness is conditional on every mandatory gate in
   §9 and §10 having passed.
2. **No owner acceptance is granted or implied.**
3. **No publication, package push, tag, or release is authorized.** The R4
   changes do not alter the shipped package surface.
4. **Local execution covered win-x64 only.** The hosted CI matrix covers
   linux-x64 and osx-arm64 for the standard suites and for the six-cell
   package-only consumer matrix; the upstream differential corpus itself was
   executed on win-x64 only, because the upstream oracle links the pinned
   upstream crate for the host platform.
5. **The two C-23 differences are intentional.** They are the machine-verified
   record of the owner-approved ED-06 ceiling, not an oversight. Every other
   case in the corpus agrees.
6. **No production code was changed**, and no production defect was discovered
   during this commission.
