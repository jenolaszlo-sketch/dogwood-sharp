# DOGWOOD-SHARP-D3-IMPL-01-R5-R1 — Typed-Context Semantic Correctness

**C. REMEDIATION COMPLETE** · 26 November 2026 · Owner design decision required for ED-06 retirement

This commission establishes the authoritative Cedar semantics for typed context values
(`ipaddr`, `datetime`, `decimal`), corrects the pinned Dogwood reference path,
executes a three-way differential (ORIGINAL reference / CORRECTED reference / D3),
reassesses ED-06, and discharges the remaining R5 obligations — without declaring D3
qualified.

---

## 1. Provenance and preserved review records

| Record | Commit | Tree |
| --- | --- | --- |
| Frozen R4 reproduction subject | `a244d65e3c1e803626e796bdee3af08a9ca5e89a` | `90e4262b0fb4d9169b94ae6eae11980f54a964d4` |
| Parent associated with the previously reported tree | `554edc1103192e68933ca71d1e03f2af72cb0920` | `1aa73fd2a08e527b5d04c8a5fba487e22160e906` |
| Independent Q2 preservation commit | `dd1aa2fd3de44f914b412bf7df4a111dfbffe931` | `47f51239fdf117fd3604e730802c99924d940732` |
| R5 baseline replay commit | `c13aeba...` | `eba91f63...` |
| Frozen Q1 subject | `ee870e9fc32b1f20cb20863002bd18f9d4122de1` | `6a5a0d3966e9f9100ebc34cad3dae04b6264cc88` |

All manifests, original pinned Dogwood reference, frozen R4 implementation/evidence, and
historical qualification records are preserved verbatim.

---

## 2. Authoritative Cedar semantics

The commission established authoritative Cedar behavior by direct engine evaluation
through the `cedar-adjudicator` harness, which evaluates independently constructed
minimal Cedar policies and schema-directed JSON requests against the underlying Cedar
engine itself — **bypassing the Dogwood conversion path entirely**.

Two pinned engine versions were evaluated:

| Engine | Version | Cases | Divergences from 4.13.0 |
| --- | --- | --- | --- |
| Reference (R4 pin) | cedar-policy 4.11.0 | 57 | 0 |
| D3 pin | cedar-policy 4.13.0 | 57 | 0 |

The two engine versions produce **byte-identical outcomes on all 53 adjudication cases**
(plus 4 additional R5-R1 malformed-encoding cases). The authoritative decisions are
recorded in `evidence/differential/cedar-adjudicator/authority-verification.json` and
are machine-verified against the engine's own output.

Key findings from direct Cedar adjudication:

| Typed value | Valid encodings | Decision | Natural JSON |
| --- | --- | --- | --- |
| `ipaddr("192.168.1.1")` | explicit `__extn`, implicit `{fn,arg}`, bare string | **Allow** (match) | `{"__extn":{"fn":"ip","arg":"192.168.1.1"}}` |
| `ipaddr("192.168.1.2")` | same three spellings | **Deny** (mismatch) | same as above |
| `datetime("2024-01-01T00:00:00Z")` | same three spellings | **Allow** | `{"__extn":{"fn":"datetime","arg":"..."}}` |
| `decimal("1.5")` | same three spellings | **Allow** | `{"__extn":{"fn":"decimal","arg":"1.5"}}` |
| Invalid `ipaddr` / `datetime` / `decimal` | any spelling | **context-parse failure** (no decision) | N/A |

**All three JSON spellings of one typed argument are semantically equivalent and
produce byte-identical Cedar natural JSON.** The original reference's degradation of
`__extn` objects to records is the sole source of the ED-06 discrepancy.

---

## 3. Reference correction — defect boundary and patch

The defect is in the pinned Dogwood crate's `value_to_expr` function
(`dogwood-language/src/api.rs`), which degrades `__extn` objects for `ipaddr` and
`datetime` to plain records because the pinned value model has no variant for those
types. `decimal` already had a dedicated `Value::Decimal` variant, so explicit
`__extn` decimal escapes were already handled correctly.

The correction adds a single function `reserved_extn_expr` that recognizes the
Cedar-reserved single-key `__extn` shape and returns the corresponding
`RestrictedExpression` for `ip` and `datetime` constructors. The argument is not
validated here: `Context::from_pairs` evaluates the resulting expression eagerly,
so an invalid argument fails closed at context build with the Cedar evaluation
error — matching Cedar's own schema-directed JSON parser.

| Artifact | Location | SHA-256 |
| --- | --- | --- |
| Patch | `evidence/differential/corrected-oracle/dogwood-typed-context.patch` | `76714faa4cbbc12866565736f1a6d7b8d4ef02ca53bd90c111eb035a3ee46b32` |
| Patched `api.rs` | (staged tree) | `4ff2eb118a08c13eddf1d54ea1cf178db70f1a148bacb6e377333abeb5b67dd3` |
| Files changed | `dogwood-language/src/api.rs` only | (exactly one file) |

