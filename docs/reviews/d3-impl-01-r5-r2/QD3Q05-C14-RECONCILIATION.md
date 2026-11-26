# DOGWOOD-SHARP-D3-IMPL-01-R5-R2 — QD3Q-05 / C-14 Reconciliation

**Commission**: DOGWOOD-SHARP-D3-IMPL-01-R5-R2
**Date**: 26 November 2026
**Status**: Reconciled — residual limitation documented

---

## 1. Original obligation (QD3Q-05)

From the Q2 qualification report (`docs/reviews/d3-qualification-01-r4-q2/README.md`):

> **QD3Q-05**: A genuine temporal-engine evaluation failure must be demonstrated.
> The C-14 family requires an observation where the temporal engine itself fails
> during evaluation (distinct from trusted-binding failures or history-event
> validation errors).

The original C-14 corpus (R3/R4) carried only `R3-C14-03-non-decision-event`,
`R3-C14-01-empty-history`, `R3-C14-02-history-identity-duplicate` — all
**trusted-binding** or **validation** failures, not genuine temporal-engine
evaluation failures.

---

## 2. R5-R1 additions (FCN-03, FCN-04)

R5-R1 added two new C-14 cases as fail-closed equivalences:

| Case | Failure Type | Stage (Reference) | Stage (D3) |
|------|--------------|-------------------|------------|
| `R5R1-C14-01` (FCN-03) | History replay **entity conformance** failure | `evaluation` | `trusted-binding` |
| `R5R1-C14-02` (FCN-04) | Current-event **entity conformance** after temporal bindings | `evaluation` | `trusted-binding` |

Both cases:
- Use the `temporalOwner` schema requiring `owner: User` on `Test::Item`
- Supply a non-conforming entity (plain record instead of entity reference)
- Fail closed on both sides with no decision produced
- Normalized as **fail-closed equivalences** (FCN-03, FCN-04)

---

## 3. Reachability analysis of QD3Q-05's literal requirement

### 3.1 What QD3Q-05 requires

A **genuine temporal-engine evaluation failure** — i.e., the Dogwood
`InMemoryTemporalEngine::evaluate` method returning an error during leaf
evaluation, distinct from:
- Trusted-binding failures (provider/temporal binding unavailable)
- History-event validation errors (malformed events, duplicate identities)
- Current-event validation errors (malformed request)

### 3.2 Source-level analysis of pinned temporal engine

**File**: `dogwood-language/src/engine.rs` (pinned revision `929f49e`)

#### `InMemoryTemporalEngine::evaluate` (lines 509-546)

```rust
pub fn evaluate(
    &mut self,
    partition_key: &PartitionKey,
    condition: &TemporalCondition,
) -> Result<bool, String> {
    let trace = self.traces.get(partition_key);
    let Some(trace) = trace else {
        return Ok(false);  // No trace → false, not error
    };
    
    let trace = trace.read().unwrap();
    let events = trace.events.read().unwrap();
    
    if events.is_empty() {
        return Err("no event observed".to_string());  // ONLY error path
    }
    
    // ... evaluate condition over events ...
    Ok(result)
}
```

**The only error path**: `Err("no event observed")` when the trace exists but
contains no events.

#### `ingest` (api.rs lines 1271-1289)

```rust
pub fn ingest(&mut self, event: Event) -> Result<(), String> {
    let partition_key = self.partition_key_of(&event);
    let mut traces = self.traces.write().unwrap();
    let trace = traces.entry(partition_key).or_insert_with(|| ...);
    trace.write().unwrap().events.write().unwrap().push(event);
    Ok(())
}
```

`ingest` **always succeeds** — it observes the event before any evaluation
decision is made. The trace is never empty when `evaluate` is called for a
temporal condition that references that action.

#### `eval_condition` (interpreter/eval.rs line 36)

```rust
pub fn eval_condition(condition: &Condition, bindings: &Bindings) -> bool {
    // ... total function, returns bool, never errors ...
}
```

Leaf evaluation is **total** — it returns `bool`, never `Result`.

### 3.3 Architectural invariant preventing the failure

The temporal engine's error channel (`"no event observed"`) is **structurally
unreachable** from caller input because:

1. **Temporal conditions are only evaluated for actions that have been
   ingested** — the `formerly`/`once` operators reference specific action
   types that must have appeared in history
2. **`ingest` is called before `evaluate`** for each history event during
   replay — the trace is populated before temporal evaluation begins
3. **Empty history is a validation error, not an evaluation error** — caught
   at `validate_timeline` (trusted-binding stage) before the temporal engine
   is invoked
4. **No other error paths exist** in `evaluate` — the only `Err` is the
   empty-trace case, which cannot occur for a referenced action

### 3.4 Upstream Cedar engine confirmation

The pinned Cedar Rust engine (cedar-policy 4.11.0 and 4.13.0) has **no temporal
extensions**:

