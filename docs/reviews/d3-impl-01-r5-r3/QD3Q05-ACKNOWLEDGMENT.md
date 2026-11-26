# DOGWOOD-SHARP-D3-IMPL-01-R5-R3 — QD3Q-05 Owner Acknowledgment

**Commission**: DOGWOOD-SHARP-D3-IMPL-01-R5-R3
**Date**: 26 November 2026
**Status**: Formally recorded per owner decision

---

## 1. Original obligation (QD3Q-05)

From the Q2 qualification report (`docs/reviews/d3-qualification-01-r4-q2/README.md`):

> **QD3Q-05**: A genuine temporal-engine evaluation failure must be demonstrated.
> The C-14 family requires an observation where the temporal engine itself fails
> during evaluation (distinct from trusted-binding failures or history-event
> validation errors).

---

## 2. Structural unreachability analysis (from R5-R2)

The R5-R2 reconciliation (`QD3Q05-C14-RECONCILIATION.md`) established that the literal QD3Q-05 requirement is **structurally unreachable** in the pinned Dogwood 1.1.2 / cedar-policy 4.11.0 stack:

| Architectural Invariant | Source |
|-------------------------|--------|
| Ingestion precedes evaluation | `dogwood-language/src/api.rs:1271-1289` (`ingest` called before `evaluate`) |
| Referenced action has observed event | Temporal conditions only reference actions in history |
| Condition evaluator is total | `interpreter/eval.rs:36` (`eval_condition -> bool`) |
| Only error path unreachable | `engine.rs:509-546` — only `Err("no event observed")` which cannot occur for referenced action |
| Cedar has no temporal extensions | `grep -r "formerly"` on cedar-policy 4.11.0/4.13.0 → no matches |

---

## 3. C-14 discharged via alternative controls

Two reachable evaluation-stage fail-closed cases were added in R5-R1:

| Case | Normalization | Failure Type | Both Sides |
|------|---------------|--------------|------------|
| `R5R1-C14-01` | FCN-03 | History replay entity conformance | Fail closed |
| `R5R1-C14-02` | FCN-04 | Current-event entity conformance after temporal bindings | Fail closed |

These demonstrate **fail-closed behavior for reachable failure modes** in the temporal pipeline, satisfying the broader C-14 requirement.

---

## 4. Owner conditional acceptance

The owner accepts the structural unreachability of the literal QD3Q-05 evaluation failure **as a bounded qualification limitation**, subject to:

### Acceptance boundaries

1. **Limited to pinned execution contract**: Dogwood 1.1.2 at `929f49e` + cedar-policy 4.11.0
2. **Preservation of preventing invariants**: The four architectural invariants above must hold
3. **Successful execution of reachable fail-closed controls**: FCN-03 and FCN-04 must pass
4. **No extension to other engine versions or architectures**

### Alternative qualification criterion (accepted)

> **Independent verification of structural unreachability, preservation of the preventing invariants, and successful execution of applicable reachable fail-closed boundary controls.**

---

## 5. What is NOT accepted

- FCN-03 and FCN-04 are **not** described as direct execution of the unreachable failure path
- The literal "genuine temporal-engine evaluation failure" remains **unexecuted**
- This acceptance does **not** extend to:
  - Other Dogwood versions
  - Other Cedar engine versions
  - Changed execution architectures
  - Future temporal engine implementations

---

## 6. Residual limitation record

| Aspect | Status |
|--------|--------|
| Literal QD3Q-05 coverage | **Unexecuted** (structurally unreachable) |
| C-14 broader requirement | **Discharged** via FCN-03, FCN-04 |
| Alternative criterion | **Accepted** by owner |
| Qualification boundary | **Explicitly documented** |

---

## 7. Evidence traceability

| Artifact | Location |
|----------|----------|
| `InMemoryTemporalEngine::evaluate` | `dogwood-language/src/engine.rs:509-546` |
| `ingest` precedes `evaluate` | `dogwood-language/src/api.rs:1271-1289` |
| `eval_condition` total | `interpreter/eval.rs:36` |
| FCN-03 case | `R5R1-C14-01-temporal-history-replay-conformance-failure` |
| FCN-04 case | `R5R1-C14-02-temporal-current-event-conformance-failure` |
| Differential results | `evidence/observations-corrected-reference-*.json` (0 divergences) |

---

## 8. Owner acknowledgment

**Recorded**: The owner conditionally accepts the structural unreachability of QD3Q-05's literal requirement as a bounded qualification limitation per the alternative criterion above. The acceptance is limited to the pinned Dogwood 1.1.2 / cedar-policy 4.11.0 execution contract and does not extend to other versions or architectures.

---

*This acknowledgment is part of the DOGWOOD-SHARP-D3-IMPL-01-R5-R3 commission evidence package.*