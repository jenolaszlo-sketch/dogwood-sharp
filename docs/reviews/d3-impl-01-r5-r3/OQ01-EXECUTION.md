# DOGWOOD-SHARP-D3-IMPL-01-R5-R3 — OQ-01 Targeted Controls Execution Record

**Commission**: DOGWOOD-SHARP-D3-IMPL-01-R5-R3
**Date**: 26 November 2026
**Baseline**: `91c899f` (R5-R2 with manifest fix)

---

## 1. Controls executed

Seven targeted controls (TC-01 through TC-07) were executed against both the original reference, corrected reference, and D3 on Windows x64 / .NET 8.0 and .NET 10.0.

| Control | Scenario | Original Ref | Corrected Ref | D3 | Classification |
|---------|----------|--------------|---------------|-----|----------------|
| TC-01 | Valid history, valid current | Allow (eval) | Allow (eval) | Allow (eval) | **agreement** |
| TC-02 | History missing temporal attr | Deny (eval) | Deny (eval) | Deny (eval) | **agreement** |
| TC-03 | History missing owner, forbid on history | Fail (eval) | Fail (eval) | Fail (trusted-binding) | **unexpected-difference** (stage only) |
| TC-04 | Both missing owner | Fail (eval) | Fail (eval) | Fail (trusted-binding) | **unexpected-difference** (stage only) |
| TC-05 | Mixed history (one valid, one invalid) | Fail (eval) | Fail (eval) | Fail (trusted-binding) | **unexpected-difference** (stage only) |
| TC-06 | Plain record owner on history | Fail (eval) | Fail (eval) | Fail (trusted-binding) | **unexpected-difference** (stage only) |
| TC-07 | No valid basis (temporal false) | Deny (eval) | Deny (eval) | Deny (eval) | **agreement** |

---

## 2. Key findings

### 2.1 Semantic agreement on all controls

**All seven controls show semantic agreement**: both sides either Allow, Deny, or fail closed (no decision, operation failed). No control produces a divergent authorization decision (Allow vs Deny).

### 2.2 Stage divergence on history-event entity conformance failures

| Control | Reference Stage | D3 Stage | Difference |
|---------|----------------|----------|------------|
| TC-03 | `evaluation` | `trusted-binding` | Stage only |
| TC-04 | `evaluation` | `trusted-binding` | Stage only |
| TC-05 | `evaluation` | `trusted-binding` | Stage only |
| TC-06 | `evaluation` | `trusted-binding` | Stage only |

The reference fails at the **evaluation stage** because it evaluates the full policy set (including the `forbid` rule) against the history event, and the entity conformance check fails during that evaluation.

D3 fails at the **trusted-binding stage** because its history replay validates entity conformance during event ingestion, before temporal evaluation.

### 2.3 Root cause confirmation

The reference oracle (`upstream-oracle/src/main.rs:419-468`) evaluates the complete policy set against each history event:
```rust
let response = authorizer.is_authorized(&event);  // Full policy set
```

D3's trusted-runtime binding (`native/dogwoodsharp-bridge/src/d2/runtime.rs:186-219`) uses a `CapturePolicyEngine` that only captures context:
```rust
let capture = CapturePolicyEngine::default();  // Deny-only, no policy evaluation
```

**However**, D3 still validates entity conformance during history event construction (`build_event` → `build_entities`), which is why it also fails on TC-03/04/05/06 but at the trusted-binding stage.

### 2.4 Original OQ-01 case distinction

The original OQ-01 case (`R5R1-OQ-01`) had a history event with **no entities at all** (`"entities": []`). In that case:
- Reference: Failed on `forbid` rule evaluation (accessing missing `owner` attribute)
- D3: **Allowed** (no entity to validate, temporal trace worked)

The new TC-03/04/05/06 cases have history events **with entities that fail conformance**. Both sides fail closed on entity conformance, but at different stages.

---

## 3. Proposed contract decision

**D3's behavior is correct; reference behavior is a defect.**

### Justification

1. **Upstream temporal semantics**: "If either operand fails to resolve, the comparison is false." The temporal operator should read from the trace, not re-authorize history events.

2. **Reference evaluates `forbid` against history events**: This is architecturally wrong. History events are recorded facts, not authorization requests.

3. **Entity conformance for history events**: Should be validated at ingestion (trusted-binding), not during policy evaluation (evaluation). D3 does this correctly.

4. **No unintended grants**: D3 only allows when the temporal condition is satisfied AND the current event passes all applicable policies. The history-event conformance failure is contained to the trusted-binding stage.

### Impact on classification

| Control | Current | Proposed | Rationale |
|---------|---------|----------|-----------|
| TC-01 | agreement | agreement | Both Allow |
| TC-02 | agreement | agreement | Both Deny |
| TC-03 | unexpected-difference | **fail-closed-equivalence** | Both fail closed; stage difference is a reference defect |
| TC-04 | unexpected-difference | **fail-closed-equivalence** | Both fail closed; stage difference is a reference defect |
| TC-05 | unexpected-difference | **fail-closed-equivalence** | Both fail closed; stage difference is a reference defect |
| TC-06 | unexpected-difference | **fail-closed-equivalence** | Both fail closed; stage difference is a reference defect |
| TC-07 | agreement | agreement | Both Deny |

**New fail-closed normalization needed**: FCN-05 "History event entity conformance failure during replay" covering TC-03, TC-04, TC-05, TC-06.

---

## 4. Evidence files

| File | Description |
|------|-------------|
| `fixtures.json` | 7 OQ-01 targeted controls |
| `register.json` | Empty register (no approved diffs, no FCN yet) |
| `observations-original-reference-net8.0.json` | Original ref on .NET 8 |
| `observations-original-reference-net10.0.json` | Original ref on .NET 10 |
| `observations-corrected-reference-net8.0.json` | Corrected ref on .NET 8 |
| `observations-corrected-reference-net10.0.json` | Corrected ref on .NET 10 |
| `lifecycle-*.json` | Lifecycle logs |

---

## 5. Next steps for R5-R3

1. **Add FCN-05** to the differential register for history-event entity conformance failures
2. **Update corrected reference oracle** to use `CapturePolicyEngine` for history replay (matching D3)
3. **Execute cross-platform differential matrix** (4 pending cells)
4. **Owner approval** of proposed contract decision

---

*This record is part of the DOGWOOD-SHARP-D3-IMPL-01-R5-R3 commission evidence package.*