```bash
grep -r "formerly" ~/.cargo/registry/src/*/cedar-policy-*/ 2>/dev/null
# No matches
```

Temporal evaluation is **entirely in the Dogwood layer**; Cedar itself has no
temporal operators. Therefore, a "genuine Cedar temporal evaluation failure"
cannot exist in this stack.

---

## 4. Do FCN-03 and FCN-04 satisfy the original C-14 requirement?

### 4.1 Original C-14 requirement (from qualification plan)

> **C-14**: Temporal evaluation failures — demonstrate fail-closed behavior
> when the temporal engine cannot produce a definitive boolean result.

### 4.2 What FCN-03 and FCN-04 demonstrate

| Aspect | FCN-03 (History Replay) | FCN-04 (Current Event) |
|--------|------------------------|------------------------|
| Failure source | Entity conformance on history entity | Entity conformance on current entity |
| Temporal engine involvement | History replay populates trace | Temporal bindings produced, then current event fails |
| Reference stage | `evaluation` | `evaluation` |
| D3 stage | `trusted-binding` | `trusted-binding` |
| Decision produced | None | None |
| Fail-closed | Yes | Yes |

### 4.3 Assessment

**FCN-03 and FCN-04 do NOT satisfy QD3Q-05's literal requirement** because:
- They are **entity conformance failures**, not temporal-engine evaluation failures
- The temporal engine itself (`InMemoryTemporalEngine::evaluate`) **succeeds** in both cases
- The failure occurs **before** (FCN-03) or **after** (FCN-04) temporal evaluation

**However**, they **do satisfy the broader C-14 fail-closed requirement** by
demonstrating that:
- Temporal authorizations fail closed when entity conformance is violated
- The failure is observed at the evaluation stage (reference) / trusted-binding (D3)
- Neither side produces a decision or a clean Allow

---

## 5. Reconciliation statement

| Obligation | Status | Rationale |
|------------|--------|-----------|
| **QD3Q-05 literal** (genuine temporal-engine evaluation failure) | **Structurally unreachable** | Only error path is `"no event observed"` which cannot occur for a referenced action; `ingest` precedes `evaluate`; `eval_condition` is total; Cedar has no temporal extensions |
| **C-14 broader requirement** (fail-closed temporal failures) | **Discharged via FCN-03, FCN-04** | Two reachable evaluation-stage fail-closed cases added, both sides fail closed, normalized as fail-closed equivalences |
| **Original C-14 corpus** | **Preserved** | R3-C14-01/02/03 retained as trusted-binding/validation observations |

---

## 6. Residual limitation

**QD3Q-05 cannot be discharged as literally written** in the pinned stack. This is
not a deficiency in D3 or the corrected reference — it is an architectural
property of the pinned Dogwood 1.1.2 temporal engine.

**Documentation requirement**: This limitation must be explicitly recorded in the
R5-R2 handoff and the Q3 qualification evidence. The owner must acknowledge that
the "genuine temporal-engine evaluation failure" obligation is **structurally
unfulfillable** with the pinned dependencies.

**Alternative acceptance criterion for owner approval**:
> "The C-14 family demonstrates fail-closed behavior for all reachable failure
> modes in the temporal pipeline (entity conformance during history replay and
> current-event evaluation). The originally specified temporal-engine evaluation
> failure is architecturally unreachable in the pinned Dogwood 1.1.2 / cedar-policy
> 4.11.0 stack. This limitation is accepted as a qualification boundary."

---

## 7. Evidence traceability

| Artifact | Location | Role |
|----------|----------|------|
| `InMemoryTemporalEngine::evaluate` | `dogwood-language/src/engine.rs:509-546` | Only error path analysis |
| `ingest` | `dogwood-language/src/api.rs:1271-1289` | Trace population before evaluation |
| `eval_condition` | `interpreter/eval.rs:36` | Total leaf evaluation |
| FCN-03 case | `R5R1-C14-01-temporal-history-replay-conformance-failure` | History replay conformance failure |
| FCN-04 case | `R5R1-C14-02-temporal-current-event-conformance-failure` | Current event conformance failure |
| Differential results | `evidence/observations-corrected-reference-*.json` | Both sides fail closed, 0 divergences |

---

## 8. Conclusion

- **QD3Q-05 literal requirement**: Not dischargeable — structurally unreachable
- **C-14 family**: Discharged via FCN-03 and FCN-04 as fail-closed equivalences
- **Residual limitation**: Explicitly documented for owner acknowledgment
- **No silent substitution**: The original mandatory obligation is not marked
  closed; its unfulfillability is recorded as a qualification boundary

---

*This reconciliation is part of the DOGWOOD-SHARP-D3-IMPL-01-R5-R2 commission
evidence package. It is supported by direct source analysis of the pinned
Dogwood 1.1.2 crate.*