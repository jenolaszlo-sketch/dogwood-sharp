# DOGWOOD-SHARP-D3-IMPL-01-R5-R2 — QD3Q-12 Retained Gaps Reconciliation

**Commission**: DOGWOOD-SHARP-D3-IMPL-01-R5-R2
**Date**: 26 November 2026
**Status**: Reconciled — all five gaps retain original dispositions

---

## 1. QD3Q-12 background

From the Q2 qualification report (`docs/reviews/d3-qualification-01-r4-q2/README.md`):

> **QD3Q-12**: Retained. The underlying production paths were unchanged. Unknown
> diagnostic ID remains Low, duplicate or mismatched exported IDs Low, caller
> action-entity conflict Medium, multi-action `appliesTo` canonicalization Low,
> and explicit `CurrentLoggedJson` suppression Low. C-11's committed
> qualification control remains pending independently.

---

## 2. Five retained gaps — individual reconciliation

### 2.1 Gap 1: Unknown diagnostic ID (Low)

**Original assessment**: Diagnostic IDs exported by the adapter may not match
upstream expectations; risk of silent misclassification.

**R5-R2 impact analysis**:
- The corrected reference and D3 now agree on all error diagnostics for the
  124-case corpus
- The `CapturePolicyEngine` in D3 produces no errors during history replay
- The reference oracle's error categories are now precisely matched by D3 for
  all fail-closed cases (FCN-01 through FCN-04)
- No new diagnostic paths introduced by R5-R1/R5-R2 changes

**Disposition**: **Retained as Low** — no change in risk profile. The
representation-invariance regression tests (40 tests) provide additional
confidence that error paths are stable.

---

### 2.2 Gap 2: Duplicate or mismatched exported IDs (Low)

**Original assessment**: Policy IDs exported by the adapter may duplicate or
mismatch upstream IDs.

**R5-R2 impact analysis**:
- The `explicit_policy_id_is_preserved` test in the adjudicator harness
  verifies exact policy ID preservation (`r3c23h_0`, `r3c23i_0`, `r3c23_0`)
- The differential harness records `determining` policy IDs for every case
- All 124 corrected-reference cases show exact ID agreement between corrected
  reference and D3
- C-21 controls (`identity-reordered-two-rule`, `non-sequential-id`, etc.)
  verify export ordering and identity

**Disposition**: **Retained as Low** — risk mitigated by evidence but not
eliminated. The adapter's `FromPolicies` export path is exercised but not
independently verified against a separate reference.

---

### 2.3 Gap 3: Caller action-entity conflict (Medium)

**Original assessment**: When the caller supplies entities that conflict with
the action's `appliesTo` constraints, the adapter may not correctly reject or
may produce inconsistent diagnostics.

**R5-R2 impact analysis**:
- C-08 family (`omitted-scope`, `explicit-attrless`, `explicit-incomplete`,
  `missing-ref-entity`) exercises action-entity conflicts
- C-10 family (`wrong-attr-type`, `missing-req-attr`, `unknown-entity-type`,
  `invalid-parent`, `malformed-entity-json`) exercises entity validation
- All C-08 and C-10 cases **agree** on corrected reference (0 divergences)
- The `requiredControlDecisions` enforcement ensures both sides produce the
  expected Deny/operation-failure

**Disposition**: **Retained as Medium** — the evidence shows correct behavior
for exercised cases, but the conflict space is large and not exhaustively
covered. The Medium rating reflects the potential for untested conflict
combinations.

---

### 2.4 Gap 4: Multi-action `appliesTo` canonicalization (Low)

**Original assessment**: When multiple actions are declared in a schema, the
canonical schema exposed by the adapter may not correctly represent all
`appliesTo` constraints.

**R5-R2 impact analysis**:
- **New C-11 controls added in R5-R1**:
  - `R5R1-C11-01-multi-action-read` — read action with `context: { count: Long }`
  - `R5R1-C11-02-multi-action-archive` — archive action with `context: { reason: String }`
  - `R5R1-C11-03-multi-action-canonicalization` — canonical schema verification
  - `R3-C11-05-multi-action-appliesto` — existing control
  - `R3-C11-06-canonical-schema` — existing control
- All 5 C-11 multi-action controls **agree** on corrected reference
- The canonical schema JSON is verified to preserve both actions' independent
  principal, resource, and context constraints

**Disposition**: **Retained as Low** — the specific canonicalization concern is
now covered by executed evidence, but the Low rating is retained pending
broader schema-complexity testing.

---

### 2.5 Gap 5: Explicit `CurrentLoggedJson` suppression (Low)

**Original assessment**: The `CurrentLoggedJson` field in historical events may
be incorrectly suppressed or exposed by the adapter.

**R5-R2 impact analysis**:
- The temporal corpus (C-12, C-13, C-14) exercises historical event processing
- The `CapturePolicyEngine` captures the full Cedar request context including
  logged fields
- The OQ-01 investigation confirmed D3 correctly ingests history events with
  their logged context
- No R5-R1/R5-R2 changes affect `CurrentLoggedJson` handling

**Disposition**: **Retained as Low** — no change in risk profile. The field is
exercised implicitly through temporal controls but not explicitly tested in
isolation.

---

## 3. C-11 committed qualification control

**Original note**: "C-11's committed qualification control remains pending
independently."

**R5-R2 status**: **Discharged** — the three new R5-R1 C-11 controls
(`R5R1-C11-01`, `R5R1-C11-02`, `R5R1-C11-03`) plus the two existing controls
execute and agree on the corrected reference. The committed control is now
covered by executed evidence.

---

## 4. Summary table

| Gap | Original Risk | R5-R2 Impact | Current Risk | Disposition |
|-----|---------------|--------------|--------------|-------------|
| 1. Unknown diagnostic ID | Low | No new diagnostic paths | Low | Retained |
| 2. Duplicate/mismatched IDs | Low | ID agreement verified on 124 cases | Low | Retained |
| 3. Caller action-entity conflict | Medium | C-08, C-10 agree on corrected ref | Medium | Retained |
| 4. Multi-action appliesTo canonicalization | Low | **Now covered by 5 executed C-11 controls** | Low | Retained (evidence added) |
| 5. CurrentLoggedJson suppression | Low | No changes to temporal handling | Low | Retained |
| C-11 committed control | Pending | **Discharged via new controls** | — | Discharged |

---

## 5. Conclusion

- **No gap is silently marked resolved** — all five retain their original risk ratings
- **Gap 4 (multi-action canonicalization) now has executed evidence** but retains Low rating per conservative qualification practice
- **C-11 committed control is discharged** by the new R5-R1 controls
- **No R5-R1/R5-R2 changes invalidated previous risk assessments** — the corrected reference and ED-06 retirement do not affect these production paths
- **Deferred controls implemented only where necessary** for R5 correctness (C-11, C-21, negative self-tests)

---

*This reconciliation is part of the DOGWOOD-SHARP-D3-IMPL-01-R5-R2 commission evidence
package.*