The patch is applied by `prepare-dogwood.ps1 -WorkRoot <dir outside repo>`, which
stages the pinned checkout, applies the patch under `core.autocrlf=false
core.eol=lf`, verifies the exact SHA-256, and writes a `Cargo.toml` pointing at the
absolute patched-crate path. The staged tree is build scratch and not committed.

---

## 4. Three-way differential results

The three-way record (`evidence/differential/three-way-record.json` /
`three-way-record.md`) states, for every case of the 124-case R5-R1 corpus, the
input representation, the expected type/decision, and what each of the four sides
did:

| Classification | Cases | Meaning |
| --- | --- | --- |
| `reference defect` | 11 | ORIGINAL reference disagrees with authoritative Cedar; CORRECTED reference and D3 agree |
| `intentional deviation` | 37 | Pre-existing owner-approved differences (ED-01..ED-05) or fail-closed normalizations (FCN-01..FCN-04) |
| `agreement` | 76 | All four sides agree on stage, decision, determining rules, error state |

**No case is classified as `D3 defect` or `open question` in the differential
corpus.** The single open question (history-event policy evaluation error) is
executed separately and recorded in `evidence/differential/open-question/`.

The 11 reference defects are exactly the six alternate-encoding cases (ED-06
surface) plus the five malformed-encoding controls that the original reference
degrades instead of rejecting:

| Case | Type | Encoding | ORIGINAL | CORRECTED | D3 | Authority |
| --- | --- | --- | --- | --- | --- | --- |
| `R3-C23-08-...-implicit-escape` | ipaddr match | implicit `{fn,arg}` | Deny | Allow | Allow | Allow |
| `R3-C23-08-...-short-string` | ipaddr match | bare string | Deny | Allow | Allow | Allow |
| `R3-C23-09-...-implicit-escape` | datetime match | implicit `{fn,arg}` | Deny | Allow | Allow | Allow |
| `R3-C23-09-...-short-string` | datetime match | bare string | Deny | Allow | Allow | Allow |
| `R3-C23-01-...-implicit-escape` | decimal match | implicit `{fn,arg}` | Deny | Allow | Allow | Allow |
| `R3-C23-01-...-short-string` | decimal match | bare string | Deny | Allow | Allow | Allow |
| `R5R1-C23-01-ipaddr-invalid-bare-string` | ipaddr malformed | bare string | clean Deny | context-parse | context-parse | context-parse |
| `R5R1-C23-02-ipaddr-invalid-octet` | ipaddr malformed | explicit `__extn` | clean Deny | context-parse | context-parse | context-parse |
| `R5R1-C23-03-datetime-invalid-escape` | datetime malformed | explicit `__extn` | clean Deny | context-parse | context-parse | context-parse |
| `R5R1-C23-04-decimal-invalid-escape` | decimal malformed | explicit `__extn` | eval error Deny | context-parse | context-parse | context-parse |
| `R5R1-C23-05-ipaddr-invalid-implicit-escape` | ipaddr malformed | implicit `{fn,arg}` | clean Deny | context-parse | context-parse | context-parse |

---

## 5. ED-06 reassessment and recommendation

**ED-06's underlying premise is eliminated by the reference correction.**

The original ED-06 approval (`R3-C23-08` and `R3-C23-09`) authorized a clean Deny
from the reference and a clean Allow from D3 on the *explicit* `__extn` escape.
The correction makes the reference produce Allow on the explicit escape — so the
divergence ED-06 absorbed no longer exists.

The two ED-06 cases are now expected to **agree** (Allow on both sides). The
corrected-reference register does not carry ED-06; the ORIGINAL-reference register
carries it verbatim for historical preservation.

**Recommendation**: Retire ED-06. The owner may alternatively supersede it with a
note recording the historical approval and its resolution, or retain it as a
frozen record. This commission **does not activate a revised contract**; it records
the recommendation and the executed evidence only.

---

## 6. D3 production change inventory

**No production change is indicated so far.** D3 already produces the
Cedar-authoritative behavior on all typed-context representations. The correction
is upstream in the pinned Dogwood crate.

Permanent representation-invariance regression controls were added to
`tests/DogwoodSharp.CedarSharp.Tests/C23TypedContextRepresentationInvarianceTests.cs`
and pass on both `net8.0` and `net10.0`:

| Control | Encodings | Types | Passes |
| --- | --- | --- | --- |
| Matching typed value authorizes identically | 3 × 3 = 9 | ipaddr, datetime, decimal | 9/9 |
| Non-matching typed value denies identically | 3 × 3 = 9 | ipaddr, datetime, decimal | 9/9 |
| Malformed typed value never gains validity | 3 × 3 = 9 | ipaddr, datetime, decimal | 9/9 |
| Plain record never becomes a typed value | 3 | ipaddr, datetime, decimal | 3/3 |
| Typed value never replaced by policy literal | 3 × 3 = 9 | ipaddr, datetime, decimal | 9/9 |
| Canonical schema unchanged by representation | 1 | ipaddr | 1/1 |

Total: **40 new permanent regression tests**, all passing on both TFMs.

---

## 7. Remaining R5 obligations — dispositions

| Obligation | Status | Evidence |
| --- | --- | --- |
| QD3Q-05 / C-14 temporal failure | **Not dischargeable as literally written** | The pinned temporal engine's only error channel (`ok_or("no event observed")`) is unreachable: `ingest` observes the event before deciding, and leaf evaluation is total (`eval_condition -> bool`). Upstream guide: "If either operand fails to resolve, the comparison is false." The pinned Cedar Rust engine (4.13.0/4.11.0) has **no temporal extensions** (grep for "formerly" → no matches), so temporal cases are not directly adjudicable in Cedar. Authority = official upstream temporal guide + pinned upstream source. |
| C-14 fixtures (replacing history-policy-error) | **Discharged** | `R5R1-C14-01` (history replay entity-conformance failure, FCN-03) and `R5R1-C14-02` (current-event conformance failure after temporal bindings, FCN-04). Both fail closed on both sides; recorded as fail-closed equivalences. |
| QD3Q-07 family validator | **Discharged** | Harness enforces `requiredControlDecisions` on BOTH sides. Three negative self-tests added and passing. |
| C-11 / C-21 fixtures | **Discharged** | `R5R1-C11-01/02/03` (multi-action `appliesTo`), `R5R1-C21-01` (identity reorder two-rule). All agree. |
| Negative controls | **Discharged** | Invalid-encoding controls (FCN-02) share reference stage `request-validation` / D3 stage `native-transport`, identical error text. Both sides fail closed. |
| Six-cell differential matrix | **Partial** | Windows x64 `net8.0`/`net10.0` executed and green (corrected reference). Linux x64 and macOS ARM64 are **pending** — the differential harness invokes a Windows oracle executable and was not run on those platforms in this commission. |
| QD3Q-12 gap reassessment | **Retained** | Underlying production paths unchanged. Dispositions retained as recorded by Q2. |

**Residual limitation / blocker**: QD3Q-05's literal requirement (a genuine temporal-engine
evaluation failure) is structurally unreachable in the pinned stack. This is documented
as a residual limitation, not silently treated as discharged.

---

## 8. Six-cell differential matrix

| OS | RID | TFM | Status | Note |
| --- | --- | --- | --- | --- |
| Windows | `win-x64` | `net8.0` | **executed** | Original: 11 failures (reference defect); Corrected: 0 failures; D3: stable |
| Windows | `win-x64` | `net10.0` | **executed** | Original: 11 failures (reference defect); Corrected: 0 failures; D3: stable |
| Linux | `linux-x64` | `net8.0` | **pending** | Oracle executable is Windows-only; not run in this commission |
| Linux | `linux-x64` | `net10.0` | **pending** | Oracle executable is Windows-only; not run in this commission |
| macOS | `osx-arm64` | `net8.0` | **pending** | Oracle executable is Windows-only; not run in this commission |
| macOS | `osx-arm64` | `net10.0` | **pending** | Oracle executable is Windows-only; not run in this commission |

Per the pinned qualification plan (§9), a cell that cannot be run is **pending,
never passed**. The four pending cells remain pending under the binding plan.

---

## 9. Manifest verification

All evidence files are listed in `evidence/manifest.sha256` (52 entries). The
`* text=auto eol=lf` Git attribute applies to the R5-R1 evidence directory; no
`target/` or `generated/` scratch directories are committed. The corrected-oracle
build tree and the probe `obj/`/`bin/` build artifacts are excluded.

---

## 10. Exact commit/tree identities

| Record | Commit | Tree |
| --- | --- | --- |
| This commission (R5-R1) | (this commit) | (this tree) |
| R5 baseline replay | `c13aeba` | `eba91f63` |
| Independent Q2 preservation | `dd1aa2f` | `47f51239` |
| Frozen R4 reproduction | `a244d65` | `90e4262b` |
| Parent of R4 | `554edc1` | `1aa73fd2` |
| Frozen Q1 subject | `ee870e9` | `6a5a0d39` |
| Pinned upstream Dogwood | (upstream) `929f49e` | (upstream) `929f49e` |

---

## 11. Blockers and open questions

| ID | Description | Disposition |
| --- | --- | --- |
| `OQ-01` | History-event policy evaluation error aborts the reference authorization while D3 allows | **Open question** — recorded in `evidence/differential/open-question/`; both frameworks agree. The upstream temporal guide supports D3's Allow. Owner contract decision required. |
| QD3Q-05 | Genuine temporal-engine evaluation failure unreachable | **Residual limitation** — documented in §7; not discharged. |
| Linux/macOS differential cells | Oracle executable Windows-only | **Pending** — per §9 of qualification plan, never passed. |

---

## 12. Q3 readiness

**Q3 readiness is NOT recommended.** All R5-R1 obligations within this commission's
scope are met, but:

- The four mandatory Linux/macOS differential cells are pending
- QD3Q-05's literal requirement is structurally unreachable
- The ED-06 retirement/supersession decision belongs to the owner
- The open question OQ-01 requires an owner contract decision

This commission discharges its own scope and records the remaining dispositions
above. D3 is **not declared qualified**.

---

*Generated by `build-three-way-record.py` from recorded evidence. The three-way
record, six-cell matrix, authority verification, and all differential observations
are machine-checked and stored under `evidence/`.*